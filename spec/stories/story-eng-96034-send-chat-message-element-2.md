# Story 2: Modify a Send chat message element in place

**Feature**: eng-96034-send-chat-message-element
**Jira**: ENG-96034 Send chat message element (Story; epic ENG-92704 Create BP via AI Toolkit)
**Spec**: [spec-eng-96034-send-chat-message-element.md](../prd/spec-eng-96034-send-chat-message-element.md) - R1, R3, R5
**ADR**: [adr-eng-96034-send-chat-message-element.md](../adr/adr-eng-96034-send-chat-message-element.md) - D1, D4, D6
**Test plan**: [tp-eng-96034-send-chat-message-element.md](../test-plans/tp-eng-96034-send-chat-message-element.md)
**Status**: review (moves with story 1)
**Repo**: the same three PRs as story 1
**Depends on**: [story 1](story-eng-96034-send-chat-message-element-1.md)

---

## As a

coding agent changing an existing business process

## I want

to change a Send chat message element through `setElement` with only the members I send, switch it between a
template and a custom message, and be stopped when I bypass the block with a raw mapping

## So that

an edit never leaves the element sending the designer's copied template body (with its macros) as literal text, or a
lookup value the designer card shows empty

---

## Acceptance Criteria

- AC1: `setElement` with a partial `chatMessage` keeps every member it does not send; `columnValues: []` clears the
  column values (TC-U-02; the package's own unit tests).
- AC2: a switch to `messageSource: "custom"` is refused unless the same update carries `message`; with it, the
  template is cleared and the message written (TC-I-05).
- AC3: a raw `addMapping` onto `Template`, `MessageType`, `RecordColumnValues`, `IgnoreErrors`, or a constant onto
  `Channel`/`Contact`, is refused naming the block member (Q14); a formula on `Message` and mappings from the outputs
  stay allowed (TC-I-06; the package's own unit tests).
- AC4: the modify contract and prompt state these rules and route the block to its guide (TC-U-11).

## Definition of Done

- [x] TC-U-11 implemented and green.
- [x] TC-I-05 and TC-I-06 implemented.
- [x] TC-I-05 and TC-I-06 run green on a stand with OmnichannelMessaging and the chat cut installed.
