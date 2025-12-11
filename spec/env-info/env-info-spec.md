# Environment Information Command Specification

## Overview

This specification defines the enhancement to the `clio envs` command system to provide detailed environment information retrieval. The goal is to allow users and AI agents to easily access and inspect environment configurations stored in the clio config file.

## Current State

The current `show-web-app-list` (aliases: `envs`, `show-web-app`) command displays a list of all environments with their URIs. However, there is no convenient way to view detailed settings for a specific environment by name.

### Current Command Implementation

```bash
clio envs                  # Shows all environments (short or full format)
clio envs --short          # Shows table with Name and Url
clio envs -s               # Shows table with Name and Url (short alias)
clio envs {EnvName}        # Shows specific environment settings
```

**Implementation Location:** `clio/Command/ShowAppListCommand.cs`

## Requirements

### Functional Requirements

1. **View Specific Environment Details**
   - Command: `clio env {EnvName}`
   - Purpose: Retrieve and display all configuration properties for a specific environment
   - Output: Full environment settings in JSON or table format

2. **Alternative Syntax Support**
   - Command: `clio env -e {EnvName}`
   - Purpose: Same as above, using explicit environment option flag
   - Compatibility: Both syntaxes should work

3. **View All Environments (Default Behavior)**
   - Command: `clio env`
   - Purpose: Display all configured environments (current behavior)
   - Output: Full list of all environments with their settings

4. **Existing Command Preservation**
   - Current `clio envs` command must continue to work
   - Maintain backward compatibility with all existing aliases
   - No breaking changes to existing functionality

### Use Cases

1. **Developer Workflow**
   - Developer needs to check configuration for a specific environment
   - Quick command to see all properties without opening config file manually

2. **AI Agent Integration**
   - AI agents can programmatically query environment details
   - Structured output enables automated decision-making
   - Example: AI agent checking if environment is NetCore before executing commands

3. **Configuration Verification**
   - Verify environment configuration before operations
   - Check credentials, URLs, and feature flags
   - Ensure environment is properly configured

4. **Documentation and Debugging**
   - Quick reference to see all environment properties
   - Debugging configuration issues
   - Understanding environment capabilities

## Technical Design

### Architecture Overview

```
ShowAppListCommand (existing)
    ↓
    ├─→ All environments (clio envs)
    ├─→ Specific environment (clio envs {name})
    └─→ Short format (clio envs --short)

GetEnvironmentInfoCommand (new)
    ↓
    ├─→ Specific environment (clio env {name} or clio env -e {name})
    ├─→ All environments (clio env)
    └─→ Enhanced formatting options
```

### New Command Class: GetEnvironmentInfoCommand

**Location:** `clio/Command/GetEnvironmentInfoCommand.cs`

**Class Definition:**
```csharp
[Verb("env", Aliases = new[] { "environment", "env-info" }, 
       HelpText = "Get detailed environment configuration information")]
public class GetEnvironmentInfoOptions
{
    [Value(0, MetaName = "Environment", Required = false, 
            HelpText = "Name of environment (optional)")]
    public string Environment { get; set; }

    [Option('e', "environment", Required = false, 
            HelpText = "Environment name")]
    public string EnvironmentExplicit { get; set; }

    [Option('f', "format", Default = "json", 
            HelpText = "Output format: json, table, yaml")]
    public string Format { get; set; }

    [Option("raw", Required = false, 
            HelpText = "Show raw unformatted output")]
    public bool Raw { get; set; }
}

public class GetEnvironmentInfoCommand : Command<GetEnvironmentInfoOptions>
{
    // Implementation details
}
```

### Option Resolution Logic

1. **Resolve Environment Name:**
   - Use `-e` option if provided: `Environment = options.EnvironmentExplicit`
   - Use positional argument if provided: `Environment = options.Environment`
   - If both provided, `-e` takes precedence
   - If neither provided, show all environments or use default

2. **Output Handling:**
   - If environment name is specified, show single environment details
   - If no name provided, show all environments
   - Support multiple output formats: JSON, table, YAML

### Environment Settings Structure

**Source:** `clio/Environment/ConfigurationOptions.cs`

```csharp
public class EnvironmentSettings
{
    public string Uri { get; set; }
    public string Login { get; set; }
    public string Password { get; set; } // Masked in output
    public bool IsNetCore { get; set; }
    public bool DeveloperModeEnabled { get; set; }
    public bool Safe { get; set; }
    public string Maintainer { get; set; }
    public string ClientId { get; set; }
    public string ClientSecret { get; set; }
    public string AuthAppUri { get; set; }
    public string WorkspacePathes { get; set; }
    public string EnvironmentPath { get; set; }
    // ... other properties
}
```

### Dependency Injection

