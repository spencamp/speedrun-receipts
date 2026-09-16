# LiveSplit Thermal Receipt Printer — Technical Handoff / README

## Purpose

This document captures the product decisions, printer behavior, ESC/POS discoveries, failure modes, and implementation requirements learned while prototyping a LiveSplit component that automatically prints a physical speedrun receipt whenever a run is completed.

The goal is for another engineer or AI coding agent to be able to pick this up and build the real LiveSplit integration without repeating the hardware experiments.

---

# 1. Project Goal

Build a LiveSplit component for Windows that automatically prints a receipt when a speedrun is completed.

The receipt should feel like:

> An official physical record of one completed run, with a little personality.

It should **not** feel like:

- a generic retail receipt
- a terminal dump
- an analytics report
- a casino ticket
- a novelty printout
- a full dashboard rendered onto paper

The receipt is meant to become part of the ritual of finishing a run.

**Important product rule:** do **not** print receipts for resets or incomplete runs. The printer firing should mean the runner actually completed the run.

---

# 2. Hardware / Windows Environment

## Printer

- Model: **MJ-5890K**
- Cheap 58 mm thermal receipt printer
- USB connection
- ESC/POS-compatible enough for common commands
- No cutter assumed
- Print width used successfully: approximately 32 Font-A characters or approximately 42 Font-B characters
- The printer appears to have a narrow memory/input buffer and incomplete ESC/POS compatibility. Treat it as a low-cost clone, not as a fully compliant Epson device.

## Windows print queue

Known working queue:

```text
Thermal Printer POS-58
```

Known driver:

```text
POS-58 11.3.0.1
```

Known port:

```text
USB001
```

USB hardware seen by Windows:

```text
USB\VID_0416&PID_5011\PRINTER
```

Windows may also expose:

```text
USBPRINT\UNKNOWNPRINTER\...\USB001
```

That did not prevent successful printing.

---

# 3. Recommended Architecture

The desired architecture is:

```text
LiveSplit
  ↓
C# LiveSplit component DLL
  ↓
receipt data model
  ↓
ESC/POS byte builder
  ↓
Windows RAW print job
  ↓
Thermal Printer POS-58
  ↓
USB001
  ↓
MJ-5890K
```

A separate background application is not required.

The component can send RAW ESC/POS bytes directly through the Windows spooler using `winspool.drv`.

Do **not** fork LiveSplit unless some unrelated integration issue forces it. The intended solution is a normal LiveSplit component.

---

# 4. Windows RAW Printing

A RAW Windows spooler path was tested successfully.

The core approach is:

1. `OpenPrinter`
2. `StartDocPrinter` with datatype `RAW`
3. `StartPagePrinter`
4. `WritePrinter`
5. `EndPagePrinter`
6. `EndDocPrinter`
7. `ClosePrinter`

Minimal C# pattern:

```csharp
[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
public class DOC_INFO_1
{
    [MarshalAs(UnmanagedType.LPStr)]
    public string pDocName;

    [MarshalAs(UnmanagedType.LPStr)]
    public string pOutputFile;

    [MarshalAs(UnmanagedType.LPStr)]
    public string pDatatype;
}

[DllImport("winspool.drv", SetLastError = true, CharSet = CharSet.Ansi)]
static extern bool OpenPrinter(
    string pPrinterName,
    out IntPtr phPrinter,
    IntPtr pDefault);

[DllImport("winspool.drv", SetLastError = true)]
static extern bool ClosePrinter(IntPtr hPrinter);

[DllImport("winspool.drv", SetLastError = true, CharSet = CharSet.Ansi)]
static extern bool StartDocPrinter(
    IntPtr hPrinter,
    int Level,
    [In] DOC_INFO_1 pDocInfo);

[DllImport("winspool.drv", SetLastError = true)]
static extern bool EndDocPrinter(IntPtr hPrinter);

[DllImport("winspool.drv", SetLastError = true)]
static extern bool StartPagePrinter(IntPtr hPrinter);

[DllImport("winspool.drv", SetLastError = true)]
static extern bool EndPagePrinter(IntPtr hPrinter);

[DllImport("winspool.drv", SetLastError = true)]
static extern bool WritePrinter(
    IntPtr hPrinter,
    byte[] pBytes,
    int dwCount,
    out int dwWritten);
```

Use:

```csharp
doc.pDatatype = "RAW";
```

Do not render through GDI/Windows printer layout APIs if it can be avoided. The successful prototype sends ESC/POS bytes directly.

---

# 5. Major Printer Compatibility Discovery

## Do not render the entire receipt as one large bitmap

This was tested and failed badly.

