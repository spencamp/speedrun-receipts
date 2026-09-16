# Thermal Run Receipt for LiveSplit

Prints one native ESC/POS receipt when a newly observed LiveSplit attempt reaches its final split. Targets **Windows LiveSplit 1.8.37 / .NET Framework 4.8.1**, with the tested MJ-5890K 58 mm printer profile (32 Font A / 42 Font B cells). Printing is disabled by default.

## Install and use

1. Close LiveSplit. Copy `dist/Components/LiveSplit.ThermalReceipt.dll` into the `Components` directory beside your **LiveSplit.exe**. The release ZIP has this directory structure already. Do not copy the development `vendor` or `build` folders into your installation.
2. Start LiveSplit. Right-click it, choose **Edit Layout**, then **+ → Other → Thermal Run Receipt**.
3. Open this component's **Layout Settings**. Select your Windows printer queue (or type its exact name). The default is `Thermal Printer POS-58`; **Refresh printer queues** refreshes the list.
4. Press **Print Test Receipt** to explicitly submit **one** synthetic receipt. This works even while automatic printing is disabled. It uses the production renderer and selected queue.
5. Check **Enable receipt printing**, close settings, and **Save Layout**. Start a new attempt. The final split submits its receipt in the background.

Resets, partial attempts, undo notifications, initialization, and loading a finished timer do not print. If the component is added/reloaded partway through an attempt, printing begins with the **next** attempt, because a trustworthy start baseline is unavailable for the current one. Undoing and re-finishing an already printed attempt never submits another receipt.

The settings status reports queued/sent/failure. “Sent to Windows spooler” means the RAW job was accepted, not that the printer physically finished. There is no automatic retry. Logs are at `%LOCALAPPDATA%\LiveSplit\ThermalReceipt\receipt.log` (1 MiB rollover).

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
- `dist/LiveSplit.ThermalReceipt-1.0.0.zip` — component plus installation/engineering notes.
- `build/ThermalReceipt.Tests.exe` — framework test executable using fake printers.
- `build/test-results.txt` — latest automated results.
- `build/test-receipt.txt` — extracted receipt text for inspection; native styles/borders are not represented.
- `build/test-receipt.bin` — ESC/POS debug artifact; it is never automatically sent anywhere.

`-SkipTests` is available for a compilation-only iteration. Run without it before release. The optional SDK project `src/LiveSplit.ThermalReceipt.csproj` supports IDE use with a .NET SDK and the 4.8.1 Developer Pack; the no-SDK PowerShell build is the validated release path. Test scripts never invoke `WindowsRawPrinter.Print` or enumerate actual printer queues.

## Receipt behavior

Uses the newer Markdown handoff's text-only design: startup rule, game/category, large completed time, native reverse PB/tie callout, conditional context, Font B split table, metadata, and two small standalone `ESC *` fortune borders. No large raster, custom glyph, code-page, cutter, or inline-image commands exist.

PB context uses the record at attempt start. Split deltas use the comparison selected at completion. Recent averages use up to nine previous completed LiveSplit attempts plus the current result. Missing selected-method times remain `--`; they never silently become times from another timing method. Golds use LiveSplit's best-segment test. Skipped rows remain visible; the immediately following segment duration is unavailable instead of showing a combined duration.

Fortunes cycle through a shuffle bag, independently of performance. The remaining bag is serialized in the layout settings. **Save Layout** checkpoints the bag; reloading an older unsaved layout can restore an older bag position. There is no separate run-performance database.

## Physical validation still required

Automated validation and component-loader smoke checks do not print. On your MJ-5890K, use the manual test once to check reverse text, Font B alignment, the small geometric borders, feed/tear distance, and queue behavior. Then test a real completion, reset, and undo/re-finish. The exact new implementation's command stream has not yet been physically tested.

For transport trouble, check the component log and Windows queue before deliberately printing again. The component never clears the queue, retries a possibly printed receipt, or modifies the run to recover from printer errors.
