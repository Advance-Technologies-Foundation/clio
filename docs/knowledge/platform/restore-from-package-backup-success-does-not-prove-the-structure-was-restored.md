---
description: PackageInstallerService.svc/RestoreFromPackageBackup answers a failed structure update with HTTP 200, success:false and a generic message, and a repeated restore of the same backup answers success:true while the DB structure stays in the newer state
applies-to:
  - clio/Command/RestoreFromPackageBackupCommand.cs
ticket: ENG-105476
date: 2026-11-04
---

**What is true** — when the restore cannot narrow a column because existing rows are longer than the
backup's length (Text 250 back to Text 50), `RestoreFromPackageBackup` returns HTTP 200 with
`{"errorInfo":{"errorCode":"Exception","message":"Errors occured during configuration restoring from backup"},"success":false}`.
The reason (`It is not possible to change type of column ...`) is only in the installation log (`GetLogFile`).
By then the package metadata is the backup's version, the table keeps the newer structure, and the bound
data is not reverted. The same restore run again saves nothing, reverts only the data, and answers
`success:true`. Reproduced on Creatio 8.3.3.3293 (.NET Framework, PostgreSQL 16) for SR-01561629.

**Why it is this way** — `ZipPackageBackupManager.RestoreFromBackup` saves the packages and updates the
structure first, with `continueIfError`, and reverts the data only when that step succeeded. The second
run finds no packages to save, so the structure step has nothing to fail on. The Core fix (all-or-nothing
restore) is tracked in the same ticket.

**What breaks if you ignore it** — exit code `0` is read as proof that the environment is back on the
backup's version. After a restore that failed once, a second run exits `0` while the DB keeps the newer
columns. A retried request is such a second run, so `restore-configuration` sends the restore once.
Verify the DB structure after a failed restore; clio cannot see the mismatch, and the next release that
narrows the column again fails with HTTP 500 `column ... does not exist`.
