## update-shell Command Specification

### Purpose
Create the `clio update-shell` command that can be invoked from any directory inside the monorepo. It packages the shell build and uploads it to Creatio via cliogate.

### Main Steps
1. Locate folder `dist/apps/studio-enterprise/shell` (relative to repository root).
2. Compress its contents into a single gzip archive.
3. Read Creatio system setting `MaxFileSize` (value in MB). If archive size exceeds current value, update it to `ArchiveSizeMB + 5`.
4. Call endpoint `CreatioApiGateway/UploadStaticFile` passing the archive and extraction path `Shell`.

### Command Parameters
- `-e ENV_NAME` — required target environment name.
- `--build` — optional: before packaging run `npm run build:shell` in repo root.

### Behavior
- Must work from any subdirectory (determine root via presence of `package.json` and `apps` folder or other reliable marker).
- All filesystem operations must use System.IO.Abstractions.
- Ensure cliogate is installed before calling the endpoint.
- Temporary archive created in system temp or `.clio/tmp`.

### Example
```bash
clio update-shell -e DEV --build
```

### Errors and Validation
- Build folder missing → friendly message and non‑zero exit code.
- Folder empty → abort upload.
- Network/auth failure → readable message.
- Unable to update MaxFileSize → warn and stop.

### MaxFileSize Logic
- Archive size (bytes) → `sizeMb = Math.Ceiling(bytes / 1024d / 1024d)`.
- If `currentMax < sizeMb` → new value `sizeMb + 5`.
- Update only when needed.

### Tests (Minimum Cases)
1. Successful upload without `--build`.
2. With `--build` (simulate successful build run).
3. MaxFileSize expansion.
4. Missing directory → proper message.
5. Endpoint upload failure handling.
6. No update needed when equal.

### Acceptance Criteria
1. Works from any directory.
2. Correctly calculates and updates MaxFileSize when needed.
3. Produces valid gzip archive.
4. Uploads via UploadStaticFile with extraction path `Shell`.
5. Supports `-e` and `--build` options.
6. Documentation (README / Commands.md) updated.
7. Unit tests cover key branches and pass.

### Implementation Notes
- Reuse existing services for:
  - Determining working directory root.
  - HTTP calls to cliogate / Creatio.
  - Reading/updating system settings.
- Temp file name: `shell-{timestamp}.gz`.
- Log: path, file count, archive size (bytes & MB), MaxFileSize before/after.

### Possible Future Extensions (Out of Scope)
- `--path` custom extraction target.
- `--skip-size-check`.
- Parallel compression.