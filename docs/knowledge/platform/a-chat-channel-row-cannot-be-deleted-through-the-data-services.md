---
description: on Creatio 10.2.1122 with OmnichannelMessaging 7.8.0 a Channel row cannot be deleted through OData or DataService - the server answers ArgumentException "No mapping exists from object type System.String[] to a known managed provider native type." - so a test that creates a chat channel must create it once and reuse it, never plan to delete it
applies-to:
  - clio.mcp.e2e/SendChatMessageElementToolE2ETests.cs
ticket: ENG-96034
date: 2026-11-14
---

**What is true** — measured on a test stand (Creatio 10.2.1122, OmnichannelMessaging 7.8.0) on 2026-11-14: an SMS
`Channel` row created through `odata-create` (Id, Name, Provider = the SMS provider, IsActive, Source; no queue, no
messenger settings) could not be deleted. `odata-delete` answered "An error has occurred." and a DataService
`DeleteQuery` on the same Id answered HTTP 500 with `ArgumentException: No mapping exists from object type
System.String[] to a known managed provider native type.` The failure sits in the server's delete path for the
entity, not in the request: the row has no referencing records, and the same request shape deletes other entities.

**Why it is this way** — not investigated in the OmnichannelMessaging source; the message is the ADO.NET refusal of
an array bound as a single SQL parameter, which points at a delete-time handler of the entity.

**What breaks if you ignore it** — a fixture that creates a channel and deletes it in teardown fails its cleanup on
every run and leaves one more channel each time. `SendChatMessageElementToolE2ETests` therefore creates ONE channel,
named "Clio e2e chat channel", only on a stand with no active channel, and keeps it: the next run finds an active
channel and creates nothing. A channel left by a measurement can be deactivated, not removed, through the API.
