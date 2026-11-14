# SPEC: Send chat message element (ENG-96034)

**Jira**: ENG-96034 Send chat message element (Story; epic ENG-92704 Create BP via AI Toolkit)
**ADR**: [adr-eng-96034-send-chat-message-element.md](../adr/adr-eng-96034-send-chat-message-element.md)
**Stories**: [1](../stories/story-eng-96034-send-chat-message-element-1.md) (build, describe, refusals),
[2](../stories/story-eng-96034-send-chat-message-element-2.md) (modify semantics),
[3](../stories/story-eng-96034-send-chat-message-element-3.md) (guidance and run-time proof)
**Test plan**: [tp-eng-96034-send-chat-message-element.md](../test-plans/tp-eng-96034-send-chat-message-element.md)
**Status**: decided. The task owner decided Q1-Q18 on 2026-11-13 (Q13 deferred; it must be answered before the clio
PR merges). The analysis, the designer captures and the stand runs are kept with the team's research notes outside
this repository; this file and the ADR carry every decision the clio side depends on.

> One clio PR carries this BMAD set together with the code. The analysis proposed a separate spec-only PR first (the
> ENG-92710 / ENG-92719 precedent); the owner asked for exactly three PRs for this story (package, knowledge, clio),
> so the set travels with the clio code PR instead.

## Problem

The AI toolkit cannot build, change or explain the Process Designer's "Send chat message" element
(`SendChatMessageUserTask`, shipped by OmnichannelMessaging). Today an agent can only name the task on a generic
`userTask`, which builds an element with no channel, no contact and no message: it runs in template mode with no
template and fails. The element's lookup parameters are MAPPING-edited, so the designer stores a picked record as a
`[#Lookup.…#]` Script macro; a builder that writes the Send email shape (ConstValue + bare Guid) saves green and shows
empty fields. Its two outputs carry no direction, so the generic describe hides them.

## Users and use cases

An agent building a business process for a user who wants a proactive chat message: a case update, a reminder, an
appointment confirmation, sent through an existing chat channel to the contact of the record being processed.

Out of scope: bulk or campaign sending; reaching a contact who never wrote on the channel (SMS excepted); waiting
for the reply (the created chat is already Completed); provider (WhatsApp approved) templates (Q2).

## Requirements

| # | Requirement | Owner repo |
|---|---|---|
| R1 | A dedicated element type `sendChatMessage` with a `chatMessage` block `{from, to, messageSource, template, message, ignoreErrors, columnValues}` on create, `addElement` and `setElement` (Q4). | package (+ clio key set) |
| R2 | `from` is the chat channel and `to` ONE contact, each in the shared value-source shape (a constant, a process parameter, an element output with an optional column, a formula). A list in `to` is refused with the one-contact hint (Q4): the server binds it as an unknown `item` key and the package refuses it; clio refuses it first, before sending, and covers `from` too. | package + clio |
| R3 | `template` takes only a message template of type "Chat template" (by name, id or macro); a provider template is refused with "pick it in the designer" (Q2). `message` is plain text; a formula goes through `mappings` on `Message`. | package |
| R4 | A generic `userTask` naming `SendChatMessageUserTask` is refused on create, pointing to `type: "sendChatMessage"` (Q1). | package (+ clio wording) |
| R5 | `describe-business-process` reports the block in its write shape, so it replays unchanged; `templateName`, `outputs` (`SendStatus`, `SendStatusDetails`, always listed) and `issues` are describe-only; `ignoreErrors` is always stated. | package + clio DTO |
| R6 | `validate-process-graph` knows the `sendchatmessage` build token. | clio |
| R7 | The tool contracts name only the type, the block and the guide pointer, paid by swaps with no ceiling raise (Q10). | clio |
| R8 | The guide `process-send-chat-message` owns every rule of the block, gated on the full contract naming it (Q9). | clio-knowledge |
| R9 | clio refuses a stand whose CrtProcessBuilder predates the block, with the install hint (floor raise). | clio |

## Acceptance (clio side)

- The key check accepts the block on every route and refuses unknown keys with the block's accepted keys (TC-U-02..06).
- The describe DTO reads the block and keeps unknown members (TC-U-09).
- The descriptions and prompts name the type, the block, the guide and the Q1 refusal (TC-U-10..14); the budgets hold
  (TC-U-15).
- The bundled archive declares the block, the drift tests pass, and the floor names the chat cut (TC-U-07, 08, 16, 17).
- The e2e class covers build/describe in both modes, the refusals, the mode switch, the mapping guard, validation,
  designer parity and the ignore-errors run (TC-I-01..10), and skips with an explicit reason on a stand without
  OmnichannelMessaging, an active channel or a chat template.
