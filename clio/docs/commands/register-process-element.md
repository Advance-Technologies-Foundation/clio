# register-process-element

## Name

register-process-element - Generate package-owned process element registration

## Synopsis

```bash
clio register-process-element --workspace-path <PATH> --package-name <PACKAGE> --user-task-uid <GUID> --caption <TEXT>
```

## Description

Validates an existing workspace user task and creates PostgreSQL and SQL Server
after-package registration scripts. No environment connection is required.
Matching existing files and script identities are preserved; conflicts fail
without overwrite. Package installation preserves existing registrations and captions.

## Options

```bash
--workspace-path    Required. Explicit local clio workspace directory.
--package-name      Required. Owning package listed in workspace settings.
--user-task-uid     Required. Existing user-task schema UId.
--caption           Required. Caption inserted on first registration.
```

## Example

```bash
clio register-process-element --workspace-path ./MyWorkspace --package-name UsrExample --user-task-uid 90810b59-c2aa-4133-8b6b-61f57b12143c --caption "Format text"
```

## Notes

Deploy using push-pkg or push-workspace; pkg-to-db does not execute the scripts.
Keep the archive filename equal to the package name (for example `UsrExample.gz`).
Reopen the process designer to verify toolbox visibility.

## Reporting Bugs

    https://github.com/Advance-Technologies-Foundation/clio

- [Clio Command Reference](../../Commands.md#register-process-element)
