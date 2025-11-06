# Package Ignoring in Workspace Specification

## Overview

This specification describes the package filtering functionality that allows users to exclude specific packages from workspace operations through configuration settings.

## Requirements

**Objective:**
Implement a mechanism to ignore packages during workspace operations (publishing, building, installation, etc.)

**Core Requirements:**
- Package ignore list must be stored in `.clio/workspaceSettings.json` in the workspace root
- Configuration format:
	```json
	{
		"IgnorePackages": [
			"PackageName1",
			"PackageName2"
		]
	}
	```
- Packages from the `IgnorePackages` list must be completely excluded from processing during workspace operations
- If the file or key is missing, standard processing of all packages occurs
- Support for wildcard patterns (e.g., `*Test*`, `Demo*`, `?Debug`)
- All changes must be covered by unit tests
- Usage documentation must be added to README.md and Commands.md

## Implementation

### Architecture

The implementation follows SOLID principles with clean separation of concerns:

**Core Components:**
1. **IWorkspacePackageFilter** - Interface abstraction for package filtering operations
2. **WorkspacePackageFilter** - Concrete implementation with logging integration
3. **Workspace** - Main facade class integrated with filtering functionality
4. **BindingsModule** - Dependency injection configuration

### Key Classes and Methods

#### IWorkspacePackageFilter Interface
```csharp
public interface IWorkspacePackageFilter
{
    IEnumerable<string> FilterPackages(IEnumerable<string> packages, WorkspaceSettings settings);
    IEnumerable<string> FilterPackages(IEnumerable<string> packages, IEnumerable<string> ignorePatterns);
}
```

#### WorkspacePackageFilter Implementation
- **Null-Safe Processing**: Graceful handling of null inputs without throwing exceptions
- **Pattern Matching**: Uses `PackageIgnoreMatcher` utility for wildcard support (* and ?)
- **User Feedback**: Integrated logging to inform users about filtered packages
- **Defensive Programming**: Returns appropriate defaults for edge cases

#### Integration Points
- **Workspace.Install()** - Filters packages before installation
- **Workspace.PublishZipToFolder()** - Excludes ignored packages from publishing
- **Workspace.GetFilteredPackages()** - Provides filtered package list for other operations

### Behavior Specification

#### Pattern Matching Rules
- `*` matches any sequence of characters
- `?` matches any single character
- Case-insensitive matching
- Exact name matching when no wildcards are used

#### Null Handling Strategy
| Input Scenario | Behavior |
|---------------|----------|
| `packages = null` | Returns empty collection |
| `ignorePatterns = null` | Returns original packages unchanged |
| `packages = empty` | Returns empty collection |
| `ignorePatterns = empty` | Returns original packages unchanged |

#### Configuration Loading
- Workspace settings are loaded from `.clio/workspaceSettings.json`
- Missing file or missing `IgnorePackages` key results in no filtering
- Invalid JSON format is handled gracefully without breaking operations

### Testing Coverage

**Test Scenarios:**
1. **FilterPackages_WithWorkspaceSettings_FiltersCorrectly** - Validates filtering with workspace settings
2. **FilterPackages_WithIgnorePatternsList_FiltersCorrectly** - Tests direct pattern filtering
3. **FilterPackages_WithoutIgnorePatterns_ReturnsAllPackages** - Ensures no filtering when patterns are empty
4. **FilterPackages_WhenAllPackagesIgnored_ReturnsEmpty** - Validates complete filtering scenario
5. **FilterPackages_WithNullIgnorePatterns_ReturnsAllPackages** - Tests null pattern handling
6. **FilterPackages_WithNullOrEmptyPackages_ReturnsEmpty** - Tests null package input handling

## Usage Examples

### Basic Configuration
```json
{
	"IgnorePackages": [
		"TestPackage",
		"DevPackage"
	]
}
```

### Wildcard Patterns
```json
{
	"IgnorePackages": [
		"*Test*",
		"Demo*",
		"?Debug",
		"Mock*Package"
	]
}
```

### Real-World Scenario
In `.clio/workspaceSettings.json`:
```json
{
	"IgnorePackages": [
		"UnitTestFramework",
		"*Test*",
		"Demo*",
		"SampleData*",
		"DevTools"
	]
}
```

When publishing workspace:
- `UnitTestFramework` - exact match, ignored
- `MyPackageTest`, `TestHelper` - wildcard match, ignored  
- `DemoPackage`, `DemoData` - prefix match, ignored
- `SampleDataLoader` - prefix match, ignored
- `DevTools` - exact match, ignored
- `ProductionPackage` - no match, included

## Integration with Existing Commands

**Affected Commands:**
- `pushw` (push workspace) - Filters packages before pushing
- `restorew` (restore workspace) - Filters packages during restoration
- Workspace publishing operations - Excludes packages from output
- Package building operations - Skips ignored packages

**User Feedback:**
The system provides logging output when packages are filtered:
```
Package 'TestPackage' is ignored according to workspace settings
Package 'DemoHelper' is ignored according to workspace settings
```

## Technical Implementation Details

### Dependency Injection Setup
```csharp
// In BindingsModule.cs
builder.RegisterType<WorkspacePackageFilter>()
    .As<IWorkspacePackageFilter>()
    .SingleInstance();
```

### Workspace Integration
```csharp
// Constructor injection in Workspace class
public Workspace(IWorkspacePackageFilter workspacePackageFilter, /* other dependencies */)
{
    _workspacePackageFilter = workspacePackageFilter;
}

// Usage in operations
var filteredPackages = _workspacePackageFilter.FilterPackages(packages, Settings);
```

### Error Handling Strategy
- **No Exceptions**: The filtering system never throws exceptions for configuration issues
- **Graceful Degradation**: Invalid or missing configuration results in no filtering (all packages processed)
- **User Notification**: Clear logging messages inform users about filtering actions

## Performance Considerations

- **Singleton Pattern**: Filter service is registered as singleton for optimal performance
- **Lazy Evaluation**: Filtering uses LINQ with deferred execution
- **Pattern Caching**: `PackageIgnoreMatcher` optimizes regex patterns for repeated use

## Future Enhancements

Potential future improvements (not in current scope):
- Support for complex pattern expressions
- Package dependency-aware filtering
- Conditional filtering based on environment or configuration
- Performance metrics for filtering operations