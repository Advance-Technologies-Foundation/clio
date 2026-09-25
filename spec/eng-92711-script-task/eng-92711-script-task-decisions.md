# ENG-92711 Script task element — decisions and stand evidence

The analysis this ticket started from (README, platform-reference, compilation, serialization-capture,
traps, plan, test-plan, purpose-and-guidance, code-generation-guidance, compilation-triggers,
corpus-catalog) is attached to the Jira issue ENG-92711 "Script task element" rather than committed here.
This file records only what the implementation decided and what the stand measured.

## Scope delivered (three repositories)

| Repository | What |
|---|---|
| crt-process-builder | `scriptTask` element (create, addElement, setElement, describe); process-level `usings[]`, `addUsing` / `removeUsing`, describe `usings[]`; process methods `methods`, `setMethods`, describe `methods` / `compiledMethods`; the save-time script notices; CrtProcessBuilder 1.6.6.30 |
| clio | describe DTOs (`DescribedScriptTask`, `DescribedUsing`); create/modify compile note GATED on the server's compile-required warning instead of appended unconditionally; tool descriptions; `[RequiresPackage]` 1.6.6.30; bundled archive 1.6.6.30; unit + E2E coverage |
| clio-knowledge | `process-script-task` rewritten around WHEN to use a script task, how clio builds one, default namespaces, usings and aliases; catalog / modeling / routing no longer call it unbuildable; libraryVersion 1.15.80 |

## Decisions (the review's open questions, answered)

| # | Question | Decision | Why |
|---|---|---|---|
| Q1 | Is "required Methods/Usings are declared" satisfied by an empty set? | Both ARE delivered here, on the owner's request (2026-09-25): usings (`usings[]`, `addUsing` / `removeUsing`) and the process methods (`methods`, `setMethods`, describe `methods` / `compiledMethods`). ENG-91852 keeps the other process properties. | Usings are what script tasks need (62% of script-task schemas declare one, 1.4% of the rest), and `Terrasoft.Configuration` is not a default import. Methods are the helpers several scripts of ONE process share; they compile into the same class and reach parameters the same way. |
| Q2 | A `requiresCompilation` response field? | Not added. The compile demand travels as a warning carrying the phrase clio already gates on. | It reuses the existing, tested gate (`WithCompileNotRequiredNote`) and needs no new wire contract. |
| Q3 | Validator rule R17 for a script-task target | Unchanged (advisory). | Out of the element's write path. |
| Q4 | Set `UseSystemSecurityContext` like the designer? | Not set - and the designer does NOT set it either. | `base-process-schema.js` defaults it to `false`; nothing sets it on a new process. The shipped `FindContactForTA` has `IJ10 = true` by its author's choice. Script tasks of a clio-built process run with the caller's rights, same as a designer-built one. |
| Q5 | Does `process-script-task` join the process guide set? | No banner change. | Kept ungated and routed, as `ProcessScriptTaskGuidanceTests` requires. |
| Q6 | Hold or raise the `[RequiresPackage]` floor? | Raised to 1.6.6.30 for create / modify / new version; the process compile (Q8) needs 1.6.6.32, where `CompileProcess` first ships. The bundled archive is 1.6.6.32. | Silent-discard shape: an older server drops a build's top-level `usings[]` and `methods` (and a `scriptTask` block riding a setElement beside another field) while answering success. The type token and the two operations would be refused loudly, the usings would not. |
| Q7 | The userTask after-activity-save script | Out of scope. | Sibling compile trigger; the gate added here is keyed on the server's warning, so a later ticket only has to emit the same phrase. |
| Q8 | Which compile makes a saved script task run? | The package compiles it: `CompileProcess` (`IWorkspaceBuilder.Build([package])`, the installer's path), reached through `compile-creatio process-name=<process>`, after asking the user. Not a side effect of the save. | On Creatio 10.x the Publish (`Build`) and `RebuildPackage` compile only packages a DESIGNER save marked, and a server-side save cannot mark one (the marking service is internal). Measured: `Build` compiled nothing, `RebuildPackage(Custom)` compiled nothing and then regenerated static content for 17 min, `--all` took 20 min; `CompileProcess` took 3 min 21 s and the edit ran. A mode of `compile-creatio` rather than a new tool, so the consent rule, the progress heartbeat, the response deadline with `compile-status`, and the one-build-at-a-time reservation are the same code. Not automatic, because a compile reloads the runtime for every user. |

Also decided without asking:

- `UseFlowEngineScriptVersion` is always `true` and not exposed. It is the palette's default and the only
  variant in which `Get`/`Set` compile (198 of 198 flag-on corpus bodies use them, 0 of 220 flag-off ones do).
- An alias on a default namespace is REFUSED; a plain default namespace is stored with a "redundant" notice.
- `Get`/`Set` names are checked on every save and reported as NOTICES, never refusals: only string literals
  are visible, and the platform's lookup is case-sensitive while every package lookup is not.
- The compile demand is raised only when the request changed C# (a new script task, a replaced body, a
  changed using); an unrelated edit keeps the compile-not-required note.

## Measured on the stand (local `Creatio`, .NET Framework, 2026-09-25)

- Serialization matches a designer-built script task (`FindContactForTA`) except `BL8`, which no element
  the package builds carries - a pre-existing gap, not this element's.
- A using's `CreatedInPackageId` is `SysPackage.Id`, not the UId; the first cut stored the UId and was fixed.
- CS0104 for `SysSettings` once `Terrasoft.Configuration` is imported, and the alias fix, reproduced against
  the stand's own assemblies.
- Never compiled: `Publish the "<name>" process before starting it`. Compiled: runs. Body edited and not
  compiled again: the PREVIOUS body runs, silently.
- Which compile picks an edit up (Q8, read from `Build.log`'s `compiled: N [...]`): `compile-configuration`
  compiled 0 packages in 57 ms; `compile-package Custom` compiled 0 and spent 17 min on static content;
  `--all` compiled 321 packages in 20 min; the package's `CompileProcess` compiled `Custom` in 3 min 21 s,
  and the methods probe went from `63` to `84`. The mechanism is recorded in
  `docs/knowledge/platform/a-server-side-process-save-is-invisible-to-the-optimized-compile.md`.
- Run results were read from `BusinessProcess.log` as well as the returned value, because the
  `ProcessEngineService` Execute endpoint returns an output even when the process then fails.

The package-side capture with the byte-level detail is `docs/script-task-element-capture.md` in
crt-process-builder.

## Left for manual QA

- A fresh designer capture of a script task + usings (the comparison above used a shipped specimen).
- Opening a clio-built script task in the designer: the body editor, the "For interpreted process" flag,
  Process properties -> Methods -> Usings.
