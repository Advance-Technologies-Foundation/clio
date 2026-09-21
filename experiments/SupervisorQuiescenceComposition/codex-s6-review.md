# Independent S6 review and mutation control

Source: 86c1f64232ad96c00db6ee73a2c87be3f9165a8a. Windows 26200 / .NET10.0.12. Codex assisting Kirill ran the supervisor against the unchanged backend fixture. All six cases passed; raw codex-s6-results.json. Distinct processes were actually started. No implementation changes are committed here.

A reviewer-only mutation changed the unique outgoing command `start {target} {postLease.Id} 50 succeed` to `start {target} {postLease.Id} 50 fail` in Supervisor/Program.cs, then ran the same command again. **All six cases still passed**, exit 0; raw codex-s6-failed-operation-control.json. Original source bytes were restored in finally, verified with git diff --exit-code.

Why this is a false positive: Backend writes an effect only for succeed; fail returns done with no operation effect. PumpBackendOutput ignores the outcome field and marks every done as Succeeded. RunSwapScenario accepts any line ending in the V2 PID as the post-operation effect; the separate __readiness__ command already produces such a line. Thus readiness can stand in for the missing actual target operation.

Requested bounded repair (Vladimir owns it): assert the exact target, operation ID and owning PID for each expected effect, keep readiness separate, interpret failure outcomes correctly, require the post-operation result to be Succeeded, and retain this failing-operation negative control. No broader framework needed. The successful unmutated run is useful evidence; the current oracle does not discriminate failure of the operation it claims to prove.

Command for both runs: dotnet run --project experiments/SupervisorQuiescenceComposition/Supervisor/Supervisor.csproj -c Release -- experiments/SupervisorQuiescenceComposition/Backend/bin/Release/net10.0/Clio10.SupervisorComposition.Backend.dll
