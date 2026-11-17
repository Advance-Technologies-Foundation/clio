---
description: run-process withholds text, Guid and collection results of any process that calls a web service or reads a SecureText setting - decided per process, because tracing each value through mappings was tried and dropped; what the rule knowingly leaves out
applies-to:
  - clio/Command/ProcessModel/ProcessCredentialSources.cs
  - clio/Command/RunProcessCommand.cs
ticket: ENG-105475
date: 2026-11-16
---

**What is true** — a process puts a credential into a result two measured ways: a Call web service element's
`Response` echoes the request (an echo API returned the `Authorization` header and a setting-filled `X-Api-Key`), and
a parameter that reads a SecureText setting gets it decrypted (platform/a-process-parameter-reads-a-securetext-setting-decrypted).
clio never sees the decrypted key, so it cannot mask by value. The rule is per process: a web service element or a
SecureText setting anywhere in the process or the active versions of the processes it calls withholds every requested
text, Guid or collection result; flags, numbers and dates are returned.

**Why it is this way** — the first version traced each value through meta-path tokens, element inputs and Formula
targets over the raw schema JSON: about 600 lines, formats learned only by measuring, cycles and nested event
sub-processes to handle, and it still missed the direct setting read. Context hygiene for an agent does not need
that precision: the agent judges the run by status, `Success`, `StatusCode` and effects, and the user reads the values
in the process log.

**What breaks if you ignore it** — reintroducing per-value tracing brings back the fragility for little gain, and
returning text from such a process returns echoed keys. Knowingly NOT covered: a number that holds a key; a Script
task whose C# reads a setting or calls an API itself; a SecureText entity column read by Read data (not measured);
reading the records a process wrote with the data tools; calling the platform's run endpoint directly. clio is not a
boundary against a user who can build processes.
