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
        public int Images, FontBCommands, ReverseCommands, DoubleCommands, Cuts;
        public Trace(byte[] bytes, PrinterProfile profile = null, bool expectCut = false, bool expectFortunes = true)
        {
            profile = profile ?? PrinterProfiles.Pos58;
            int columns = profile.FontAColumns, multiplier = 1; bool fontB = false;
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
                        int width = bytes[i++] + 256 * bytes[i++]; Assert(profile.Supports24DotImages); Assert(width == profile.PrintableDots); Assert(i + width * 3 <= bytes.Length); i += width * 3; Images++; continue;
                    }
                    byte value = bytes[i++];
                    if (b == 27 && command == 77) { fontB = value == 1; columns = fontB ? profile.FontBColumns : profile.FontAColumns; if (fontB) FontBCommands++; }
                    else if (b == 29 && command == 33) { Assert(value == 0 || value == 17); multiplier = value == 17 ? 2 : 1; if (multiplier == 2) DoubleCommands++; }
                    else if (b == 29 && command == 66) { Assert(profile.SupportsReverse); if (value == 1) ReverseCommands++; }
                    else if (b == 29 && command == 86) { Assert(profile.SupportsCut && expectCut && value == 0); Cuts++; }
                    else Assert(b == 27 && (command == 97 || command == 69 || command == 51), "Unsafe/unrecognized ESC/POS command");
                }
                else if (b == 10) { Assert(line.Length * multiplier <= columns, "Line wraps: " + line); Lines.Add(line.ToString()); line.Clear(); }
                else { Assert(b >= 32 && b <= 126, "Non-ASCII/control text"); line.Append((char)b); }
            }
            Assert(Images == (profile.Supports24DotImages && expectFortunes ? 2 : 0)); Assert(FontBCommands > 0); Assert(profile.SupportsReverse ? ReverseCommands >= 2 : ReverseCommands == 0); Assert(DoubleCommands > 0); Assert(Cuts == (expectCut ? 1 : 0));
            Assert(Lines[0] == new string('-', profile.FontAColumns));
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
    private static Run ArchiveFixture()
    {
        var run = new Run(new StandardComparisonGeneratorsFactory());
        run.Add(new Segment("First")); run.Add(new Segment("Second")); run.Add(new Segment("Final"));
        AddArchive(run, 1, 100, 100, 100);
        AddArchive(run, 2, 90, 95, 95);
        AddArchive(run, 3, 110, 90, 100);
        AddArchive(run, 4, 90, 95, 95);
        AddArchive(run, 5, 80, 80, 80);
        foreach (var s in run) { s.BestSegmentTime = TT(80); s.PersonalBestSplitTime = TT(1); }
        return run;
    }
    private static void AddArchive(IRun run, int id, params double[] durations)
    {
        var date = new AtomicDateTime(new DateTime(2026, 9, id, 12, 0, 0, DateTimeKind.Utc), false);
        run.AttemptHistory.Add(new Attempt(id, TT(durations.Sum()), date, date, null));
        for (int i = 0; i < durations.Length; i++) run[i].SegmentHistory.Add(id, TT(durations[i]));
    }
    private static ReceiptRun Archive(IRun run, int id, DateTime? printed = null)
    { return new HistoricalReceiptReconstructor().Reconstruct(run, id, TimingMethod.GameTime, printed ?? new DateTime(2026, 9, 16), "Fresh fortune."); }
    private static void ArchiveTests()
    {
        Test("Archive normal uses PB at run and historical difference", () => { var r = Archive(ArchiveFixture(), 3); Assert(r.Result == "NORMAL" && r.PreviousPB == T(280)); Assert(Render(r).Text.Contains("PB AT RUN") && Render(r).Text.Contains("+20.0")); });
        Test("Archive PB no longer current", () => { var r = Archive(ArchiveFixture(), 2); Assert(r.Result == "PB" && r.PreviousPB == T(300)); Assert(Render(r).Text.Contains("PREVIOUS PB")); });
        Test("Archive PB remains current", () => Assert(Archive(ArchiveFixture(), 5).Result == "PB"));
        Test("Archive tied PB", () => Assert(Archive(ArchiveFixture(), 4).Result == "TIE"));
        Test("Archive future records and current comparisons cannot contaminate", () => { var run = ArchiveFixture(); var before = new ReceiptRenderer().Render(Archive(run, 3)); AddArchive(run, 6, 1, 1, 1); run[0].BestSegmentTime = TT(1); run[0].PersonalBestSplitTime = TT(1); Assert(before.SequenceEqual(new ReceiptRenderer().Render(Archive(run, 3)))); });
        Test("Archive fewer than ten average excludes later completions", () => { var r = Archive(ArchiveFixture(), 3); Assert(r.Recent.Count == 3 && r.Average == TimeSpan.FromTicks((T(300).Ticks + T(280).Ticks + T(300).Ticks) / 3)); });
        Test("Archive ten run average", () => { var run = ArchiveFixture(); for (int i = 6; i <= 15; i++) AddArchive(run, i, i, i, i); var r = Archive(run, 14); Assert(r.Recent.Count == 10 && r.Recent[0] == T(42) && r.Recent[9] == T(240)); });
        Test("Archive historical golds survive newer bests", () => Assert(Archive(ArchiveFixture(), 2).GoldCount == 3));
        Test("Archive equal earlier best is not gold", () => Assert(Archive(ArchiveFixture(), 4).GoldCount == 0));
        Test("Archive only improved historical segment is gold", () => { var r = Archive(ArchiveFixture(), 3); Assert(r.GoldCount == 1 && r.Splits[1].Gold); });
        Test("Archive unreliable Sum of Best omitted", () => { var r = Archive(ArchiveFixture(), 2); Assert(!r.PreviousBest.HasValue && !r.NewBest.HasValue && !Render(r).Text.Contains("SUM OF BEST")); });
        Test("Archive skips retain combined cumulative but no fabricated segment or gold", () => { var run = ArchiveFixture(); run[0].SegmentHistory[3] = default(Time); run[1].SegmentHistory[3] = TT(200); var r = Archive(run, 3); Assert(r.Splits[0].Skipped && !r.Splits[1].Segment.HasValue && r.Splits[1].Cumulative == T(200) && !r.Splits[1].Gold); Assert(Render(r).Text.Contains("SKIP")); });
        Test("Archive split comparison is historical PB", () => { var r = Archive(ArchiveFixture(), 3); Assert(r.Splits[0].Delta == T(20) && r.Splits[2].Delta == T(20) && Render(r).Text.Contains("VS PB")); });
        Test("Archive original history attempt ID", () => Assert(Archive(ArchiveFixture(), 3).Attempt == 3));
        Test("Archive historical run date and later reprint date", () => { var text = Render(Archive(ArchiveFixture(), 3)).Text; Assert(text.Contains("RUN SEP 3 2026") && text.Contains("REPRINTED SEP 16 2026") && text.Contains("ARCHIVE REPRINT")); });
        Test("Archive same day omits reprint date", () => { var run = ArchiveFixture(); var date = run.AttemptHistory.First(a => a.Index == 3).Ended.Value.Time.ToLocalTime(); Assert(!Render(Archive(run, 3, date.AddMinutes(1))).Text.Contains("REPRINTED")); });
        Test("Archive missing date is explicit", () => { var run = ArchiveFixture(); var a = run.AttemptHistory[2]; a.Ended = null; run.AttemptHistory[2] = a; Assert(Render(Archive(run, 3)).Text.Contains("RUN DATE UNKNOWN")); });
        Test("Archive incomplete attempts rejected and hidden", () => { var run = ArchiveFixture(); run.AttemptHistory.Add(new Attempt(6, default(Time), null, null, null)); Assert(new HistoricalReceiptReconstructor().Completed(run, TimingMethod.RealTime).Length == 5); bool rejected = false; try { Archive(run, 6); } catch (InvalidOperationException) { rejected = true; } Assert(rejected); });
        Test("Archive unavailable timing method rejected", () => { var run = ArchiveFixture(); var a = run.AttemptHistory[2]; a.Time = new Time(T(300), null); run.AttemptHistory[2] = a; bool rejected = false; try { Archive(run, 3); } catch (InvalidOperationException) { rejected = true; } Assert(rejected); });
        Test("Archive missing history never fabricated or classified skip", () => { var run = ArchiveFixture(); run[0].SegmentHistory.Remove(3); var r = Archive(run, 3); Assert(!r.Splits[0].Skipped && !r.Splits[0].Cumulative.HasValue && !r.Splits[1].Segment.HasValue && r.Final == T(300)); });
        Test("Archive first retained completion has unknown prior PB", () => { var r = Archive(ArchiveFixture(), 1); Assert(r.Result == "NORMAL" && !r.Average.HasValue && !r.HistoricalPbKnown); });
        Test("Archive history list sorts indices rather than storage or timestamp", () => { var run = ArchiveFixture(); var a = run.AttemptHistory[0]; run.AttemptHistory.RemoveAt(0); run.AttemptHistory.Add(a); Assert(new HistoricalReceiptReconstructor().Completed(run, TimingMethod.GameTime)[0].Index == 5 && Archive(run, 3).PreviousPB == T(280)); });
        Test("Archive source data unchanged", () => { var run = ArchiveFixture(); var time = run[0].PersonalBestSplitTime; Archive(run, 3); Assert(run[0].PersonalBestSplitTime.Equals(time) && run[0].SegmentHistory.Count == 5); });
        Test("Archive widths safe for every profile", () => { foreach (var p in PrinterProfiles.All) new Trace(new ReceiptRenderer(p).Render(Archive(ArchiveFixture(), 3)), p); });
        Test("Archive explicit print gets fresh shuffle bag fortune and isolates live claims", () => {
            using (var f = new Fixture()) using (var c = new ReceiptComponent(f.State, new FakePrinter())) {
                var run = ArchiveFixture(); foreach (var a in run.AttemptHistory) f.State.Run.AttemptHistory.Add(a);
                for (int i = 0; i < 3; i++) foreach (var h in run[i].SegmentHistory) f.State.Run[i].SegmentHistory.Add(h.Key, h.Value);
                var settings = (ReceiptSettings)c.GetSettingsControl(LayoutMode.Vertical); settings.Queue = "fake";
                string bag = settings.Fortunes.Save(); c.PrintArchive(2, TimingMethod.GameTime); Assert(settings.Fortunes.Save() != bag);
                f.Finish(); Assert(f.Receipts.Count == 1); f.Timer.UndoSplit(); f.Split(270); Assert(f.Receipts.Count == 1);
            }
        });
        Test("Archive missing queue rejects before fortune consumption", () => { using (var f = new Fixture()) { var p = new FakePrinter(); using (var c = new ReceiptComponent(f.State, p)) { var s = (ReceiptSettings)c.GetSettingsControl(LayoutMode.Vertical); string bag = s.Fortunes.Save(); c.PrintArchive(1, TimingMethod.GameTime); Assert(p.Count == 0 && s.Fortunes.Save() == bag); } } });
        Test("Archive printer failure stays in dispatcher", () => { using (var f = new Fixture()) { var p = new FakePrinter { Fail = true }; f.State.Run.AttemptHistory.Add(new Attempt(1, TT(300), null, null, null)); using (var c = new ReceiptComponent(f.State, p)) { var s = (ReceiptSettings)c.GetSettingsControl(LayoutMode.Vertical); s.Queue = "fake"; c.PrintArchive(1, TimingMethod.GameTime); Assert(p.Called.WaitOne(3000)); Assert(f.State.Run.AttemptHistory.Count == 1); } } });
    }

    private static int Main()
    {
        Test("Fortunes default on and disabled setting persists", () => {
            using (var s = new ReceiptSettings()) using (var copy = new ReceiptSettings()) {
                Assert(s.PrintFortunes); s.PrintFortunes = false; copy.Restore(s.Save(new XmlDocument())); Assert(!copy.PrintFortunes);
                copy.Restore(null); Assert(copy.PrintFortunes);
                var xml = new XmlDocument(); xml.LoadXml("<Settings><PrintFortunes>invalid</PrintFortunes></Settings>"); copy.Restore(xml.DocumentElement); Assert(copy.PrintFortunes);
            }
        });
        Test("Fortunes off removes text and both graphic or text borders only", () => {
            foreach (var profile in new[] { PrinterProfiles.Pos58, PrinterProfiles.Generic80 }) {
                var run = Custom(fortune: "UNIQUE FORTUNE TEXT");
                var on = new Trace(new ReceiptRenderer(profile).Render(run), profile);
                var off = new Trace(new ReceiptRenderer(profile, printFortunes: false).Render(run), profile, expectFortunes: false);
                Assert(on.Text.Contains("UNIQUE FORTUNE TEXT") && !off.Text.Contains("UNIQUE FORTUNE TEXT"));
                int metadataEnd = on.Lines.IndexOf("REAL TIME") + 2;
                Assert(on.Lines.Take(metadataEnd).SequenceEqual(off.Lines.Take(metadataEnd)));
                Assert(off.Lines.Skip(metadataEnd).SequenceEqual(new[] { new string('-', profile.FontAColumns), "", "", "" }));
            }
        });
        Test("Automatic and manual receipts honor fortunes off without consuming bag", () => {
            using (var f = new Fixture()) { f.Capture.Dispose(); var p = new FakePrinter(); using (var c = new ReceiptComponent(f.State, p)) {
                var s = (ReceiptSettings)c.GetSettingsControl(LayoutMode.Vertical); s.Queue = "fake"; s.PrintingEnabled = true; s.PrintFortunes = false;
                s.Fortunes.Next(); string bag = s.Fortunes.Save(); f.Finish();
                var d = (PrintDispatcher)typeof(ReceiptComponent).GetField("dispatcher", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(c);
                d.Drain.Wait(); Assert(p.Count == 1); new Trace(p.Last, expectFortunes: false); Assert(s.Fortunes.Save() == bag);
                var button = s.Controls[0].Controls.OfType<Button>().Single(b => b.Text == "Print Test Receipt");
                typeof(Button).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(button, new object[] { EventArgs.Empty });
                d.Drain.Wait(); Assert(p.Count == 2); new Trace(p.Last, expectFortunes: false); Assert(s.Fortunes.Save() == bag);
                s.PrintFortunes = true; f.Timer.Reset(); f.Finish(); d.Drain.Wait(); Assert(p.Count == 3); new Trace(p.Last); Assert(s.Fortunes.Save() != bag);
            } }
        });
        Test("Confirmation defaults off and persists in layout settings", () => {
            using (var s = new ReceiptSettings()) using (var copy = new ReceiptSettings()) {
                Assert(!s.ConfirmBeforePrinting); s.ConfirmBeforePrinting = true;
                copy.Restore(s.Save(new XmlDocument())); Assert(copy.ConfirmBeforePrinting);
                var legacy = new XmlDocument(); legacy.LoadXml("<Settings><Enabled>true</Enabled></Settings>");
                copy.Restore(legacy.DocumentElement); Assert(!copy.ConfirmBeforePrinting);
                legacy.LoadXml("<Settings><ConfirmBeforePrinting>invalid</ConfirmBeforePrinting></Settings>");
                copy.Restore(legacy.DocumentElement); Assert(!copy.ConfirmBeforePrinting);
            }
        });
        foreach (bool confirmation in new[] { false, true }) foreach (bool answer in new[] { false, true })
        {
            bool ask = confirmation, accept = answer;
            Test("Completed receipt confirmation enabled=" + ask + " answer=" + accept, () => {
                using (var f = new Fixture()) { f.Capture.Dispose(); var p = new FakePrinter(); int prompts = 0;
                    using (var c = new ReceiptComponent(f.State, p, () => { prompts++; return accept; })) {
                        var s = (ReceiptSettings)c.GetSettingsControl(LayoutMode.Vertical);
                        s.Queue = "fake"; s.PrintingEnabled = true; s.ConfirmBeforePrinting = ask;
                        f.Finish(); Replay(f.State, "OnSplit"); f.Timer.UndoSplit(); f.Split(280);
                        var d = (PrintDispatcher)typeof(ReceiptComponent).GetField("dispatcher", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(c);
                        d.Drain.Wait(); Assert(prompts == (ask ? 1 : 0)); Assert(p.Count == (!ask || accept ? 1 : 0));
                        f.Timer.Reset(); f.Finish(); d.Drain.Wait();
                        Assert(prompts == (ask ? 2 : 0)); Assert(p.Count == (!ask || accept ? 2 : 0));
                    }
                }
            });
        }
        Test("No confirmation when disabled or queue missing; test receipt stays explicit", () => {
            using (var f = new Fixture()) { f.Capture.Dispose(); var p = new FakePrinter(); int prompts = 0;
                using (var c = new ReceiptComponent(f.State, p, () => { prompts++; return false; })) {
                    var s = (ReceiptSettings)c.GetSettingsControl(LayoutMode.Vertical); s.ConfirmBeforePrinting = true;
                    s.PrintingEnabled = true; f.Finish(); Assert(prompts == 0);
                    f.Timer.Reset(); s.Queue = "fake"; s.PrintingEnabled = false; f.Finish(); Assert(prompts == 0);
                    var button = s.Controls[0].Controls.OfType<Button>().Single(b => b.Text == "Print Test Receipt");
                    typeof(Button).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(button, new object[] { EventArgs.Empty });
                    var d = (PrintDispatcher)typeof(ReceiptComponent).GetField("dispatcher", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(c);
                    d.Drain.Wait(); Assert(p.Count == 1 && prompts == 0);
                }
            }
        });
        Test("POS-58 byte-for-byte V1 baseline", () => {
            var r = SyntheticReceipt.Create();
            var fixedTime = new ReceiptRun(r.Game, r.Category, r.Final, r.PreviousPB, r.PreviousBest, r.NewBest, r.Comparison, r.TimingMethod,
                r.Attempt, new DateTime(2026, 9, 16, 7, 6, 0), r.Splits, r.Recent, r.Fortune);
            Assert(File.ReadAllBytes("tests/fixtures/pos58-v1.bin").SequenceEqual(new ReceiptRenderer(PrinterProfiles.Pos58).Render(fixedTime)));
        });
        Test("Blank queue rejected before dispatch", () => { var p = new FakePrinter(); var d = new PrintDispatcher(p); foreach (string queue in new[] { null, "", "  " }) d.Submit(queue, new byte[] { 1 }); d.Drain.Wait(); Assert(p.Count == 0); Assert(d.Status.Contains("nothing printed")); });
        Test("Queue enumeration never selects a printer and preserves explicit queue", () => {
            using (var s = new ReceiptSettings()) {
                Assert(s.Queue == ""); s.SetAvailableQueues(new[] { "Office Laser", "Microsoft Print to PDF", "Receipt" }); Assert(s.Queue == "");
                s.Queue = "Receipt"; s.SetAvailableQueues(new[] { "Office Laser" }); Assert(s.Queue == "Receipt");
                var d = new XmlDocument(); using (var copy = new ReceiptSettings()) { copy.Restore(s.Save(d)); Assert(copy.Queue == "Receipt"); copy.Restore(null); Assert(copy.Queue == ""); }
            }
        });
        Test("Unconfigured manual and automatic paths do not print", () => {
            using (var f = new Fixture()) { f.Capture.Dispose(); var p = new FakePrinter(); using (var c = new ReceiptComponent(f.State, p)) {
                var s = (ReceiptSettings)c.GetSettingsControl(LayoutMode.Vertical); s.PrintingEnabled = true;
                var button = s.Controls[0].Controls.OfType<Button>().Single(b => b.Text == "Print Test Receipt"); Assert(!button.Enabled);
                typeof(Button).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(button, new object[] { EventArgs.Empty });
                f.Finish(); var dispatcher = (PrintDispatcher)typeof(ReceiptComponent).GetField("dispatcher", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(c);
                dispatcher.Drain.Wait(); Assert(p.Count == 0);
            } }
        });
        foreach (var selected in new[] { PrinterProfiles.Pos58, PrinterProfiles.Generic58, PrinterProfiles.Generic80,
            new PrinterProfile("custom", "Custom", 480, 40, 53, true, true), new PrinterProfile("custom", "Narrow", 192, 24, 32, false, false) })
        {
            var profile = selected;
            Test(profile.DisplayName + " widths, ASCII, rules, borders and safe commands", () => {
                var trace = new Trace(new ReceiptRenderer(profile).Render(Custom(game: "Pokémon★" + new string('G', 150), category: new string('C', 150))), profile);
                Assert(trace.Lines.Contains(new string('-', profile.FontBColumns)));
                Assert(trace.Lines.Count(l => l == new string('-', profile.FontAColumns)) >= 2);
                Assert(trace.Lines.Any(l => l.Contains("SPLITS") && l.Length == profile.FontAColumns));
                int begin = trace.Lines.IndexOf(new string('-', profile.FontBColumns));
                Assert(trace.Lines.Skip(begin + 1).Take(3).All(l => l.Length == profile.FontBColumns));
                Assert(trace.Text.Contains("..."));
                new Trace(new ReceiptRenderer(profile).Render(Custom(final: 900000000, pb: 900000001, comparison: "Long comparison")), profile);
            });
        }
        Test("Wider profiles allocate extra cells to split names and retain numeric columns", () => {
            var a = new Trace(new ReceiptRenderer(PrinterProfiles.Generic58).Render(Custom()), PrinterProfiles.Generic58);
            var b = new Trace(new ReceiptRenderer(PrinterProfiles.Generic80).Render(Custom()), PrinterProfiles.Generic80);
            string rowA = a.Lines[a.Lines.IndexOf(new string('-', 42)) + 1], rowB = b.Lines[b.Lines.IndexOf(new string('-', 64)) + 1];
            Assert(rowA.Substring(20) == rowB.Substring(42)); Assert(rowA.Substring(0, 20).EndsWith("..."));
            Assert(rowB.StartsWith("  A very long split name that needs"));
        });
        Test("Independent reverse and image fallbacks", () => {
            foreach (bool reverse in new[] { false, true }) foreach (bool images in new[] { false, true }) {
                var p = new PrinterProfile("custom", "Custom", 513, 48, 64, reverse, images);
                var t = new Trace(new ReceiptRenderer(p).Render(Custom()), p); Assert(t.Text.Contains("NEW PB"));
            }
        });
        Test("Cut requires both support and explicit opt-in; raster and Unicode never emitted", () => {
            var p = new PrinterProfile("custom", "Custom", 576, 48, 64, false, false, true, true, true);
            new Trace(new ReceiptRenderer(p).Render(Custom(game: "Pokémon★")), p);
            new Trace(new ReceiptRenderer(p, true).Render(Custom()), p, true);
            new Trace(new ReceiptRenderer(PrinterProfiles.Generic80, true).Render(Custom()), PrinterProfiles.Generic80);
        });
        Test("Profile and custom settings persist; malformed widths safe", () => {
            var xml = new XmlDocument(); xml.LoadXml("<Settings><PrinterQueue>Explicit</PrinterQueue><PrinterProfile>custom</PrinterProfile><PrintableDots>513</PrintableDots><FontAColumns>40</FontAColumns><FontBColumns>53</FontBColumns><SupportsReverse>true</SupportsReverse><Supports24DotImages>true</Supports24DotImages><SupportsCut>true</SupportsCut><CutAfterReceipt>true</CutAfterReceipt></Settings>");
            using (var s = new ReceiptSettings()) using (var copy = new ReceiptSettings()) {
                s.Restore(xml.DocumentElement); copy.Restore(s.Save(new XmlDocument()));
                Assert(copy.ProfileId == "custom" && copy.Queue == "Explicit" && copy.CutAfterReceipt);
                var p = copy.SelectedProfile; Assert(p.PrintableDots == 513 && p.FontAColumns == 40 && p.FontBColumns == 53 && p.SupportsReverse && p.Supports24DotImages && p.SupportsCut);
                s.ProfileId = "generic80"; copy.Restore(s.Save(new XmlDocument())); Assert(copy.ProfileId == "generic80" && !copy.CutAfterReceipt);
                copy.ProfileId = "custom"; Assert(copy.SelectedProfile.PrintableDots == 513);
                xml.LoadXml("<Settings><Enabled>true</Enabled><PrinterProfile>unknown</PrinterProfile><PrintableDots>-1</PrintableDots><FontAColumns>9999</FontAColumns><FontBColumns>oops</FontBColumns></Settings>"); copy.Restore(xml.DocumentElement); Assert(!copy.PrintingEnabled && copy.ProfileId == "generic58");
                copy.ProfileId = "custom"; Assert(copy.SelectedProfile.PrintableDots == 384);
            }
        });
        Test("Saved 80mm profile used by automatic and manual production paths", () => {
            using (var f = new Fixture()) { f.Capture.Dispose(); var p = new FakePrinter(); using (var c = new ReceiptComponent(f.State, p)) {
                var xml = new XmlDocument(); xml.LoadXml("<Settings><Enabled>true</Enabled><PrinterQueue>fake</PrinterQueue><PrinterProfile>generic80</PrinterProfile></Settings>");
                c.SetSettings(xml.DocumentElement); f.Finish();
                var dispatcher = (PrintDispatcher)typeof(ReceiptComponent).GetField("dispatcher", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(c);
                dispatcher.Drain.Wait(); Assert(p.Count == 1); new Trace(p.Last, PrinterProfiles.Generic80);
                var s = (ReceiptSettings)c.GetSettingsControl(LayoutMode.Vertical); s.PrintingEnabled = false;
                var button = s.Controls[0].Controls.OfType<Button>().Single(b => b.Text == "Print Test Receipt");
                typeof(Button).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(button, new object[] { EventArgs.Empty });
                dispatcher.Drain.Wait(); Assert(p.Count == 2); new Trace(p.Last, PrinterProfiles.Generic80);
            } }
        });
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
        Test("Manual button uses fake queue while auto printing disabled", () => { using (var f = new Fixture()) { f.Capture.Dispose(); var p = new FakePrinter(); using (var c = new ReceiptComponent(f.State, p)) { var settings = (ReceiptSettings)c.GetSettingsControl(LayoutMode.Vertical); settings.Queue = "explicit fake"; var panel = settings.Controls[0]; var button = panel.Controls.OfType<Button>().Single(b => b.Text == "Print Test Receipt"); typeof(Button).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(button, new object[] { EventArgs.Empty }); Assert(p.Called.WaitOne(5000)); Assert(p.Count == 1); Assert(new Trace(p.Last).Text.Contains("SKIP")); } } });
        Test("Deterministic renderer byte snapshot", () => { var r = Custom(); var renderer = new ReceiptRenderer(); Assert(renderer.Render(r).SequenceEqual(renderer.Render(r))); var bytes = renderer.Render(SyntheticReceipt.Create()); var trace = new Trace(bytes); Directory.CreateDirectory("build"); File.WriteAllBytes("build/test-receipt.bin", bytes); File.WriteAllText("build/test-receipt.txt", trace.Text); });
        ArchiveTests();
        Console.WriteLine("RESULT: " + passed + " passed, " + failed + " failed. No physical printer used.");
        return failed == 0 ? 0 : 1;
    }
}
