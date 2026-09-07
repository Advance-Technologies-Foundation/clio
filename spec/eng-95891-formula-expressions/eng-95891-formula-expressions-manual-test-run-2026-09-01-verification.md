# ENG-95891 — verification of the 2026-09-01 manual test run

Companion to `eng-95891-formula-expressions-manual-test-run-2026-09-01.md`. That run was executed by a
separate session which has since ended and is no longer reachable, so this was written here rather than
sent back to it.

Every claim below is a measurement, with the command that produced it. Where the original run's diagnosis
differs from what the measurement shows, both are stated — the observations in that report are sound; two
of its three **diagnoses** attribute the cause to the wrong thing.

## Summary

| Finding in the run | Observation | Diagnosis |
|---|---|---|
| D1 — parameter reference inside `expression` unreachable from guidance | correct | it is the pinned GENERATION, not the current article |
| D3 — the article does not fit one response | correct | same cause; closed in master and on the branch, not in what was served |
| D2 — everything dispatched through `clio-run` | correct | not re-diagnosed: no measurement was available |
| TC-11's note ("the SHORT form works") | not tested by that run | verified correct by a write |

## D1 and D3 have one cause: the served library predates both the split and this branch

The run pinned library **1.13.54**, revision `950a998`. Unpacked, that generation carries:

```
guidance/mcp/guides/processes/  ->  process-modeling.md  process-script-task.md  run-process-button.md
wc -c process-modeling.md       ->  102145
grep -c '\[#\[Parameter:{'      ->  0
```

Three articles, a 102 KB monolith, and no parameter-reference form anywhere. The commit that first
teaches the form is `4feb042` ("ENG-95891: tell the agent how to reference a parameter — it could not"),
and it is not an ancestor of that generation:

```
git log --format="%H %s" --reverse -S '[#[Parameter:{' --all -- guidance/ | head -1
git merge-base --is-ancestor <that commit> <the 1.13.54 revision>   # -> false
```

So the article the run's agent read **could not** have carried the form. Its behaviour — exhausting every
name-based spelling, then recovering the working token by building a structural `processParameter`
mapping and reading the stored value back — is what that absence looks like from the inside, and the
2.75x call cost is its price.

Measured against this branch's generation (**1.13.65**, unreleased), through an isolated `CLIO_HOME`:

```
get-guidance name=process-modeling  ->  24 698 characters, no spill to file
get-guidance name=process-formulas  ->  19 372 characters, no spill to file
grep -c '[#[Parameter:{' formulas.md -> 4
```

Both articles are read whole in one response, which is the acceptance criterion the run named as the one
that matters. The seven-article split (plus a ninth article, `process-formulas`, holding formulas and
conditional branch conditions) is in clio-knowledge master and on this branch; it was not in what was
served.

**The distinction worth keeping.** "Closed in master" and "closed in what the agent reads" are different
claims, and conflating them is what made D3 look contradictory: it is closed in the repository and live
in the run. Any case run on this machine measures the ACTIVE generation, whatever a branch contains —
read `info-knowledge` → `Library version` before drawing a conclusion from a guidance-dependent run. See
`docs/knowledge/McpServer/a-failed-knowledge-sync-keeps-serving-the-previous-generation.md`.

## TC-11's note is correct — verified by a write

The run states, accurately, that it did not test the short form. It was tested here against
`CrtProcessBuilder 1.4.0.18` on `krestov-test`:

```
modify-business-process
  addParameter  ShortForm2Parameter : Integer
  addMapping    targetProcessParameter=ShortForm2Parameter
                expression="[#[Parameter:{1fcffc7a-d76e-4e19-a81c-70d118ef1073}]#] + 1"
  -> exit-code 0, stored
```

Both forms are accepted. The long UId meta-path is what the SERVER writes, which is why a read-back
yields it and why an agent with no guidance finds that one first; the short form is what a human or agent
writes by hand. The note in TC-11 stands.

**A false signal worth recording, because it pointed the wrong way.** The first attempt at this check
printed "TC-11's note is wrong". Cause: `UID` is a readonly variable in bash, so `$UID` expanded to the
numeric user id instead of the parameter's uid, and the server correctly refused a reference that is not
a parameter of the process. The probe erred toward "the note is wrong" — the direction that invites
editing a correct note. Use any variable name other than `UID`.

## Do not use a field NAME as a capability probe

Paid for separately during this work, and it applies directly to verifying a manual run: one field name
lives in three contracts — create, describe and modify — and which of the three carries it is the only
thing that distinguishes a feature from its ancestor.

```
grep -c 'DataMember(Name = "caption")'  over the whole archive       -> 7
                                        in ModifyContracts.cs only   -> 0
ProcessElementUpdateDescriptor.Caption                               -> 0
```

All three numbers are true, about an archive that does **not** support an editable caption. Only a field
on a named type settles it. A probe that agrees with what you already believe is not evidence, even when
the conclusion turns out right; confirm it can come back both ways.

## State this verification left behind

- **Nothing of the original run was disturbed.** Its six processes (`UsrPrice_ComputeTotal`,
  `UsrNumbers_ComputeResult`, `UsrDue_ExtractDateParts`, `UsrAmount_SetDefault`,
  `UsrResult_SetFromTotal`, `UsrSum_ComputeTotal`, captions `BPTest ENG95891 A1…B3`) are untouched: they
  are the input for the design-time and runtime pass, which no run has performed yet.
- The production `CLIO_HOME` was **not** repointed — the original run's browser pass depends on its
  configuration, including `knowledge-allow-unsequenced` and the `.bpskills-backup` beside
  `appsettings.json`.
- The shared case harness was **also** not repointed: its git source had already been redirected to
  another branch by a third session. Measurement of 1.13.65 was done in a separate, isolated
  `CLIO_HOME` with its own knowledge root, precisely so that neither of the other two states moved.
- Left on the stand by this verification: `UsrPositiveControlProbe`, a scratch process that accumulates
  parameters as guards are probed. Not an input to anything; safe to delete.

## What is still unmeasured

- **Design time and runtime for formulas.** No run has opened a process in the designer or started one.
  Stored-level behaviour is all that any run has established.
- **The six cases against 1.13.65.** Re-running them would show whether D1 and D3 survive in the
  generation this branch actually ships. Not done here to avoid colliding with the six processes the
  browser pass needs; a re-run should give its processes a distinct name suffix.
- **D2.** Why an agent that had already loaded `create-business-process`,
  `modify-business-process` and `describe-business-process` through discovery still dispatched through
  `clio-run` is a question about the tool profile and routing. It is not re-diagnosed here because no
  measurement was available, and a guess would be worth what the other guesses in this document cost.
