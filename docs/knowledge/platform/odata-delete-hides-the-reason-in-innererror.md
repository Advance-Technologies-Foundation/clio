---
description: Creatio's OData DELETE answers "An error has occurred." in error.message and puts the real refusal reason in error.innererror.message - e.g. "Item cannot be deleted because it is being used in a process." for the activity of a parked Perform task
applies-to:
  - clio/Command/RuntimeVerification/RecordDeletionService.cs
  - clio/Command/McpServer/Tools/ODataDeleteTool.cs
ticket: ENG-103024
date: 2026-10-13
---

**What is true** — a refused OData DELETE returns HTTP 500 with
`{"error":{"code":"","message":"An error has occurred.","innererror":{"message":"<reason>","type":"","stacktrace":""}}}`.
Measured on two .NET Framework stands on 2026-10-12: deleting the Activity a parked Perform task created
gives the inner message "Item cannot be deleted because it is being used in a process." (the Activity's
`ProcessElementId` is set); an FK refusal carries the PostgreSQL/SQL Server constraint text there. A DELETE of
a row that does not exist answers 404 with an empty body, which the shared transport reports as success.

**Why it is this way** — the OData layer only localizes the headline; the exception text travels in the
inner error.

**What breaks if you ignore it** — the CAADT runs saw "An error has occurred." on 12 of 23 deletes and
retried or gave up. odata-delete therefore reads the inner message (fenced as untrusted text) and adds its own
diagnosis: referencing rows from `SysEntitySchemaReference`, the `ProcessElementId` binding, and visibility.
