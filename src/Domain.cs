using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Text;

namespace LiveSplit.ThermalReceipt
{
    public sealed class ReceiptSplit
    {
        public string Name { get; private set; }
        public TimeSpan? Segment { get; private set; }
        public TimeSpan? Cumulative { get; private set; }
        public TimeSpan? Delta { get; private set; }
        public bool Skipped { get; private set; }
        public bool Gold { get; private set; }
        public ReceiptSplit(string name, TimeSpan? segment, TimeSpan? cumulative, TimeSpan? delta, bool skipped, bool gold)
        { Name = name; Segment = segment; Cumulative = cumulative; Delta = delta; Skipped = skipped; Gold = gold; }
    }

    public sealed class ReceiptRun
    {
        public readonly string Game, Category, Comparison, TimingMethod, Fortune;
        public readonly TimeSpan? Final, PreviousPB, PreviousBest, NewBest;
        public readonly int Attempt;
        public readonly DateTime Finished;
        public readonly ReadOnlyCollection<ReceiptSplit> Splits;
        public readonly ReadOnlyCollection<TimeSpan> Recent;
        public ReceiptRun(string game, string category, TimeSpan? final, TimeSpan? pb, TimeSpan? oldBest,
            TimeSpan? newBest, string comparison, string method, int attempt, DateTime finished,
            IEnumerable<ReceiptSplit> splits, IEnumerable<TimeSpan> recent, string fortune)
        {
            Game = game; Category = category; Final = final; PreviousPB = pb; PreviousBest = oldBest;
            NewBest = newBest; Comparison = comparison; TimingMethod = method; Attempt = attempt; Finished = finished;
            Splits = Array.AsReadOnly(splits.ToArray()); Recent = Array.AsReadOnly(recent.Take(10).ToArray()); Fortune = fortune;
        }
        public string Result { get { return !Final.HasValue ? "NORMAL" : !PreviousPB.HasValue || Final < PreviousPB ? "PB" : Final == PreviousPB ? "TIE" : "NORMAL"; } }
        public int GoldCount { get { return Splits.Count(s => s.Gold); } }
        public bool BestImproved { get { return NewBest.HasValue && PreviousBest.HasValue && NewBest < PreviousBest; } }
        public TimeSpan? Average { get { return Recent.Count < 2 ? (TimeSpan?)null : TimeSpan.FromTicks((long)Recent.Average(t => (decimal)t.Ticks)); } }
    }

    public static class Format
    {
        public static string Ascii(string text)
        {
            text = (text ?? "").Replace("±", "+/-").Replace("★", "*").Replace("…", "...")
                .Replace("’", "'").Replace("‘", "'").Replace("“", "\"").Replace("”", "\"").Replace("–", "-").Replace("—", "-");
            var b = new StringBuilder();
            foreach (char c in text.Normalize(NormalizationForm.FormD))
                if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                    b.Append(c >= 32 && c <= 126 ? c : c < 32 ? ' ' : '?');
            return b.ToString();
        }
        public static string Fit(string text, int width)
        { text = Ascii(text); return text.Length <= width ? text : width <= 3 ? text.Substring(0, width) : text.Substring(0, width - 3) + "..."; }
        public static string Time(TimeSpan? time, int decimals)
        {
            if (!time.HasValue) return "--";
            decimal scale = decimals == 2 ? 100 : 10;
            decimal units = Math.Round((decimal)time.Value.Ticks / TimeSpan.TicksPerSecond * scale, 0, MidpointRounding.AwayFromZero);
            string sign = units < 0 ? "-" : ""; units = Math.Abs(units);
            long seconds = (long)(units / scale), fraction = (long)(units % scale);
            string body = seconds >= 3600 ? (seconds / 3600) + ":" + (seconds / 60 % 60).ToString("00") + ":" + (seconds % 60).ToString("00")
                : (seconds / 60) + ":" + (seconds % 60).ToString("00");
            return sign + body + "." + fraction.ToString(decimals == 2 ? "00" : "0");
        }
        public static string Delta(TimeSpan? time)
        {
            if (!time.HasValue) return "--";
            decimal v = Math.Round((decimal)time.Value.Ticks / TimeSpan.TicksPerSecond, 1, MidpointRounding.AwayFromZero);
            return v == 0 ? "+/-0.0" : (v < 0 ? "-" : "+") + Math.Abs(v).ToString("0.0", CultureInfo.InvariantCulture);
        }
        public static IEnumerable<string> Wrap(string text, int width)
        {
            string line = "";
            foreach (string word in Ascii(text).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string rest = word;
                if (line.Length > 0 && line.Length + 1 + rest.Length > width) { yield return line; line = ""; }
                while (rest.Length > width) { yield return rest.Substring(0, width); rest = rest.Substring(width); }
                line += (line.Length == 0 ? "" : " ") + rest;
            }
            if (line.Length > 0) yield return line;
        }
    }
}
