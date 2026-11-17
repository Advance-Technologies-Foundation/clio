---
description: a process parameter or Formula element that reads [#SysSettings.X#] of a SecureText setting gets the decrypted value, and the run endpoint returns it as a result - while the API reads the same setting as ***
applies-to:
  - clio/Command/ProcessModel/ProcessCredentialSources.cs
ticket: ENG-105475
date: 2026-11-16
---

**What is true** — measured 2026-11-16 on env `Creatio` (.NET Framework, CrtProcessBuilder 1.6.6.173), test values
only: a process with two Out parameters, one mapped from `[#SysSettings.UsrRpmApiKey#]` (stored as a source-3 value
with that text), one written by a Formula element whose `body` is the same macro, returned the decrypted SecureText
value in both through `RunSpecifiedProcessVersion` (`run-process` with `result-parameters`). A parameter whose value
source is SystemSetting (source 5, value = the setting code) is read the same way: the generated code calls
`SysSettings.GetValue` (`ProcessSchemaGeneratorNew`). Through the API, `get-sys-setting` returns `***` for that setting;
its `SysSettings.ValueTypeName` is readable.

**Why it is this way** — a process needs the real value to use it (a key sent to an API), and anyone who can build
and run a process may read settings by design. Nothing in the platform marks a parameter that holds one.

**What breaks if you ignore it** — treating `***` from the API as "the toolkit cannot see SecureText" is wrong: one
run-process call with a parameter mapped from the setting returns the key in clear. clio withholds such results
(ProcessCredentialSources); it cannot make the setting unreadable to that user.
