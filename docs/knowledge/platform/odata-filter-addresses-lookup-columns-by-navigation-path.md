---
description: Creatio OData $filter must address a lookup column by its navigation path (Entity/Id, Tag/Id) while an insert payload uses the flat name (EntityId, TagId) - the flat name in a filter fails with "Column by path EntityId not found in schema"
applies-to:
  - clio/Command/Branding/SetBackgroundImageCommand.cs
  - clio.tests/Command/SetBackgroundImageCommandTests.cs
ticket: ENG-92981
date: 2026-08-19
---

**What is true** — the two halves of one OData round trip name the same lookup column differently.
`EnsureMembershipForTag` reads with
`odata/SysImageInTag?$filter=Entity/Id eq {imageId} and Tag/Id eq {tagId}`, while
`PostGalleryRegistration` inserts the very same row with a body of `{ "EntityId": …, "TagId": … }`.
Both forms are required; neither works in the other position.

**Why it is this way** — the OData layer exposes a lookup as a navigation property, so a filter is
evaluated over the entity graph and has to traverse it. The write payload is a column projection of
the row itself, where the foreign key appears under its physical column name.

**What breaks if you ignore it** — a filter written with the payload spelling is rejected by the server
with `Column by path EntityId not found in schema SysImageInTag`, which reads like a schema or
permission problem rather than a syntax one and sends you looking for a missing column. The reverse
mistake (a navigation path in an insert body) is worse: the property is ignored, the row is created with
a null lookup, and the request succeeds. `SetBackgroundImageCommandTests` pins the read form; nothing
pins the write form, so re-check the payload spelling by hand when adding an OData insert.
