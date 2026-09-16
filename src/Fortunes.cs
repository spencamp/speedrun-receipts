using System;
using System.Collections.Generic;
using System.Linq;

namespace LiveSplit.ThermalReceipt
{
    public sealed class FortuneBag
    {
        public static readonly string[] Pool = {
            "A new PB is on the horizon.", "Your goals will come to you naturally.",
            "A series of bad runs will be followed by an amazing one.",
            "The segment giving you trouble will soon feel easy.",
            "One small adjustment will save more time than expected.",
            "A familiar route still has something to teach you.",
            "A slow practice session can produce a fast run.",
            "The next breakthrough will happen before the timer starts.",
            "Consistency is about to become speed.", "The segment you avoid practicing knows.",
            "The time you are looking for is closer than it seems.",
            "Practice the part you keep hoping will go right." };
        private readonly Random random = new Random();
        private readonly Queue<int> remaining = new Queue<int>();
        public string Save() { return String.Join(",", remaining); }
        public void Load(string saved)
        {
            remaining.Clear(); var seen = new HashSet<int>();
            foreach (string value in (saved ?? "").Split(',')) { int index; if (Int32.TryParse(value, out index) && index >= 0 && index < Pool.Length && seen.Add(index)) remaining.Enqueue(index); }
        }
        public string Next()
        {
            if (remaining.Count == 0)
            {
                int[] bag = Enumerable.Range(0, Pool.Length).ToArray();
                for (int i = bag.Length - 1; i > 0; i--) { int j = random.Next(i + 1); int t = bag[i]; bag[i] = bag[j]; bag[j] = t; }
                foreach (int i in bag) remaining.Enqueue(i);
            }
            return Pool[remaining.Dequeue()];
        }
    }
    public static class SyntheticReceipt
    {
        public static ReceiptRun Create()
        {
            Func<double, TimeSpan> t = TimeSpan.FromSeconds;
            return new ReceiptRun("SUPER MARIO 64", "16 STAR - NO MAJOR GLITCHES", t(1122.37), t(1135.62), t(1102.3), t(1101.6),
                "Personal Best", "GAME TIME", 1284, DateTime.Now, new[] {
                    new ReceiptSplit("Lakitu Skip", t(102.3), t(102.3), t(-0.4), false, false),
                    new ReceiptSplit("Whomp's Fortress", t(121.8), t(224.1), t(0.2), false, false),
                    new ReceiptSplit("Dark World", t(97.2), t(321.3), t(-1.1), false, true),
                    new ReceiptSplit("Fire Sea", t(114.7), t(436), t(0), false, false),
                    new ReceiptSplit("Basement Bunny", null, null, null, true, false),
                    new ReceiptSplit("Ridiculously Long Split Name", null, t(617.1), t(-4), false, false),
                    new ReceiptSplit("DDD", t(69.9), t(687), t(-3.6), false, false),
                    new ReceiptSplit("Fire Sea Re-entry", t(71.2), t(758.2), t(-5.2), false, true),
                    new ReceiptSplit("BLJs", t(134.8), t(893), t(-7.8), false, false),
                    new ReceiptSplit("Bowser in the Sky", t(229.37), t(1122.37), t(-13.25), false, false)
                }, new[] { t(1122.37), t(1135.62), t(1170) }, "A series of bad runs will be followed by an amazing one.");
        }
    }
}
