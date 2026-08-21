---
description: ExecutePostRequest<T> is constrained to where T : BaseResponse, new(), so it cannot deserialize native Creatio services that return a bare result payload such as {"GetCanExecuteOperationResult":true} - use CreatioServiceClient.PostAndDeserialize
applies-to:
  - clio/Common/IApplicationClient.cs
  - clio/Common/CreatioServiceClient.cs
  - clio/Common/CreatioRightsClient.cs
  - clio/Common/CreatioLicenseClient.cs
date: 2026-08-19
---

**What is true** — the convenient typed overload
`T ExecutePostRequest<T>(...) where T : BaseResponse, new()` is welded to the DataService-style
`{success, errorInfo}` envelope. Several native Creatio configuration services do not use that
envelope: `RightsService/GetCanExecuteOperation` answers
`{"GetCanExecuteOperationResult": true}`, `LicenseService/GetLicOperationStatuses` answers
`{"GetLicOperationStatusesResult": {...}}`. That is the reason `CreatioServiceClient` exists at
all: it calls the plain `string`-returning `ExecutePostRequest` and deserializes the body itself,
raising `InvalidOperationException` on an empty or non-JSON response.

**Why it is this way** — the generic overload also carries the re-auth detection that inspects the
raw body before deserializing (an expired session returns the HTML login page). Making it envelope
agnostic would mean losing the typed `BaseResponse` failure check every existing caller relies on.

**What breaks if you ignore it** — declaring a response DTO that inherits `BaseResponse` just to
satisfy the constraint compiles and then reads its own fields as absent: `success` is `false` and
the real result property is never populated, so a permitted operation reports "not granted". The
failure is silent and looks like a rights problem on the environment rather than a deserialization
mismatch. New Common-layer clients derive `CreatioServiceClient` instead.
