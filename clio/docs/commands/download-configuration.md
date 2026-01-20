# download-configuration

## Purpose
Downloads Creatio configuration files (libraries, assemblies, and package binaries) to the workspace `.application` folder. 
This command supports three modes: downloading from a running environment, extracting from a ZIP file, or copying from a pre-extracted directory.

## Command Aliases
- `dconf`

## Usage
```bash
clio download-configuration [OPTIONS]
clio dconf [OPTIONS]
```

## Options

### Authentication Options (for Environment Mode)

| Option        | Short | Description                                    | Example                        |
|---------------|-------|------------------------------------------------|--------------------------------|
| --Environment | -e    | Environment name to download from              | `-e production`                |
| --uri         | -u    | Application URI                                | `-u https://myapp.creatio.com` |
| --Login       | -l    | User login (administrator permission required) | `-l administrator`             |
| --Password    | -p    | User password                                  | `-p mypassword`                |

### Build Mode Options

| Option  | Short | Required | Description                                     | Example             |
|---------|-------|----------|-------------------------------------------------|---------------------|
| --build | -b    | No       | Path to Creatio ZIP file or extracted directory | `-b C:\creatio.zip` |

### Additional Options

| Option  | Description                  | Example   |
|---------|------------------------------|-----------|
| --debug | Enable detailed debug output | `--debug` |

## Operation Modes

### Mode 1: Download from Running Environment

Downloads libraries and assemblies from a live Creatio instance to the workspace `.application` folder.

**Usage:**
```bash
clio download-configuration -e <ENVIRONMENT_NAME>
clio dconf -e production
```

**Requirements:**
- Valid registered Creatio environment
- Network connectivity to the environment
- Valid credentials

**What it downloads:**
- Core assemblies and binaries
- Configuration libraries
- Package binaries from packages with `Files\bin` folders

### Mode 2: Extract from ZIP File

Extracts Creatio configuration from an installation ZIP file. Useful for offline development or analyzing installations.

**Usage:**
```bash
clio download-configuration --build <PATH_TO_ZIP_FILE>
clio dconf --build C:\path\to\creatio.zip
```

**Requirements:**
- File must exist and have `.zip` extension
- File must not be empty
- ZIP must contain valid Creatio installation structure

**Process:**
1. Creates temporary directory
2. Extracts ZIP contents
3. Processes and copies files to workspace
4. **Automatically deletes temporary directory**

### Mode 3: Copy from Pre-extracted Directory

Uses an already-extracted Creatio directory. Ideal for CI/CD pipelines with pre-prepared folders.

**Usage:**
```bash
clio download-configuration --build <PATH_TO_DIRECTORY>
clio dconf --build C:\extracted\creatio
```

**Requirements:**
- Directory must exist
- Directory must not be empty
- Directory must contain valid Creatio installation structure

**Process:**
1. Detects Creatio type (NetFramework or NetCore)
2. Copies files to workspace `.application` folder
3. **Source directory is preserved** (not deleted)

### Auto-detection

The command automatically detects whether the input is a ZIP file or directory:
- **ZIP files:** Must have `.zip` extension
- **Directories:** Any path without `.zip` extension is treated as an extracted directory

## How It Works

### 1. Input Detection
Checks file extension to determine input type:
- `.zip` extension → ZIP file mode (extract + copy + cleanup)
- No `.zip` extension → Directory mode (copy only)

### 2. Creatio Type Detection

**NetFramework:**
- Detected by presence of `Terrasoft.WebApp` folder

**NetCore (NET8):**
- Detected when `Terrasoft.WebApp` folder is absent

### 3. File Copying

#### For NetFramework Applications

