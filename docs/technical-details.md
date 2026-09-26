# Technical details

## Completed-run receipts

Capture observes LiveSplit's start, split, and reset events. At start it clones the run and both timing methods' baselines; at the final split it freezes receipt data before background printing. It never writes history or changes the live run.

PB classification compares exact ticks against the start baseline. PB comparison splits also use that baseline; other comparisons use completion-time values. Recent averages include up to nine previous completed attempts plus the current result. Missing selected-method values remain unavailable.

Golds follow LiveSplit's best-segment test. A gold can follow a skipped split even when the individual segment duration is unavailable, because LiveSplit can compare a combined branch. Sum of Best uses LiveSplit's branch-aware calculation and appears only when it improves. Skipped rows remain visible; the following combined duration is not presented as an individual segment.

An attempt's completion claim is consumed before rendering or submission. Duplicate notifications, undo/re-finish, and simultaneous component instances do not cause another automatic submission. Adding/reloading a component mid-attempt waits for a new start. Changing the segment count mid-attempt suppresses capture; arbitrary route rewrites and plugins resetting the timer before capture are not supported reconstruction cases.

The Windows RAW dispatcher serializes jobs with a bounded backlog. Spooler acceptance is not physical-print confirmation. Failures never release a completion claim or cause automatic retries. Pending jobs can be lost on process exit and are not replayed.

## Archive reconstruction

Archive receipts clone the current run and reconstruct the selected completed attempt in the explicitly chosen timing method. LiveSplit history is editable and may be incomplete.

- Earlier attempts are ordered by history ID. The prior PB is the fastest earlier retained completion, with the earliest ID winning ties. Recent context uses at most ten completions ending at the selected attempt.
- Later attempts and today's PB/best-segment fields are not calculation inputs. Imported/manual PBs, deleted history, and timing-method changes can violate the inferred fastest-completion progression.
- Segment names, game, and category come from the current route. Reconstruction assumes retained attempts describe that route.
- A retained null segment entry identifies a skip. An absent entry means missing data. Combined post-skip durations can contribute to cumulative times but are not individual segment times or individual golds.
- Golds need known earlier individual durations from dated attempt history, including partial attempts. Undated imported PB/best entries and merged segments cannot establish those baselines.
- Missing entries or a cumulative/final mismatch invalidate affected reconstruction. The final time stored in attempt history remains authoritative. Unrecoverable PB split deltas stay unavailable.
- The first retained completion has unknown prior PB, so it receives no inferred PB celebration or historical time difference.
- Historical Sum of Best is omitted: editable history and undated imports cannot reliably recover the branch-aware record as it existed then.
- History IDs are preserved, but are not necessarily the displayed attempt counter. The original counter, comparison selection, and timing-method selection are not stored per attempt.
- RUN uses the stored completion timestamp. Unknown dates are identified explicitly; REPRINTED appears when the local calendar date differs. No timestamp or original fortune is fabricated. Each reprint can select a new random fortune when enabled.

These limits follow LiveSplit 1.8.37's `TimerModel.UpdateTimes`, attempt/segment history storage, and history import/fixup behavior. No receipt snapshots or separate performance database are maintained.

## Printer output and settings

Output uses ASCII-compatible text with normalization and control-character filtering. Unsupported characters become `?`. Rows truncate rather than wrap; numeric values too large for their bounded slots become unavailable rather than misleadingly truncated. Profile widths control text columns and the small standalone border independently.

Custom supports 192–832 printable dots, 24–96 Font A columns, and 32–128 Font B columns (B must be at least A). Reverse text and standalone 24-dot borders require explicit support. Cutting requires both cutter capability (`GS V 0`) and opt-in. Raster and Unicode capability fields do not enable raster or Unicode output.

Settings persist in layout XML. Older saved queues are retained, so verify the queue when upgrading. Missing queues stay empty; refresh does not select a default or discard a disconnected saved queue. Unknown profile IDs disable automatic printing pending setup. Fortunes are independently random, permit repeats, and have no saved selection state.

## Maintenance and release

`bootstrap.ps1` verifies the official LiveSplit 1.8.37 archive with a pinned SHA-256. `build.ps1` compiles with warnings as errors, runs the complete framework test runner, and packages only the component DLL, README, and license. LiveSplit and UpdateManager are build references supplied by the user's LiveSplit installation, not redistributed DLLs.

The source is organized into receipt data (`Domain.cs`), live capture (`Lifecycle.cs`), historical reconstruction (`HistoricalReceiptReconstructor.cs`), rendering, profiles, transport, settings/factory, and fortunes. The tests cover real TimerModel integration, fake transport, settings persistence, component loading, archive reconstruction, and command-aware ESC/POS output checks.

`tests/fixtures/pos58-v1.bin` is the fixed compatibility baseline. Do not regenerate it from the renderer being tested. The suite does not print or enumerate physical queues. Validate physical output separately on supported hardware.

For a release, start with empty generated `build/` and `dist/` directories, run bootstrap and the full build without `-SkipTests`, inspect the test result and ZIP contents, and confirm the factory version and assembly version remain the intended release. The ZIP uses a fixed file allowlist so stale files cannot enter the distribution. ZIP timestamps and compiler-generated identifiers are not a byte-for-byte reproducibility guarantee.

The project is distributed under the MIT License. Keep `LICENSE` in both the repository and release package.