A full receipt was rasterized and sent using an ESC/POS raster command (`GS v 0`). The printer initially rendered correctly, then lost synchronization and began printing raw image bytes as visible characters. This produced an extremely long receipt containing garbage characters.

Observed behavior:

- top of the bitmap looked correct
- printer later began dumping binary data as text
- printer occasionally resynchronized and rendered later content
- receipt became several feet long

Likely cause:

- clone printer firmware / small buffer / incomplete raster implementation
- large raster payload destabilized the command stream

**Rule: never send the complete receipt as a large raster image on this printer.**

---

# 6. Proven-Safe Rendering Strategy

The stable strategy is:

## Use native ESC/POS text for almost everything

Use printer-native text for:

- game
- category
- final time
- PB treatment
- summary stats
- split table
- metadata
- fortune text

## Use only very small 24-dot graphics when absolutely necessary

The old ESC/POS `ESC *` 24-dot image mode worked reliably for a small decorative strip.

A test consisting of:

```text
SHORT IMAGE TEST
[small geometric strip]
END OF TEST
```

printed successfully.

The final prototype safely uses `ESC *` graphics only for the two decorative borders around the fortune.

Do not use inline image tricks inside text rows.

Do not use large raster graphics.

---

# 7. ESC/POS Commands Proven Useful

## Initialize

```text
ESC @
1B 40
```

## Left align

```text
ESC a 0
1B 61 00
```

## Center

```text
ESC a 1
1B 61 01
```

## Bold on/off

```text
ESC E 1
1B 45 01

ESC E 0
1B 45 00
```

## Reverse / white-on-black text

This works and is important.

```text
GS B 1
1D 42 01

GS B 0
1D 42 00
```

This is preferred over drawing a black bitmap.

## Character size

Normal:

```text
GS ! 0
1D 21 00
```

Double width + double height:

```text
GS ! 0x11
1D 21 11
```

## Font A

```text
ESC M 0
1B 4D 00
```

Approximately 32 characters across the receipt.

## Font B

```text
ESC M 1
1B 4D 01
```

Approximately 42 characters across.

Font B was extremely useful for the split table because it allows substantially longer split names before truncation.

---

# 8. 24-Dot Image Mode

The safe graphic technique is:

```text
ESC * m nL nH [data]
```

The tested mode was:

```text
m = 33
```

This is 24-dot double-density mode.

Before printing the image:

```text
ESC 3 24
```

After the image:

```text
LF
ESC 2
```

The successful fortune-border implementation used:

- width: 320 dots
- height: 24 dots
- 3 bytes vertically per x-coordinate
- small zigzag / geometric pattern
- only ~960 image data bytes per border

This was stable.

---

# 9. Features That Were Tried and Should NOT Be Used

## 9.1 Full-receipt bitmap / `GS v 0`

**Do not use.**

Caused printer command-stream desynchronization and enormous garbage receipts.

---

## 9.2 User-defined custom characters (`ESC &`)

A custom star glyph was attempted.

It failed.

The printer did not interpret the custom-character definition correctly. Glyph-definition bytes leaked out as visible garbage near the beginning of the receipt, and the print later stopped.

**Do not rely on user-defined characters on this device.**

---

## 9.3 Inline bitmap star inside a text row

A tiny `ESC *` star inserted directly before a split name was attempted.

Windows reported:

```text
Error printing on Thermal Printer POS-58
The printer couldn't print Inline Star Test
```

A plain-text recovery print immediately afterward worked.

Conclusion:

**Do not interleave bitmap commands inside split-table text rows.**

The standalone fortune borders are safe; inline row graphics are not.

---

## 9.4 Random code-page probing

An attempt was made to search printer code pages for a native star.

Some candidate bytes were below `0x20`, meaning they were control characters rather than printable symbols. This made the output overlap and behave unpredictably.

Do not send arbitrary byte values in search of characters.

For V1, gold splits simply use:

```text
*
```

This is stable, readable, and acceptable.

---

# 10. Final V1 Receipt Design

This is the approved receipt hierarchy:

```text
sacrificial top line

GAME
CATEGORY

[final time OR PB callout]

run summary stats

████████████ SPLITS ████████████
split table
████████████████████████████████

metadata

geometric fortune border

fortune text

geometric fortune border

closing line
blank tear feed
```

---

# 11. Top Startup Line

The printer does not always print perfectly at the exact beginning of a feed.

Therefore every receipt should start with a sacrificial line:

```text
--------------------------------
```

Then one blank line.

If the first millimeters are faint or clipped, only this disposable rule is affected.

This is intentional and should remain part of the design.

---

# 12. Masthead

No image.

Earlier versions included Mario / character artwork, but it consumed too much vertical space and introduced unnecessary complexity.

