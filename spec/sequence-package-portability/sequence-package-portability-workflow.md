# Package a sequence definition

Use native package data bindings; no sequence-specific serialization format is required. Prefer DataService for reading definition rows. `get-sequence-context` discovers the live schema and lookup IDs, but its compact step projection is not a full export: read the additional content fields explicitly.

## Portable closure

| Binding | Include |
|---|---|
| SequenceRuleset | Stable Id, name, enrollment eligibility, stage transitions, limits and other configured rules |
| DeliverySchedule | Stable Id, name, timezone policy and explicit supported target references |
| DeliveryScheduleSlot | Stable Id, schedule, weekday, TimeFrom and TimeTo for every configured slot |
| Sequence | Stable Id, name, Draft status, ruleset and schedule references, other intended definition settings |
| SequenceStep | Stable Id, sequence, Index, Name, type, delay/unit, priority/action, description, subject/body and email mode/threading as applicable |

Bind custom referenced rows too, or declare and verify a target dependency. Product lookup IDs must resolve in the target version. A same-name target row with a different Id is not the same binding identity; explicitly map or refuse it rather than assuming name matching. Reuse stable package-owned IDs across releases.

Exclude SequenceParticipant, Activity, execution history and mailbox credentials. Configure target-local owners, sequencers, mailboxes and audiences after installation. A portable Draft definition is not permission to start outreach.

## Existing command flow

1. Declare installed platform package dependencies supporting the definition, email fields and execution. Verify package UIds on the supported platform version.
2. Create each binding with `create-data-binding`, explicitly selecting the complete intended column projection and first row. Add remaining rows with `add-data-binding-row`. These tools take a workspace root, package name and JSON `values` string; lookup values use `{ "value": "<guid>", "displayValue": "<caption>" }`.
3. Preserve rich-text Description/Body as HTML strings. Time-only schedule columns use the platform Time type; do not treat the date portion of a readback as a meaningful schedule date.
4. Inspect native descriptor/data files and their reference closure. Compress the package with `compress`, install with `push-pkg` on a fresh compatible target, and read back the full definitions and content.
5. Reinstall and verify stable IDs and counts. Default non-key `IsForceUpdate: false` preserves customer edits; unchanged-version reinstall does not prove that changed definitions will upgrade. Test intended upgrades separately and decide overwrite policy explicitly.
6. Reopen the sequence designer, verify ordered steps, bodies and schedule, then configure target-local ownership. Activate only when intended and enroll synthetic target contacts with `enroll-sequence-participants`. Verify the native first activity and participant state; never bind runtime participants or fabricate Active rows.

A missing required platform package, unresolved lookup, incomplete binding projection or failed readback is a failed portability check. Successful upload alone is insufficient.
