# ADR: Send chat message element (ENG-96034)

**Status**: Accepted. The task owner decided Q1-Q18 on 2026-11-13 (Q13 deferred until before the clio PR merges).
**Date**: 2026-11-13
**Jira**: ENG-96034 Send chat message element (Story; epic ENG-92704 Create BP via AI Toolkit)
**Spec**: [spec-eng-96034-send-chat-message-element.md](../prd/spec-eng-96034-send-chat-message-element.md)
**Test plan**: [tp-eng-96034-send-chat-message-element.md](../test-plans/tp-eng-96034-send-chat-message-element.md)

Sources outside the public repositories (the CrtProcessBuilder package, OmnichannelMessaging, CrtProcessDesigner,
the core platform) are cited by type name, not by path.

## Context

`SendChatMessageUserTask` (UId `eb39d4a6-55db-40ee-a8e7-705b5ad7b9c0`, OmnichannelMessaging) has nine parameters,
all of direction Variable and none with a default: `Channel` (Lookup on Channel), `Contact` (Lookup on Contact),
`MessageType` (Integer, `0` template / `1` custom; unset runs as template), `Template` (Lookup on EmailTemplate; the
page filters it to type "Chat template"), `Message` (Text; a constant lives in the schema resources), `IgnoreErrors`
(Boolean, no default), `RecordColumnValues` (column values on the created `OmniChat`), and the outputs `SendStatus`
(`Sent` / `Error`) and `SendStatusDetails`. Designer captures on a test stand (2026-11-13) measured that `Channel`,
`Contact` and `Template` constants are stored as `Script` `[#Lookup.<root schema UId>.<id>#]`, a system setting as the
typed `[#SysSettings.<Code><Guid>#]`, and an untouched IgnoreErrors box as `false` with or without a source. Stand runs
measured that an unset element STOPS the process on a failed send (unlike Send email) and that a failed send on the
regular path leaves no chat on current builds.

## Decisions

**D1 - A dedicated type and block, built on the Process file pattern (Q1, Q4).** `type: "sendChatMessage"` with a
`chatMessage` block, members `from`, `to`, `messageSource`, `template`, `message`, `ignoreErrors`, `columnValues`;
describe adds `templateName`, `outputs`, `issues`. The package configures the element in a handler `Configure` pass
with a static binder and a setElement applier; the generic `userTask` route naming the task is refused on create.
*Rejected*: the generic route with mappings (cannot type-check the template, write column values or keep the
designer's macro shape); the injected-applier pattern of Send email (touches about 29 test call sites instead of 16
for no behavioural gain).

**D2 - Reuse the shared value-source vocabulary for `from`/`to` and the change-data entry for `columnValues`.** The
package reuses `FileProcessingValueSourceDescriptor` (and its describe twin with `display`) and
`ChangeDataValueDescriptor`. clio mirrors them: `chatMessage.from`/`to` link the existing value-source key set, and
`columnValues` entries get their own label ("columnValues entry") over the same contract, so a refusal names the
member the caller wrote. A constant `from`/`to` reads back as the record id with its name in `display`.

**D3 - A list is refused on both sides; clio answers first (Q4).** The server binds a JSON list in
`chatMessage.from`/`to` as an unknown `item` key of the value-source object, and the package refuses it (with its own
one-contact hint for `to`); a bare string the serializer cannot read, so that request fails as a whole. clio's
pre-send `ProcessBlockKeyCheck` refuses both before anything is sent, names the path, covers `from` too, and says how
to reach several contacts (one element each, or a multi-instance sub-process).

**D4 - Lookup constants are written as the designer writes them (R1 of the analysis).** `Channel`, `Contact` and
`Template` constants are `Script` lookup macros, a system setting the typed macro; never the Send email
`EmailTemplateId` ConstValue shape. The clio knowledge record on the two lookup-constant forms is generalised: the
editor (value vs mapping), not the element, decides the form.

**D5 - The contracts name only the type, the block and the guide (Q9, Q10).** Every rule lives in the gated
clio-knowledge guide `process-send-chat-message`. The contract text is paid by swaps with no ceiling raise; if a swap
had not fitted, the item would have gone back to the owner with the numbers.

**D6 - Describe reports the block re-appliably (Q11, Q17).** Every write member reads back in a form the write side
takes; `message` is suppressed in template mode (the designer's copy of the template body would otherwise trip the
template-plus-message refusal on replay); `ignoreErrors` is always stated; both outputs are always listed. The
generic describe also lists `MessageType` and `Contact` under `connections[]` for this task; replaying them aborts the
edit, so the describe prompt and the guide say to ignore them (the generic suppression is a package follow-up).

**D7 - OmnichannelMessaging stays a runtime prerequisite, not a dependency.** CrtProcessBuilder's `DependsOn` stays
empty and the package never references OmnichannelMessaging types. The e2e class skips with "install
OmnichannelMessaging" where the task is missing; whether the process-designer lane stand carries it is Q13.

**D8 - Floor.** create, modify and modify-as-new-version raise their `[RequiresPackage]` literal to the chat cut: an
older server refuses the type and block as unknown keys, in a message that does not say the package is behind.

## Consequences

- One package cut, one knowledge generation and one clio rebundle; merge order package -> knowledge -> clio.
- The contract budgets are close to spent again (create 16, describe 11, modify 31 bytes of headroom after this
  change); the em dashes that paid for it are a finite source.
- The chat e2e cases prove nothing on a stand without OmnichannelMessaging; Q13 decides whether the lane proves them.
