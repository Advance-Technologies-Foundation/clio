# Independent S5 review

Source: d01e236277654a996bc189be816b1eb655fadfbe. Windows 26200 / .NET10.0.12. Codex assisting Kirill reran the supervisor with the unchanged backend fixture built in the prior review: 5/5 passed, raw observations in codex-s5-results.json. No source changed.

S5 confirms a direct ledger Begin is refused while its global window is held and permitted after disposal. It does not establish real handover: RunHandoverScenario disposes the window before killing the original backend, registers V2 against that same original Process object, and marks that lease Succeeded locally without sending a command to a backend. No replacement is started anywhere in the probe. Preserve these as ledger-admission observations; the requested replacement/readiness/post-replacement-call proof remains open and owned by Vladimir.

Command: dotnet run --project experiments/SupervisorQuiescenceComposition/Supervisor/Supervisor.csproj -c Release -- experiments/SupervisorQuiescenceComposition/Backend/bin/Release/net10.0/Clio10.SupervisorComposition.Backend.dll
