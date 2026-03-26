# DB Image Spec

## Purpose

The bundled `db` image produced by:

```bash
clio build-docker-image --template db --from <zip-or-folder>
```

is a database-backup carrier image. It does not contain Creatio application files. It exists only to distribute the contents of the source `db` folder as a container image artifact.

## Source Resolution Rules

`clio` accepts any of the following as `--from`:

- a Creatio ZIP archive that contains a `db/` directory
- an extracted directory that contains a `db/` directory
- the `db` directory itself

Resolution rules:

- for ZIP input, `clio` extracts the archive and resolves `zip-root/db`
- for directory input, `clio` resolves `<source>/db`
- if the provided source directory itself is named `db`, `clio` uses it directly

If no `db` directory can be resolved, the command fails.

## Base Image

- Base image: `busybox:1.36.1`
- Working directory: `/db`

The image uses BusyBox so restore sidecars and operators have basic shell and file utilities available, such as `sh`, `ls`, and `cp`.

## Filesystem Layout

The effective Dockerfile is:

```dockerfile
FROM busybox:1.36.1

LABEL org.creatio.capability.db="true"

WORKDIR /db
COPY db/ ./
```

As a result:

- the payload root inside the image is `/db`
- every file from the source `db/` folder is copied into `/db`
- relative directory structure under `db/` is preserved

Example source layout:

```text
db/
  BPMonline834StudioNet8.backup
```

Result inside the image:

```text
/db/BPMonline834StudioNet8.backup
```

Example nested layout:

```text
db/
  backups/
    full.backup
  metadata/
    info.json
```

Result inside the image:

```text
/db/backups/full.backup
/db/metadata/info.json
```

## Backup Discovery Contract

Consumers such as `creatio-operator` should inspect `/db`.

Assumptions:

- there is no extra wrapper directory under `/db`
- backup files are expected somewhere under `/db`
- typical file extensions are `.backup` or `.bak`
- the exact filename is inherited from the original source payload

Recommended operator behavior:

- verify the image label `org.creatio.capability.db=true`
- enumerate files under `/db`
- choose the expected backup file by extension or naming convention

## OCI Labels

Every bundled `db` image includes these OCI labels:

- `org.creatio.capability.db=true`
- `org.creatio.capability.db-source=<source-name>`

### `org.creatio.capability.db`

Marks the image as a database-backup carrier image.

### `org.creatio.capability.db-source`

Preserves the original source identity:

- for ZIP input: ZIP file name without `.zip`
- for directory input: source folder name

Example:

```text
org.creatio.capability.db=true
org.creatio.capability.db-source=8.3.4.1971_StudioNet8_Softkey_PostgreSQL_ENU
```

## Image Naming

By default, `clio` names the image:

```text
creatio-db:<sanitized-source-name>
```

Example:

```text
creatio-db:8.3.4.1971_studionet8_softkey_postgresql_enu
```

Consumers should rely on OCI labels and `/db` layout rather than the tag format.

## Operational Assumptions

`creatio-operator` or any other consumer can safely assume:

- if `org.creatio.capability.db=true`, this is a DB image
- the DB payload lives under `/db`
- the container `WORKDIR` is `/db`
- the image base is `busybox:1.36.1`
- BusyBox userland tools are available
- no Creatio application payload is expected in this image

## Not Present

Current `db` images do not include:

- a checksum label
- a manifest file describing the backup contents
- an enforced single-backup-file rule
- a database-engine label

These can be added later if the operator needs stronger validation or routing metadata.
