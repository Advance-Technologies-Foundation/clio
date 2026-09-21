# Independent E3 repair recheck

Reviewed source: `551f25c925383f27e58d6961e5cf07f3ebc91288`; no implementation changes. Codex assisting Kirill reran the README's runtime builds and host runner on Windows 26200 / .NET10.0.12. **21/21 passed**, exit 0; raw JSON in `codex-windows-recheck.json`.

Source inspection closes the four original findings within this probe's contract: admission and registration share the window lock; global/target exclusion is symmetric; terminal evidence precedes publication and owner release under that lock; A5d starts and awaits actual work after release.

A5h observed 114 admissions, 10 refusals, 7 windows and zero sampled violations in this run. It samples immediately after window acquisition, before the hold delay, so it is not a continuous assertion over the whole window. A5g samples completed persistence after polling rather than forcing the old persistence race. These are limits of regression sensitivity, not evidence that the repaired source retains the original races.

This establishes the tested ledger boundary, not completed runtime-task teardown, arbitrary crash recovery, disk-error handling, fairness, real client continuity, or production readiness. Complete must be called only after the operation's relevant work/cleanup is finished; the caller must not treat a terminal record as proof of arbitrary code that runs after Complete.

Validation commands: build RuntimeV1/DetachedRuntime.V1.csproj and RuntimeV2/DetachedRuntime.V2.csproj in experiments/DetachedOperations with -c Release; then dotnet run --project experiments/DetachedOperations/Host/Host.csproj -c Release -- run artifacts/detached/10.0.0.0 artifacts/detached/10.1.0.0.
Local Claude recheck rev_d75a28ade73446db independently marks all four prior source defects closed. Its P2 concern about A5h regression sensitivity is accepted; its disk-I/O failure and failure-reporting observations are recorded as limitations, not production guarantees. Claude ran no tests and could not traverse the main checkout Git metadata under its read-only isolation; Codex resolved the full SHA above and inspected the actual repair diff. The new .NET10.0.12 JSON is Codex evidence, separate from Alex's committed .NET10.0.11 run.
