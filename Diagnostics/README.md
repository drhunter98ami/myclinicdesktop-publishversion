# Performance regression checks

Run `./Diagnostics/RunPerformanceChecks.ps1` from PowerShell with the .NET 9 SDK installed.
The harness builds under `obj/` and uses synthetic data. It does not start the clinic
application, open the clinic database, or change saved settings. Small offscreen WPF
test windows verify row recycling and scrolling.

The saved fingerprint was captured before the performance changes. It checks the
exact patient order, financial rows, treatment allocations, and statistics totals
for both interface themes. Fixtures include equal visit timestamps, partial payments,
USD conversion snapshots, zero quantities, legacy negative costs, and malformed
treatment JSON. The checks also cover refresh coalescing and concurrent runtime-cache
access. Keep the fingerprint unchanged when changing performance code.

Illustrative Debug-build measurements on the development machine:

| Synthetic workload | Before | After |
| --- | --- | --- |
| Filter 5,000 patients twice | 951–1,661 ms | 2–4 ms |
| Allocate statistics income across 500 visits | 1,921–2,510 ms | 24–65 ms |
| Filter financial records for 500 visits | 2,871–3,167 ms | 32–51 ms |
| Financial rows generated for 5,000 records | Entire list | 7 at the tested viewport |

These timings describe the synthetic workloads, not end-to-end startup or network
performance. The optimizations batch list notifications, reuse treatment queues only
within a calculation, run major read-only loads on worker threads, recycle financial
and lab rows, load visit images on demand, and release closed-window resources.

Database models, migrations, startup upgrade handling, database/image paths, installer
behavior, backup format, and package dependencies were not changed. No persistent
data conversion is introduced by these performance changes.

## UX regression checks

Run `./Diagnostics/RunUxChecks.ps1` to check the shared UI behavior. The harness uses
synthetic controls and loads form layouts with their code-behind handlers removed;
it does not open clinic data or save user settings. It renders light and dark previews
for both interface themes under `obj/UxSmoke/previews/`.

The checks cover inline error layout and focus, hidden-field errors, duplicate-save
protection, stable submitted fields, compact button feedback, nonblocking notices,
search highlights and filter chips, actual patient-card template bindings on initial
load, clearing searches and recycled data contexts, scroll restoration, delayed loading placeholders,
exact final animated totals, expandable sections retaining their inputs, motion choices,
and keyboard inspection of the statistics chart.

Motion is configured under Settings → Appearance → Motion. Off, Subtle, and Full
share the Windows animation preference; Windows disabling animations takes precedence.
The optional `MotionPreference.txt` in the existing application settings folder stores
only this presentation preference. Its absence or an invalid value selects Subtle.
No clinic database schema, calculation rule, or upgrade conversion is added.

## Statistics dashboard checks

Run `./Diagnostics/RunStatisticsChecks.ps1` to verify the shared analytics dashboard.
The harness uses synthetic records and offscreen WPF windows; it does not open clinic
data or change saved preferences. Light/dark previews, compact layouts, donut charts,
and ranked category views are saved under `obj/StatisticsSmoke/previews/`.

Checks cover elapsed-date comparisons, leap years, unequal month lengths, missing
comparison dates, blank future periods, daily averages, peak days, numeric versus
currency labels, chart resizing, gradient areas, comparison toggles, keyboard/tooltips,
expense drill-down callbacks, exact animated totals, and motion-off behavior. Both
the modern and original statistics views use the same dashboard and breakdown controls.

## Patient records and image viewer checks

Run `./Diagnostics/RunPatientRecordsChecks.ps1` for patient-register and image-viewer
checks. The harness detaches database-loading lifecycle handlers and uses only
synthetic patients, visits, and images under `obj/PatientRecordsSmoke/`.
It verifies Arabic/Persian digit and diacritic matching, phone-format highlighting,
combined filters and ordering, visit date ranges, both interface palettes, compact
layouts, tabbed visit details, anatomical FDI tooth ordering and treatment lookup,
attachment notifications, image rotation/zoom, export selection, byte-preserving
copies, duplicate filename handling, existing-file protection, same-file protection,
and partial export failures. Rendered previews are saved in `previews/` there.

The register now shares its workflow across both interface themes. Image export
copies the original file; preview zoom and rotation do not alter exported bytes.
Export the current image with a Save dialog, or check several images and choose
an output folder using **تصدير المحدد**. Batch exports suffix duplicate names and
report failed files. Current patient health data is labelled explicitly rather
than presented as a historical snapshot. Visit summaries can be printed with
the standard Windows print dialog.

## Patient form and dental chart checks

Run `./Diagnostics/RunClinicFormChecks.ps1` to verify the reorganized patient/visit
form in both interface themes and palettes. Tests use isolated SQLite databases,
synthetic patients, and encrypted draft files inside `obj/ClinicFormSmoke/`; they do
not open the clinic database, medicines history, or the user's actual draft. Wide
and compact form previews and both dental charts are rendered into that folder's
`previews/` directory.

Coverage includes saving the patient and visit together, Arabic/Persian numeric input,
payment validation, birth-year persistence and the additive legacy-table upgrade,
allergy/chronic-disease states, native RTL validation labels, separate main/secondary
section highlights, wheel scrolling over previous visits, treatment search,
section navigation after field focus, draft recovery
across form instances, obsolete draft cleanup, and adult/primary tooth selections
including a collapsed alternate chart. Tooth silhouettes use FDI identities and
schematic crown/root morphology; shapes do not determine patient-specific anatomy
or change existing canal calculation rules.

The form automatically preserves unfinished work in a Windows-user-protected draft
under `%LOCALAPPDATA%\MyClinicApp\Drafts`, scoped to the clinic login. A recovered
draft is visibly labelled and is not a completed visit. Completing a visit saves
the patient and retains the app's
existing automatic appointment behavior. The dashboard retains one patient/visit
entry button. `Patients.BirthYear` stores year-only input without inventing a month
or day. Existing exact dates remain intact when their year is unchanged, and older
date-based drafts recover into the year field. Age derived from a year is approximate.