The final V1 masthead is text only.

Example:

```text
SUPER MARIO 64
16 STAR - NO MAJOR GLITCHES
```

Rules:

- game gets its own line
- category gets its own line
- game may be bold
- category normal weight
- truncate only as a last resort
- do not aggressively shrink fonts
- do not print runner name
- do not print split-file filename
- do not print LiveSplit branding
- do not print software/component branding

---

# 13. Final Time

The giant time always represents **the run that just finished**.

This is true whether the run is:

- a PB
- tied PB
- slower than PB

The receipt is fundamentally a record of this run.

---

# 14. New PB Treatment

If the completed run is a new PB:

```text
NEW PB 18:42.37
```

Print as:

- centered
- double width + double height
- bold
- reverse white-on-black

Important printer-width constraint:

At double width the printer supports roughly 16 normal Font-A character cells.

The string:

```text
NEW PB 18:42.37
```

is 15 characters and fits.

Do **not** add leading/trailing padding spaces. An earlier version used:

```text
 NEW PB 18:42.37 
```

which was 17 characters and wrapped one black block onto the next line.

---

# 15. PB Tie Treatment

An exact tied PB should be celebrated exactly like a new PB.

Use:

```text
PB TIED 18:42.37
```

Same visual treatment:

- reverse
- bold
- double size
- centered

A tied PB is considered a special event.

---

# 16. Normal Run Treatment

A non-PB run should **not** have a box or decorative frame around the time.

Use:

- large centered time
- whitespace
- no reverse block

This preserves contrast: the black PB/tie block feels special because normal runs are clean.

---

# 17. PB Context Semantics

The receipt must compare the completed run against the PB that existed **when the run started**.

This is important for historical truth.

## New PB receipt

Use:

```text
PREVIOUS PB       18:55.62
TIME DIFFERENCE      -13.3
```

## Exact tied PB

Use:

```text
PB                18:42.37
TIME DIFFERENCE      +/-0.0
```

Do not label it `PREVIOUS PB`, because the PB was not replaced by a different time.

## Normal run

Use:

```text
PB                18:42.37
TIME DIFFERENCE      +26.3
```

---

# 18. TIME DIFFERENCE

`TIME DIFFERENCE` is always:

```text
completed run final time - PB at run start
```

Examples:

```text
-13.3
+26.3
+/-0.0
```

Negative means faster than the prior PB.

Positive means slower.

This stat is always relative to PB, regardless of which comparison LiveSplit was displaying for individual splits.

---

# 19. Summary Stats Block

The summary block appears immediately after the final-time treatment and before the split table.

It has **no heading**.

Example:

```text
PREVIOUS PB             18:55.62
TIME DIFFERENCE            -13.3
10-RUN AVERAGE           19:08.4
GOLD SPLITS                     2
SUM OF BEST       18:21.6 (-0.7)
```

Do not add a label like:

```text
RUN STATS
```

That makes the receipt feel too dashboard-like.

---

# 20. Precision Rules

Use a deliberate precision hierarchy.

## Hundredths

Use full hundredths for:

- headline final time
- PB / Previous PB

Example:

```text
18:42.37
```

## Tenths

Use tenths for contextual / dense information:

- split segment time
- cumulative split time
- split delta
- TIME DIFFERENCE
- recent-run average
- Sum of Best
- Sum of Best improvement

Examples:

```text
1:42.3
18:42.4
-13.3
19:08.4
```

This makes the narrow receipt substantially easier to scan.

---

# 21. Recent Completed-Run Average

Every receipt should show a recent completed-run average when at least two completed runs are available.

Source of truth should be LiveSplit historical attempt data.

The average includes the run that just finished.

Rules:

```text
1 completed run   -> omit average entirely
2 completed runs  -> 2-RUN AVERAGE
3 completed runs  -> 3-RUN AVERAGE
...
9 completed runs  -> 9-RUN AVERAGE
10+ completed     -> 10-RUN AVERAGE
```

For 10+ runs, average only the 10 most recent **completed** runs.

Do not count resets.

Do not maintain a separate printer-specific run database if LiveSplit history already contains the needed runs.

Do not celebrate a “best 10-run average.” The value is context only.

---

# 22. Gold Splits

A gold split means the run produced a new best segment.

Behavior:

- prepend `* ` to the split name
- count all golds
- print `GOLD SPLITS N` only when `N > 0`

Example:

```text
* Dark World
```

V1 deliberately uses `*`, not `★`.

A real star was explored but printer compatibility was poor. Stability wins.

Gold splits are supporting information. They do not change the top-level headline unless the run itself is a PB/tie.

---

