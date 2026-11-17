---
description: SsoSecurityEventsSettingsService answers every call with HTTP 200 and a reason code, a site failure included
applies-to:
  - clio/Command/SsoSecurityEventsCommand.cs
ticket: ENG-106080
date: 2026-11-17
---

**What is true** — The SsoSecurityEvents package's settings service (Creatio 10.2+,
`rest/SsoSecurityEventsSettingsService/<Method>`) never answers with an HTTP error for a problem it can name: refused
rights (`AccessDenied`), the feature off (`FeatureOff`), a refused events address (`SiteAddressRefused`), and even an
exception of the site itself (`Unavailable` with the exception type) all come back as `200` with a non-empty `reason`.
An empty `reason` is success. Only a site without the package answers `404`.

**Why it is this way** — The package serves the provider card, onboarding and clio from one service; the card shows
a message per reason code and must never fall into a generic failure (ENG-104753, accepted by the PO on 2026-11-11).

**What breaks if you ignore it** — A caller that judges success by the HTTP status (or by clio's exit code) reports
every refused call as done: an onboarding would log "events on" for a site whose provider refused the address. The
clio commands exit 0 on any reason code by design, so automation must read the printed `reason` (or `reason` in
`--format json`).
