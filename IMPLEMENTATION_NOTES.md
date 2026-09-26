# V1 engineering handoff

## Baseline and dependencies

The initial workspace contained only `LiveSplit_Thermal_Run_Receipt_V1_Spec.docx` and `livesplit_thermal_receipt_handoff.md`. Both were read; the Markdown handoff takes precedence. There was no source, project, existing test suite, Git repository, logging infrastructure, or installed LiveSplit reference in this workspace.

Pinned target: LiveSplit **1.8.37**, official source tag at commit prefix `683cd60`, .NET Framework **4.8.1**. Source was inspected under ignored `vendor/LiveSplit`; the official release is extracted to ignored `vendor/runtime`. `bootstrap.ps1` verifies the release ZIP SHA-256 before extracting. Dependencies are build references, not redistributed in the component package. Source references:

- [TimerModel.cs, 1.8.37](https://github.com/LiveSplit/LiveSplit/blob/1.8.37/src/LiveSplit.Core/Model/TimerModel.cs)
- [LiveSplitStateHelper.cs](https://github.com/LiveSplit/LiveSplit/blob/1.8.37/src/LiveSplit.Core/Model/LiveSplitStateHelper.cs)
- [SumOfBest.cs](https://github.com/LiveSplit/LiveSplit/blob/1.8.37/src/LiveSplit.Core/Model/SumOfBest.cs)
- [SumOfSegmentsHelper.cs](https://github.com/LiveSplit/LiveSplit/blob/1.8.37/src/LiveSplit.Core/Model/SumOfSegmentsHelper.cs)
- [ComponentManager.cs](https://github.com/LiveSplit/LiveSplit/blob/1.8.37/src/LiveSplit.Core/UI/Components/ComponentManager.cs)

## Lifecycle and historical truth

`RunCapture` subscribes to the supplied `LiveSplitState.OnStart`, `OnSplit`, and `OnReset`; all are detached on disposal. There is no polling for completion and no subscription to undo as a print trigger.

`TimerModel.Start` increments `AttemptCount`, records `AttemptStarted`, sets Running/index 0, then raises OnStart. We clone the run, with both timing methods' records and history, and clear the private clone's current split times. Repeated start notifications do not overwrite the baseline.

`TimerModel.Split` writes the current split, increments the index, sets Ended and `AttemptEnded` on the final split, then raises OnSplit. `TimerForm`'s subscriber updates UI controls; it does not update PB/history. We synchronously copy the finished split values and build a read-only receipt snapshot at this event. The native `AtomicDateTime` is UTC; metadata converts the captured finish timestamp to local time before background work.

Normally `TimerModel.Reset(true)` later calls `UpdateTimes`: attempt history, best segments, PB, segment history. It then clears split times, raises OnReset, and fixes splits. Aborted history entries have empty `Time`. Our reset handler only disarms capture. We never call `UpdateTimes`, `Reset`, or modify the live run from production component code.

PB and history come from the start clone. The final result classification compares exact ticks, not rounded printed values: NEW PB / PB TIED / NORMAL. The first result can be NEW PB without fabricating a previous time. PB split-comparison values also use the start clone, preventing another component's early PB update from rewriting the receipt. Other comparisons use the active comparison's completion-time values.

Golds use `LiveSplitStateHelper.CheckBestSegment` on a private baseline run populated with the finished split times. This retains LiveSplit's combined-segment comparison behavior across skips. The gold marker may therefore appear after a skip even while SEG is `--`; this matches LiveSplit's gold test, not a fabricated individual duration. An equal best segment is not a new gold. The helper's ordinary previous-segment method searches backward across skips; we deliberately do **not** use that combined duration as the receipt's individual SEG.

Sum of Best uses LiveSplit's full history/branch-aware `SumOfBest.CalculateSumOfBest`, not a sum of best-segment fields. Baseline uses `useCurrentRun=false`; the private finished clone uses `true`. That includes current-run paths without writing history or PB. Tests compare the projected result with LiveSplit's actual post-reset result, including a skipped branch. A line prints only when both values exist and the new value is strictly smaller.

Recent history sorts the start snapshot's attempt entries by descending Index, filters for available completed times in the selected method, prepends the current available final time, and takes ten. Thus resets and the current run cannot be counted twice, including if an earlier event subscriber called UpdateTimes. Changing timing method during the run remains supported because both baselines were preserved.

## Once-per-attempt submission

An instance arms only after observing a valid start. The Ended/final-index callback consumes that attempt **before** rendering or queuing. Reset disarms; undo and repeated notifications never re-arm it. A weak table keyed by the actual LiveSplitState shares only a completion claim (run object, AttemptStarted, AttemptCount), suppressing duplicate submissions from simultaneous component instances. It retains no performance history. A reloaded component never arms itself from an existing running/ended state.

Guarantee: **at most one automatic spooler submission per observed completed attempt**, and one on the normal enabled/successful path. This cannot guarantee exactly one physical piece of paper after power loss, process termination, driver failure, or partial hardware output: Win32 RAW offers no such transaction. Claims are not released after failure; there is no automatic retry. Loading an old completion in a new process still cannot print because no start was observed. A process exit can abandon pending work; nothing is replayed on restart.

## Modules

- `src/Domain.cs`: immutable receipt/split snapshots; centralized ASCII, time, delta, wrapping and truncation.
- `src/Lifecycle.cs`: LiveSplit acquisition, baseline preservation, historical calculations and completion claims.
- `src/Renderer.cs`: opinionated layout and restricted ESC/POS writer; only the two profile-width by 24-dot standalone borders contain bitmap data, when supported.
- `src/PrinterProfile.cs`: immutable printer widths/capabilities and four small preset choices; no printer detection database.
- `src/Printing.cs`: native RAW transport, serial background dispatcher, bounded 32-job backlog, non-throwing rotating log.
- `src/Component.cs`: assembly factory registration, zero-size LogicComponent, WinForms settings, XML persistence, explicit manual test.
- `src/Fortunes.cs`: random fortune selection and synthetic receipt fixture.
- `tests/Tests.cs`: no-dependency framework test runner, fake print sink, real TimerModel integration, command-aware ESC/POS parser.

No assembly beyond the component DLL is installed. The factory is in Other; built-in LiveSplit settings/registration interfaces are used. Update URLs are empty because no release server exists; updates are manual.

## Formatting and hardware limits

POS-58 table allocation remains name 20 / SEG 6 / TIME 7 / delta 6 plus three spaces = 42. Other profiles assign remaining Font B cells to the name, keeping the numeric widths stable where possible. Narrow Custom widths reserve at least four name cells and bound numeric slots without wrapping. Numeric columns expand for long runs/comparison headers, reducing name width; every split remains a single line. Extremely large values that exceed the bounded numeric slots display `--` rather than misleading truncation. Masthead uses profile Font A first, then Font B for a longer title before truncating at that profile width; it does not resize the rest of the receipt.

Time values round away from zero to the requested precision with correct minute/hour carry. Near-zero deltas render `+/-0.0`. Exact result status still uses ticks. Hundredths are used for headline/PB; contextual values use tenths. Long PB/tie headlines split label and time into separate double-size reverse lines instead of wrapping or losing celebration. Unicode is normalized/transliterated where possible, unsupported characters become `?`, and text control bytes become spaces, preventing data from injecting printer commands.

`WindowsRawPrinter` uses Unicode queue APIs, datatype RAW, a real DWORD job-id return from StartDocPrinter, checked partial-write progress, pinned-buffer cleanup, abort-on-failure, and close-in-finally. Spooler I/O runs in serialized task continuations. Pending jobs hold bytes/queue values, not mutable LiveSplit state or UI controls. Successful EndDocPrinter means spooler acceptance only. Failures include native Win32 codes in logs. The bounded backlog reports failure rather than blocking the timer or growing indefinitely; failed/dropped receipts are not automatically replayed.

## Settings, persistence and tests

Enabled, queue, profile ID, Custom widths/capabilities, optional cutting, schema version 2 and Print fortunes serialize in layout XML. New queue settings are empty; queue listing never selects an entry or falls back to the Windows default. Blank queues are rejected in settings, the automatic submission predicate and PrintDispatcher, as well as RAW transport. Disabled is the default. Queue enumeration occurs only when opening settings or pressing refresh. A test click always uses one synthetic receipt with PB, golds, skip, unavailable fields, tie delta, summary, and fortune; it does not consume a real fortune or completion claim. Fortune selection uses one long-lived Random per FortuneBag, selects directly from the approved pool and allows repeats. No fortune-selection state is serialized; legacy FortuneBag XML is ignored and omitted on the next save. There is no printer-performance database or hidden queue-retry persistence.

Canonical build/test: `./build.ps1` (Windows PowerShell or PowerShell 7). It compiles all production files into `dist/Components/LiveSplit.ThermalReceipt.dll`, compiles the test runner, runs it against official release assemblies, writes `build/test-results.txt`, and packages a ZIP. The installed Framework compiler is used because this environment has runtimes but no .NET SDK. `src/LiveSplit.ThermalReceipt.csproj` is provided for SDK/IDE users; that alternate build requires a .NET SDK and 4.8.1 targeting pack.

Validation includes normal/new PB/exact tie/first completion, counts 1–15 with resets excluded, gold counts, conditional SoB, missing values, skips, comparisons and timing-method changes, hour/ten-hour times, large attempts, 500 splits, three-line fortunes, ASCII/control safety, all command/line-width restrictions, repeated random selections, selection-independent settings and legacy fortune XML, duplicate notifications, undo/re-finish, partial reset, disabled/disposed capture, reload, multiple instances, earlier history-mutating subscribers, contained errors, fake printing, XML roundtrip, and actual LiveSplit ComponentManager loading. The component/settings were instantiated and disposed with printing disabled; no interactive desktop LiveSplit session or physical printer was exercised.

## Remaining validation and assumptions

The user has confirmed the pre-compatibility MJ-5890K implementation works end-to-end. The compatibility update preserves its synthetic command stream byte-for-byte. Use README's installation instructions and explicitly click Print Test Receipt for physical verification after upgrading or choosing other hardware. Check the geometric pattern (implemented from the handoff's dimensions; original bitmap source was not supplied), text sizing, feeds, and spooler behavior. Then verify a real run using the user's autosplitter and selected timing method. Other LiveSplit versions are unvalidated.

Adding/reloading midway through a run waits until the next attempt. Editing the route's segment count during a run suppresses capture with a diagnostic. Arbitrary same-length route rewrites or other plugins that reset the timer before this subscriber runs cannot be reconstructed; ordinary LiveSplit 1.8.37 behavior is covered. Missing GT is displayed as unavailable instead of silently substituting RTA. Fortune progress survives only saved layout checkpoints. Cross-process crash recovery and automatic retries are intentionally absent.

No commits were created: the supplied directory was not a Git repository. Original specification files remain unchanged.

## Compatibility release 1.1.0

Optional completion confirmation is stored as `ConfirmBeforePrinting` in layout XML and defaults to false, including older layouts. The component asks a Yes/No question after RunCapture freezes the run and claims completion; No skips the receipt without releasing that claim. Queue and renderer settings are captured before the modal dialog opens. Test Receipt remains an explicit action without a second prompt. Tests inject confirmation answers and fake printers; no dialogs or printers are used by the suite.

The lifecycle, historical PB calculations, immutable receipt data and RAW spooler architecture remain unchanged. Printer profiles control Font A/B layout widths and the standalone border dot count. POS-58 preserves 32/42 columns, 320-dot borders and reverse text; generic 58mm uses 32/42 and generic 80mm uses 48/64 with graphics and reverse disabled. Custom allows measured widths, reverse, 24-dot support and separately gated cutting. Raster and Unicode capabilities are representable, but this renderer deliberately never emits raster or Unicode, even if a profile advertises them. No inline graphics or custom glyph path exists.

Unsupported reverse retains bold PB/tie text and uses text section bars. Unsupported 24-dot borders become text separators. No cutter command is emitted unless support and user opt-in are both true. UI snapshots the selected profile during rendering before handing bytes to background transport. Queue refresh preserves explicit text, including disconnected queues. Saved version-1 queues are retained (the old schema did not record whether a queue was prefilled or explicitly selected); missing queue fields are empty. Unknown profile IDs disable automatic printing and select conservative generic 58mm settings for reconfiguration.

Regression fixture: tests/fixtures/pos58-v1.bin was captured before editing, with its original timestamp documented beside it. The command-aware parser checks every text line against active font/size widths, skips image payloads, validates exact image widths, and rejects unsupported commands including raster. Tests use fake sinks and injected queue lists only; neither the spooler nor real queue enumeration is exercised.

Release validation: 92 tests pass, including all 69 original tests plus compatibility, confirmation and fortune-toggle cases. Build uses warnings-as-errors and produces dist/LiveSplit.ThermalReceipt-1.1.0.zip with the DLL, README and these notes. Physical checks remain necessary on other printers for font metrics, configured dot width, firmware command support, feeds and optional cutter behavior. No physical printing occurred during this pass.

PrintFortunes persists in layout XML and defaults to true for new, legacy or invalid settings. When disabled, automatic and manual receipts omit the complete fortune enclosure, including both graphic/text borders and interior spacing. The closing rule and tear feed remain. Automatic capture skips fortune selection while disabled. The default-on POS-58 byte fixture still matches exactly.

## Archive Reprint implementation

`HistoricalReceiptReconstructor` clones IRun, selects a unique completed attempt for an explicitly chosen stored timing method, and orders by attempt Index. It reconstructs the prior PB as the minimum earlier completed final time (earliest index wins equal records). Historical recent average takes at most ten available completed times with Index <= selected Index. No current comparison or current PB/best fields participate. Null preceding segment history entries encode skips; the next stored duration spans the skipped branch. Cumulative times sum these branches, but individual SEG is unavailable immediately after a skip. A missing entry breaks the cumulative chain; a final cumulative mismatch invalidates the reconstructed chain. The stored attempt final remains authoritative. Gold comparison uses earlier nonnegative IDs attached to actual attempts, including resets, and excludes merged durations and unknown baselines.

Source investigation: LiveSplit 1.8.37 TimerModel.UpdateAttemptHistory stores both timing values, optional Started/Ended, PauseTime and an Index assigned from max history index + 1, independently of the displayed AttemptCount. UpdateSegmentHistory stores segment durations and null skip entries, including combined durations after skips. UpdatePBSplits depends on the then-active method, which is not saved per attempt. IRun.ImportSegmentHistory inserts undated nonpositive synthetic PB entries; ImportBestSegment does likewise. FixSplits can clamp historical values, remove duplicates/null entries and reattach orphan IDs. Consequently an immutable historical PB setting, selected UI comparison, timing selection, attempt counter, route names and complete branch-aware Sum of Best cannot be proven from an arbitrary file. Synthetic imported entries cannot safely be assigned to a selected date and are excluded. Historical Sum of Best is deliberately unavailable rather than calculated from today's values. PB classification assumes normal fastest-completion progression in the chosen method; manually replaced PBs/method switches/deleted history may violate that assumption. These limitations are exposed in the UI and README.

Archive-specific ReceiptRun fields default to live behavior. Renderer adds only archive identification, historical summary labels and run/reprint metadata. The live POS-58 fixture remains byte-identical. Component archive submission validates the queue and selected attempt before randomly selecting a fortune and uses the same dispatcher/profile without touching RunCapture or CompletionClaims. A changed run object invalidates a stale history selection. Settings history is refreshed on opening, explicit refresh and method selection. No persistent performance data is added.

Validation: 27 archive tests plus all 92 existing tests pass (119 total). Tests cover normal/PB/tie history, future exclusion, averages, golds, unknown SoB, skips, missing data, historical comparisons, timestamps, method availability, ordering, immutable source, profile widths, fresh fortunes, automatic claim isolation, blank queues and fake printer failures. No physical printer or queue enumeration is used. Build remains warnings-as-errors, with the component DLL and release ZIP produced by build.ps1. Historical information lost or edited by LiveSplit cannot be recovered by these tests or by the component.
