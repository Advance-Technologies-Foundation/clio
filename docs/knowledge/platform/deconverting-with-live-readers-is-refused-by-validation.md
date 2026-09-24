---
description: de-converting a multi-instance Sub-process element whose service parameters still have readers is REFUSED by the platform's pre-save validation and saves nothing - so the package's "a parameter the de-conversion removed" notice never reaches a caller for element or process-parameter readers
applies-to:
  - clio.mcp.e2e/SubProcessMultiInstanceToolE2ETests.cs
ticket: ENG-99856
date: 2026-09-23
---

**What is true** — `setElement` with `subProcess.multiInstanceOptions.enabled: false` on an element whose
`OutputRecordCollection` or a counter is still read somewhere does not land. CrtProcessBuilder runs the
platform's full process validation before it saves, and that validation rejects an element parameter
whose value references a parameter the owning element no longer has. The call answers exit code 1 with
`Process validation failed: The "SubProcess2" element has an invalid value for the parameter
"InputRecordCollection". It references the parameter … of the element 'SubProcess1', which that element
does not have.` — the reader is named, and nothing is saved. Measured on the `d_krestov_n.tscrm.com:40001`
stand (CrtProcessBuilder 1.6.6.13) for an element iterating the output collection and for a process
parameter reading `TotalIterationsCount`; pinned by
`ModifyBusinessProcess_Should_RefuseADeconversion_WhileTheServiceParametersAreStillRead`. A reader in a
flow condition or a formula body was not measured.

**Why it is this way** — the de-conversion's own dangling scan runs, and the package builds a notice from
it, but the save that would deliver that notice is the step the validation blocks. So the notice exists in
code and in unit tests (which do not run the platform's validation) and not in any response a caller of
these reader kinds can receive.

**What breaks if you ignore it** — the shipped guidance promised the opposite until clio-knowledge 1.15.69:
that the de-conversion lands and "the server NAMES those readers in a notice". An agent following that
expects a success with a warning, gets a refusal, and has no reason to look for the reader to re-point
first. And a unit test of the de-conversion notice proves nothing about what a caller sees — only the E2E
against a real schema does.
