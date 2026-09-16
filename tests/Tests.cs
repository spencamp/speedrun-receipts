using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using System.Xml;
using LiveSplit.Model;
using LiveSplit.Model.Comparisons;
using LiveSplit.UI;
using LiveSplit.UI.Components;
using LiveSplit.ThermalReceipt;

internal static class Tests
{
    private static int passed, failed;
    private static TimeSpan T(double seconds) { return TimeSpan.FromSeconds(seconds); }
    private static Time TT(double seconds) { return new Time(T(seconds), T(seconds)); }
    private static void Assert(bool condition, string message = "Assertion failed") { if (!condition) throw new Exception(message); }
    private static void Test(string name, Action test)
    { try { test(); passed++; Console.WriteLine("PASS " + name); } catch (Exception ex) { failed++; Console.WriteLine("FAIL " + name + ": " + ex); } }
    private sealed class FakePrinter : IReceiptPrinter
    {
        public int Count; public bool Fail; public byte[] Last;
        public readonly ManualResetEvent Called = new ManualResetEvent(false);
        public void Print(string queue, byte[] data) { Count++; Last = data; Called.Set(); if (Fail) throw new IOException("Queue unavailable (fake)"); }
    }
    private sealed class Fixture : IDisposable
    {
        public readonly LiveSplitState State;
        public readonly TimerModel Timer;
        public readonly List<ReceiptRun> Receipts = new List<ReceiptRun>();
        public RunCapture Capture;
        public bool Enabled = true;
        public Fixture(int count = 3, bool pb = true)
        {
            var run = new Run(new StandardComparisonGeneratorsFactory());
            for (int i = 0; i < count; i++) run.Add(new Segment("Segment " + i, pb ? TT((i + 1) * 100) : default(Time), pb ? TT(90) : default(Time)));
            run.AttemptCount = 1283;
            State = new LiveSplitState(run, null, null, null, null) { CurrentComparison = Run.PersonalBestComparisonName, CurrentTimingMethod = TimingMethod.GameTime };
            Timer = new TimerModel { CurrentState = State };
            Capture = NewCapture();
        }
        public RunCapture NewCapture() { return new RunCapture(State, () => Enabled, () => FortuneBag.Pool[0], r => Receipts.Add(r)); }
        public void Start() { Timer.Start(); State.IsGameTimeInitialized = true; State.IsGameTimePaused = true; }
        public void Split(double seconds) { State.GameTimePauseTime = T(seconds); State.AdjustedStartTime = TimeStamp.Now - T(seconds); Timer.Split(); }
        public void Finish(double final = 270) { Start(); for (int i = 0; i < State.Run.Count; i++) Split(final * (i + 1) / State.Run.Count); }
        public void History(int count)
        {
            for (int i = 1; i <= count; i++) State.Run.AttemptHistory.Add(new Attempt(i, TT(300 + i), null, null, null));
            State.Run.AttemptHistory.Add(new Attempt(count + 1, default(Time), null, null, null));
        }
        public void Dispose() { Capture.Dispose(); }
    }
    private static void Replay(LiveSplitState state, string name)
    { var handler = (EventHandler)typeof(LiveSplitState).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(state); if (handler != null) handler(state, EventArgs.Empty); }

