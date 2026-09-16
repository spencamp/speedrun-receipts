using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Globalization;

namespace LiveSplit.ThermalReceipt
{
    // Native text and small standalone borders only; never rasterize a receipt.
    internal sealed class EscPosWriter
    {
        private readonly PrinterProfile profile;
        public EscPosWriter(PrinterProfile profile) { this.profile = profile; }
        private readonly MemoryStream bytes = new MemoryStream();
        private void Command(params byte[] data) { bytes.Write(data, 0, data.Length); }
        public void Initialize() { Command(27, 64); }
        public void Center(bool on) { Command(27, 97, (byte)(on ? 1 : 0)); }
        public void Bold(bool on) { Command(27, 69, (byte)(on ? 1 : 0)); }
        public void Reverse(bool on) { if (!profile.SupportsReverse) return; Command(29, 66, (byte)(on ? 1 : 0)); }
        public void FontB(bool on) { Command(27, 77, (byte)(on ? 1 : 0)); }
        public void Double(bool on) { Command(29, 33, (byte)(on ? 17 : 0)); }
        public void Line(string text = "") { var data = Encoding.ASCII.GetBytes(Format.Ascii(text)); bytes.Write(data, 0, data.Length); Command(10); }
        public void Border()
        {
            if (!profile.Supports24DotImages) { Line(new string('-', profile.FontAColumns)); return; }
            Command(27, 51, 24, 27, 42, 33, (byte)(profile.PrintableDots % 256), (byte)(profile.PrintableDots / 256));
            for (int x = 0; x < profile.PrintableDots; x++)
            {
                int y = 3 + Math.Abs(x % 32 - 16);
                for (int block = 0; block < 3; block++)
                {
                    byte value = 0;
                    for (int bit = 0; bit < 8; bit++) if (Math.Abs(block * 8 + bit - y) <= 1) value |= (byte)(128 >> bit);
                    Command(value);
                }
            }
            Command(10, 27, 50);
        }
        public void Cut() { if (profile.SupportsCut) Command(29, 86, 0); }
        public byte[] Finish() { return bytes.ToArray(); }
    }
    public sealed class ReceiptRenderer
    {
        private readonly PrinterProfile profile;
        private readonly bool cutAfterReceipt;
        public ReceiptRenderer() : this(PrinterProfiles.Pos58) { }
        public ReceiptRenderer(PrinterProfile profile, bool cutAfterReceipt = false)
        { if (profile == null) throw new ArgumentNullException("profile"); this.profile = profile; this.cutAfterReceipt = cutAfterReceipt; }
        private void Stat(EscPosWriter w, string label, string value)
        {
            bool small = label.Length + value.Length + 1 > profile.FontAColumns;
            int width = small ? profile.FontBColumns : profile.FontAColumns;
            w.FontB(small);
            if (label.Length + value.Length + 1 <= width) w.Line(label + new string(' ', width - label.Length - value.Length) + value);
            else { w.Line(label); foreach (string line in Format.Wrap(value, width)) w.Line(line); }
            w.FontB(false);
        }
        private void Bar(EscPosWriter w, string text) { if (!profile.SupportsReverse) { w.Bold(true); w.Line(text.Length == 0 ? new string('-', profile.FontAColumns) : text.PadLeft((profile.FontAColumns + text.Length) / 2, '-').PadRight(profile.FontAColumns, '-')); w.Bold(false); return; } w.Reverse(true); w.Line(text.PadLeft((profile.FontAColumns + text.Length) / 2).PadRight(profile.FontAColumns)); w.Reverse(false); }
        private void Title(EscPosWriter w, string text)
        { bool small = Format.Ascii(text).Length > profile.FontAColumns; w.FontB(small); w.Line(Format.Fit(text, small ? profile.FontBColumns : profile.FontAColumns)); w.FontB(false); }
        public byte[] Render(ReceiptRun run)
        {
            var w = new EscPosWriter(profile); w.Initialize(); w.Center(false); w.Line(new string('-', profile.FontAColumns)); w.Line();
            w.Center(true); w.Bold(true); Title(w, run.Game); w.Bold(false); Title(w, run.Category); w.Line();
            string label = run.Result == "PB" ? "NEW PB" : run.Result == "TIE" ? "PB TIED" : "";
            string time = Format.Time(run.Final, 2), hero = label.Length == 0 ? time : label + " " + time;
            w.Bold(true); w.Reverse(label.Length > 0); w.Double(true);
            if (hero.Length <= profile.FontAColumns / 2) w.Line(hero);
            else { if (label.Length > 0) w.Line(label); if (time.Length > profile.FontAColumns / 2) w.Double(false); w.Line(time); }
            w.Double(false); w.Reverse(false); w.Bold(false); w.Line(); w.Center(false);
            if (run.PreviousPB.HasValue) { Stat(w, run.Result == "PB" ? "PREVIOUS PB" : "PB", Format.Time(run.PreviousPB, 2)); Stat(w, "TIME DIFFERENCE", Format.Delta(run.Final - run.PreviousPB)); }
            if (run.Average.HasValue) Stat(w, run.Recent.Count + "-RUN AVERAGE", Format.Time(run.Average, 1));
            if (run.GoldCount > 0) Stat(w, "GOLD SPLITS", run.GoldCount.ToString());
            if (run.BestImproved) Stat(w, "SUM OF BEST", Format.Time(run.NewBest, 1) + " (" + Format.Delta(run.NewBest - run.PreviousBest) + ")");
            w.Line(); Bar(w, "SPLITS"); w.FontB(true);
            string comparison = run.Comparison == "Personal Best" ? "PB" : run.Comparison == "Average Segments" ? "AVG" : run.Comparison == "Best Segments" ? "BEST" : Format.Fit(run.Comparison, 8);
            var rows = run.Splits.Select(s => new[] { (s.Gold ? "* " : "  ") + s.Name, s.Skipped ? "SKIP" : Format.Time(s.Segment, 1), Format.Time(s.Cumulative, 1), Format.Delta(s.Delta) }).ToArray();
            int seg = Math.Max(6, rows.Select(r => r[1].Length).DefaultIfEmpty().Max());
            int cum = Math.Max(7, rows.Select(r => r[2].Length).DefaultIfEmpty().Max());
            int delta = Math.Max(Math.Max(6, 3 + comparison.Length), rows.Select(r => r[3].Length).DefaultIfEmpty().Max());
            int name = profile.FontBColumns - 3 - seg - cum - delta;
            // Even TimeSpan extremes retain honest numeric values and a non-wrapping row.
            if (name < 4) { seg = Math.Min(seg, 12); cum = Math.Min(cum, 12); delta = Math.Min(delta, 11); name = profile.FontBColumns - 3 - seg - cum - delta; }
            while (name < 4) { if (cum >= seg && cum >= delta) cum--; else if (seg >= delta) seg--; else delta--; name++; }
            w.Line(Format.Fit("SPLIT", name).PadRight(name) + " " + "SEG".PadLeft(seg) + " " + "TIME".PadLeft(cum) + " " + Format.Fit("VS " + comparison, delta).PadLeft(delta));
            w.Line(new string('-', profile.FontBColumns));
            foreach (var row in rows) w.Line(Format.Fit(row[0], name).PadRight(name) + " " + (row[1].Length > seg ? "--" : row[1]).PadLeft(seg) + " " + (row[2].Length > cum ? "--" : row[2]).PadLeft(cum) + " " + (row[3].Length > delta ? "--" : row[3]).PadLeft(delta));
            w.FontB(false); w.Line(); Bar(w, ""); w.Line();
            w.Line("ATTEMPT #" + run.Attempt.ToString("N0", CultureInfo.InvariantCulture));
            w.Line(run.Finished.ToString("MMM d yyyy - h:mm tt", CultureInfo.InvariantCulture).ToUpperInvariant()); w.Line(run.TimingMethod); w.Line();
            w.Center(true); w.Border(); w.Line(); foreach (string line in Format.Wrap(run.Fortune, profile.FontAColumns).Take(3)) w.Line(line);
            w.Line(); w.Border(); w.Line(); w.Center(false); w.Line(new string('-', profile.FontAColumns)); w.Line(); w.Line(); w.Line();
            if (cutAfterReceipt) w.Cut();
            return w.Finish();
        }
    }
}

