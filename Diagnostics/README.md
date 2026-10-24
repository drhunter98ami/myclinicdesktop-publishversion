# Performance regression checks

## Shortages, laboratory and settings workspaces

Run `./Diagnostics/RunClinicWorkspaceChecks.ps1` for isolated SQLite transaction,
legacy upgrade, purchase/payment form, and offscreen WPF layout checks. It builds
the application under `obj/ClinicWorkspaceBuild/`, so a running clinic executable
does not need to be replaced. Synthetic previews are written under
`obj/ClinicWorkspaceSmoke/`. The harness does not open the clinic database, connect
to Google Drive, or save appearance preferences.

Both interface themes host the same shortages, laboratory and settings controls.
The additive `ClinicWorkspaceSchema` upgrade and EF migration introduce immutable
`ClinicOperations` receipts, shortage price/deletion flags, independent lab work
status, payment revision checks, delivery details, lab contacts/archiving, and an
explicit treatment canal option. Legacy prepaid work retains its paid amount and
delivery status; old zero-price shortages remain unpriced. Original receipt amounts,
currencies and conversion rates are retained. A linked receipt's expense cannot be
edited independently from the financial workspace.

Purchase/payment writes claim the expected quantity/revision before saving the
receipt, expense and financial audit in one transaction. Duplicate requests reuse
their receipt. Purchase history links to the exact expense; lab history shows each
partial payment and work status change. Existing historical lab payments without
receipts are labelled as legacy entries rather than given fabricated expense links.

Lab work currency corrections remain available after payment. The correction form
shows an explicit conversion rate and the resulting cost, paid allocation and
balance. Saving uses the same revision check as payments and appends a correction
and before/after audit without modifying existing payment receipts or expenses.
Legacy paid amounts are tracked through the conversion journal when displaying
history. A patient name can be entered directly or selected from clinic suggestions.
The lab tooth picker reuses the Add Patient adult/primary anatomical chart controls.
Compact summaries and filters no longer sit inside a separate scrolling area.

Drive backups use SQLite's snapshot API, including committed WAL changes. Restore
checks the selected remote timestamp and downloaded database integrity, creates a
local recovery copy, and stages the database for the next startup. Startup saves
another recovery snapshot before applying it and upgrading its schema. The backup
contains the database; patient image files remain outside this format. OAuth flows
and actual Drive uploads/downloads require manual verification with a configured
test Google account; the automated checks do not perform these operations.

`FollowSystemTheme.txt` stores the optional Windows theme preference. Settings
category/scroll preservation lasts for the application session. Canal requirements
are saved with new selected-treatment JSON; old visit JSON retains its existing
name-based fallback. Historical visit prices are not rewritten when catalog prices
or the current exchange rate change.

Run `./Diagnostics/RunPerformanceChecks.ps1` from PowerShell with the .NET 9 SDK installed.
The harness builds under `obj/` and uses synthetic data. It does not start the clinic
application, open the clinic database, or change saved settings. Small offscreen WPF
test windows verify row recycling and scrolling.

The saved fingerprint checks the
exact patient order, financial rows, treatment allocations, and statistics totals
for both interface themes. Fixtures include equal visit timestamps, partial payments,
USD conversion snapshots, zero quantities, legacy negative costs, and malformed
treatment JSON. The checks also cover refresh coalescing and concurrent runtime-cache
access. Keep the fingerprint unchanged when changing performance code. The financial
workspace redesign intentionally replaces the modern financial fixture with the shared
decimal ledger, including forgiveness and credit allocation; its reviewed fixture is
covered independently by the financial checks below.

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

## Financial workspace checks

Run `./Diagnostics/RunFinancialChecks.ps1`. Checks use in-memory SQLite databases,
synthetic patients, and offscreen WPF windows. They do not open the clinic database,
change saved preferences, or send reports to a printer. Previews are saved under
`obj/FinancialSmoke/previews/`.

Coverage includes historic exchange rates, chronological FIFO allocation, partial
payments, standalone forgiveness, patient credits, equal timestamps, Arabic/Persian
money input, period boundaries, duplicate settlement protection, retained paid and
cancelled obligations, expense corrections, linked payment history, stale balances,
atomic audit rollback, and an idempotent upgrade from the older financial schema.
UI checks cover the expense-only category breakdown, stable green category colors,
rendered percentages and filter refreshes while expanded, both palettes and interface
themes, compact layouts with at least three visible obligations, and pixel wheel
scrolling within income details taller than the viewport. They also cover payment
previews, confirmation cancellation, column sizing, CSV escaping and formula protection,
print pagination, and motion choices.
Obligations use bounded pages of 50 records; their exports include every matching page.
All financial grids recycle rows while scrolling. The recipient field and operations-log
tab are removed from the workspace; new obligations require category, details and amount.
Previously stored recipients and transaction audit records remain in the database.

Both main windows now open the same financial workspace. Income and expense totals
follow the selected date/search filters; the category filter affects outgoings only.
Patient receivables and obligations are explicitly labelled as independent of the
period filter. Current-month/year comparisons use the equivalent elapsed part of
the preceding period; closed periods use their full preceding period.

The additive startup upgrade adds obligation dates/recipients/statuses, financial-only
visit metadata, and an audit table. Existing pending payments retain their amounts
and have no invented due date. Paid/cancelled history and audit events are retained
for new operations; already deleted records cannot be reconstructed. New standalone
patient payments are flagged explicitly; older entries are inferred from their lack
of clinical details for the visit/financial-entry count, without rewriting records.

CSV export follows the active tab and filters. Patient statements include all entries
through today, with the running balance in chronological order. Receipts and statements
use the Windows print dialog; selecting Microsoft Print to PDF saves a PDF. Corrections
retain the prior values and a reason. Correcting an expense created by an obligation
updates that linked paid-history entry in the same transaction.
