using System;

namespace LiveSplit.ThermalReceipt
{
    // Immutable rendering snapshot. Capabilities are separate from output policy:
    // this renderer always uses ASCII and never emits raster images.
    public sealed class PrinterProfile
    {
        public readonly string Id, DisplayName, Notes;
        public readonly int PrintableDots, FontAColumns, FontBColumns;
        public readonly bool SupportsReverse, Supports24DotImages, SupportsRasterImages, SupportsCut, SupportsUnicode;
        public PrinterProfile(string id, string displayName, int dots, int fontA, int fontB,
            bool reverse, bool images, bool cut = false, bool raster = false, bool unicode = false, string notes = "")
        {
            if (dots < 192 || dots > 832 || fontA < 24 || fontA > 96 || fontB < 32 || fontB > 128 || fontB < fontA)
                throw new ArgumentOutOfRangeException("width", "Use 192-832 dots, 24-96 Font A and 32-128 Font B columns (B >= A).");
            Id = id; DisplayName = displayName; PrintableDots = dots; FontAColumns = fontA; FontBColumns = fontB;
            SupportsReverse = reverse; Supports24DotImages = images; SupportsCut = cut;
            SupportsRasterImages = raster; SupportsUnicode = unicode; Notes = notes;
        }
        public override string ToString() { return DisplayName; }
    }
    public static class PrinterProfiles
    {
        public static readonly PrinterProfile Pos58 = new PrinterProfile("pos58", "POS-58 / 58mm Compatibility", 320, 32, 42, true, true,
            notes: "Known-good MJ-5890K: small standalone 24-dot borders. No raster, Unicode or cutter assumed.");
        public static readonly PrinterProfile Generic58 = new PrinterProfile("generic58", "Generic 58mm ESC/POS", 384, 32, 42, false, false,
            notes: "Conservative text fallbacks. Firmware and printable widths vary; test before enabling automatic printing.");
        public static readonly PrinterProfile Generic80 = new PrinterProfile("generic80", "Generic 80mm ESC/POS", 576, 48, 64, false, false,
            notes: "Wider text, conservative text fallbacks. No graphics enabled by paper size alone.");
        public static readonly PrinterProfile Custom = new PrinterProfile("custom", "Custom", 384, 32, 42, false, false,
            notes: "Use your printer manual and test measured widths. Raster and Unicode output remain disabled.");
        public static PrinterProfile[] All { get { return new[] { Pos58, Generic58, Generic80, Custom }; } }
    }
}