# 23. Sum of Best

Only print `SUM OF BEST` if the run actually improved Sum of Best.

Example:

```text
SUM OF BEST       18:21.6 (-0.7)
```

The first value is the **new** Sum of Best.

The parenthetical value is how much it improved.

Do not print Sum of Best on every receipt.

Implementation requires preserving the pre-run Sum of Best so the improvement can be calculated after the run updates best-segment data.

---

# 24. Splits Section

The split table is introduced by a full-width reverse bar:

```text
████████████ SPLITS ████████████
```

Use printer-native reverse mode rather than graphics.

The approved column concepts are:

```text
SPLIT | SEG | TIME | VS [comparison]
```

Definitions:

- `SPLIT` = split/segment name
- `SEG` = actual duration of this individual segment
- `TIME` = cumulative run time at this split
- `VS ...` = delta against the active LiveSplit comparison

---

# 25. Split Comparison

The final column should identify the comparison directly in the table header.

Example:

```text
VS PB
```

or:

```text
VS AVG
```

Because the comparison is visible there, do **not** repeat:

```text
COMPARISON: PERSONAL BEST
```

in the metadata block.

That metadata line was removed as redundant.

For the rare case where the runner changes comparison during a run, do not over-engineer it. Use the comparison active when the run finishes and calculate the receipt consistently against that comparison.

---

# 26. Font B for the Split Table

This was an important physical-layout improvement.

The first version used Font A and aggressively truncated names such as:

```text
Basemen...
*Fire S...
Bowser ...
```

Switching the table to Font B gives roughly 42 character columns and allowed normal names such as:

```text
Whomp's Fortress
Basement Bunny
Fire Sea Re-entry
Bowser in the Sky
```

to fit successfully.

Final prototype table used roughly:

```text
NAME:    20 chars
SEG:      6 chars
TIME:     7 chars
DELTA:    6 chars
```

plus spaces.

Example formatting concept:

```text
SPLIT                    SEG    TIME  VS PB
------------------------------------------
  Lakitu Skip          1:42.3  1:42.3   -0.4
  Whomp's Fortress     2:01.8  3:44.1   +0.2
* Dark World           1:37.2  5:21.3   -1.1
```

---

# 27. Split Name Truncation

Never wrap split rows.

Long names should truncate with:

```text
...
```

Truncation should be a last resort.

An intentionally ridiculous test name correctly became something like:

```text
Ridiculously Lo...
```

This is acceptable.

Normal real-world split names should generally fit in Font B.

---

# 28. Split Edge Cases

## Exact delta tie

Desired conceptual display:

```text
±0.0
```

However the current safe ASCII implementation uses:

```text
+/-0.0
```

This is acceptable for V1.

Do not risk printer instability just to get the Unicode ± glyph.

## Skipped split

Do not omit the row.

Use:

```text
SKIP
```

for the segment-duration field.

Example:

```text
Basement Bunny       SKIP    8:48.8   -2.7
```

## Segment duration unavailable after skip

Do not fabricate a duration spanning multiple segments.

Use:

```text
--
```

for the individual segment duration.

Cumulative time and delta may still be printed if known.

## Final split

No special table formatting.

It should look like every other split unless it is a gold split.

---

# 29. Split Table Closing Bar

After the final split:

- blank line
- full-width unlabeled reverse bar

This visually closes the detailed run record.

Then metadata begins.

This worked well physically and should remain.

---

# 30. Metadata

Metadata lives below the split table in a compact left-aligned block.

Example:

```text
ATTEMPT #1,284
SEP 15 2026 - 7:42 PM
GAME TIME
```

Rules:

- left-aligned
- small / normal Font A
- no runner name
- no receipt number
- no start time
- no completion number
- no component branding
- no LiveSplit branding
- no split filename
- no duplicate comparison line

---

# 31. Attempt Number

Use LiveSplit's attempt number.

Example:

```text
ATTEMPT #1,284
```

Do not invent a separate receipt ID.

Do not print a separate “completion number.”

---

# 32. Completion Timestamp

Print the exact date and time the final split was triggered.

Example:

```text
SEP 15 2026 - 7:42 PM
```

The semantic timestamp should be:

> moment the final split completed the run

not:

> time Windows happened to send the print job

Minute-level precision is sufficient.

---

# 33. Timing Method

Print the timing method because it changes the meaning of the headline time.

Examples:

```text
GAME TIME
REAL TIME
```

Keep it small in metadata.

---

# 34. Fortune Section

The fortune is the personality layer at the very end of the receipt.

It should be visually separated from the official run record.

Structure:

