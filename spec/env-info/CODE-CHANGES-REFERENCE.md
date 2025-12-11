# Code Changes Reference - ShowAppListCommand Enhancement

## Overview

This document shows the exact code changes needed to implement the enhanced ShowAppListCommand with format options.

**Files to Modify:**
1. `clio/Command/ShowAppListCommand.cs` - Main implementation
2. `clio/Commands.md` - Documentation updates

---

## File 1: ShowAppListCommand.cs

### Current Code Structure

```csharp
using System;
using Clio.UserEnvironment;
using CommandLine;

namespace Clio.Command
{
    [Verb("show-web-app-list", Aliases = new string[] { "envs" ,"show-web-app" }, 
          HelpText = "Show the list of web applications and their settings")]
    public class AppListOptions
    {
        [Value(0, MetaName = "App name", Required = false, HelpText = "Name of application")]
        public string Name { get; set; }
        
        [Option('s', "short", Required = false, HelpText = "Show short list")]
        public bool ShowShort { get; set; }
    }

    public class ShowAppListCommand : Command<AppListOptions>
    {
        private readonly ISettingsRepository _settingsRepository;

        public ShowAppListCommand(ISettingsRepository settingsRepository) {
            _settingsRepository = settingsRepository;
        }

        public override int Execute(AppListOptions options) {
            try {
                Console.OutputEncoding = System.Text.Encoding.UTF8;
                _settingsRepository.ShowSettingsTo(Console.Out, options.Name, options.ShowShort);
                return 0;
            } catch (Exception e) {
                Console.WriteLine(e.Message);
                return 1;
            }
        }
    }
}
```

### Changes Required

#### Step 1: Extend AppListOptions

Add format-related options:

```csharp
[Verb("show-web-app-list", Aliases = new string[] { "envs" ,"show-web-app" }, 
      HelpText = "Show the list of web applications and their settings")]
public class AppListOptions
{
    [Value(0, MetaName = "App name", Required = false, HelpText = "Name of application")]
    public string Name { get; set; }
    
    [Option('s', "short", Required = false, HelpText = "Show short list")]
    public bool ShowShort { get; set; }
    
    /// NEW OPTIONS ///
    [Option('f', "format", Default = "json", HelpText = "Output format: json, table, raw. Default: json")]
    public string Format { get; set; }
    
    [Option("raw", Required = false, HelpText = "Raw output (no formatting) - shorthand for --format raw")]
    public bool Raw { get; set; }
}
```

#### Step 2: Create Format Handlers

Add private methods to format output:

```csharp
private string MaskSensitiveData(string fieldName, string value) {
    if (string.IsNullOrEmpty(value)) {
        return value;
    }
    
    // Mask sensitive fields
    if (fieldName.Equals("Password", StringComparison.OrdinalIgnoreCase) ||
        fieldName.Equals("ClientSecret", StringComparison.OrdinalIgnoreCase)) {
        return "****";
    }
    
    return value;
}

private void OutputAsJson(EnvironmentSettings environment, string environmentName = null) {
    // Create a copy with masked sensitive data
    var sanitized = new {
        name = environmentName,
        uri = environment.Uri,
        login = environment.Login,
        password = MaskSensitiveData("Password", environment.Password),
        clientId = environment.ClientId,
        clientSecret = MaskSensitiveData("ClientSecret", environment.ClientSecret),
        authAppUri = environment.AuthAppUri,
        isNetCore = environment.IsNetCore,
        maintainer = environment.Maintainer,
        safe = environment.Safe,
        developerModeEnabled = environment.DeveloperModeEnabled,
        workspacePathes = environment.WorkspacePathes
    };
    
    var serializer = new Newtonsoft.Json.JsonSerializer() {
        Formatting = Newtonsoft.Json.Formatting.Indented,
        NullValueHandling = Newtonsoft.Json.NullValueHandling.Ignore
    };
    
    serializer.Serialize(Console.Out, sanitized);
}

private void OutputAsTable(Dictionary<string, EnvironmentSettings> environments) {
    Console.WriteLine($"\"appsetting file path: {_settingsRepository.AppSettingsFilePath}\"");
    
    var table = new ConsoleTables.ConsoleTable {
        Columns = { "Name", "Url", "Login", "IsNetCore" }
    };
    
    foreach (var env in environments) {
        table.Rows.Add(
            env.Key,
            env.Value.Uri ?? "",
            env.Value.Login ?? "",
            env.Value.IsNetCore ? "Yes" : "No"
        );
    }
    
    ConsoleLogger.Instance.PrintTable(table);
}

private void OutputAsRaw(EnvironmentSettings environment, string environmentName = null) {
    // Simple text output without formatting
    Console.WriteLine($"Name: {environmentName}");
    Console.WriteLine($"Uri: {environment.Uri}");
    Console.WriteLine($"Login: {environment.Login}");
    Console.WriteLine($"Password: {MaskSensitiveData("Password", environment.Password)}");
    Console.WriteLine($"ClientId: {environment.ClientId}");
    Console.WriteLine($"ClientSecret: {MaskSensitiveData("ClientSecret", environment.ClientSecret)}");
    Console.WriteLine($"AuthAppUri: {environment.AuthAppUri}");
    Console.WriteLine($"IsNetCore: {environment.IsNetCore}");
    Console.WriteLine($"Maintainer: {environment.Maintainer}");
    Console.WriteLine($"DeveloperModeEnabled: {environment.DeveloperModeEnabled}");
}
```

