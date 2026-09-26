# Speedrun Receipts for LiveSplit

Speedrun Receipts is a LiveSplit component that prints a physical receipt when you finish a speedrun.

Turn a completed run into a keepsake: game and category, final time, PB celebrations, splits and deltas, golds, recent performance, and an optional speedrunning fortune. Reprint older completed attempts from LiveSplit history, too.

For **Windows, LiveSplit 1.8.37, and .NET Framework 4.8.1**, with a **58mm or 80mm ESC/POS receipt printer**.

## What it does

- Prints the game, category, final time, finish date, and timing method.
- Celebrates a new personal best or an exact PB tie.
- Includes every split, segment and cumulative times, comparison deltas, and gold markers.
- Adds recent completed-run averages and relevant PB / Sum of Best context when available.
- Offers optional fortunes and archive reprints of historical completed attempts.
- Supports a known-good POS-58 profile, generic 58mm and 80mm profiles, and custom printer settings.

## Download

**[Download from GitHub Releases](https://github.com/spencamp/speedrun-receipts/releases)** — choose `LiveSplit.ThermalReceipt-1.1.0.zip` under **Assets**, not GitHub's source-code ZIP.

The ZIP contains the component DLL, this README, and the MIT license. You do not need to build from source. If no release is listed yet, the first release is still pending.

## Installation

1. **Close LiveSplit.**
2. Extract the release ZIP. Copy `Components/LiveSplit.ThermalReceipt.dll` from the extracted `LiveSplit.ThermalReceipt-1.1.0` folder into the `Components` folder beside `LiveSplit.exe`.
3. Open LiveSplit.
4. Right-click LiveSplit and choose **Edit Layout → + → Other → Thermal Run Receipt**.
5. Open the component's **Layout Settings**. Select your **Windows printer queue** and **Printer profile**. Install your receipt printer's Windows driver first if its queue is missing.
6. Leave **Enable receipt printing** unchecked and click **Print Test Receipt**. Check the paper width, alignment, and output before continuing.
7. Check **Enable receipt printing**, close settings, and **Save Layout**. Start a new attempt; finishing its final split will submit a receipt.

**Select a receipt printer, never an office printer or PDF queue.** This component sends raw ESC/POS commands. A wrong queue or profile can produce incorrect output or waste paper. The queue starts empty, and the component never falls back to the Windows default printer.

## Printer compatibility

| Printer profile | Text columns (Font A / B) | Best use |
| --- | --- | --- |
| POS-58 / 58mm Compatibility | 32 / 42 | Known-good [MJ-5890K](https://www.amazon.com/dp/B0CRR7TWKV?lv=shuf&channelId=500&plpRedirect=mhFallback) baseline (non-affiliate link); reverse text and small 320-dot borders |
| Generic 58mm ESC/POS | 32 / 42 | Other 58mm printers; conservative text separators |
| Generic 80mm ESC/POS | 48 / 64 | Wider printers and longer split names; text separators |
| Custom | Configurable | Measured widths and explicitly supported printer capabilities |

Generic ESC/POS compatibility varies by hardware and firmware. Paper width alone does not guarantee compatible fonts, printable area, graphics, or cutting. The component does not detect printer models. Start with a suitable profile and test your own printer before enabling automatic printing.

## Settings / features

| Setting or action | What it does |
| --- | --- |
| **Enable receipt printing** | Automatically prints newly observed completed attempts. Off by default. |
| **Confirm before printing completed runs** | Asks before each automatic receipt. Choosing No skips it; undo/re-finish does not ask again. |
| **Print fortunes** | Includes an optional fortune and its borders. On by default; fortunes are random and may repeat. |
| **Print Test Receipt** | Sends one sample receipt to your selected queue, even with automatic printing off. |
| **ARCHIVE / RECEIPT HISTORY** | Selects an older completed attempt for an explicit reprint. |
| **Custom** printer profile | Configures printable dots, Font A/B columns, reverse text, and standalone 24-dot borders. Use your printer manual and a test receipt. |
| **Supports ESC/POS cutter (GS V 0)** + **Cut after receipt** | Enables cutting only when both are selected under Custom and your hardware supports it. |

Use **Save Layout** to keep your settings. Updates are manual: close LiveSplit and replace the component DLL with the newer release's DLL.

## Archive reprints

In **Layout Settings → ARCHIVE / RECEIPT HISTORY**, choose **REAL TIME** or **GAME TIME**, select a completed attempt, and click **Print Selected Receipt**. Use **Refresh completed attempts** to reload the list. LiveSplit normally adds a finished attempt to history when the timer is reset.

Selecting or refreshing does not print. Reprints work with automatic printing off and use your current queue, profile, and fortune setting. Receipts are marked **ARCHIVE REPRINT**.

History is **reconstructed from LiveSplit's retained data**, not an immutable receipt database. Edited, imported, or deleted history can leave gaps; unknown values stay unavailable. Names come from the current route, historical PBs are inferred from earlier completions, and the original fortune is not restored. See [archive reconstruction details](https://github.com/spencamp/speedrun-receipts/blob/main/docs/technical-details.md#archive-reconstruction) for the full limitations.

## Troubleshooting

| Problem | What to check |
| --- | --- |
| No queue / test button disabled | Install the Windows printer driver, click **Refresh printer queues**, and select the receipt printer or type its exact queue name. An empty queue blocks printing. |
| Nothing prints or output is garbled | Confirm the queue belongs to an ESC/POS receipt printer and its driver/connection accepts RAW ESC/POS data. Check the Windows print queue, connection, power, and paper. |
| Text clips or paper feeds unexpectedly | Check **Printer profile** and measured widths. Disable automatic printing and use **Print Test Receipt** to verify settings. |
| A completed run did not print | Enable printing, save the layout, and start a new attempt. Adding/reloading the component during an attempt waits until the next one. Resets and incomplete runs do not print. |
| An old attempt is missing | Reset the finished timer, refresh completed attempts, and check the selected timing method has a stored final time. |
| Status says sent, but there is no receipt | “Sent to Windows spooler” means Windows accepted the RAW job; it does not guarantee physical printing. Inspect the queue and printer before deliberately printing again. |

Logs: `%LOCALAPPDATA%\LiveSplit\ThermalReceipt\receipt.log`.

There is **no automatic retry**. Undoing and re-finishing an attempt does not submit another automatic receipt. Printer failures do not reset or modify your run.

## Building from source

Developer information: normal installation uses the release ZIP above.

Use Windows with **.NET Framework 4.8.1** and the **LiveSplit 1.8.37** release assemblies. The validated build uses the installed Framework C# compiler; it needs no .NET SDK, NuGet restore, or physical printer.

From the repository folder in PowerShell:

```powershell
# Download and verify the pinned official LiveSplit 1.8.37 archive:
.\bootstrap.ps1

# Compile, run the full test suite, then package:
.\build.ps1

# Alternatively, use an existing extracted LiveSplit 1.8.37 installation:
.\build.ps1 -LiveSplitPath 'C:\Tools\LiveSplit'
```

Outputs:

- `dist/Components/LiveSplit.ThermalReceipt.dll` — installable component.
- `dist/LiveSplit.ThermalReceipt-1.1.0.zip` — release package.
- `build/test-results.txt` — test results; tests use fake printers.
- `build/test-receipt.txt` and `build/test-receipt.bin` — diagnostic test output, excluded from the ZIP.

Do not use `-SkipTests` for release builds. The optional `src/LiveSplit.ThermalReceipt.csproj` supports IDE builds with a .NET SDK and the 4.8.1 Developer Pack. See [maintenance and release details](https://github.com/spencamp/speedrun-receipts/blob/main/docs/technical-details.md#maintenance-and-release).

## Technical details / limitations

- Output uses native ESC/POS text and ASCII-compatible characters, not full Unicode or full-receipt images. Long names may be truncated.
- PB context comes from attempt start; split deltas use the comparison selected at completion. Missing timing data is never replaced with another timing method.
- There is no separate receipt database, crash recovery, or automatic update service.
- Automated tests do not certify physical output. Generic profiles and custom settings require testing on the target printer; other LiveSplit versions are unvalidated.

[Technical details](https://github.com/spencamp/speedrun-receipts/blob/main/docs/technical-details.md) cover calculations, history limits, and maintenance.

## License

Speedrun Receipts is available under the [MIT License](LICENSE).
