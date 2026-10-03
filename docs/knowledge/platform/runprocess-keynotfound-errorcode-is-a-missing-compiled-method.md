---
description: a RunProcess errorCode of KeyNotFoundException is most often a script task saved since the last compile, whose method the run looks up in the old compiled wrapper; user C# never produces an errorCode
applies-to:
  - clio/Command/RunProcessCommand.cs
ticket: ENG-92711
date: 2026-10-02
---

**What is true** - on an interpreted process, the run looks a script task's `<name>Execute` up in the methods
wrapper of the LOADED assembly (`ProcessModel.GetScriptTaskMethod`, a dictionary filled by the compiled
wrapper's constructor). A script task saved since the last compile is missing there, so the run throws
`KeyNotFoundException` when the flow reaches it - after the elements before it ran - and the exception
escapes the run, so RunProcess reports `errorCode` = `KeyNotFoundException`. With UseOldStartupExceptionHandling
on, the platform rethrows it and returns no process id, which looks like a refusal to start. An exception the
user's own C# throws is swallowed by its element and carries NO `errorCode`, so the code cannot come from it;
another platform lookup during element setup can still raise one, which is why the hint says "most often".

**Why it is this way** - the pre-run check (`CheckCompiledMethodsInAssembly`) only asks whether the wrapper TYPE
exists, so a process compiled before passes it with a stale wrapper; a never-compiled one fails it with
"Publish the ... process" instead.

**What breaks if you ignore it** - matching the case by message text instead of by `errorCode` would also catch
user C# that throws a KeyNotFoundException and send the caller into a compile, which reloads the runtime for
every user, for nothing. Reporting the no-id variant as "not started" would invite a re-run of the elements that
already ran.