#### Step 3: Update Execute Method

Replace the Execute method to handle format options:

```csharp
public override int Execute(AppListOptions options) {
    try {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        
        // Determine format (raw flag takes precedence)
        string format = options.Raw ? "raw" : (options.Format ?? "json");
        
        // Handle short format (backward compatibility)
        if (options.ShowShort) {
            _settingsRepository.ShowSettingsTo(Console.Out, options.Name, showShort: true);
            return 0;
        }
        
        // Handle format options
        if (!string.IsNullOrEmpty(options.Name)) {
            // Single environment
            var environment = _settingsRepository.FindEnvironment(options.Name);
            if (environment == null) {
                Console.WriteLine($"Environment '{options.Name}' not found");
                return 1;
            }
            
            switch (format.ToLower()) {
                case "json":
                    OutputAsJson(environment, options.Name);
                    break;
                case "table":
                    Console.WriteLine($"Environment: {options.Name}");
                    OutputAsRaw(environment, options.Name); // Use raw format for single env table
                    break;
                case "raw":
                    OutputAsRaw(environment, options.Name);
                    break;
                default:
                    Console.WriteLine($"Unknown format: {format}. Use: json, table, or raw");
                    return 1;
            }
        } else {
            // All environments
            var allEnvs = _settingsRepository.GetAllEnvironments(); // Need to implement this
            
            switch (format.ToLower()) {
                case "json":
                    _settingsRepository.ShowSettingsTo(Console.Out, environment: null, showShort: false);
                    break;
                case "table":
                    OutputAsTable(allEnvs);
                    break;
                case "raw":
                    _settingsRepository.ShowSettingsTo(Console.Out, environment: null, showShort: false);
                    break;
                default:
                    Console.WriteLine($"Unknown format: {format}. Use: json, table, or raw");
                    return 1;
            }
        }
        
        return 0;
    } catch (Exception e) {
        Console.WriteLine(e.Message);
        return 1;
    }
}
```

---

## File 2: Commands.md

### Current Documentation

Find the section for `show-web-app-list` command and update it.

### Changes Required

#### Add Format Options to Command Documentation

```markdown
## show-web-app-list / envs / show-web-app

Show the list of web applications and their settings

### Syntax

```bash
clio envs [OPTIONS] [NAME]
```

### Options

| Option | Short | Long | Default | Description |
|--------|-------|------|---------|-------------|
| NAME | - | - | - | Application name (optional) |
| Format | `-f` | `--format` | json | Output format: json, table, raw |
| Raw | - | `--raw` | false | Raw output (shorthand for --format raw) |
| Short | `-s` | `--short` | false | Show short format (name and URL only) |

### Examples

#### List all environments
```bash
clio envs
clio show-web-app-list
```

#### Show specific environment
```bash
clio envs prod
clio show-web-app prod
```

#### JSON format (default)
```bash
clio envs prod -f json
clio envs prod --format json
```

Output:
```json
{
  "name": "prod",
  "uri": "https://creatio.com",
  "login": "admin",
  "password": "****",
  "clientId": "",
  "clientSecret": "****",
  "isNetCore": true,
  "maintainer": "John Doe"
}
```

#### Table format
```bash
clio envs -f table
```

Output:
```
appsetting file path: /home/user/.clio/appsettings.json

