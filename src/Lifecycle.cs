using System;
using System.Linq;
using System.Runtime.CompilerServices;
using LiveSplit.Model;
using LiveSplit.Model.Comparisons;

namespace LiveSplit.ThermalReceipt
{
    // Shared claim only, keyed by the actual state. No performance history lives here.
    // Survives layout/component replacement in this LiveSplit process.
    internal sealed class CompletionClaims
    {
        public CompletionClaims() { }
        private static readonly ConditionalWeakTable<LiveSplitState, CompletionClaims> States = new ConditionalWeakTable<LiveSplitState, CompletionClaims>();
        private DateTime started;
        private int attempt = -1;
        private IRun run;
        public static bool Claim(LiveSplitState state)
        {
            var claims = States.GetOrCreateValue(state);
            lock (claims)
            {
                if (ReferenceEquals(claims.run, state.Run) && claims.started == state.AttemptStarted.Time && claims.attempt == state.Run.AttemptCount) return false;
                claims.run = state.Run; claims.started = state.AttemptStarted.Time; claims.attempt = state.Run.AttemptCount;
                return true;
            }
        }
    }
    public sealed class RunCapture : IDisposable
    {
        private readonly LiveSplitState state;
        private readonly Func<bool> enabled;
        private readonly Func<string> fortune;
        private readonly Action<ReceiptRun> completed;
        private IRun baseline, original;
        private DateTime started;
        private int attempt;
        private bool consumed, disposed;
        public RunCapture(LiveSplitState state, Func<bool> enabled, Func<string> fortune, Action<ReceiptRun> completed)
        {
            this.state = state; this.enabled = enabled; this.fortune = fortune; this.completed = completed;
            state.OnStart += Start; state.OnSplit += Split; state.OnReset += Reset;
        }
        private void Start(object sender, EventArgs args)
        {
            try
            {
                if (disposed || state.CurrentPhase != TimerPhase.Running || state.CurrentSplitIndex != 0) return;
                // Do not re-arm on repeated start notifications for the same attempt.
                if (original == state.Run && started == state.AttemptStarted.Time && attempt == state.Run.AttemptCount) return;
                original = state.Run; started = state.AttemptStarted.Time; attempt = state.Run.AttemptCount;
                consumed = false; baseline = null;
                baseline = (IRun)state.Run.Clone();
                foreach (var s in baseline) s.SplitTime = default(Time);
            }
            catch (Exception ex) { baseline = null; Log.Write("Run baseline failed " + ex); }
        }
        private void Reset(object sender, TimerPhase previous) { baseline = null; original = null; consumed = true; }
        private void Split(object sender, EventArgs args)
        {
            try
            {
                if (disposed || consumed || baseline == null || original != state.Run || state.CurrentPhase != TimerPhase.Ended
                    || state.CurrentSplitIndex != state.Run.Count || state.Run.Count == 0) return;
                consumed = true;
                if (!enabled() || !CompletionClaims.Claim(state)) return;
                ReceiptRun receipt = Snapshot(fortune());
                Log.Write("Run completed game=" + receipt.Game + " category=" + receipt.Category + " final=" + receipt.Final
                    + " previousPB=" + receipt.PreviousPB + " result=" + receipt.Result + " attempt=" + receipt.Attempt
                    + " splits=" + receipt.Splits.Count + " golds=" + receipt.GoldCount + " bestImproved=" + receipt.BestImproved + " recent=" + receipt.Recent.Count);
                completed(receipt);
            }
            catch (Exception ex) { Log.Write("Completion capture failed " + ex); }
        }
        private ReceiptRun Snapshot(string selectedFortune)
        {
            TimingMethod method = state.CurrentTimingMethod;
            if (baseline.Count != state.Run.Count) throw new InvalidOperationException("Route changed during attempt; receipt suppressed.");
            var finished = (IRun)baseline.Clone();
            for (int i = 0; i < finished.Count; i++) finished[i].SplitTime = state.Run[i].SplitTime;
            var checkState = new LiveSplitState(finished, null, null, null, null);
            string comparison = state.CurrentComparison;
            var splits = Enumerable.Range(0, finished.Count).Select(i =>
            {
                TimeSpan? cumulative = finished[i].SplitTime[method];
                TimeSpan? previous = i == 0 ? TimeSpan.Zero : finished[i - 1].SplitTime[method];
                // A missing GT value is not necessarily a skipped split; RTA records actual skips.
                bool skipped = !finished[i].SplitTime.RealTime.HasValue && !finished[i].SplitTime.GameTime.HasValue;
                return new ReceiptSplit(state.Run[i].Name, cumulative - previous, cumulative,
                    cumulative - (comparison == Run.PersonalBestComparisonName ? baseline[i] : state.Run[i]).Comparisons[comparison][method], skipped,
                    LiveSplitStateHelper.CheckBestSegment(checkState, i, method));
            }).ToArray();
            TimeSpan? final = finished[finished.Count - 1].SplitTime[method];
            // The history snapshot predates this attempt, so the current run cannot be counted twice,
            // even when another component calls UpdateTimes before our OnSplit handler.
            var recent = baseline.AttemptHistory.OrderByDescending(a => a.Index).Select(a => a.Time[method]).Where(t => t.HasValue).Select(t => t.Value);
            if (final.HasValue) recent = new[] { final.Value }.Concat(recent);
            return new ReceiptRun(state.Run.GameName, state.Run.CategoryName, final,
                baseline[baseline.Count - 1].PersonalBestSplitTime[method],
                SumOfBest.CalculateSumOfBest(baseline, false, false, method),
                SumOfBest.CalculateSumOfBest(finished, false, true, method), comparison,
                method == TimingMethod.GameTime ? "GAME TIME" : "REAL TIME", attempt, state.AttemptEnded.Time.ToLocalTime(),
                splits, recent.Take(10), selectedFortune);
        }
        public void Dispose()
        {
            if (disposed) return; disposed = true;
            state.OnStart -= Start; state.OnSplit -= Split; state.OnReset -= Reset; baseline = null; original = null;
        }
    }
}
