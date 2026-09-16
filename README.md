# Thermal Run Receipt for LiveSplit

Prints one native ESC/POS receipt when a newly observed LiveSplit attempt reaches its final split. Targets **Windows LiveSplit 1.8.37 / .NET Framework 4.8.1**, using a Windows RAW queue and an **ESC/POS receipt printer**. Printing is disabled and the printer queue is empty on first use. The known-good MJ-5890K / POS-58 profile remains the baseline.

## Install and use

1. Close LiveSplit. Copy `dist/Components/LiveSplit.ThermalReceipt.dll` into the `Components` directory beside your **LiveSplit.exe**. The release ZIP has this directory structure already. Do not copy the development `vendor` or `build` folders into your installation.
2. Start LiveSplit. Right-click it, choose **Edit Layout**, then **+ → Other → Thermal Run Receipt**.
3. Open this component's **Layout Settings**. Explicitly select your receipt printer's Windows queue (or type its exact name). **Refresh printer queues** updates the list without selecting anything. Do not select an ordinary office/document printer or a PDF queue: this component sends raw ESC/POS commands, not Windows document pages. It never falls back to the Windows default printer. An empty queue disables Test Receipt and blocks automatic submission.
4. Choose a **Printer profile** using the table below. Keep automatic printing disabled. Press **Print Test Receipt** to explicitly submit **one** synthetic receipt. This works even while automatic printing is disabled. It uses the production renderer and selected queue.
5. Check **Enable receipt printing**, close settings, and **Save Layout**. Start a new attempt. The final split submits its receipt in the background.

Completed runs print automatically by default once receipt printing is enabled. To approve each receipt first, check **Confirm before printing completed runs** and **Save Layout**. The prompt asks, “Would you like to print your Run Receipt?” Choose **Yes** to print or **No** to skip that receipt. A skipped receipt will not prompt again on undo/re-finish. **Print Test Receipt** remains a direct, explicit test action without an extra prompt.

Resets, partial attempts, undo notifications, initialization, and loading a finished timer do not print. If the component is added/reloaded partway through an attempt, printing begins with the **next** attempt, because a trustworthy start baseline is unavailable for the current one. Undoing and re-finishing an already printed attempt never submits another receipt.

The settings status reports queued/sent/failure. “Sent to Windows spooler” means the RAW job was accepted, not that the printer physically finished. There is no automatic retry. Logs are at `%LOCALAPPDATA%\LiveSplit\ThermalReceipt\receipt.log` (1 MiB rollover).

## Printer profiles and setup

**Warning:** Choosing the wrong printer profile may result in a lot of wasted paper. Check your printer's capabilities before using **Print Test Receipt**.

| Profile | Font A / B columns | Border width | Defaults |
| --- | --- | --- | --- |
| POS-58 / 58mm Compatibility | 32 / 42 | 320 dots | Known-good MJ-5890K; reverse text and small standalone 24-dot borders |
| Generic 58mm ESC/POS | 32 / 42 | 384 dots | Text separators; no reverse or graphics assumed |
| Generic 80mm ESC/POS | 48 / 64 | 576 dots | Same receipt with longer split names; text separators |
| Custom | Configurable | Configurable | Conservative until capabilities are explicitly selected |

Generic profiles are compatibility-oriented starting points, **not guarantees for every ESC/POS clone**. Firmware, Font B metrics and printable area vary even among printers sold with the same paper width. Install the Windows driver and identify the correct queue yourself; the component does not detect printer models. Test alignment and paper width before enabling automatic receipts.

Custom exposes printable dots (192–832), Font A columns (24–96), Font B columns (32–128, at least Font A), reverse support, standalone 24-dot border support, cutter support and Cut after receipt. Character widths are measured printable columns, not paper millimeters; use your printer manual and a test receipt to set them. The dot width controls only the small border, not font metrics. Do not enable 24-dot graphics merely because paper is wider. Cutter support means the printer accepts GS V 0; enable cutting separately only if your hardware supports it. Raster and Unicode capability metadata exist in the model, but output stays ASCII and never uses raster graphics, even on capable printers.

If reverse or border graphics are unsupported, leave them unchecked: bold PB treatment and text section/fortune separators retain the receipt hierarchy. Numeric split columns keep their sensible widths; additional space goes to split names. Rows truncate instead of wrapping.

**Save Layout** persists the queue, profile, Custom values and enable state. Existing layouts retain their saved queue and use POS-58 when no profile is present; older settings cannot distinguish a previously prefilled queue from an explicitly chosen one, so check the saved queue when upgrading. New or missing queue settings remain empty. An unknown profile disables automatic printing pending setup. Refreshing queues preserves a saved queue even if temporarily disconnected; a missing queue reports failure and never redirects to another printer.
## Build and test

The canonical build uses Windows' installed .NET Framework C# compiler. No .NET SDK, NuGet package restore, or printer is required. .NET Framework 4.8.1 and the LiveSplit release assemblies must be installed/available.

From this folder in PowerShell:

```powershell
# One-time download of official, SHA-256-pinned LiveSplit 1.8.37:
.\bootstrap.ps1

# Compile component, compile tests, run all tests, and package:
.\build.ps1

# Or use your already-extracted LiveSplit release:
.\build.ps1 -LiveSplitPath 'C:\Tools\LiveSplit'
```

