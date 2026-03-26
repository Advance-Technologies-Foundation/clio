# DB Image Restore From ZIP Spec

## Purpose

This document describes the PostgreSQL template-database creation flow used when a user runs:

```bash
clio rdb --backupPath F:\CreatioBuilds\8.3.4\8.3.4.1971_StudioNet8_Softkey_PostgreSQL_ENU.zip --drop-if-exists --as-template
```

The goal is to create or refresh a reusable PostgreSQL template database from a backup stored inside a Creatio ZIP package.

## Scope

This specification covers:

- ZIP backup discovery
- template database naming
- template lookup rules
- template metadata comments
- exact PostgreSQL commands used to create, restore, and mark the template
- useful SQL queries for operators and automation

This specification does not describe internal application classes or framework abstractions.

## Input Expectations

The input is a ZIP archive that contains a PostgreSQL backup file, usually under `db/`.

Accepted backup file extensions:

- `.backup`
- `.bak`

For PostgreSQL template mode, the expected file is `.backup`.

When scanning the ZIP:

- files under `db/` are preferred
- root-level backup files are also accepted if present
- if both `.backup` and `.bak` exist and the database type is not preselected, `.backup` is preferred

If no matching backup file is found, the operation fails.

## Source Identifier

For ZIP-based template creation, the source identifier is the ZIP file name without the `.zip` extension.

Example:

- ZIP path:
  `F:\CreatioBuilds\8.3.4\8.3.4.1971_StudioNet8_Softkey_PostgreSQL_ENU.zip`
- source identifier:
  `8.3.4.1971_StudioNet8_Softkey_PostgreSQL_ENU`

This source identifier is used:

- to look up an existing template
- to populate the database comment metadata

## Template Database Naming Convention

### New templates

When a matching template does not already exist, a new template database name is generated as:

```text
template_<32-lowercase-hex-guid>
```

Example:

```text
template_4f4dfd6e6d3f4e6f8f5c7f2f9d6c8b1a
```

### Existing template lookup

Existing templates are discovered by metadata first, not by a fixed name derived from the ZIP file.

Primary lookup rule:

```sql
SELECT datname
FROM pg_database
WHERE datistemplate = true
  AND shobj_description(oid, 'pg_database') LIKE '%sourceFile:<source-identifier>%'
LIMIT 1;
```

For the example ZIP:

```sql
SELECT datname
FROM pg_database
WHERE datistemplate = true
  AND shobj_description(oid, 'pg_database') LIKE '%sourceFile:8.3.4.1971_StudioNet8_Softkey_PostgreSQL_ENU%'
LIMIT 1;
```

Backward-compatibility fallback:

```text
template_<source-identifier>
```

Example:

```text
template_8.3.4.1971_StudioNet8_Softkey_PostgreSQL_ENU
```

This old-style name is checked only if comment-based lookup does not find a match.

## Database Comment Metadata

Each created template database is tagged with a PostgreSQL database comment.

Comment format:

```text
sourceFile:<source-identifier>|createdDate:<utc-iso8601>|version:1.0
```

Example:

```text
sourceFile:8.3.4.1971_StudioNet8_Softkey_PostgreSQL_ENU|createdDate:2026-03-26T08:15:30.1234567Z|version:1.0
```

Field meanings:

- `sourceFile`
  ZIP file name without `.zip`
- `createdDate`
  UTC timestamp in ISO 8601 round-trip format
- `version`
  Static metadata format version, currently `1.0`

The comment is written with:

```sql
COMMENT ON DATABASE "<template-db-name>" IS '<metadata-string>';
```

Example:

```sql
COMMENT ON DATABASE "template_4f4dfd6e6d3f4e6f8f5c7f2f9d6c8b1a"
IS 'sourceFile:8.3.4.1971_StudioNet8_Softkey_PostgreSQL_ENU|createdDate:2026-03-26T08:15:30.1234567Z|version:1.0';
```

To read the comment back:

```sql
SELECT obj_description(oid, 'pg_database')
FROM pg_database
WHERE datname = '<template-db-name>';
```

## Sequence Of Steps

### 1. Derive the source identifier

Take the ZIP file name and remove `.zip`.

Example:

```text
8.3.4.1971_StudioNet8_Softkey_PostgreSQL_ENU.zip
-> 8.3.4.1971_StudioNet8_Softkey_PostgreSQL_ENU
```

### 2. Extract one PostgreSQL backup from the ZIP

Extract the selected `.backup` file from the ZIP into a temporary local directory.

This produces a local extracted backup file such as:

```text
<temp>\BPMonline834StudioNet8.backup
```

### 3. Resolve the target PostgreSQL server

In this direct mode without `--dbServerName`, the template is created in the default PostgreSQL instance used by the clio infrastructure Kubernetes environment.

The workflow resolves:

- PostgreSQL host
- PostgreSQL port
- PostgreSQL username
- PostgreSQL password

### 4. Try to find an existing template for this source

Run:

