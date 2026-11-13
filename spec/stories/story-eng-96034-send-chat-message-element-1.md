# Story 1: Build, describe and refuse a Send chat message element through the process builder

**Feature**: eng-96034-send-chat-message-element
**Jira**: ENG-96034 Send chat message element (Story; epic ENG-92704 Create BP via AI Toolkit)
**Spec**: [spec-eng-96034-send-chat-message-element.md](../prd/spec-eng-96034-send-chat-message-element.md) - R1-R7, R9
**ADR**: [adr-eng-96034-send-chat-message-element.md](../adr/adr-eng-96034-send-chat-message-element.md) - D1-D8
**Test plan**: [tp-eng-96034-send-chat-message-element.md](../test-plans/tp-eng-96034-send-chat-message-element.md)
**Status**: in-progress (moved to review when the clio feature PR leaves draft; done in the next clio PR that touches
`spec/sprint-status.yaml` after the last merge, with only the really ticked boxes)
**Repo**: crt-process-builder, then clio-knowledge, then clio (one PR each, titled `ENG-96034 Send chat message
element`, branch `feature/ENG-96034-send-chat-message`). Stories 2 and 3 ship in the same three PRs.
**Depends on**: ENG-95986 (Send email template mode; the template resolver), ENG-91844 (element-output value sources)
**Blocks**: [story 2](story-eng-96034-send-chat-message-element-2.md), [story 3](story-eng-96034-send-chat-message-element-3.md)

---

## As a

coding agent building a business process through the clio MCP process tools

## I want

to add a Send chat message element that sends a chat template or a plain message from a chat channel to one
contact, set column values on the created chat, and read the element back in the shape I write it

## So that

a process can message the contact of the record it handles without the Process Designer, and the stored element is
the one the designer would have saved

---

## Acceptance Criteria

- AC1: create and `addElement` build `type: "sendChatMessage"` with its `chatMessage` block; clio's key check accepts
  every member and refuses any other key with the block's accepted keys and a pointer for `body`, `channel` and
  `contact` (TC-U-02, TC-U-03, TC-U-04, TC-I-01, TC-I-02).
- AC2: a list or a bare string in `from`/`to` is refused before sending with the one-contact hint (TC-U-06, TC-I-03).
- AC3: a generic `userTask` naming `SendChatMessageUserTask`, an unknown system setting on `from`, an unknown chat
  template and an email-typed template are refused and nothing is created (TC-I-03, TC-I-04).
- AC4: describe reads the block in its write shape, keeps unknown members, states `ignoreErrors`, lists both outputs
  and suppresses `message` in template mode; the described block replays as a no-op (TC-U-05, TC-U-09, TC-I-01).
- AC5: `validate-process-graph` accepts the described element (TC-U-01, TC-I-07).
- AC6: the create, modify, describe, validate and list-user-tasks contracts name the type, the block, the guide and
  the Q1 refusal within the budgets, with no ceiling raise (TC-U-10 to TC-U-15, TC-I-10).
- AC7: the bundled archive declares the block, the drift tests pass and the floor names the chat cut (TC-U-07,
  TC-U-08, TC-U-16, TC-U-17).

## Definition of Done

- [ ] Package PR merged with the chat cut; clio rebundled from it; floor raised.
- [ ] clio-knowledge PR merged; `curated-knowledge-names.json` re-pinned to its generation.
- [x] Unit tests TC-U-01 to TC-U-06 and TC-U-09 to TC-U-15 implemented and green.
- [ ] TC-U-07, TC-U-08, TC-U-16 and TC-U-17 green on the rebundled archive.
- [x] e2e TC-I-01 to TC-I-10 implemented (skip with a reason without OmnichannelMessaging, a channel or a chat template).
- [ ] e2e run green on a stand with OmnichannelMessaging and the chat cut installed.
- [ ] Q13 answered (what the process-designer lane stand carries).