| Source | Destination | Description |
|--------|-------------|-------------|
| `Terrasoft.WebApp\bin` | `.application\net-framework\core-bin\` | Core binaries |
| `Terrasoft.WebApp\Terrasoft.Configuration\Lib` | `.application\net-framework\bin\` | Libraries |
| `Terrasoft.WebApp\conf\bin\{LATEST}` | `.application\net-framework\bin\` | Configuration DLLs |
| `Terrasoft.WebApp\Terrasoft.Configuration\Pkg\*\Files\bin` | `.application\net-framework\packages\{PackageName}\` | Package binaries |

**Configuration files copied:**
- `Terrasoft.Configuration.dll`
- `Terrasoft.Configuration.ODataEntities.dll`

#### For NetCore (NET8) Applications

| Source | Destination | Description |
|--------|-------------|-------------|
| Root `*.dll` and `*.pdb` | `.application\net-core\core-bin\` | Root assemblies |
| `Terrasoft.Configuration\Lib` | `.application\net-core\bin\` | Libraries |
| `conf\bin\{LATEST}` | `.application\net-core\bin\` | Configuration DLLs |
| `Terrasoft.Configuration\Pkg\*\Files\bin` | `.application\net-core\packages\{PackageName}\` | Package binaries |

**Configuration files copied:**
- `Terrasoft.Configuration.dll`
- `Terrasoft.Configuration.ODataEntities.dll`

### 4. Cleanup

- **ZIP mode:** Temporary directory is automatically deleted after processing
- **Directory mode:** Source directory is preserved for reuse

## Examples

### Download from Registered Environment
```bash
clio dconf -e development
```

### Extract from ZIP File
```bash
clio download-configuration --build C:\Downloads\creatio-8.3.3.zip
```

### Use Pre-extracted Directory (CI/CD)
```bash
clio dconf --build C:\build\extracted-creatio
```

### With Debug Output
```bash
clio dconf --build C:\creatio.zip --debug
```

## Debug Mode

Use `--debug` flag to see detailed information about file operations:

```bash
clio dconf --build C:\path\to\creatio.zip --debug
```

### Debug Output Includes:
- Input path and detection (ZIP vs Directory)
- Workspace location and source path
- Temporary directory creation (for ZIP mode)
- Detected Creatio type (NetFramework vs NetCore)
- Numbered folder detection and latest folder selection
- Each file being copied with source and destination paths
- Summary of copied/skipped files and packages
- Root assembly files (DLL/PDB) being copied
- Directory structure creation

### Example Debug Output:
```
[DEBUG] DownloadConfigurationCommand: Using build mode with path=C:\creatio.zip
[DEBUG] DownloadFromZip started: ZipFile=C:\creatio.zip
[DEBUG]   Temporary directory created: C:\Temp\clio_abc123
[DEBUG] Detected NetFramework Creatio
[DEBUG] CopyCoreBinFiles: Source=...\Terrasoft.WebApp\bin, Destination=...\.application\net-framework\core-bin
[DEBUG]   CopyAllFiles: 142 files from ...
[DEBUG] CopyConfigurationBinFiles: ConfBinPath=...
[DEBUG]   Found 3 numbered folders: 3, 2, 1
[DEBUG]   Selected latest folder: 3
[DEBUG] CopyPackages: NetFramework packages summary: Copied=15, Skipped=3
```

## Validation

The command validates input parameters before execution:

### ZIP File Validation
- ✅ File exists
- ✅ Has `.zip` extension
- ✅ File is not empty

### Directory Validation
- ✅ Directory exists
- ✅ Directory is not empty

### Error Codes
- `FILE001` - Path not found
- `FILE002` - Invalid file extension (must be `.zip`)
- `FILE003` - ZIP file is empty
- `FILE004` - Directory is empty

## Output

### Successful Execution
```
Done
```

### With Debug Mode
```
[DEBUG] DownloadConfigurationCommand: Using build mode with path=C:\creatio.zip
[DEBUG] DownloadFromZip started: ZipFile=C:\creatio.zip
[DEBUG]   Temporary directory created: C:\Temp\clio_xyz
[DEBUG] Detected NetFramework Creatio
...
Done
```

### Error Examples
```
Path not found: C:\nonexistent.zip
```
```
File must have .zip extension. Current extension: .rar
```

## Return Values
- **0:** Command executed successfully
- **1:** Error occurred during execution

## Use Cases

### Offline Development
Extract configuration without a running instance:
```bash
clio dconf --build C:\installations\creatio-8.3.3.zip
```

### Version Comparison
Analyze different Creatio versions by extracting configurations to compare:
```bash
clio dconf --build C:\creatio-8.2.zip
# Compare with
clio dconf --build C:\creatio-8.3.zip
```

### Quick Workspace Setup
Initialize workspace from installation package:
```bash
# Create workspace
clio new-workspace MyWorkspace
cd MyWorkspace