Outputs:

- `dist/Components/LiveSplit.ThermalReceipt.dll` — the only DLL to install.
- `dist/LiveSplit.ThermalReceipt-1.1.0.zip` — component plus installation/engineering notes.
- `build/ThermalReceipt.Tests.exe` — framework test executable using fake printers.
- `build/test-results.txt` — latest automated results.
- `build/test-receipt.txt` — extracted receipt text for inspection; native styles/borders are not represented.
- `build/test-receipt.bin` — ESC/POS debug artifact; it is never automatically sent anywhere.

`-SkipTests` is available for a compilation-only iteration. Run without it before release. The optional SDK project `src/LiveSplit.ThermalReceipt.csproj` supports IDE use with a .NET SDK and the 4.8.1 Developer Pack; the no-SDK PowerShell build is the validated release path. Test scripts never invoke `WindowsRawPrinter.Print` or enumerate actual printer queues.

## Receipt behavior

Uses the newer Markdown handoff's text-only design: startup rule, game/category, large completed time, native reverse PB/tie callout, conditional context, Font B split table, metadata, and two small standalone `ESC *` fortune borders. No large raster, custom glyph, code-page, or inline-image commands exist. Profiles without reverse or 24-dot support use text fallbacks. Cutting requires both Custom cutter support and an explicit Cut after receipt opt-in.

PB context uses the record at attempt start. Split deltas use the comparison selected at completion. Recent averages use up to nine previous completed LiveSplit attempts plus the current result. Missing selected-method times remain `--`; they never silently become times from another timing method. Golds use LiveSplit's best-segment test. Skipped rows remain visible; the immediately following segment duration is unavailable instead of showing a combined duration.

**Print fortunes** is on by default in component settings. Uncheck it to remove the fortune text and both surrounding borders (including text fallback borders) from completed runs and Test Receipt. **Save Layout** preserves your choice. The receipt's closing rule and tear feed remain.

Fortunes cycle through a shuffle bag, independently of performance. Turning fortunes off pauses the bag without consuming entries. The remaining bag is serialized in the layout settings. **Save Layout** checkpoints the bag; reloading an older unsaved layout can restore an older bag position. There is no separate run-performance database.

## Physical validation still required

Automated validation and component-loader smoke checks do not print. On your MJ-5890K, use the manual test once to check reverse text, Font B alignment, the small geometric borders, feed/tear distance, and queue behavior. Then test a real completion, reset, and undo/re-finish. The compatibility profile is checked byte-for-byte against the previous working renderer. This update has not been physically printed during development; generic profiles and custom widths still require validation on the target printer.

For transport trouble, check the component log and Windows queue before deliberately printing again. The component never clears the queue, retries a possibly printed receipt, or modifies the run to recover from printer errors.


## Archive Reprint

Open **Layout Settings → ARCHIVE / RECEIPT HISTORY**. Choose **REAL TIME** or **GAME TIME**, select a completed attempt, and press **Print Selected Receipt**. The list shows local completion date/time, final time and LiveSplit history attempt ID. Refresh reloads history; LiveSplit normally adds a finished attempt to history when the timer is reset. Selecting or refreshing never prints. Archive printing works with automatic printing disabled, requires an explicit queue, and uses the current printer profile and fortune setting.

Receipts say **ARCHIVE REPRINT**, use **VS PB** against the earliest retained attempt with the fastest earlier final time for the chosen method, and show **PB AT RUN** (or **PREVIOUS PB**), historical time difference, and up to ten completions ending at the selected attempt. Later attempts and current PB/best-segment fields are never calculation inputs. Gold markers compare reconstructable individual durations with earlier dated segment history, including partial attempts. Post-skip combined times are never presented as individual segments or awarded individual golds. Each print selects a fresh fortune through the normal shuffle bag; disabled fortunes consume nothing.

The original stored completion timestamp is labeled RUN; REPRINTED appears for a different local calendar date. Missing completion dates explicitly say RUN DATE UNKNOWN. No receipt database or snapshots are created.

### Historical limits

LiveSplit history is editable, not an immutable audit log. Reconstruction assumes retained attempts describe the same route and that the PB followed the fastest completion in the chosen timing method. Imported/manual PBs, manual best-segment changes, deleted history and original timing-method choices cannot be dated reliably. When both timing values exist, the archive selector explicitly chooses which data to print; it does not claim to know the old UI selection. Game/category/segment names use the current route because old names are not stored.

The first retained completion has unknown prior PB and prints without a PB celebration or time difference. Golds need a known earlier individual duration; missing baselines and merged segments cannot earn an inferred gold. Missing or inconsistent segment history prints unavailable fields, preserving the final time from attempt history. A retained null segment entry identifies a skip; an absent entry does not. Earlier PB split deltas remain unavailable when that PB's cumulative history cannot be recovered.

Historical **Sum of Best is omitted**: LiveSplit imports undated PB/best-segment history and can rewrite/remove old history; the historical branch-aware record cannot generally be recovered reliably. Attempt history IDs are preserved verbatim. These are not always the displayed attempt counter: LiveSplit assigns history IDs independently of AttemptCount and does not retain the original displayed count per attempt. No fabricated count, timestamp, original fortune, or current-stat fallback is used.
