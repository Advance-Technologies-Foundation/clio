---
description: creatio.client password login sends the Clio host TimeZoneOffset using the browser getTimezoneOffset sign convention, and Creatio applies it to the session UserConnection time zone
applies-to:
  - Directory.Packages.props
ticket: "#930"
date: 2026-08-30
---

**What is true** — `creatio.client` 1.0.40 password login sends the current Clio process host offset
as UTC-minus-local minutes, matching browser `Date.getTimezoneOffset()`. Creatio negates that value,
selects a matching system time zone, and passes it into `UserConnection.Login`. OAuth client-credentials,
bearer passthrough, and NTLM authentication do not send this password-login field.

**Why it is this way** — the browser sends the same value on password login, while the Creatio server
contract and resolution logic live outside this repository. Clio does not explicitly choose an offset,
so its forms-auth sessions intentionally follow the machine on which Clio is running.

**What breaks if you ignore it** — running Clio in a different time zone can change the session-local
date/time conversions used by Creatio. An explicit offset requires direct use of the CreatioClient
property or additive constructor; configuring OAuth credentials or passing a bearer token cannot carry it.
