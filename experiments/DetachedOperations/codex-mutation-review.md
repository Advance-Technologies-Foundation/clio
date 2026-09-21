# Independent mutation-control reproduction

Source: 1f17a7763d4a (Alex's branch); Windows 26200 / .NET10.0.12. Codex assisting Kirill inspected the Host/Program.cs and OperationLedger.cs delta, then ran the host against the unchanged V1/V2 fixture binaries previously built from 551f25c92538. Those fixture sources and projects have not changed in this delta.

22/22 passed. Repaired arm: 0 sampled violations, 66 admissions, 55 refusals, 9 windows. Deliberately split arm: 16 violations, 20 admissions, 84 refusals, 16 windows. Raw output: codex-mutation-results.json. This independently confirms that the revised sampling harness can detect this injected defect; it is not exhaustive concurrency, timing or production safety proof. No new Claude consultation: the normal admission implementation is unchanged from the reviewed repair; the delta adds test instrumentation and the mutation control.

Command: dotnet run --project experiments/DetachedOperations/Host/Host.csproj -c Release -- run artifacts/detached/10.0.0.0 artifacts/detached/10.1.0.0
