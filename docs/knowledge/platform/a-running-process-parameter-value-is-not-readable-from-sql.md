---
description: there is no SQL route to a Creatio process instance's parameter values - a completed element's SysProcessElementData row is deleted and a parked process's SysProcessData blob carries structure only, so branch on the value and read SysProcessElementLog instead
applies-to:
  - spec/eng-99856-multi-instance/
ticket: ENG-99856
date: 2026-09-22
---

**What is true** — you cannot read a running or finished process instance's parameter values out of the
database. Measured on a .NET Framework stand (`CrtProcessBuilder 1.6.6.0`, 2026-09-22):

- `SysProcessElementData.PropertiesData` is **plain JSON**, not a compressed blob — the first bytes are
  `7B 0D 0A 20 20 22 50 65` (`{\r\n  "Pe`), so `CAST(PropertiesData AS varchar(max))` reads it. But the
  row exists only while the element is LIVE. A process parked on a Perform task **after** a multi-instance
  sub-process held exactly one element-data row — for the Perform task — and none for the sub-process
  element, whose parameters (the two collections and the three counters) were the ones being looked for.
- `SysProcessData.PropertiesData` is plain JSON too, and carries **structure only**. For a parked instance
  it measured 545 bytes: the schema UId, the instance UId, the manager name, and `"HM5": []` / `"HM12": {}`.
  A `Variable` process parameter written by a Formula task that had already run and completed did not
  appear in it at all.

So both halves fail, and they fail for different reasons: the element data is deleted on completion, and
the process data never held parameter values in the first place.

**The route that works** is to branch on the value and read the branch back out of
`SysProcessElementLog`, which IS persistent:

```
SubProcess1 --[conditional]--> PerformTask "COUNTERS completed=3 total=3 terminated=0"
SubProcess1 --[conditional]--> PerformTask "COUNTERS completed=0"
SubProcess1 --[default]------> PerformTask "COUNTERS something else"
```

with the condition written in the ordinary flow dialect
(`[#SubProcess1.CompletedIterationsCount#] == 3 && …`). Give each arm a caption that states the value it
proves; the caption lands in `SysProcessElementLog.Caption` and the row that exists is the answer. For a
value that is not numeric, do the same one level down — hand it to a called process that branches on it
and captions its own arms.

Two details that make the recipe usable:

- The arm must be a real element, and it must be reached. A Perform task PARKS the process, which is fine
  and is what you want if you also need the instance alive; a Formula task completes instantly if you do
  not.
- A root `SysProcessLog` row with no `CompleteDate` and status `Running` is a **parked** process, not a
  hung one.

**Why it is this way** — process instance state is the engine's working memory, serialized only as far as
it needs to survive a suspension, and pruned as each element finishes. It was never a reporting surface.
The element log is the reporting surface, and it records that an element ran, not what it held.

**What breaks if you ignore it** — you write a fixture that captures a value into a process variable, park
the process, query `SysProcessData`, find 545 bytes of structure, and conclude the capture did not happen
or the feature under test did not work. The blob is not empty and does not error; it simply has no place
for the value, which reads exactly like a value that was never written. Two fixtures were built this way
on ENG-99856 before the branch recipe replaced them.
