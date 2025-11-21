# Link Package Store to Environment Packages

## Task Overview

Create a command similar to `link4r` that links packages from a PackageStore to environment packages with multi-branch version control support.

## Command Name
`link-package-store` (alias: `lps`)

## Problem Statement

The command should:
1. Accept a path to a PackageStore directory containing packages
2. Link ONLY packages that exist in BOTH the PackageStore AND the application/environment
3. Skip packages not present in the application (do not add new packages)
4. Support multi-branch package version management in PackageStore

## PackageStore Structure

```
PackageStore/
├── {Package_name}/
│   ├── {branch_name}/
│   │   ├── {version}/
│   │   │   └── [package content]
│   │   └── ...
│   └── ...
└── ...
```

**Example:**
```
PackageStore/
├── cliogate/
│   ├── main/
│   │   ├── 2.0.0.37/
│   │   └── 2.0.0.38/
│   └── develop/
│       ├── 2.0.0.35/
│       └── 2.0.0.36/
└── AnotherPackage/
    ├── main/
    │   └── 1.0.0.10/
    └── develop/
        └── 1.0.0.09/
```

## Environment Package Structure

```
Environment/Packages/
├── {package_name}/
│   └── [package content]
└── ...
```

## Implementation Algorithm

### Phase 1: PackageStore Scanning
1. Scan PackageStore root directory for package names
2. For each package, enumerate all branches
3. For each branch, enumerate all versions
4. Aggregate all available versions from all branches per package

**Result:** Map of `{package_name} -> {all_versions_from_all_branches}`

### Phase 2: Environment Analysis
1. Scan environment packages directory
2. For each package, read `descriptor.json`
3. Extract `PackageVersion` from the descriptor
4. Build a map of `{package_name} -> version`

### Phase 3: Linking
1. For each package in the environment:
   - Check if package exists in PackageStore
   - Check if the required version exists in ANY branch
   - If both conditions are met:
     - Search through branches to find the exact version location
     - Create or update symbolic link from environment package to store location
     - Log success
   - If package or version not found:
     - Log warning and skip
     - Do NOT add new packages

2. Return appropriate exit code

## Package Version Detection

Version is determined from `descriptor.json` in each environment package:

```json
{
  "Descriptor": {
    "UId": "e24226f9-c177-458f-af34-9338e2699983",
    "PackageVersion": "2.0.0.37",
    "Name": "cliogate",
    "Type": 0,
    "ProjectPath": "",
    "ModifiedOnUtc": "/Date(1757629189000)/",
    "Maintainer": "Advanced Technologies Foundation",
    "DependsOn": []
  }
}
```

The `PackageVersion` field contains the version string (e.g., "2.0.0.37").

## Linking Mechanics

- Create symbolic links (not copies) for efficient disk usage
- If a link already exists, remove and recreate it
- Maintain the same directory structure as the environment package
- Links point from environment to PackageStore location

## Key Implementation Points

1. **Multi-Branch Support**: Search all branches to find the required version
2. **Version Aggregation**: Build a complete map of available versions before linking
3. **Selective Linking**: Only link packages that already exist in the environment
4. **Descriptor Parsing**: Read package versions from `descriptor.json` files
5. **Cross-Platform**: Support Windows, macOS, and Linux
6. **Error Handling**: Log warnings for missing packages/versions, continue processing
7. **Symbolic Links**: Use OS-native symlink creation for efficiency

## Command Options

- `--packageStorePath <path>` (Required): Path to PackageStore directory
- `--envPkgPath <path>` (Optional): Path to environment package folder
- `-e, --Environment <name>` (Optional, Windows only): Environment name from clio settings

## Exit Codes

- `0`: Success (all packages linked)
- `1`: Errors occurred during linking or validation

## Benefits

- Efficient disk usage through symbolic links
- Support for multiple branches and versions
- Safe linking (no unwanted packages added)
- Clear error reporting
- Cross-platform compatibility