    // Parse commands, skipping image payloads rather than confusing binary border data with commands.
    private sealed class Trace
    {
        public readonly List<string> Lines = new List<string>();
        public int Images, FontBCommands, ReverseCommands, DoubleCommands;
        public Trace(byte[] bytes)
        {
            int columns = 32, multiplier = 1; bool fontB = false;
            var line = new StringBuilder();
            for (int i = 0; i < bytes.Length;)
            {
                byte b = bytes[i++];
                if (b == 27 || b == 29)
                {
                    byte command = bytes[i++];
                    if (b == 27 && (command == 64 || command == 50)) continue;
                    if (b == 27 && command == 42)
                    {
                        Assert(line.Length == 0, "Inline image forbidden"); Assert(bytes[i++] == 33);
                        int width = bytes[i++] + 256 * bytes[i++]; Assert(width == 320); i += width * 3; Images++; continue;
                    }
                    byte value = bytes[i++];
                    if (b == 27 && command == 77) { fontB = value == 1; columns = fontB ? 42 : 32; if (fontB) FontBCommands++; }
                    else if (b == 29 && command == 33) { Assert(value == 0 || value == 17); multiplier = value == 17 ? 2 : 1; if (multiplier == 2) DoubleCommands++; }
                    else if (b == 29 && command == 66) { if (value == 1) ReverseCommands++; }
                    else Assert(b == 27 && (command == 97 || command == 69 || command == 51), "Unsafe/unrecognized ESC/POS command");
                }
                else if (b == 10) { Assert(line.Length * multiplier <= columns, "Line wraps: " + line); Lines.Add(line.ToString()); line.Clear(); }
                else { Assert(b >= 32 && b <= 126, "Non-ASCII/control text"); line.Append((char)b); }
            }
            Assert(Images == 2); Assert(FontBCommands > 0); Assert(ReverseCommands >= 2); Assert(DoubleCommands > 0);
            Assert(Lines[0] == new string('-', 32));
        }
        public string Text { get { return String.Join("\n", Lines); } }
    }
    private static Trace Render(ReceiptRun receipt) { return new Trace(new ReceiptRenderer().Render(receipt)); }
    private static ReceiptRun Custom(double final = 1122.37, double? pb = 1135.62, int golds = 0, int count = 3,
        double? oldBest = 1100, double? newBest = 1100, string comparison = "Personal Best", string method = "REAL TIME",
        string game = "GAME", string category = "CATEGORY", string fortune = "A new PB is on the horizon.")
    {
        return new ReceiptRun(game, category, T(final), pb.HasValue ? T(pb.Value) : (TimeSpan?)null,
            oldBest.HasValue ? T(oldBest.Value) : (TimeSpan?)null, newBest.HasValue ? T(newBest.Value) : (TimeSpan?)null,
            comparison, method, 1234567, new DateTime(2026, 9, 15, 19, 42, 0),
            Enumerable.Range(0, count).Select(i => new ReceiptSplit("A very long split name that needs truncation " + i,
                T(final / count), T(final * (i + 1) / count), i == 0 ? T(0) : T(-13.3), false, i < golds)), new[] { T(final) }, fortune);
    }
    [STAThread]
    private static int Main()
    {
        Test("Normal run", () => { var r = Custom(1200); Assert(r.Result == "NORMAL"); var t = Render(r); Assert(t.ReverseCommands == 2); Assert(t.Text.Contains("PB ")); Assert(!t.Text.Contains("PREVIOUS PB")); });
        Test("New PB preserves previous record", () => { var r = Custom(); Assert(r.Result == "PB"); var t = Render(r); Assert(t.Text.Contains("NEW PB 18:42.37")); Assert(t.Text.Contains("PREVIOUS PB")); Assert(t.Text.Contains("18:55.62")); Assert(t.ReverseCommands == 3); });
        Test("Exact PB tie retains PB label", () => { var r = Custom(1135.62); Assert(r.Result == "TIE"); var t = Render(r); Assert(t.Text.Contains("PB TIED 18:55.62")); Assert(!t.Text.Contains("PREVIOUS PB")); Assert(t.Text.Contains("+/-0.0")); Assert(t.ReverseCommands == 3); });
        Test("First completion omits prior record and average", () => { var t = Render(Custom(pb: null)); Assert(!t.Text.Contains("PREVIOUS PB")); Assert(!t.Text.Contains("TIME DIFFERENCE")); Assert(!t.Text.Contains("RUN AVERAGE")); });
        for (int n = 1; n <= 15; n++)
        {
            int total = n;
            Test("Average completed count " + n, () => { using (var f = new Fixture()) { f.History(total - 1); f.Finish(); var r = f.Receipts.Single(); Assert(r.Recent.Count == Math.Min(total, 10)); Assert(r.Recent[0] == T(270)); Assert(r.Average.HasValue == (total > 1)); if (total > 1) { var expected = new[] { T(270) }.Concat(Enumerable.Range(1, total - 1).Reverse().Select(i => T(300 + i))).Take(10).ToArray(); Assert(r.Average == TimeSpan.FromTicks((long)expected.Average(t => (decimal)t.Ticks))); Assert(Render(r).Text.Contains(Math.Min(total, 10) + "-RUN AVERAGE")); } } });
        }
        for (int n = 0; n <= 3; n++) { int gold = n; Test("Gold count " + n, () => { var r = Custom(golds: gold); Assert(r.GoldCount == gold); var t = Render(r); Assert(t.Text.Contains("GOLD SPLITS") == (gold > 0)); Assert(t.Lines.Count(l => l.StartsWith("* ")) == gold); }); }
        Test("Unchanged SoB omitted", () => Assert(!Render(Custom()).Text.Contains("SUM OF BEST")));
        Test("Improved SoB shown", () => { var t = Render(Custom(newBest: 1099.3)); Assert(t.Text.Contains("SUM OF BEST")); Assert(t.Text.Contains("(-0.7)")); });
        Test("Unknown SoB omitted", () => Assert(!Render(Custom(oldBest: null)).Text.Contains("SUM OF BEST")));
        Test("Synthetic skip and unavailable fields", () => { var t = Render(SyntheticReceipt.Create()); Assert(t.Text.Contains("SKIP")); Assert(t.Text.Contains("--")); Assert(t.Text.Contains("+/-0.0")); });
        Test("Long names and titles stay within physical widths", () => { var t = Render(Custom(game: new string('G', 100), category: new string('C', 100))); Assert(t.Lines.Contains(new string('G', 39) + "...")); Assert(t.Lines.Contains(new string('C', 39) + "...")); });
        Test("Non-PB comparison", () => Assert(Render(Custom(comparison: "Average Segments")).Text.Contains("VS AVG")));
        Test("Custom comparison", () => Assert(Render(Custom(comparison: "My Route Target")).Text.Contains("VS My Ro...")));
        Test("Real time metadata", () => Assert(Render(Custom()).Text.Contains("REAL TIME")));
        Test("Game time metadata", () => Assert(Render(Custom(method: "GAME TIME")).Text.Contains("GAME TIME")));
        Test("Hour time", () => Assert(Render(Custom(4000)).Text.Contains("1:06:40.00")));
        Test("Ten-hour PB safely fits", () => Assert(Render(Custom(40000, 41000)).Text.Contains("11:06:40.00")));
        Test("Ten-hour tie safely fits", () => Assert(Render(Custom(40000, 40000)).Text.Contains("PB TIED")));
        Test("Large attempts", () => Assert(Render(Custom()).Text.Contains("ATTEMPT #1,234,567")));
        Test("All 500 splits retained", () => { var t = Render(Custom(count: 500)); int begin = t.Lines.IndexOf(new string('-', 42)); Assert(t.Lines.Skip(begin + 1).Take(500).All(l => l.Length == 42)); });
        Test("Fortunes fit three lines", () => { foreach (string fortune in FortuneBag.Pool) Assert(Format.Wrap(fortune, 32).Count() <= 3); Assert(Format.Wrap("A familiar route still has something to teach you. A small change will reveal a new path.", 32).Count() == 3); Render(Custom(fortune: "A familiar route still has something to teach you. A small change will reveal a new path.")); });
        Test("Precision carry and negative zero", () => { Assert(Format.Time(T(59.999), 2) == "1:00.00"); Assert(Format.Delta(T(-0.01)) == "+/-0.0"); Assert(Format.Time(T(3599.99), 1) == "1:00:00.0"); });
        Test("ASCII normalization and injection safety", () => { Assert(Format.Ascii("é★±…\u001b@") == "e*+/-... @"); Render(Custom(game: "Pokémon\n\u001b@★")); });
        Test("Run snapshot frozen through PB/history reset", () => { using (var f = new Fixture()) { f.Finish(240); var r = f.Receipts.Single(); Assert(r.PreviousPB == T(300)); Assert(r.GoldCount == 3); Assert(r.PreviousBest == T(270)); Assert(r.NewBest == T(240)); f.Timer.Reset(); Assert(f.State.Run.Last().PersonalBestSplitTime.GameTime == T(240)); Assert(r.PreviousPB == T(300)); Assert(r.Final == T(240)); Assert(r.Recent.Count == 1); } });
        Test("LiveSplit SoB projection equals reset result", () => { using (var f = new Fixture()) { f.Start(); f.Split(80); f.Timer.SkipSplit(); f.Split(220); var r = f.Receipts.Single(); Assert(r.Splits[1].Skipped); Assert(r.Splits[2].Segment == null); f.Timer.Reset(); Assert(r.NewBest == SumOfBest.CalculateSumOfBest(f.State.Run, false, false, TimingMethod.GameTime)); } });
        Test("Normal lifecycle run with gold", () => { using (var f = new Fixture()) { f.Start(); f.Split(80); f.Split(200); f.Split(320); var r = f.Receipts.Single(); Assert(r.Result == "NORMAL"); Assert(r.GoldCount == 1); } });
        Test("Lifecycle exact tie", () => { using (var f = new Fixture()) { f.Finish(300); Assert(f.Receipts.Single().Result == "TIE"); } });
        Test("Lifecycle no previous PB", () => { using (var f = new Fixture(pb: false)) { f.Finish(); Assert(f.Receipts.Single().PreviousPB == null); Assert(f.Receipts.Single().Result == "PB"); } });
        Test("Finish comparison independent of PB", () => { using (var f = new Fixture()) { f.Start(); f.State.CurrentComparison = "Average Segments"; for (int i = 0; i < 3; i++) f.State.Run[i].Comparisons["Average Segments"] = TT((i + 1) * 120); f.Split(90); f.Split(180); f.Split(270); var r = f.Receipts.Single(); Assert(r.Splits.Last().Delta == T(-90)); Assert(r.Final - r.PreviousPB == T(-30)); } });
        Test("Mid-run timing method switch retains baseline", () => { using (var f = new Fixture()) { f.State.CurrentTimingMethod = TimingMethod.RealTime; f.Start(); f.State.CurrentTimingMethod = TimingMethod.GameTime; f.Split(90); f.Split(180); f.Split(270); Assert(f.Receipts.Single().PreviousPB == T(300)); } });
        Test("Duplicate event and undo-refinish do not reprint", () => { using (var f = new Fixture()) { f.Finish(); Replay(f.State, "OnSplit"); Replay(f.State, "OnStart"); f.Timer.UndoSplit(); Assert(f.Receipts.Count == 1); f.Split(280); Assert(f.Receipts.Count == 1); f.Timer.Reset(); f.Finish(260); Assert(f.Receipts.Count == 2); } });
        Test("Partial reset and initial state print nothing", () => { using (var f = new Fixture()) { Replay(f.State, "OnSplit"); f.Start(); f.Split(90); f.Timer.Reset(); Assert(f.Receipts.Count == 0); } });
        Test("Undo before finish prints only at completion", () => { using (var f = new Fixture()) { f.Start(); f.Split(90); f.Timer.UndoSplit(); Assert(f.Receipts.Count == 0); f.Split(95); f.Split(190); f.Split(280); Assert(f.Receipts.Count == 1); } });
        Test("Reload ended/active attempt never retroactively prints", () => { using (var f = new Fixture()) { f.Finish(); f.Capture.Dispose(); f.Capture = f.NewCapture(); Replay(f.State, "OnSplit"); f.Timer.UndoSplit(); f.Split(290); Assert(f.Receipts.Count == 1); f.Timer.Reset(); f.Start(); f.Capture.Dispose(); f.Capture = f.NewCapture(); f.Split(90); f.Split(180); f.Split(270); Assert(f.Receipts.Count == 1); } });
        Test("Multiple component instances share completion claim", () => { using (var f = new Fixture()) using (var other = f.NewCapture()) { f.Finish(); Assert(f.Receipts.Count == 1); } });
        Test("Disabled printing does not print later old completion", () => { using (var f = new Fixture()) { f.Enabled = false; f.Finish(); f.Enabled = true; Replay(f.State, "OnSplit"); Assert(f.Receipts.Count == 0); f.Timer.Reset(); f.Finish(); Assert(f.Receipts.Count == 1); } });
        Test("Capture callback failure contained and not retried", () => { using (var f = new Fixture()) { f.Capture.Dispose(); int calls = 0; using (var c = new RunCapture(f.State, () => true, () => "fortune", r => { calls++; throw new Exception("fake render error"); })) { f.Finish(); Replay(f.State, "OnSplit"); Assert(calls == 1); } } });
        Test("Missing GT remains unavailable rather than RTA fallback", () => { using (var f = new Fixture()) { f.Timer.Start(); f.State.AdjustedStartTime = TimeStamp.Now - T(10); f.Timer.Split(); f.Timer.Split(); f.Timer.Split(); var r = f.Receipts.Single(); Assert(r.Final == null); Assert(!r.Splits[0].Skipped); Render(r); } });
        Test("Fake printer production render path", () => { var p = new FakePrinter(); var d = new PrintDispatcher(p); d.Submit("fake", new ReceiptRenderer().Render(SyntheticReceipt.Create())); d.Drain.Wait(); Assert(p.Count == 1); new Trace(p.Last); });
        Test("Unavailable queue and printer failures contained", () => { var p = new FakePrinter { Fail = true }; var d = new PrintDispatcher(p); d.Submit("missing", new byte[] { 1 }); d.Drain.Wait(); Assert(d.Status.StartsWith("Printing failed")); Assert(p.Count == 1); p.Fail = false; d.Submit("available", new byte[] { 1 }); d.Drain.Wait(); Assert(p.Count == 2); });
        Test("Fortune shuffle bag and reload", () => { var bag = new FortuneBag(); var seen = new HashSet<string>(); for (int i = 0; i < 5; i++) Assert(seen.Add(bag.Next())); var reloaded = new FortuneBag(); reloaded.Load(bag.Save()); for (int i = 5; i < FortuneBag.Pool.Length; i++) Assert(seen.Add(reloaded.Next())); Assert(seen.Count == FortuneBag.Pool.Length); Assert(FortuneBag.Pool.Contains(reloaded.Next())); });
        Test("Settings XML roundtrip and invalid state", () => { using (var s = new ReceiptSettings()) using (var copy = new ReceiptSettings()) { Assert(!s.PrintingEnabled); s.PrintingEnabled = true; s.Queue = "Queue & <name>"; s.Fortunes.Next(); var xml = new XmlDocument(); xml.AppendChild(s.Save(xml)); copy.Restore(xml.DocumentElement); Assert(copy.PrintingEnabled); Assert(copy.Queue == s.Queue); Assert(copy.Fortunes.Save() == s.Fortunes.Save()); copy.Restore(null); Assert(!copy.PrintingEnabled); } });
        Test("Component factory/load/settings/dispose smoke", () => { using (var f = new Fixture()) { var attribute = (ComponentFactoryAttribute)typeof(ReceiptFactory).Assembly.GetCustomAttributes(typeof(ComponentFactoryAttribute), false).Single(); var factory = (IComponentFactory)Activator.CreateInstance(attribute.ComponentFactoryClassType); using (var component = factory.Create(f.State)) { var doc = new XmlDocument(); component.SetSettings(component.GetSettings(doc)); Assert(component.GetSettingsControl(LayoutMode.Vertical) != null); component.Update(null, f.State, 0, 0, LayoutMode.Vertical); f.Finish(); } Assert(f.Receipts.Count == 1); } });
        Test("Actual LiveSplit component loader", () => { var factory = ComponentManager.LoadFactory<IComponentFactory>(typeof(ReceiptFactory).Assembly.Location); Assert(factory != null); Assert(factory.ComponentName == "Thermal Run Receipt"); });
        Test("Exact PB decision uses ticks not display rounding", () => { var r = Custom(1135.619, 1135.62); Assert(r.Result == "PB"); Assert(Format.Time(r.Final, 2) == Format.Time(r.PreviousPB, 2)); });
        Test("Real-time lifecycle completion", () => { using (var f = new Fixture()) { f.State.CurrentTimingMethod = TimingMethod.RealTime; f.Finish(240); var r = f.Receipts.Single(); Assert(r.TimingMethod == "REAL TIME"); Assert(Math.Abs(r.Final.Value.TotalSeconds - 240) < 1); Assert(r.PreviousPB == T(300)); } });
        Test("Completion timestamp is final event local time", () => { using (var f = new Fixture()) { f.Finish(); Assert(f.Receipts.Single().Finished == f.State.AttemptEnded.Time.ToLocalTime()); } });
        Test("Disposed capture detaches events", () => { using (var f = new Fixture()) { f.Capture.Dispose(); f.Finish(); Assert(f.Receipts.Count == 0); } });
        Test("Earlier completion subscriber updating history", () => { using (var f = new Fixture()) { f.Capture.Dispose(); f.State.OnSplit += delegate { if (f.State.CurrentPhase == TimerPhase.Ended) f.Timer.UpdateTimes(); }; f.Capture = f.NewCapture(); f.Finish(240); var r = f.Receipts.Single(); Assert(r.PreviousPB == T(300)); Assert(r.GoldCount == 3); Assert(r.Recent.Count == 1); Assert(r.Splits.Last().Delta == T(-60)); } });
        Test("Enabled component end-to-end with fake printer", () => { using (var f = new Fixture()) { f.Capture.Dispose(); var p = new FakePrinter(); using (var c = new ReceiptComponent(f.State, p)) { var xml = new XmlDocument(); xml.LoadXml("<Settings><Enabled>true</Enabled><PrinterQueue>fake</PrinterQueue></Settings>"); c.SetSettings(xml.DocumentElement); f.Finish(); Assert(p.Called.WaitOne(5000)); Replay(f.State, "OnSplit"); Assert(p.Count == 1); new Trace(p.Last); f.Timer.Reset(); Assert(p.Count == 1); } } });
        Test("Manual button uses fake queue while auto printing disabled", () => { using (var f = new Fixture()) { f.Capture.Dispose(); var p = new FakePrinter(); using (var c = new ReceiptComponent(f.State, p)) { var settings = c.GetSettingsControl(LayoutMode.Vertical); var panel = settings.Controls[0]; var button = panel.Controls.OfType<Button>().Single(b => b.Text == "Print Test Receipt"); typeof(Button).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(button, new object[] { EventArgs.Empty }); Assert(p.Called.WaitOne(5000)); Assert(p.Count == 1); Assert(new Trace(p.Last).Text.Contains("SKIP")); } } });
        Test("Deterministic renderer byte snapshot", () => { var r = Custom(); var renderer = new ReceiptRenderer(); Assert(renderer.Render(r).SequenceEqual(renderer.Render(r))); var bytes = renderer.Render(SyntheticReceipt.Create()); var trace = new Trace(bytes); Directory.CreateDirectory("build"); File.WriteAllBytes("build/test-receipt.bin", bytes); File.WriteAllText("build/test-receipt.txt", trace.Text); });
        Console.WriteLine("RESULT: " + passed + " passed, " + failed + " failed. No physical printer used.");
        return failed == 0 ? 0 : 1;
    }
}