Name    | Url                       | Login | IsNetCore
--------|---------------------------|-------|----------
prod    | https://creatio.com       | admin | Yes
staging | https://staging.creatio.com | dev | Yes
```

#### Raw format
```bash
clio envs prod --raw
```

Output:
```
Name: prod
Uri: https://creatio.com
Login: admin
Password: ****
ClientId: 
ClientSecret: ****
IsNetCore: True
```

#### Short format (existing)
```bash
clio envs -s
```

### Notes

- **Sensitive Data Masking:** Password and ClientSecret are always masked with `****` in output for security
- **Backward Compatible:** All existing commands work unchanged
- **Default Format:** JSON format is the default when no format is specified
- **Raw Flag:** `--raw` is shorthand for `--format raw`

### Related Commands

- `reg-web-app` - Register/configure environment
- `unreg-web-app` - Remove environment
- `ping` - Check environment connectivity
```

---

## Implementation Order

### Phase 1: Extend AppListOptions
1. Add Format property
2. Add Raw property
3. Add validation if needed

### Phase 2: Create Format Handlers
1. Create MaskSensitiveData method
2. Create OutputAsJson method
3. Create OutputAsTable method
4. Create OutputAsRaw method

### Phase 3: Update Execute Method
1. Parse format option (with Raw flag precedence)
2. Handle short format (backward compatibility)
3. Route to appropriate formatter
4. Handle errors

### Phase 4: Update Documentation
1. Find show-web-app-list section in Commands.md
2. Add format options table
3. Add examples for each format
4. Add notes section

---

## Key Points for Implementation

### Backward Compatibility
- ✅ Keep existing `ShowSettingsTo()` calls for short format
- ✅ Default format is JSON (matches existing behavior)
- ✅ All existing options work unchanged
- ✅ No breaking changes

### Sensitive Data Masking
- ✅ Always mask Password field
- ✅ Always mask ClientSecret field
- ✅ Apply in all output formats
- ✅ Use `****` for masked values

### Error Handling
- ✅ Handle environment not found
- ✅ Handle invalid format option
- ✅ Provide helpful error messages
- ✅ Return error code 1 on failure

### Testing Requirements
- Test each format type
- Test masking in each format
- Test backward compatibility
- Test error scenarios
- Test with empty environments
- Test with many environments

---

## Dependencies

### Already Available
- ✅ `ISettingsRepository` - Environment access
- ✅ `ConsoleTables` - Table formatting library
- ✅ `Newtonsoft.Json` - JSON serialization
- ✅ `ConsoleLogger` - Console output

### May Need to Add
- Methods to get all environments (if not available)
  - Check `ISettingsRepository` interface
  - Implement if needed, or use reflection pattern from StartCommand/StopCommand

---

## Success Criteria

### ✅ Code Implementation
- [ ] All format options work (json, table, raw)
- [ ] Backward compatibility maintained
- [ ] No exceptions thrown
- [ ] Sensitive data properly masked
- [ ] Code follows Microsoft style
- [ ] No code duplication

### ✅ Testing
- [ ] All unit tests pass
- [ ] New format tests added (8-10 cases)
- [ ] Backward compatibility tests pass
- [ ] Error scenarios handled
- [ ] Coverage >= 85%

### ✅ Documentation
- [ ] Commands.md updated with new options
- [ ] Examples provided for each format
- [ ] Output examples shown
- [ ] Notes and caveats documented
- [ ] Consistent with project style

---

**Ready to Implement:** YES  
**Complexity:** Low-Medium  
**Estimated Time:** 2 hours  
**Risk Level:** Low