```text
/\/\/\/\/\/\/\/\/\/\/\/\/\/\/\/\

A SERIES OF BAD RUNS WILL BE
FOLLOWED BY AN AMAZING ONE.

/\/\/\/\/\/\/\/\/\/\/\/\/\/\/\/\
```

The tested implementation uses a small geometric zigzag rendered with safe 24-dot `ESC *` graphics.

There is no label such as:

```text
FORTUNE:
```

---

# 35. Fortune Tone

Fortunes should feel:

- like fortune-cookie predictions
- understated
- slightly mysterious
- speedrunning-aware
- playful
- not corny
- not congratulatory boilerplate
- not motivational-poster copy

Examples:

```text
A new PB is on the horizon.

Your goals will come to you naturally.

A series of bad runs will be followed by an amazing one.

The segment giving you trouble will soon feel easy.

One small adjustment will save more time than expected.

A familiar route still has something to teach you.

A slow practice session can produce a fast run.

The next breakthrough will happen before the timer starts.

Consistency is about to become speed.

The segment you avoid practicing knows.
```

Maximum target length: roughly 3 printed lines.

The final tested fortune used 2 lines.

---

# 36. Fortune Selection

Recommended behavior: shuffle bag.

A shuffle bag means:

1. shuffle the full fortune pool
2. use each fortune once
3. do not repeat until all have been used
4. reshuffle when exhausted

The fortune should not depend on run performance.

That makes it feel like a genuine fortune rather than a post-run evaluation.

---

# 37. No Character Images

Earlier designs planned:

- fixed Mario image at top
- rotating character image near fortune

These were removed.

Reasons:

- too much vertical space
- adds raster complexity
- receipt looked cleaner without them
- text itself has enough personality
- hardware is more stable when graphics are minimized

V1 should contain **no character artwork**.

---

# 38. Bottom Closing Line / Tear Feed

After the fortune:

1. geometric bottom border
2. blank line
3. sacrificial/closing horizontal line
4. several line feeds

Example:

```text
--------------------------------



```

The extra feed provides enough blank paper for a clean manual tear.

No cutter command is required.

---

# 39. Triggering Logic

Print **only** when the run reaches its final split and is genuinely completed.

Do not print on:

- reset
- undo split
- aborted run
- incomplete attempt
- layout reset
- timer initialization

The physical print should be semantically equivalent to:

```text
Run completed.
```

---

# 40. LiveSplit Data Requirements

The receipt renderer needs a snapshot containing at least:

```csharp
class ReceiptRunData
{
    string GameName;
    string CategoryName;

    TimeSpan FinalTime;
    TimeSpan? PreRunPersonalBest;
    TimeSpan? FinalVsPreRunPb;

    string TimingMethod;

    int AttemptNumber;
    DateTime FinishedAt;

    string ActiveComparisonName;
    string ActiveComparisonShortName;

    IReadOnlyList<ReceiptSplitData> Splits;

    int GoldSplitCount;

    TimeSpan? PreviousSumOfBest;
    TimeSpan? NewSumOfBest;

    IReadOnlyList<TimeSpan> RecentCompletedRuns;

    string Fortune;
}
```

Split data should include:

```csharp
class ReceiptSplitData
{
    string Name;

    TimeSpan? SegmentTime;
    TimeSpan? CumulativeTime;

    TimeSpan? ComparisonDelta;

    bool WasSkipped;
    bool WasGold;
}
```

---

# 41. Important LiveSplit State-Snapshot Problem

PB, gold-split, and Sum-of-Best state can mutate as LiveSplit finishes and later resets/updates the run.

The component must preserve historical truth.

At minimum capture:

## At run start

- PB existing at run start
- Sum of Best existing at run start
- any other baseline needed for post-run comparison

## During / at final split

Capture before LiveSplit destroys or rewrites state:

- completed split times
- segment times
- active comparison
- comparison deltas
- which segments were golds
- final time
- timing method
- attempt number
- completion timestamp

Do not calculate “previous PB” by reading the PB after LiveSplit has already updated it to the new run.

---

# 42. LiveSplit Data Feasibility Notes

During research, the following LiveSplit concepts were identified as available or derivable:

- cumulative split time via split-time data (`SplitTime`)
- actual segment duration can be derived from current and prior cumulative split times; LiveSplit also exposes helpers such as previous-segment calculations
- comparison data / deltas are available
- `CurrentComparison` is available
- timing method is accessible through the current timing method
- raw timing values are `TimeSpan`
- best-segment detection exists in LiveSplit logic (for example through best-segment checking/update logic)

When implementing, verify current LiveSplit APIs against the exact source/version being targeted rather than relying on these names blindly.

---

# 43. Recent Run History

The printer component should use LiveSplit's existing attempt history rather than creating its own independent database.

