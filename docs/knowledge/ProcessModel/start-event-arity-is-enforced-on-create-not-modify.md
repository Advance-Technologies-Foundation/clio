---
description: R1 start-event arity is refused by validate-process-graph and by the package's create-path ValidateStructure, but not by modify-business-process; wiring the guard into modify breaks 37 shipped processes
applies-to:
  - clio/Command/ProcessModel/ProcessGraphValidator.cs
ticket: ENG-91853
date: 2026-09-08
---

**What is true** — clio's R1 (`ProcessGraphValidator.cs:199`, `outs.Count != 1`) and the package's
`ValidateStructure` (`ProcessGraphBuilder.cs:959-961`, `outgoing[start].Count > 1`) both refuse a start
event with more than one outgoing flow. That parity is deliberate and the package comments say so —
the server build must not persist a graph clio calls invalid. `modify-business-process` refuses
neither: `ValidateStructure` has exactly one caller, `ProcessGraphBuilder.cs:98`, inside the create
path, and `ProcessModifyHandler` injects `IProcessGraphBuilder` only to call `GetPrimaryLane`. So one
shape gets three answers — create refuses, `validate-process-graph` errors, modify accepts — and the
runtime executes it. Modify is the outlier, not the validator. Two details that look like gaps and are
not: the zero-outgoing start is refused on create as well, by the reachability clause rather than the
arity one (`reachableFromStart` is `{start}` alone, so the end event reports "is not reachable from a
start event" — the first branch of that loop, not the end-reachability one); and both handlers do call the platform's `EnsureValidForSave` (`ProcessBuildHandler.cs:114`,
`ProcessModifyHandler.cs:119`), which is why a self-loop is refused through both doors. Modify
re-validates something narrower, not nothing.

**Why it is this way** — `ValidateStructure` judges the whole graph: reachability, start and end
arity, orphaned nodes. On the create path the caller authored every element, so the whole graph is
theirs to be judged. On the modify path it is not — the caller is editing one edge of a process
somebody else wrote, possibly years ago. `ProcessGraphBuilder.cs:96` states the scoping ("create-path
only ... must not run over an arbitrary existing process") and `:928` names the alternative: rules
about which flow kinds may leave which element are per-flow authoring rules in `FlowKindRules`, "so
they also cover the modify path this guard must not run on."

**What breaks if you ignore it** — from clio's side the asymmetry reads as a plain bug (the validator
errors on a shape modify happily stores), and the fix that suggests itself is to call
`ValidateStructure` from the modify handler. Do that and 37 processes shipped in Creatio 7.8.0 become
un-modifiable for *any* edit: 34 start events carry more than one outgoing flow and 3 carry none,
`SaveNewApiKey` among them (census over `C:/Projects/PackageStore`, start-event arity only). The
failure is also misleading — the error names the start event while the user was changing an activity
somewhere else entirely, so it reads as a tool defect rather than as a pre-existing condition in the
process being edited. If the authoring shape must be refused, refuse the `addFlow` that would give a
start event its second outgoing flow, per operation, where `FlowKindRules` already sits. Whole-graph
guard and per-operation authoring rule are different tools; this is the axis to keep them on.
