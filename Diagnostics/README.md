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