The goal is for a newly installed printer component to still be able to calculate:

```text
10-RUN AVERAGE
```

from completed runs that occurred before the component was installed.

Only successful completed attempts should be used.

Resets should not enter the average.

---

# 44. Comparison Semantics

There are intentionally two different comparison concepts.

## Run-level comparison

`TIME DIFFERENCE`

Always versus PB that existed at run start.

## Split-level comparison

`VS PB`, `VS AVG`, etc.

Versus the comparison that LiveSplit was using.

Do not collapse these into one concept.

This lets a runner use Average Segments or Best Segments during the run while still knowing how the final run compared to their PB.

---

# 45. Receipt Renderer Design Recommendation

Keep the renderer independent of LiveSplit.

Recommended architecture:

```text
LiveSplit state
      ↓
ReceiptRunData
      ↓
ReceiptFormatter
      ↓
EscPosWriter
      ↓
byte[]
      ↓
RawPrinter
```

Example interfaces:

```csharp
public interface IReceiptRenderer
{
    byte[] Render(ReceiptRunData run);
}

public interface IReceiptPrinter
{
    void Print(byte[] data);
}
```

Benefits:

- renderer can be unit-tested
- LiveSplit integration can be tested separately
- real printer not required for every test
- receipt snapshots can be saved as `.bin` for regression testing
- edge cases can be generated programmatically

---

# 46. ESC/POS Writer Recommendation

Do not scatter raw byte literals across business logic.

Use a small abstraction:

```csharp
sealed class EscPosWriter
{
    private readonly MemoryStream _stream = new();

    public void Initialize();
    public void AlignLeft();
    public void AlignCenter();

    public void Bold(bool value);
    public void Reverse(bool value);

    public void FontA();
    public void FontB();

    public void NormalSize();
    public void DoubleSize();

    public void TextAscii(string value);
    public void LineFeed(int count = 1);

    public void Print24DotImage(...);

    public byte[] ToArray();
}
```

The component should make printer capabilities explicit rather than assuming full ESC/POS support.

---

# 47. ASCII Safety

The stable receipt currently stays mostly ASCII.

Safe:

```text
*
+/-
--
-
:
#
()
```

Avoid casually sending Unicode through `Encoding.ASCII`, because unsupported characters become `?`.

Do not assume:

```text
★
±
…
```

will work natively.

For V1:

```text
*       instead of ★
+/-0.0  instead of ±0.0
...     instead of …
```

This is a deliberate compatibility decision.

---

# 48. Potential Future Printer Profile System

The component may eventually support more capable printers.

Recommended capability object:

```csharp
record PrinterProfile(
    int FontAColumns,
    int FontBColumns,
    bool SupportsReverse,
    bool Supports24DotImages,
    bool SupportsRasterImages,
    bool SupportsCut,
    bool SupportsUnicode,
    string QueueName);
```

For the MJ-5890K / POS-58 profile:

```text
Font A columns: ~32
Font B columns: ~42
Reverse: yes
24-dot images: yes, standalone small images
Large raster: NO / unsafe
Inline images: unsafe
Custom glyphs: unsafe
Automatic cutter: do not assume
Unicode: do not assume
```

---

# 49. Failure Recovery

If the printer starts producing garbage:

1. stop sending data
2. power printer off
3. wait ~5 seconds
4. power it back on
5. clear Windows print queue if necessary

PowerShell queue clear example:

```powershell
Get-PrintJob -PrinterName "Thermal Printer POS-58" -ErrorAction SilentlyContinue |
    Remove-PrintJob -ErrorAction SilentlyContinue
```

Then send a tiny text-only recovery job before attempting anything more complex.

A successful recovery test used:

```text
RECOVERY TEST

Printer is OK.
```

---

# 50. Debugging Philosophy Learned From Hardware Testing

When debugging this printer:

**Change one transport/rendering variable at a time.**

Good sequence:

1. plain ASCII text
2. alignment
3. bold
4. size
5. reverse
6. Font B
7. one tiny standalone graphic

Do not test:

- multiple new ESC/POS features
- several receipts
- large bitmaps
- custom code pages
- custom glyphs

all at once.

Cheap printer firmware often fails in ways that look like application bugs.

---

# 51. Approved V1 Visual Example

Approximate final receipt:

