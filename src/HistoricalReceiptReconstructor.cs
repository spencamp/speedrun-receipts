using System;
using System.Linq;
using LiveSplit.Model;

namespace LiveSplit.ThermalReceipt
{
    // Reads a private clone. Attempt indices, never timestamps/list order, define chronology.
    public sealed class HistoricalReceiptReconstructor
    {
        public Attempt[] Completed(IRun run, TimingMethod method)
        {
            return run.AttemptHistory.Where(a => a.Time[method].HasValue)
                .OrderByDescending(a => a.Index).ToArray();
        }
        private static TimeSpan?[] Cumulative(IRun run, int id, TimingMethod method)
        {
            var result = new TimeSpan?[run.Count];
            TimeSpan? total = TimeSpan.Zero;
            for (int i = 0; i < run.Count; i++)
            {
                Time entry;
                if (!run[i].SegmentHistory.TryGetValue(id, out entry)) { total = null; continue; }
                if (entry[method].HasValue) { total += entry[method]; result[i] = total; }
            }
            // Missing/edited history must not masquerade as the original cumulative times.
            var attempt = run.AttemptHistory.First(a => a.Index == id);
            if (result.Length > 0 && result[result.Length - 1] != attempt.Time[method])
                Array.Clear(result, 0, result.Length);
            return result;
        }
        public ReceiptRun Reconstruct(IRun source, int id, TimingMethod method, DateTime printed, string fortune)
        {
            var run = (IRun)source.Clone();
            var matches = run.AttemptHistory.Where(a => a.Index == id).ToArray();
            if (run.Count == 0 || matches.Length != 1 || !matches[0].Time[method].HasValue)
                throw new InvalidOperationException("Select a completed attempt with the requested timing data.");
            if (run.AttemptHistory.GroupBy(a => a.Index).Any(g => g.Count() != 1))
                throw new InvalidOperationException("Duplicate attempt IDs prevent reliable reconstruction.");
            var selected = matches[0];
            var earlier = Completed(run, method).Where(a => a.Index < id).ToArray();
            var pb = earlier.OrderBy(a => a.Time[method]).ThenBy(a => a.Index).Take(1).ToArray();
            TimeSpan? prior = pb.Length == 0 ? (TimeSpan?)null : pb[0].Time[method];
            var comparison = pb.Length == 0 ? new TimeSpan?[run.Count] : Cumulative(run, pb[0].Index, method);
            var cumulative = Cumulative(run, id, method);
            var rows = Enumerable.Range(0, run.Count).Select(i =>
            {
                Time entry;
                bool present = run[i].SegmentHistory.TryGetValue(id, out entry);
                bool skip = present && !entry.RealTime.HasValue && !entry.GameTime.HasValue;
                TimeSpan? previous = i == 0 ? TimeSpan.Zero : cumulative[i - 1];
                TimeSpan? duration = cumulative[i] - previous;
                // Only individual segments with an established dated baseline qualify.
                var best = run[i].SegmentHistory.Where(h => h.Key >= 0 && h.Key < id && h.Value[method].HasValue
                    && run.AttemptHistory.Any(a => a.Index == h.Key)
                    && (i == 0 || (run[i - 1].SegmentHistory.ContainsKey(h.Key) && run[i - 1].SegmentHistory[h.Key][method].HasValue)))
                    .Select(h => h.Value[method]).OrderBy(t => t).FirstOrDefault();
                return new ReceiptSplit(run[i].Name, duration, i == run.Count - 1 ? selected.Time[method] : cumulative[i],
                    (i == run.Count - 1 ? selected.Time[method] : cumulative[i]) - comparison[i], skip,
                    duration.HasValue && best.HasValue && duration < best);
            }).ToArray();
            return new ReceiptRun(run.GameName, run.CategoryName, selected.Time[method], prior, null, null,
                Run.PersonalBestComparisonName, method == TimingMethod.GameTime ? "GAME TIME" : "REAL TIME", id,
                selected.Ended.HasValue ? selected.Ended.Value.Time.ToLocalTime() : DateTime.MinValue, rows,
                Completed(run, method).Where(a => a.Index <= id).Take(10).Select(a => a.Time[method].Value), fortune,
                true, printed.Kind == DateTimeKind.Utc ? printed.ToLocalTime() : printed, selected.Ended.HasValue, prior.HasValue);
        }
    }
}
