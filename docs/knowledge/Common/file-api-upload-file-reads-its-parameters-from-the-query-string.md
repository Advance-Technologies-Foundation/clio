---
description: FileApiService/UploadFile with a Content-Range header reads entitySchemaName, columnName, fileId, parentColumnName and parentColumnValue from the query string; it answers HTTP 200 success:false with a stack trace in errorInfo, a file security rejection is HTTP 400 {"error":...}, and a call without a parent creates an orphan file record
applies-to:
  - clio/Common/RecordFileUploader.cs
  - clio/Common/IRecordFileUploader.cs
  - clio/Command/UploadFileCommand.cs
  - clio/Command/McpServer/Tools/UploadFileTool.cs
ticket: ENG-104665
date: 2026-10-27
---

**What is true** — measured on stand `se102760` (10.2.760, .NET 8, PostgreSQL), 2026-10-27:

- `POST /rest/FileApiService/UploadFile` with a `Content-Range: bytes 0-<n-1>/<n>` header and the raw
  bytes as the body is a chunked upload. `FileUploadInfo` then reads every parameter from the QUERY
  STRING (without `Content-Range` it reads the multipart form instead): `totalFileLength`, `fileId`
  (becomes the record id), `fileName`, `mimeType`, `columnName=Data`, `entitySchemaName`,
  `parentColumnName`, `parentColumnValue`. `ICreatioApplicationClient.UploadImageAsync` builds exactly
  this request, so `upload-file` reuses it with a different URL. The obsolete `Upload` operation
  (since 7.19.0) is not used.
- Every service-side failure is HTTP 200 with `{"success":false,"errorInfo":{"errorCode","message",
  "stackTrace"}}`. `message` carries server text (for example the PostgreSQL constraint name in
  `23503: insert or update on table "ContactFile" violates foreign key constraint ...`). Observed
  codes: `ItemNotFoundException` "Item with name ..." (unknown entity), "Collection item with name
  ... not found." (unknown parent column), `Value "Data" was not found.` (entity without a Data
  column), `DbOperationException` 23503 (parent record missing).
- The platform file security filter runs BEFORE the service. A file whose extension does not match
  its content (random bytes named `.bin`) gets HTTP 400 `{"error":"The extension of the file does not
  correspond to the file type signature"}` and nothing is stored.
- A call without `parentColumnName`/`parentColumnValue` answers `success:true` and creates a file
  record that belongs to no record.
- The stored bytes are readable at `odata/<Entity>(<fileId>)/Data`.

**Why it is this way** — the file API is the only write path for a file entity's `Data` column
(`Edm.Stream`, see `../McpServer/odata-create-model-validation-rejection-is-a-confirmed-no-write.md`).
`RecordFileUploader` therefore never copies `errorInfo.message` (it maps the known codes to its own
text), treats the 400 `{"error":...}` answer as a definite rejection, requires a parent column and id,
and proves success by a byte-identical readback.

**What breaks if you ignore it** — parameters sent as multipart fields together with `Content-Range`
are ignored, because `FileUploadInfo` (CrtCoreBase) reads only the query string for a chunked upload. Copying
`errorInfo.message` puts a stack trace and database object names into the MCP transcript. Treating
the security-filter 400 as "not confirmed" sends the agent to look for a record that was never
created. Making the parent optional lets an agent create file records that no page shows.