```text
--------------------------------

SUPER MARIO 64
16 STAR - NO MAJOR GLITCHES

████████████████████████████████
        NEW PB 18:42.37
████████████████████████████████

PREVIOUS PB             18:55.62
TIME DIFFERENCE            -13.3
10-RUN AVERAGE           19:08.4
GOLD SPLITS                     2
SUM OF BEST       18:21.6 (-0.7)

█████████████ SPLITS ███████████

SPLIT                    SEG    TIME  VS PB
------------------------------------------
  Lakitu Skip          1:42.3  1:42.3   -0.4
  Whomp's Fortress     2:01.8  3:44.1   +0.2
* Dark World           1:37.2  5:21.3   -1.1
  Fire Sea             1:54.7  7:16.0 +/-0.0
  Basement Bunny         SKIP  8:48.8   -2.7
  Ridiculously Lo...       -- 10:17.1   -4.0
  DDD                  1:09.9 11:27.0   -3.6
* Fire Sea Re-entry    1:11.2 12:38.2   -5.2
  BLJs                 2:14.8 14:53.0   -7.8
  Bowser in the Sky    3:49.4 18:42.4  -13.3

████████████████████████████████

ATTEMPT #1,284
SEP 15 2026 - 7:42 PM
GAME TIME

/\/\/\/\/\/\/\/\/\/\/\/\/\/\/\/\

A SERIES OF BAD RUNS WILL BE
FOLLOWED BY AN AMAZING ONE.

/\/\/\/\/\/\/\/\/\/\/\/\/\/\/\/\

--------------------------------
```

The actual printer uses reverse native text for the black bars rather than literal block characters.

---

# 52. Conditional Receipt Variants

The renderer needs at least three tested states.

## New PB

Headline:

```text
NEW PB 18:42.37
```

Stats begin with:

```text
PREVIOUS PB
```

## Exact tied PB

Headline:

```text
PB TIED 18:42.37
```

Stats begin with:

```text
PB
TIME DIFFERENCE +/-0.0
```

## Normal run

Headline:

```text
18:42.37
```

Large centered time, no box.

Stats begin with:

```text
PB
TIME DIFFERENCE +...
```

---

# 53. Edge Cases the Automated Test Suite Should Cover

Create synthetic receipt data for:

1. normal completed run
2. new PB
3. exact PB tie
4. one gold split
5. multiple gold splits
6. no gold splits
7. Sum of Best improved
8. Sum of Best unchanged
9. skipped split
10. split after a skip where segment duration is unavailable
11. very long split name
12. very long game name
13. very long category name
14. active comparison = PB
15. active comparison = Average Segments
16. fewer than 10 completed runs
17. exactly 1 historical completed run
18. exactly 2 completed runs
19. 10+ completed runs
20. Game Time
21. Real Time
22. run longer than one hour
23. run longer than 10 hours
24. PB missing / first-ever completed run
25. comparison delta unavailable
26. cumulative split time unavailable
27. many splits / long receipt
28. thousands in attempt number
29. fortune wrapping to 3 lines
30. printer queue unavailable

The renderer should never throw merely because a stat is unavailable. Use omission or `--` according to the documented rules.

---

# 54. First-Ever Run Considerations

The prototype did not fully finalize every first-run semantic.

Recommended implementation:

- if no previous PB exists, omit PB and TIME DIFFERENCE rather than inventing a value
- if only one completed run exists after completion, omit recent-run average
- the completed time itself becomes the PB, but the receipt should not fabricate a `PREVIOUS PB`

If product behavior for first-ever runs becomes important, revisit explicitly.

---

# 55. Avoid Over-Engineering Rare Cases

A deliberate product decision was made not to architect around extremely rare runtime UI behavior.

Example:

> What if the runner repeatedly changes LiveSplit comparison during the run?

V1 approach:

- use the active comparison at completion
- calculate the receipt consistently against that comparison
- move on

The component should be robust, but not become unnecessarily complex because of exotic behavior.

---

# 56. No Extra Analytics

Do not add:

- biggest gain
- biggest loss
- number of splits ahead
- number of splits behind
- completion percentage
- reset statistics
- change versus 10-run average
- best recent-average achievement
- generic “run complete” title
- extra completion number

The receipt should remain an official record of one run, not a printed analytics dashboard.

---

# 57. What Makes the Receipt Feel Special

The product identity comes from:

- physical paper
- the printer firing only on completion
- giant final time
- dramatic reverse PB/tie state
- dense but readable split record
- black section bars
- fortune at the end
- geometric fortune border
- consistent top/bottom lines

It does **not** require character art.

The final physical test confirmed the text-only approach looks better and is more reliable.

---

# 58. Current Status

The polished physical receipt has been printed successfully.

Confirmed working together in one receipt:

- startup line
- game/category
- reverse NEW PB treatment
- summary stat alignment
- reverse SPLITS bar
- Font B split table
- long split names
- `*` gold markers
- SKIP
- `--`
- `+/-0.0`
- closing reverse bar
- compact metadata
- safe geometric fortune borders
- fortune text
- bottom line and feed

