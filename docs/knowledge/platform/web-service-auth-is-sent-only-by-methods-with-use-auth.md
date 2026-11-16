---
description: A web service's authentication is sent only by methods with UseAuthInfo, for REST and SOAP alike; adding auth to the service does not reach other methods
applies-to:
  - clio/Command/WebServiceDesigner/WebServiceDesigner.cs
ticket: ENG-105475
date: 2026-11-14
---

**What is true** — `ServiceClientRequest.GetRequestData` (Terrasoft.Services) attaches the service authentication
only when the method's `UseAuthInfo` is set, whatever the service type. Measured on stand `Creatio` (.NET Framework)
on 2026-11-14 with fake setting values: SOAP 1.1, SOAP 1.2 and REST send the same `Authorization: Basic` header; a
SOAP 1.2 method saved with `use-auth` false sends none after the service gets Basic.

**Why it is this way** — the designer stores authentication on the service and a "use authentication" flag on each
method; a method registered while the service had none gets the flag off.

**What breaks if you ignore it** — giving a service authentication does not reach a method saved without it: the API
answers 401 and nothing points at the method flag. So `modify-web-service` names such methods after a change of the
authentication, and its usage guard counts new authentication as a change only for methods with the flag.
