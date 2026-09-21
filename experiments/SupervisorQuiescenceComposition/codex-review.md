# Independent Windows review

Source: `07ac87df051708f7d8130f2206ebe37e4704fcf0` (Vladimir's published branch). No source changes. Codex assisting Kirill reproduced S1-S4 on Windows 26200 / .NET 10.0.12 using the README build/run commands. All four passed; raw observations are in `codex-windows-results.json`.

Measured: S1 loses envA; S2 retains envA; S3 retains envA but loses envB; S4 retains both. This supports global rather than per-target drain for terminating a shared backend in the controlled cases.

Source review qualification: `RunScenario` calls `StartBackend` once and then kills it. It does not respawn a replacement or send a post-replacement operation. The README's "Kill + respawn" claim is not implemented. The window is disposed immediately after termination. Therefore the measured scope is drain-before-termination, not full handover or end-to-end client continuity.

This branch still embeds E3 at e3138962c. A linked source file is shared within a checkout; changes pushed on Alex's branch do not automatically update this Git snapshot. Vladimir needs to explicitly bring in the repaired ledger at 551f25c925383f27e58d6961e5cf07f3ebc91288 (or newer) and publish the new revision.

Requested next evidence, owned by Vladimir: global admission closure through termination and replacement readiness; a new process identity; a successful post-replacement operation; an attempted admission during handover; bounded failure cleanup of owned child processes. The acknowledgement deadline currently falls through without asserting all acknowledgements arrived; fail explicitly so the control's precondition is observable. Do not infer external MCP transport continuity from these four scenarios.

No product code changes, production deployment, or broader architectural guarantee. This is a focused independent reproduction and review, not a competing supervisor implementation.
