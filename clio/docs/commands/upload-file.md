# upload-file

## Command Type

    Customization commands

## Name

upload-file - upload a local file into a file entity, attach it to a record and print the file id

## Description

`upload-file` uploads a local file into a Creatio file entity, such as `ContactFile`, `AccountFile` or
`KnowledgeBaseFile`. The new file record is linked to the parent record you name. clio then reads the
stored bytes back, compares them with the local file, and prints the created file id.

Use this command for a file attached to a record. `odata-create` cannot write the binary `Data` column of
a file entity. For an image in `SysImage`, use [`upload-image`](upload-image.md).

Each call creates a new file record. The command never changes or deletes existing data.

The file is capped at 10 MB. The environment must have login/password credentials registered; OAuth-only
environments are not supported.

## Synopsis

```bash
clio upload-file <file> --entity <entity> --parent-column <column> --parent-id <id> [options]
```

## Options

```bash
<file>                          Path to the local file to upload.

--entity                        File entity schema name, for example ContactFile, AccountFile or KnowledgeBaseFile.
                                Required.

--parent-column                 Lookup column of the file entity that points to the parent record, for example Contact.
                                Required.

--parent-id                     Id (GUID) of the parent record to attach the file to. Required.

--uri               -u          Application uri

--Password          -p          User password

--Login             -l          User login (administrator permission required)

--Environment       -e          Environment name

--Maintainer        -m          Maintainer name
```

## Example

```bash
clio upload-file ./contract.pdf --entity ContactFile --parent-column Contact --parent-id 410006e1-ca4e-4502-a9ec-e54d922d2c00 -e myapp
upload the file, attach it to the contact and print its file id
```

## Notes

- The file security policy of the environment can reject a file before it is stored. A typical cause is an
  extension that does not match the file content. Rename the file or use an allowed extension.
- A failure after the file was sent also prints the file id. The record can exist. Read it with
  `odata-read` before you upload the file again.
- The parent record must exist. The command reports a missing entity, a missing parent column, or a missing
  parent record by name.
- The MCP tool `upload-file` takes the same values as `environment-name`, `file`, `entity`, `parent-column`
  and `parent-id`, and returns `{ success, file-id, error }`.

## Reporting Bugs

    https://github.com/Advance-Technologies-Foundation/clio

- [Clio Command Reference](../../Commands.md#upload-file)
