---
description: TEMPORARY - until Sub-task SF ("SysFile attachment storage in the Process file element") the fileProcessing block refuses SysFile storage on both sides (ProcessDesignConstants.FileProcessing.SysFileStorageSupported = false); designer-built SysFile elements are described with an issue and their setElement / setFilter refused. Delete this record with the SF change.
applies-to:
  - clio/CrtProcessBuilder/CrtProcessBuilder.gz
  - clio/Command/McpServer/Tools/ProcessDesigner/CreateBusinessProcessTool.cs
  - clio/Command/McpServer/Tools/ProcessDesigner/ModifyBusinessProcessTool.cs
  - clio/Command/McpServer/Tools/ProcessDesigner/DescribeProcessTool.cs
ticket: ENG-96505
date: 2026-10-15
---

**What is true** — read in the package source at tag `crtprocessbuilder-1.6.6.100`:
`ProcessDesignConstants.FileProcessing.SysFileStorageSupported = false`, the `NotSupportedException` in
`AttachmentStorageResolver.SysFileMode`, and `FileProcessingBlockDescriber.SysFileIssue`. An object with no
attachment object of its own (the designer's SysFile case, `platform/attachment-storage-is-chosen-per-entity-by-the-designer.md`)
is refused as an `attachments` source and as a `saveTo` target: "reading or saving the attachments of 'UsrX' needs
SysFile storage, which this version does not support yet; use an object with its own attachment object, or use the
files in the process". A designer-built element that already stores SysFile is described without loss, with an
issue saying this version "describes but does not write" it, and `setElement fileProcessing` / `setFilter` on it
are refused.

**Why it is this way** — decision D15 (option 2): the SysFile path has no runtime evidence (0 of 30 shipped
elements use it), and the measurements that would prove it (M7, M8, M21, M23, M25) include designer probes the user
must build. Keeping them off ENG-96505's critical path meant refusing SysFile until a separate Sub-task measures it.

**What breaks if you ignore it** — clio guidance or e2e tests that build an `attachments` element on an object such
as "Account address" expect success and get the refusal; a test that expects the refusal turns red the day SF
flips the switch. If M23 fails, SF may ship SysFile sources only and keep the target refusal, so do not assume both
sides lift together. **Delete this record in the SF clio PR (CL-SF)**; its Jira key is not assigned yet.
