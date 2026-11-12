---
description: a web service response parameter the API did not send gets its default in ParameterValues, a system-setting value included
applies-to:
  - clio/Command/WebServiceDesigner/WebServiceResponseMasker.cs
  - clio/Command/WebServiceDesigner/WebServiceDesigner.cs
ticket: ENG-105475
date: 2026-11-12
---

**What is true** — when a response path, header or cookie reads nothing, the platform fills that response parameter
with its default before it reports `ParameterValues`; a system-setting default is read with `SysSettings.GetValue`
(Terrasoft.Services `ServiceParameterValueProvider`, `XmlResponseBodyParser`, `JsonResponseBodyParser`,
`ServiceClientResponse` header and cookie filling). Measured 2026-11-12 on a .NET Framework stand (core 10.2.1122):
a REST response header parameter bound to a SecureText setting came back filled with the setting value when the API
did not send the header (clio returned it as `[masked]`).

**Why it is this way** — the default is meant to keep a process running when an API omits an optional value.

**What breaks if you ignore it** — the setting's value reaches whoever reads the parsed outputs without ever having
been sent, so masking only what the request carried misses it: a response header parameter with
`default-sys-setting` returns the live secret on a 401 that lacks the header. `test-web-service-method` therefore
masks `response-values` by code for every response parameter with a setting or secret default, a cookie, or a
credential-named header, besides the sent secrets.