This should be treated as the **V1 visual baseline**.

---

# 59. Recommended Next Engineering Step

Stop iterating on PowerShell layout.

Build the actual LiveSplit component around the proven renderer.

Suggested order:

1. Create component skeleton.
2. Detect run start and snapshot pre-run PB / Sum of Best.
3. Detect successful final split.
4. Build immutable `ReceiptRunData`.
5. Pull completed-run history.
6. Calculate recent-run average.
7. Calculate gold count and conditional Sum-of-Best improvement.
8. Format split rows.
9. Render ESC/POS using only proven-safe features.
10. Send RAW to configured Windows printer queue.
11. Add configuration UI.
12. Add synthetic receipt-preview/test button.
13. Add logs and graceful printer failure handling.
14. Test real LiveSplit runs.
15. Only then consider optional support for other printers.

---

# 60. Recommended Component Settings

At minimum expose:

```text
Printer queue:
    Thermal Printer POS-58

Print test receipt:
    [button]

Enable receipt printing:
    [checkbox]
```

Potential later options:

```text
Fortunes enabled
Fortune pool file
Paper width / printer profile
Automatically retry failed print
Debug logging
```

Avoid exposing dozens of visual customization options initially. The receipt design is intended to be opinionated.

---

# 61. Error Handling Requirements

Printing should never crash LiveSplit.

If printing fails:

- catch the exception
- log detailed error
- preserve the completed run
- do not block/reset the timer
- optionally surface a small non-modal component status
- do not repeatedly resend automatically unless retry logic is explicitly designed

A printer failure must remain a peripheral failure, not a speedrun-data failure.

---

# 62. Logging Recommendations

Useful log fields:

```text
run completed
game
category
final time
pre-run PB
new PB / tie / normal
attempt number
split count
gold count
Sum of Best changed?
recent completed-run count
receipt byte length
printer queue
print start timestamp
print result
Windows error code if failed
```

Never dump huge binary ESC/POS buffers into normal logs.

Optionally allow saving the byte stream to disk in debug mode.

---

# 63. Final Principle

The most important lesson from the prototype is:

> Use the printer as a receipt printer, not as a tiny graphics printer.

Native text, native reverse blocks, native font sizing, and one tiny standalone geometric graphic produced a clean, reliable result.

Every attempt to become clever with large rasters, custom glyphs, inline graphics, or undocumented character behavior made the cheap printer less reliable.

For this hardware, simplicity is not merely aesthetic — it is part of the transport protocol.

---

# 64. V1 Design Decisions — Quick Reference

```text
Print on completed runs only                  YES
Print on reset                                NO
Full-receipt bitmap                           NO
Character art                                 NO
QR code                                       NO
Runner name                                   NO
LiveSplit branding                            NO
Receipt number                                NO
Completion number                             NO
Game                                          YES
Category                                      YES
Final time                                    YES
PB / Previous PB                              YES
Time difference                               YES
Recent completed-run average                  YES
Gold split count when >0                      YES
Sum of Best only when improved                YES
All splits                                    YES
Segment duration                              YES
Cumulative time                               YES
Active-comparison delta                       YES
Skipped splits remain visible                 YES
Attempt number                                YES
Finish date/time                              YES
Timing method                                 YES
Comparison repeated in metadata               NO
Fortune                                       YES
Fortune label                                 NO
Geometric fortune border                      YES
Gold marker                                   *
Exact-tie split delta                         +/-0.0
PB tie special treatment                      YES
PB/tie reverse headline                       YES
Normal run headline box                       NO
Font B split table                            YES
```

---

# 65. Known Good Printer Profile Summary

```yaml
printer:
  model: MJ-5890K
  queue: "Thermal Printer POS-58"
  driver: "POS-58 11.3.0.1"
  port: "USB001"
  connection: "USB"
  esc_pos:
    initialize: true
    alignment: true
    bold: true
    reverse: true
    double_size: true
    font_a: true
    font_b: true
    esc_star_24dot_standalone: true
    large_gs_v_0_raster: false
    inline_bitmap_rows: false
    custom_glyphs: false
    unicode: "do not assume"
    cutter: "do not assume"
```

---

# 66. Bottom Line

The hardware path is proven.

The V1 visual system is proven.

The remaining work is software integration:

```text
LiveSplit event/state handling
        +
historical run calculations
        +
immutable receipt data model
        +
the proven ESC/POS formatter
        +
RAW Windows printing
```

Do not redesign the receipt unless real LiveSplit data exposes a concrete issue.

The next project should treat the polished physical receipt as the specification, not as another exploratory prototype.