```sql
SELECT datname
FROM pg_database
WHERE datistemplate = true
  AND shobj_description(oid, 'pg_database') LIKE '%sourceFile:<source-identifier>%'
LIMIT 1;
```

If nothing is found, also check whether this older name exists:

```text
template_<source-identifier>
```

### 5. If an existing template is found and `--drop-if-exists` is set, drop it

Because PostgreSQL does not allow dropping a database while `datistemplate = true`, the workflow first clears the template flag, terminates active sessions, and then drops the database.

Used SQL:

```sql
UPDATE pg_database
SET datistemplate = 'false'
WHERE datname = '<template-db-name>';
```

```sql
SELECT pg_terminate_backend(pg_stat_activity.pid)
FROM pg_stat_activity
WHERE pg_stat_activity.datname = '<template-db-name>';
```

```sql
DROP DATABASE IF EXISTS "<template-db-name>";
```

### 6. If no existing template remains, create a new empty database

Generate a new name:

```text
template_<32-lowercase-hex-guid>
```

Create it with:

```sql
CREATE DATABASE "<template-db-name>" ENCODING UTF8 CONNECTION LIMIT -1;
```

### 7. Copy the extracted backup into the PostgreSQL environment

The backup file is copied into the PostgreSQL pod backup-images folder so that `pg_restore` can access it from inside the pod.

The restore command expects the file at:

```text
/usr/local/backup-images/<backup-file-name>
```

### 8. Restore the backup into the newly created database

The restore is executed inside the PostgreSQL pod with:

```bash
pg_restore /usr/local/backup-images/<backup-file-name> \
  --dbname=<template-db-name> \
  --verbose \
  --no-owner \
  --no-privileges \
  --jobs=4 \
  --username=postgres
```

Example:

```bash
pg_restore /usr/local/backup-images/BPMonline834StudioNet8.backup \
  --dbname=template_4f4dfd6e6d3f4e6f8f5c7f2f9d6c8b1a \
  --verbose \
  --no-owner \
  --no-privileges \
  --jobs=4 \
  --username=postgres
```

### 9. Mark the restored database as a PostgreSQL template

Used SQL:

```sql
UPDATE pg_database
SET datistemplate = 'true'
WHERE datname = '<template-db-name>';
```

### 10. Write metadata comment onto the template database

Construct metadata:

```text
sourceFile:<source-identifier>|createdDate:<utc-iso8601>|version:1.0
```

Write it with:

```sql
COMMENT ON DATABASE "<template-db-name>" IS '<metadata-string>';
```

### 11. Delete the copied backup file from the PostgreSQL pod

After a successful restore and template marking, remove the backup artifact from the PostgreSQL pod.

Equivalent command:

```bash
rm /usr/local/backup-images/<backup-file-name>
```

## Useful SQL For Operators

### Find template by source identifier

```sql
SELECT datname,
       shobj_description(oid, 'pg_database') AS comment
FROM pg_database
WHERE datistemplate = true
  AND shobj_description(oid, 'pg_database') LIKE '%sourceFile:8.3.4.1971_StudioNet8_Softkey_PostgreSQL_ENU%';
```

### List all template databases with metadata comments

```sql
SELECT datname,
       datistemplate,
       shobj_description(oid, 'pg_database') AS comment
FROM pg_database
WHERE datistemplate = true
ORDER BY datname;
```

### Read the metadata comment for one template

```sql
SELECT obj_description(oid, 'pg_database')
FROM pg_database
WHERE datname = 'template_4f4dfd6e6d3f4e6f8f5c7f2f9d6c8b1a';
```

### Mark an existing database as a template

```sql
UPDATE pg_database
SET datistemplate = 'true'
WHERE datname = 'template_4f4dfd6e6d3f4e6f8f5c7f2f9d6c8b1a';
```

### Remove template flag before dropping

```sql
UPDATE pg_database
SET datistemplate = 'false'
WHERE datname = 'template_4f4dfd6e6d3f4e6f8f5c7f2f9d6c8b1a';
```

### Terminate active connections before drop

```sql
SELECT pg_terminate_backend(pg_stat_activity.pid)
FROM pg_stat_activity
WHERE pg_stat_activity.datname = 'template_4f4dfd6e6d3f4e6f8f5c7f2f9d6c8b1a';
```

### Drop template database

```sql
DROP DATABASE IF EXISTS "template_4f4dfd6e6d3f4e6f8f5c7f2f9d6c8b1a";
```

## Practical Operator Notes

- The reusable identity of a template database is the `sourceFile:<zip-name-without-extension>` token in the database comment, not the generated `template_<guid>` name.
- A newly created template name is intentionally opaque and random.
- The operator should search by metadata comment first.
- Older environments may still contain `template_<source-identifier>` databases without comment-based metadata lookup. Keeping the fallback check is useful for backward compatibility.
- The direct ZIP-based template flow restores from the extracted backup file, not directly from the ZIP stream.
- The PostgreSQL template flag and the metadata comment are both required to fully reproduce the current behavior.
