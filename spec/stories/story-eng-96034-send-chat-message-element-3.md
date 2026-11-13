# Story 3: Guide the agent and prove the run

**Feature**: eng-96034-send-chat-message-element
**Jira**: ENG-96034 Send chat message element (Story; epic ENG-92704 Create BP via AI Toolkit)
**Spec**: [spec-eng-96034-send-chat-message-element.md](../prd/spec-eng-96034-send-chat-message-element.md) - R8
**ADR**: [adr-eng-96034-send-chat-message-element.md](../adr/adr-eng-96034-send-chat-message-element.md) - D5, D7
**Test plan**: [tp-eng-96034-send-chat-message-element.md](../test-plans/tp-eng-96034-send-chat-message-element.md)
**Status**: review (moves with story 1)
**Repo**: clio-knowledge (the guide), clio (pointers, re-pin, run e2e)
**Depends on**: [story 1](story-eng-96034-send-chat-message-element-1.md)

---

## As a

coding agent asked to notify a contact through chat

## I want

one guide that tells me how to pick the channel, the contact and the template, when to ask the user, what
ignoreErrors does and which run-time prerequisites a send has

## So that

I build a process that actually sends, and I tell the user what to set up when it cannot

---

## Acceptance Criteria

- AC1: clio-knowledge publishes `process-send-chat-message`, gated on the full contract naming `sendChatMessage`
  (Q9); routing and the element catalog point to it; template selection asks the user instead of guessing (Q6); the
  failure wording follows Q8.
- AC2: the create, modify and describe contracts point to the guide, and `curated-knowledge-names.json` is re-pinned
  to the generation that publishes it (TC-U-10 to TC-U-12 and the guidance-name oracle).
- AC3: an ignore-errors run reaches the end with `SendStatus = Error`, and a strict run ends in Error (TC-I-09).
- AC4: column values on the created chat are proven at run time on the WireMock Telegram stub (Q18; Tier B, outside
  this repository's automated suites).

## Definition of Done

- [ ] Knowledge PR merged and re-pinned here.
- [x] TC-I-09 implemented (gated by `McpE2E:AllowDestructiveMcpTests`).
- [x] TC-I-09 run green on a stand with OmnichannelMessaging and the chat cut installed.
- [ ] Tier B (WireMock Telegram) run recorded on the Jira story.
