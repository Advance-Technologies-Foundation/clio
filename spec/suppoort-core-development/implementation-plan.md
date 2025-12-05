# Implementation Plan: link-core-src command for Creatio core development

## Overview
The command will link Creatio core source code to the deployed application, synchronizing configuration, enabling LAX mode, and creating a symlink to Terrasoft.WebHost for dynamic development.

---

## Implementation Steps

### Step 1: Create LinkCoreSrcOptions class in `clio/Command/CommandLineOptions.cs`
- Define parameters: `-e {EnvName}`, `-c {CoreDirPath}` (with `--core-path` as long form)
- Inherit from `EnvironmentOptions` to inherit base options
- ⚠️ **MODIFIED:** Before execution - ask user for confirmation about linking core (display summary, request "Continue? (Y/n)")

### Step 2: Create LinkCoreSrcOptionsValidator in new file `clio/Command/LinkCoreSrcCommand.cs`
- ⚠️ **EXTENDED:** Validate that CorePath and Environment are required
- Check that CorePath exists as a directory
- Ensure the environment is registered in clio config
- **ADD:** Validate that ConnectionStrings.config exists in the application folder
- **ADD:** Validate that appsettings.config exists in the core folder
- **ADD:** Validate that app.config exists in the core folder
- **ADD:** Validate that Terrasoft.WebHost folder exists in core (recursive search)
- **ADD:** Validate that target application folder exists and is writable

### Step 3: Create LinkCoreSrcCommand in the same file
- Inherit from `Command<LinkCoreSrcOptions>`
- Inject: `ILogger`, `IFileSystem`, `ISettingsRepository`, `IValidator<LinkCoreSrcOptions>`
- Implement logic in the `Execute()` method

### Step 4: Implement connectionstring.config synchronization
- Find connectionstring.config in application folder (case-insensitive search using GetFiles())
- Read content from the deployed application
- Copy content to connectionstring.config in core ({CoreDirPath})

### Step 5: Implement port configuration in appsettings.config
- ⚠️ **CRITICAL:** If validation in step 2 failed - throw error and do NOT start any operations, stop command execution
- Get EnvironmentSettings from repository by environment name
- Extract port from Uri (e.g., from `http://localhost:82` → `82`)
- Find and update appsettings.config in core (set port)

### Step 6: Implement LAX mode enable in app.config
- Find app.config in core
- Reuse logic from ConfigureConnectionStringRequestHandler (XML update method)
- Set CookiesSameSiteMode to Lax value

### Step 7: Implement symlink creation for Terrasoft.WebHost
- Find Terrasoft.WebHost folder in {CoreDirPath} (recursive search in subfolders)
- Determine target path for symlink: `{EnvironmentPath}/Terrasoft.WebHost`
- Call `_fileSystem.CreateSymLink()` to create the link

### Step 8: Add registration in DI container in `clio/BindingsModule.cs`
- Command and validator will be automatically registered via `RegisterAssemblyTypes()`

### Step 9: Update documentation in `clio/Commands.md`
- Add description of link-core-src command
- Specify syntax, parameters, usage examples

### Step 10: Write unit tests in new file `clio.tests/Command/LinkCoreSrcCommand.Tests.cs`
- Inherit from `BaseCommandTests<LinkCoreSrcOptions>`
- Test: successful execution, option validation, handling missing files
- Use NSubstitute for mocks, FluentAssertions for assertions

---

## Additional Considerations

### User Confirmation (Step 1):
- Display summary: "The following operations will be performed: 1) synchronize connectionstring.config 2) configure ports... etc."
- Request: "Continue execution? (Y/n)"
- If user answered "n" - finish command with exit code 0 (normal completion)

### Validation (Step 2):
- Before executing any operations, perform ALL validator checks
- Display ALL validation errors at once
- If there is at least one error - do NOT execute operations, return error code 1

### Error Handling (Step 5):
- In the `Execute()` method immediately after validation - if there are errors, log them and return 1
- This blocks execution of remaining steps (4, 5, 6, 7)