# Download configuration from ZIP
clio dconf --build C:\creatio.zip
```

### CI/CD Pipeline with Pre-extracted Folders
Use pre-prepared directories in CI/CD to avoid repeated extraction:
```bash
# CI/CD script example
# Extract once
unzip creatio.zip -d /ci-cache/creatio-extracted

# Use multiple times
clio dconf --build /ci-cache/creatio-extracted
```

### Batch Processing
Process multiple pre-extracted Creatio instances:
```bash
for dir in C:\instances\*; do
  clio dconf --build "$dir"
  # Perform analysis or testing
done
```

## Prerequisites

- Must be executed in a valid clio workspace
- For environment mode:
  - Valid registered Creatio environment
  - Network connectivity
  - Valid credentials
- For ZIP mode:
  - ZIP file must exist and contain valid Creatio structure
- For directory mode:
  - Directory must exist and contain valid Creatio structure

## Workspace Structure

After successful execution, the workspace `.application` folder contains:

### For NetFramework
```
.application/
└── net-framework/
    ├── core-bin/          # Core binaries from Terrasoft.WebApp\bin
    ├── bin/               # Libraries + configuration DLLs
    └── packages/          # Package binaries
        ├── PackageA/
        └── PackageB/
```

### For NetCore (NET8)
```
.application/
└── net-core/
    ├── core-bin/          # Root DLL and PDB files
    ├── bin/               # Libraries + configuration DLLs
    └── packages/          # Package binaries
        ├── PackageA/
        └── PackageB/
```

## Related Commands

- [`new-workspace`](./new-workspace.md) - Create a new clio workspace
- [`compile-package`](./compile-package.md) - Compile package using downloaded configuration
- [`add-package`](./add-package.md) - Add package to workspace (can auto-download configuration)

## Troubleshooting

### "Path not found" Error
**Problem:** The specified ZIP file or directory doesn't exist.

**Solution:**
- Verify the path is correct
- Use absolute paths to avoid confusion
- Check file/directory permissions

### "File must have .zip extension" Error
**Problem:** The file doesn't have a `.zip` extension.

**Solution:**
- Ensure you're using a valid Creatio installation ZIP file
- Rename the file to have `.zip` extension if it's a valid ZIP archive

### "Directory is empty" Error
**Problem:** The specified directory contains no files.

**Solution:**
- Verify the directory contains extracted Creatio files
- Check if extraction completed successfully

### No Files Copied
**Problem:** Command completes but no files appear in `.application` folder.

**Solution:**
- Enable debug mode to see what's happening: `--debug`
- Verify the source contains valid Creatio structure
- Check workspace folder exists and is writable

### Permission Denied
**Problem:** Cannot write to workspace folder.

**Solution:**
- Run command with appropriate permissions
- Check workspace folder is not read-only
- Ensure no files are locked by other processes

## Notes

- The command only copies packages that have a `Files\bin` folder
- For numbered configuration folders (e.g., `conf\bin\1`, `conf\bin\2`), the **latest** (highest number) is automatically selected
- ZIP file extraction is done to a temporary directory which is automatically cleaned up
- Directory mode preserves the source directory for reuse in CI/CD scenarios
- The workspace must be created before running this command
- Configuration files from the environment take precedence over ZIP/directory files

## Security Considerations

- Ensure ZIP files and directories are from trusted sources
- Downloaded configurations may contain sensitive assembly information
- Store workspace folders securely to protect configuration data
- Use environment-based authentication for production environments
