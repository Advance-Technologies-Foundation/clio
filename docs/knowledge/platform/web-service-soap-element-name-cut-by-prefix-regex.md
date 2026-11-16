---
description: the platform reads a SOAP element path segment with an unanchored prefix:name regex, so a name with a dot or other character is cut
applies-to:
  - clio/Command/WebServiceDesigner/WebServiceDefinitionBuilder.cs
  - clio/Command/WebServiceDesigner/WebServiceResponseMasker.cs
ticket: ENG-105475
date: 2026-11-12
---

**What is true** — Terrasoft.Services `SoapBodyBuilder.ParseElementPath` splits a SOAP parameter path on `/` and reads
each `prefix:name` segment with the unanchored pattern `([a-zA-Z0-9_\-]+):([a-zA-Z0-9_\-]+)`. The element name stops at
the first other character, so `tem:Api.Key` is written as `<tem:Api>`, silently; a segment without a colon is used as
written. Traced from the platform source, not measured on a stand.

**Why it is this way** — the builder only needs the prefix to look up the namespace and never checks that the match
covers the whole segment.

**What breaks if you ignore it** — a definition with such a name saves fine and sends an element the API does not
expect, and a masker that looks the value up by the full name (`Api.Key`, or its dotted tail `Key`) misses it or takes
another element's value. clio refuses such a segment when it builds a SOAP method, and the masker reads the name the
same way the platform does.
