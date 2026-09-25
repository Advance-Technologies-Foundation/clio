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
| Q6 | Hold or raise the `[RequiresPackage]` floor? | Raised to 1.6.6.30 (the bundled archive is 1.6.6.31, which carries review fixes clio does not depend on). | Silent-discard shape: an older server drops a build's top-level `usings[]` and `methods` (and a `scriptTask` block riding a setElement beside another field) while answering success. The type token and the two operations would be refused loudly, the usings would not. |
| Q7 | The userTask after-activity-save script | Out of scope. | Sibling compile trigger; the gate added here is keyed on the server's warning, so a later ticket only has to emit the same phrase. |

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
- A changed-items build (`compile-configuration`, the designer Publish's `WorkspaceExplorerService.Build`)
  did not pick up an edited body within 12 s; the full compile (`--all`, 20 min 43 s) regenerated the source
  file with the new body and the next run returned the new result with no error in `BusinessProcess.log`.
  Hence every surface says FULL `compile-creatio` - which is also what the MCP tool runs without
  `package-name`.
- Run results were read from `BusinessProcess.log` as well as the returned value, because the
  `ProcessEngineService` Execute endpoint returns an output even when the process then fails.

The package-side capture with the byte-level detail is `docs/script-task-element-capture.md` in
crt-process-builder.

## Left for manual QA

- A fresh designer capture of a script task + usings (the comparison above used a shipped specimen).
- Opening a clio-built script task in the designer: the body editor, the "For interpreted process" flag,
  Process properties -> Methods -> Usings.
