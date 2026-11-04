# Development run 4

Development run, not for the paper.

Command: `dotnet windows-host\src\Jarvis.Eval\bin\Release\net10.0-windows\Jarvis.Eval.dll run --seed 20261025 --reps 1 --smoke --scheduler fake --out docs\eval\results\dev-run-4`

This smoke run uses the fake scheduler and has no pre-existing-process override.

Result: 10 tasks (2 per tool), 70 task/fault cases, 280 verifier rows, and 1 metadata row. Verifier D returned VERIFIED on F0 controls, FAILED on the BAD faults except F11 app.launch (VERIFIED, the already recorded D scope finding); all reminder BAD faults were FAILED.
