Total output lines: 160

=== TASK ===

Add the ability for the `dconf` command to run with the signature

```
clio dconf --build {PATH_TO_ZIP_FILE_NAME}.zip
```

When this command is executed, the specified archive must be unpacked into a temporary directory (the directory must be removed after the command finishes, whether it succeeds or not); we refer to the extracted directory as **Creatio**.

If an extracted folder contains the nested directory `Terrasoft.WebApp`, this indicates a NetFramework Creatio build.

For NetFramework you must copy:
- The contents of `Creatio\Terrasoft.WebApp\bin` into the workspace directory `.application\net-framework\core-bin`.
- The contents of `Creatio\Terrasoft.WebApp\Terrasoft.Configuration\Lib` into `.application\net-framework\bin`.
- The contents of the latest `Creatio\Terrasoft.WebApp\conf\bin\{NUMBER}` folder (choose the folder with the highest number) into `.application\net-framework\bin`. This folder must contain `Terrasoft.Configuration.dll` and `Terrasoft.Configuration.ODataEntities.dll`.
- For each package folder in `Creatio\Terrasoft.WebApp\Terrasoft.Configuration\Pkg`, if it contains `Files\bin`, copy that package folder into `.application\net-framework\packages\`.

If Creatio is not NetFramework, then it is a NET8 build and you must copy:
- All DLL and PDB files from the **root** of the Creatio directory into `.application\net-core\core-bin`.
- The contents of `Creatio\Terrasoft.Configuration\Lib` into `.application\net-core\bin`.
- The contents of the latest `Creatio\conf\bin\{NUMBER}` (choose the highest number) into `.application\net-core\bin`. This folder must contain `Terrasoft.Configuration.dll` and `Terrasoft.Configuration.ODataEntities.dll`.
- For each package directory in `Creatio\Terrasoft.Configuration\Pkg`, if it has `Files\bin\`, copy that package directory into `.application\net-core\packages\`.

**ADDITIONAL REQUIREMENT (added later):**

The command must support working not only with ZIP archives but also with already extracted directories.

When a directory path is provided (instead of a ZIP file):
- The command must automatically determine whether the path points to a ZIP file or a directory.
- If it is a directory (no `.zip` extension), process its contents directly.
- The source directory must remain untouched (it must NOT be deleted).
- This allows reusing an extracted folder multiple times without re-unpacking.

Automatic input detection:
- If the path ends with `.zip` → process it as a ZIP archive (unpack → process → delete the temporary folder).
- If the path does not end with `.zip` → treat it as an extracted directory (process in place → leave the directory untouched).


=== IMPLEMENTATION ===

✅ Task completed: 24 October 2025  
✅ NET8 update: 24 October 2025 — full NET8 support added  
✅ Directory support: 24 October 2025 — the command can now process extracted folders

Implemented functionality:
- Added the `--build (-b)` option to the `dconf` command to extract configuration from ZIP files **and** extracted directories.
- Added automatic cleanup of the temporary directory via a callback when processing ZIP archives.
- Added automatic detection of NetFramework (`Terrasoft.WebApp`) versus NET8 assemblies.
- Implemented the full copy logic for both Creatio types.

Created/updated files:
1. `clio/Command/DownloadConfigurationCommand.cs`
   - Added the `[Option('b', "build")]` BuildZipPath option.
   - Added validation for `BuildZipPath` (via `DownloadConfigurationCommandOptionsValidator`).
   - Implemented conditional routing between environment download and ZIP/directory extraction.

2. `clio/Workspace/ZipBasedApplicationDownloader.cs` (NEW FILE, ~420 lines)
   - Interface: `IZipBasedApplicationDownloader`.
   - Detection method: `IsNetFrameworkCreatio()` checks for the `Terrasoft.WebApp` directory.

   **NetFramework flow:**
   - `CopyCoreBinFiles()`: `Terrasoft.WebApp/bin` → `.application/net-framework/core-bin`.
   - `CopyLibFiles()`: `Terrasoft.WebApp/Terrasoft.Configuration/Lib` → `.application/net-framework/bin`.
   - `CopyConfigurationBinFiles()`: `Terrasoft.WebApp/conf/bin/{NUMBER}` → `.application/net-framework/bin` (select the latest folder using `OrderByDescending` and `int.Parse`).
   - `CopyPackages()`: `Terrasoft.WebApp/Terrasoft.Configuration/Pkg` → `.application/net-framework/packages` (only packages that contain `Files/bin` are copied).

   **NET8 (NetCore) flow — updated:**
   - `CopyRootAssemblies()`: copies all `*.dll` and `*.pdb` from the root folder to `.application/net-core/core-bin`.
   - `CopyNetCoreLibFiles()`: `Terrasoft.Configuration/Lib` → `.application/net-core/bin`.
   - `CopyNetCoreConfigurationBinFiles()`: `conf/bin/{NUMBER}` → `.application/net-core/bin` (latest folder logic reused).
   - `CopyNetCorePackages()`: `Terrasoft.Configuration/Pkg` → `.application/net-core/packages`.

3. `clio.tests/Workspace/ZipBasedApplicationDownloaderTests.cs` (NEW — 640+ lines)
   - Uses `NSubstitute`, `FluentAssertions`, `AutoFixture`, and utility builders (e.g., `MemFsBuilder`, `FileSystemTestHelper`).
   - Contains dedicated fixtures for NetFramework and NET8 scenarios.
   - Provides helper methods for mock file system creation, package population, and verifying copied files.


=== TESTS ===

- ✅ 18/18 unit tests pass (13 original + 5 new NET8 tests).
- ✅ No regressions in existing functionality.
- ✅ All ZIP and directory requirements are covered by tests.

Example unit tests:
- `DownloadFromZip_DetectsNetFramework_AndCopiesFiles()` ensures NetFramework builds trigger the proper copy workflow and that `Unzip()` is invoked.
- `DownloadFromZip_DetectsNetCore_AndCopiesFiles()` covers NET8 copy logic.
- `DownloadFromPath_DetectsDirectory_AndProcessesAsDirectory()` verifies that folders are processed directly, no unzip occurs, and the source directory remains untouched.


=== DEBUG LOGGING ===

✅ Added on 24 October 2025 — detailed debug logging implementation.

Functionality:
- Debug messages are printed only when the `--debug` flag is present.
- Messages are prefixed with `[DEBUG]` and describe every significant step:
  - Chosen mode (ZIP or directory).
  - Created temporary directories.
  - Copy source/destination paths for each phase.
  - Number of copied and skipped files.
  - Selected `conf/bin/{NUMBER}` folder.
  - Success summaries for NetFramework and NET8 modes.
- Full stack traces are printed in debug mode when errors occur.

Key files:
1. `clio/Command/DownloadConfigurationCommand.cs`
   - Logs the operating mode and command options.
   - Outputs the full stack trace when Program.IsDebugMode is true.

2. `clio/Workspace/ZipBasedApplicationDownloader.cs`
   - Adds `[DEBUG]` logging to every critical method in both NetFramework and NET8 paths.


=== SAMPLE DEBUG OUTPUTS ===

**ZIP mode (NetFramework):**
```
[DEBUG] DownloadConfigurationCommand: Using build mode with path=C:\creatio.zip
[DEBUG] DownloadFromZip started: ZipFile=C:\creatio.zip
[DEBUG]   Workspace root: C:\workspace
[DEBUG]   Temporary directory created: C:\Temp\clio_abc123
[DEBUG] Detected NetFramework Creatio
[DEBUG] CopyCoreBinFiles: Source=C:\Temp\clio_abc123\Terrasoft.WebApp\bin, Destination=C:\workspace\.application\net-framework\core-bin
[DEBUG]   CopyAllFiles: 142 files from C:\Temp\clio_abc123\Terrasoft.WebApp\bin
[DEBUG] CopyConfigurationBinFiles: ConfBinPath=C:\Temp\clio_abc123\Terrasoft.WebApp\conf\bin
[DEBUG]   Found 3 numbered folders: 3, 2, 1
[DEBUG]   Selected latest folder: 3, Destination=C:\workspace\.application\net-framework\bin
[DEBUG] CopyPackages: Source=C:\Temp\clio_abc123\Terrasoft.WebApp\Terrasoft.Configuration\Pkg
[DEBUG]   Destination: C:\workspace\.application\net-framework\packages
[DEBUG] NetFramework packages summary: Copied=15, Skipped=3
```

**ZIP mode (NET8):**
```
[DEBUG] DownloadConfigurationCommand: Using build mode with path=/path/to/creatio.zip
[DEBUG] DownloadFromZip started: ZipFile=/path/to/creatio.zip
[DEBUG]   Workspace root: /workspace
[DEBUG]   Temporary directory created: /tmp/clio_xyz789
[DEBUG] Detected NetCore Creatio
[DEBUG] CopyRootAssemblies: Source=/tmp/clio_xyz789, Destination=/workspace/.application/net-core/core-bin
[DEBUG]   Copied: Terrasoft.Core.dll -> /workspace/.application/net-core/core-bin/Terrasoft.Core.dll
[DEBUG]   Copied: Terrasoft.Core.pdb -> /workspace/.application/net-core/core-bin/Terrasoft.Core.pdb
[DEBUG] CopyNetCoreConfigurationBinFiles: ConfBinPath=/tmp/clio_xyz789/conf/bin
[DEBUG]   Found 3 numbered folders: 3, 2, 1
[DEBUG]   Selected latest folder: 3
[DEBUG] CopyNetCorePackages: NetCore packages summary: Copied=12, Skipped=2
```

**Directory mode (no deletion):**
```
[DEBUG] DownloadFromDirectory started: Directory=C:\extracted\creatio
[DEBUG]   Workspace root: C:\workspace
[DEBUG] Processing Creatio configuration from directory: C:\extracted\creatio
[DEBUG] Detected NetFramework Creatio
[DEBUG] CopyCoreBinFiles: Source=C:\extracted\creatio\Terrasoft.WebApp\bin, Destination=C:\workspace\.application\net-framework\core-bin
[DEBUG] Configuration download from directory completed successfully
```

Debug mode usage:
- Run: `clio dconf --build path/to/file.zip --debug`
- Mechanism: the global flag `Program.IsDebugMode` is set when `--debug` is present in the CLI args.
- Performance: minimal impact (each debug log is guarded by a simple `if` check).
- All debug messages are in English to match the project style.


=== USAGE EXAMPLES ===
```bash
# Extract configuration from a NetFramework ZIP archive
clio dconf --build C:\downloads\creatio-netframework.zip

# Extract configuration from a NET8 ZIP archive
clio dconf --build /path/to/creatio-net8.zip

# Process an already extracted directory (leaves the directory untouched)
clio dconf --build /data/creatio-extracted

# Standard environment download mode continues to work
clio dconf -e myenv

# Enable debug logging for troubleshooting
clio dconf --build /path/to/creatio.zip --debug
```