The command requires:
- `ISettingsRepository` - For accessing environment configurations
- `ILogger` - For output and error logging
- `IFileSystem` - Optional, for config file operations

### Integration Points

1. **SettingsRepository Extension:**
   - May need to add helper method: `GetAllEnvironmentNames()`
   - Current: `FindEnvironment(string name)`
   - Current: `GetEnvironment(string name)`

2. **Command Registration:**
   - Register in dependency injection container (BindingsModule.cs)
   - Register verb and aliases in CommandLine parser

3. **Documentation:**
   - Update `Commands.md` with new command syntax
   - Create command documentation file: `clio/docs/commands/GetEnvironmentInfoCommand.md`

## Output Examples

### View Specific Environment

```bash
$ clio env prod
{
  "Uri": "https://prod.example.com",
  "Login": "admin",
  "Password": "***",
  "IsNetCore": true,
  "DeveloperModeEnabled": false,
  "Safe": true,
  "Maintainer": "DevOps",
  "ClientId": "client123",
  "ClientSecret": "***",
  "AuthAppUri": "https://auth.example.com",
  "WorkspacePathes": "/opt/creatio",
  "EnvironmentPath": "/opt/creatio/prod"
}
```

### View Using Explicit Option

```bash
$ clio env -e dev
{
  "Uri": "https://dev.example.com",
  "Login": "admin",
  "Password": "***",
  "IsNetCore": true,
  "DeveloperModeEnabled": true,
  "Safe": false,
  ...
}
```

### View All Environments

```bash
$ clio env
{
  "prod": { ... },
  "dev": { ... },
  "staging": { ... }
}
```

### Table Format

```bash
$ clio env prod -f table
┌─────────────────────────────────────┐
│ Environment: prod                   │
├──────────────────────┬──────────────┤
│ Property             │ Value        │
├──────────────────────┼──────────────┤
│ Uri                  │ https://...  │
│ Login                │ admin        │
│ IsNetCore            │ true         │
│ DeveloperModeEnabled │ false        │
│ Safe                 │ true         │
│ EnvironmentPath      │ /opt/creatio │
└──────────────────────┴──────────────┘
```

## Testing Strategy

### Unit Tests Location
`clio.tests/Command/GetEnvironmentInfoCommandTests.cs`

### Test Cases

1. **Basic Functionality**
   - Get single environment by name
   - Get all environments
   - Get with explicit `-e` option

2. **Option Resolution**
   - Positional argument takes precedence over default
   - Explicit `-e` option takes precedence over positional
   - Both provided - explicit wins

3. **Output Formatting**
   - JSON format (default)
   - Table format
   - YAML format (if implemented)
   - Raw output

4. **Error Handling**
   - Environment not found
   - No environments configured
   - Invalid format option
   - Config file corruption

5. **Security**
   - Password masking in output
   - ClientSecret masking in output
   - Sensitive data protection

## Implementation Phases

### Phase 1: Basic Command Structure
- [ ] Create `GetEnvironmentInfoCommand.cs`
- [ ] Define `GetEnvironmentInfoOptions` class
- [ ] Implement basic environment retrieval
- [ ] Register command in dependency injection

### Phase 2: Option Resolution
- [ ] Implement environment name resolution logic
- [ ] Support positional argument: `clio env {name}`
- [ ] Support explicit option: `clio env -e {name}`
- [ ] Handle default behavior (show all)

### Phase 3: Output Formatting
- [ ] Implement JSON output (default)
- [ ] Implement table output
- [ ] Implement formatting options
- [ ] Add masking for sensitive data

### Phase 4: Testing & Documentation
- [ ] Create unit tests
- [ ] Create integration tests
- [ ] Update Commands.md
- [ ] Create command documentation file

### Phase 5: Enhancement Features (Optional)
- [ ] YAML output format
- [ ] Filter properties
- [ ] Compare environments
- [ ] Export to file

## Backwards Compatibility

- No changes to existing `clio envs` command
- No changes to `ShowAppListCommand`
- All existing functionality preserved
- New command uses different verb (`env` vs `envs`)

## Performance Considerations

- Environment settings are loaded from config file at startup
- No additional I/O operations required beyond initial load
- Minimal memory overhead for new command
- Fast response time for single environment queries

## Security Considerations

- Password and ClientSecret fields must be masked in output
- No plaintext sensitive data in console output
- Ensure masking works for all output formats
- Consider audit logging for sensitive data access (optional)

## Future Enhancements

1. **Export Functionality**
   - Export environment settings to file
   - Export in different formats (JSON, YAML, CSV)

2. **Comparison Features**
   - Compare two environments
   - Highlight differences

3. **Validation**
   - Validate environment connectivity
   - Check required fields

4. **Filtering**
   - Filter environments by properties
   - Search environments by criteria

5. **Integration**
   - Support for environment templates
   - Environment cloning with configuration
