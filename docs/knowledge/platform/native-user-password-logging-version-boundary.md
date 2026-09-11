---
description: Older AdministrationService UpdateOrCreateUser implementations log plaintext password payloads
applies-to:
  - clio/Command/Administration/ManageUserCommand.cs
  - clio/Command/Administration/AdministrationService.Users.cs
ticket: clio-968
date: 2026-09-11
---

**What is true** — The older generated AdministrationServiceUsers.CrtUIv2 source logs the complete
jsonObject supplied to UpdateOrCreateUser, including UserPassword. The deployed 10.1.585.0 source
instead logs identity and changed column names. Client transport redaction cannot change native logs.

**Why it is this way** — Password writes deliberately use the native user service for its validation,
hashing and lifecycle effects. The exact first fixed build has not been established; 10.1.585.0 is the
verified lower boundary for this tool. Unversioned development builds cannot prove that property.

**What breaks if you ignore it** — A password absent from CLI/MCP output can still be recorded in
server logs. Do not lower the version floor based only on a successful login test; inspect the native
logging implementation in the proposed older build and establish an immutable source/build mapping.
