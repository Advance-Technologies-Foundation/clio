# Environment Information Command - Architecture & Design Document

## System Architecture

### High-Level Component Diagram

```
┌─────────────────────────────────────────────────────────────────┐
│                      Clio CLI Application                       │
├─────────────────────────────────────────────────────────────────┤
│                                                                 │
│  ┌──────────────────────────────────────────────────────────┐  │
│  │            Command Line Parser (CommandLine)             │  │
│  │  - Parses arguments                                      │  │
│  │  - Routes to appropriate command                         │  │
│  │  - Handles help and version                             │  │
│  └───────────────────┬──────────────────────────────────────┘  │
│                      │                                          │
│        ┌─────────────┼─────────────┐                           │
│        │             │             │                           │
│  ┌─────▼──────┐ ┌────▼─────┐ ┌───▼──────────┐                │
│  │   Existing │ │  New     │ │   Other      │                │
│  │   Commands │ │ GetEnv   │ │   Commands   │                │
│  │            │ │ Command  │ │              │                │
│  │ - ShowAppL │ │          │ │ - PingAppCmd │                │
│  │   istCmd   │ │          │ │ - StartCmd   │                │
│  │ - CloneEnv │ │          │ │ - StopCmd    │                │
│  │ - etc      │ │          │ │ - etc        │                │
│  └────────────┘ └────┬─────┘ └──────────────┘                │
│                      │                                          │
│        ┌─────────────▼──────────────┐                         │
│        │   Dependency Injection     │                         │
│        │   Container (Autofac)      │                         │
│        └─────────────┬──────────────┘                         │
│                      │                                          │
│        ┌─────────────▼────────────────────┐                  │
│        │   Infrastructure Services         │                  │
│        ├────────────────────────────────────┤                 │
│        │ ISettingsRepository                │                 │
│        │ ILogger                            │                 │
│        │ IFileSystem                        │                 │
│        │ Formatters (JSON, Table)           │                 │
│        └────────────────────────────────────┘                 │
│                      │                                          │
│        ┌─────────────▼────────────────────┐                  │
│        │   Data Layer                      │                  │
│        ├────────────────────────────────────┤                 │
│        │ Configuration File (appsettings)   │                 │
│        │ Environment Settings               │                 │
│        │ Cache (if implemented)             │                 │
│        └────────────────────────────────────┘                 │
│                                                                 │
└─────────────────────────────────────────────────────────────────┘
```

## GetEnvironmentInfoCommand Component Diagram

```
┌──────────────────────────────────────────────────────────────┐
│         GetEnvironmentInfoCommand                            │
├──────────────────────────────────────────────────────────────┤
│                                                              │
│  ┌────────────────────────────────────────────────────────┐ │
│  │  GetEnvironmentInfoOptions (Request)                   │ │
│  │  ├─ Environment: string (positional)                  │ │
│  │  ├─ EnvironmentExplicit: string (-e flag)            │ │
│  │  ├─ Format: string (json|table) [default: json]      │ │
│  │  └─ Raw: bool (raw output)                           │ │
│  └────────────────────────────────────────────────────────┘ │
│                                                              │
│  ┌────────────────────────────────────────────────────────┐ │
│  │  Execute(options) - Main Entry Point                  │ │
│  │  ├─ Resolve environment name                          │ │
│  │  ├─ Fetch from repository                            │ │
│  │  ├─ Format output                                     │ │
│  │  └─ Return status code                               │ │
│  └────────────────────────────────────────────────────────┘ │
│                                                              │
│  ┌────────────────────────────────────────────────────────┐ │
│  │  Private Methods                                       │ │
│  │  ├─ ResolveEnvironmentName()                          │ │
│  │  │  └─ Precedence: explicit > positional > default    │ │
│  │  ├─ GetEnvironmentSettings()                          │ │
│  │  ├─ GetAllEnvironments()                              │ │
│  │  ├─ FormatOutput()                                    │ │
│  │  │  ├─ FormatAsJson()                                 │ │
│  │  │  └─ FormatAsTable()                                │ │
│  │  └─ MaskSensitiveData()                              │ │
│  │     ├─ Password → ***                                │ │
│  │     ├─ ClientSecret → ***                            │ │
│  │     └─ Other secrets                                 │ │
│  └────────────────────────────────────────────────────────┘ │
│                                                              │
└──────────────────────────────────────────────────────────────┘
```

## Data Flow Diagrams

### Single Environment Query Flow

```
User Input
    │
    ▼
"clio env prod -f json"
    │
    ▼
┌──────────────────────────┐
│ Parse Arguments          │
├──────────────────────────┤
│ Environment: "prod"      │
│ Format: "json"           │
└─────────┬────────────────┘
          │
          ▼
┌──────────────────────────────┐
│ ResolveEnvironmentName()      │
├──────────────────────────────┤
│ Check: explicit (-e)? → No   │
│ Check: positional?  → "prod" │
│ Result: "prod"               │
└─────────┬────────────────────┘
          │
          ▼
┌──────────────────────────────────┐
│ ISettingsRepository              │
│   .GetEnvironment("prod")        │
├──────────────────────────────────┤
│ Returns: EnvironmentSettings     │
│ {                                │
│   Uri: "https://...",            │
│   Login: "admin",                │
│   Password: "secret123",         │
│   IsNetCore: true,               │
│   ...                            │
│ }                                │
└─────────┬──────────────────────┘
          │
          ▼
┌──────────────────────────────────┐
│ MaskSensitiveData()              │
├──────────────────────────────────┤
│ Password: "secret123" → "***"    │
│ ClientSecret: "xyz" → "***"      │
└─────────┬──────────────────────┘
          │
          ▼
┌──────────────────────────────────┐
│ FormatAsJson()                   │
├──────────────────────────────────┤
│ Serialize to JSON with indent    │
└─────────┬──────────────────────┘
          │
          ▼
┌──────────────────────────────────┐
│ Console Output                   │
├──────────────────────────────────┤
│ {                                │
│   "Uri": "https://...",          │
│   "Login": "admin",              │
│   "Password": "***",             │
│   "IsNetCore": true,             │
│   ...                            │
│ }                                │
└──────────────────────────────────┘
```

### All Environments Query Flow

```
User Input
    │
    ▼
"clio env"
    │
    ▼
┌──────────────────────────┐
│ Parse Arguments          │
├──────────────────────────┤
│ Environment: null        │
│ Format: "json" (default) │
└─────────┬────────────────┘
          │
          ▼
┌──────────────────────────────────┐
│ ResolveEnvironmentName()          │
├──────────────────────────────────┤
│ Check: explicit (-e)? → No       │
│ Check: positional?  → No         │
│ Result: null (show all)          │
└─────────┬──────────────────────┘
          │
          ▼
┌──────────────────────────────────┐
│ GetAllEnvironments()             │
├──────────────────────────────────┤
│ Iterates all env from repository │
│ For each: mask sensitive data    │
│ Returns: Dictionary<string, ...> │
└─────────┬──────────────────────┘
          │
          ▼
┌──────────────────────────────────┐
│ FormatAsJson()                   │
├──────────────────────────────────┤
│ Serialize entire dictionary      │
└─────────┬──────────────────────┘
          │
          ▼
┌──────────────────────────────────┐
│ Console Output                   │
├──────────────────────────────────┤
│ {                                │
│   "prod": { ... },               │
│   "dev": { ... },                │
│   "staging": { ... }             │
│ }                                │
└──────────────────────────────────┘
```

## Sequence Diagram - Command Execution

```
User              CLI              Command          Repository        Logger
 │                 │                  │                 │               │
 ├──"clio env prod"──>                │                 │               │
 │                 │                  │                 │               │
 │                 ├─Parse Options──>│                 │               │
 │                 │                  │                 │               │
 │                 │     Execute()────>               │               │
 │                 │      (options)    │                 │               │
 │                 │                  │                 │               │
 │                 │          ResolveEnvironmentName()  │               │
 │                 │                  │                 │               │
 │                 │                  ├─GetEnvironment("prod")─>       │
 │                 │                  │                 │               │
 │                 │                  │<───EnvironmentSettings─       │
 │                 │                  │                 │               │
 │                 │          MaskSensitiveData()       │               │
 │                 │                  │                 │               │
 │                 │          FormatAsJson()            │               │
 │                 │                  │                 │               │
 │                 │                  ├────Log Info───────────────────>│
 │                 │                  │                 │               │
 │                 │     Return JSON   │                 │               │
 │                 │<─────────────────│                 │               │
 │                 │                  │                 │               │
 │<───JSON Output──┤                  │                 │               │
 │                 │                  │                 │               │
```

## Class Diagram

```
┌─────────────────────────────────────────────┐
│         Command<GetEnvironmentInfoOptions>  │
│                  (abstract)                 │
├─────────────────────────────────────────────┤
│ + Execute(options): int                     │
└────────────────────┬───────────────────────┘
                     △
                     │ extends
                     │
┌─────────────────────────────────────────────┐
│      GetEnvironmentInfoCommand              │
├─────────────────────────────────────────────┤
│ - settingsRepository: ISettingsRepository   │
│ - logger: ILogger                           │
├─────────────────────────────────────────────┤
│ + GetEnvironmentInfoCommand(...)            │
│ + Execute(options): int                     │
│ - ResolveEnvironmentName(...): string       │
│ - GetEnvironmentSettings(...): EnvironmentSett.│
│ - GetAllEnvironments(): Dictionary          │
│ - FormatOutput(...): string                 │
│ - FormatAsJson(...): string                 │
│ - FormatAsTable(...): string                │
│ - MaskSensitiveData(...): void              │
└─────────────────────────────────────────────┘

┌─────────────────────────────────────────────┐
│    GetEnvironmentInfoOptions                │
├─────────────────────────────────────────────┤
│ + Environment: string                       │
│ + EnvironmentExplicit: string               │
│ + Format: string                            │
│ + Raw: bool                                 │
└─────────────────────────────────────────────┘

┌─────────────────────────────────────────────┐
│    ISettingsRepository                      │
│         (interface)                         │
├─────────────────────────────────────────────┤
│ + FindEnvironment(name): EnvironmentSettings│
│ + GetEnvironment(name): EnvironmentSettings │
│ + GetEnvironment(options): EnvironmentSettings│
│ + IsEnvironmentExists(name): bool           │
│ + ShowSettingsTo(writer, env, short): void │
└─────────────────────────────────────────────┘

┌──────────────────────────────────────────────┐
│    EnvironmentSettings                       │
├──────────────────────────────────────────────┤
│ + Uri: string                                │
│ + Login: string                              │
│ + Password: string                           │
│ + IsNetCore: bool                            │
│ + DeveloperModeEnabled: bool                 │
│ + Safe: bool                                 │
│ + Maintainer: string                         │
│ + ClientId: string                           │
│ + ClientSecret: string                       │
│ + AuthAppUri: string                         │
│ + WorkspacePathes: string                    │
│ + EnvironmentPath: string                    │
│ + ... (other properties)                     │
└──────────────────────────────────────────────┘
```

## Option Resolution Logic

```
Options Input:
  Environment (positional): ?
  EnvironmentExplicit (-e): ?

        │
        ▼
┌──────────────────────────────────────────┐
│  Check EnvironmentExplicit (-e flag)     │
└──────┬───────────────────────────────────┘
       │
    Has value?
    /       \
   /         \
YES           NO
│              │
│              ▼
│         ┌──────────────────────────────────┐
│         │ Check Environment (positional)   │
│         └──────┬───────────────────────────┘
│                │
│             Has value?
│             /       \
│            /         \
│          YES           NO
│          │              │
│          ├─ Use it      └─ Show all environments
│          │                 (null = all)
│          │
└──────────┴──────────────────────────────┐
           │                              │
           └─ Use explicit value          │
              (higher precedence)         │
                                          │
Final Result: environment name or null    │
```

## Formatting Decision Tree

```
Format Option (-f):

    │
    ├─ "json" ────────────────────┐
    │                              ▼
    │                   ┌───────────────────────┐
    │                   │  FormatAsJson()       │
    │                   │  - Serialize object   │
    │                   │  - Indent = 2         │
    │                   │  - NullValueHandling  │
    │                   └───────────────────────┘
    │
    ├─ "table" ───────────────────┐
    │                              ▼
    │                   ┌───────────────────────┐
    │                   │  FormatAsTable()      │
    │                   │  - Use ConsoleTables  │
    │                   │  - Columns: Name/Val  │
    │                   │  - Rows per property  │
    │                   └───────────────────────┘
    │
    └─ invalid ──────────────────┐
                                  ▼
                        ┌───────────────────────┐
                        │ Throw ArgumentException
                        │ "Unknown format: ..."
                        └───────────────────────┘

Raw Flag (--raw):
    │
    ├─ true  ──────> Output as-is, no formatting
    │
    └─ false ──────> Apply formatter based on -f
```

## Sensitive Data Masking Strategy

```
Property Analysis:

Password        ──┐
ClientSecret    ──┤
AuthSecret      ──┤
ApiKey          ──┼──> Mask with "***"
RefreshToken    ──┤
AccessToken     ──┤
Token           ──┘

Security Principle:
  - Any property name containing "password", "secret", "key", "token"
  - Marked as sensitive in EnvironmentSettings
  - Always masked in console output
  - Masked in all formats (JSON, table, raw)
  - Exception: log file may contain plaintext (audit trail)

Implementation:
  MaskSensitiveData(EnvironmentSettings settings)
    │
    ├─ For each property in settings
    │    └─ Check if property is marked [Sensitive]
    │         ├─ YES ──> Replace value with "***"
    │         └─ NO  ──> Leave as-is
    │
    └─ Return masked object
```

## Error Handling Flow

```
Execute() method

    │
    ▼
Try
    │
    ├─ Resolve environment name
    │     │
    │     └─ If error: throw, catch in outer try
    │
    ├─ Fetch environment settings
    │     │
    │     └─ If not found: throw, catch in outer try
    │
    ├─ Mask sensitive data
    ├─ Format output
    └─ Write to console

Catch (specific exceptions)
    │
    ├─ KeyNotFoundException
    │     └─ Log: "Environment '{name}' not found"
    │     └─ Return: 1 (error)
    │
    ├─ ArgumentException
    │     └─ Log: "Invalid format: {format}"
    │     └─ Return: 1 (error)
    │
    ├─ Exception (generic)
    │     └─ Log: error message
    │     └─ Return: 1 (error)
    │
    └─ Success
         └─ Return: 0 (success)
```

## Performance Optimization Points

```
Load Settings
    │
    ▼
Execution Path for Single Environment:

Settings Repository (already cached)
    │
    ├─ GetEnvironment(name) ────┐
    │                             ▼
    │                    O(1) dictionary lookup
    │
    ├─ Mask data         ────┐
    │                         ▼
    │                    O(n) where n = properties
    │                    Typically < 50 properties
    │
    ├─ Serialize JSON   ────┐
    │                         ▼
    │                    O(n) serialization
    │
    └─ Write output      ────┐
                              ▼
                        O(1) console write

Total: O(n) where n = number of properties (typically < 50)
Time: < 100ms expected


Execution Path for All Environments:

Settings Repository (already cached)
    │
    ├─ Iterate all envs  ────┐
    │                          ▼
    │                    O(m) where m = number of environments
    │                    Typically 1-20 environments
    │
    ├─ Mask each         ────┐
    │                         ▼
    │                    O(m * n) = O(50) in practice
    │
    ├─ Serialize all     ────┐
    │                         ▼
    │                    O(m * n) serialization
    │
    └─ Write output      ────┐
                              ▼
                        O(1) console write

Total: O(m * n) where m ≈ 5-20, n ≈ 20-50
Time: < 100ms expected
```

## Integration Points

```
┌─────────────────────────────────────────────┐
│ Clio Application Startup                    │
├─────────────────────────────────────────────┤
│                                             │
│  BindingsModule.RegisterTypes()             │
│        │                                    │
│        ├─ Register GetEnvironmentInfoCommand│
│        │        + ISettingsRepository       │
│        │        + ILogger                   │
│        │                                    │
│        └─ Register formatters (JSON, Table) │
│                                             │
│  CommandLineParser.Default.ParseArguments() │
│        │                                    │
│        └─ Recognize verb "env", aliases    │
│                                             │
│  Container.Resolve<GetEnvironmentInfoCommand>
│        │                                    │
│        └─ Instantiate with dependencies   │
│                                             │
│  Command.Execute(options)                   │
│        │                                    │
│        └─ Execute business logic           │
│                                             │
└─────────────────────────────────────────────┘
```

## Testing Architecture

```
Unit Tests (GetEnvironmentInfoCommandTests)

    ├─ Setup Fixtures
    │    └─ Mock ISettingsRepository
    │    └─ Mock ILogger
    │    └─ Create command instance
    │
    ├─ Test Option Parsing
    │    ├─ Positional argument
    │    ├─ Explicit -e flag
    │    └─ Format option
    │
    ├─ Test Environment Resolution
    │    ├─ Single environment
    │    ├─ All environments
    │    └─ Precedence (explicit > positional)
    │
    ├─ Test Output Formatting
    │    ├─ Valid JSON output
    │    ├─ Valid table output
    │    └─ Sensitive data masking
    │
    ├─ Test Error Scenarios
    │    ├─ Environment not found
    │    ├─ No environments
    │    └─ Invalid format
    │
    └─ Cleanup & Assertions
         └─ Verify mocks called correctly
         └─ Verify output format
         └─ Verify return codes

Integration Tests

    ├─ Real ISettingsRepository
    ├─ Real command execution
    ├─ Verify actual output
    └─ Performance measurements
```

## Deployment & Versioning

```
Development → Testing → Release

Version Increment:
  - Patch version (8.0.1.X → 8.0.1.Y)
  - No breaking changes

Package Changes:
  - Update AssemblyVersion in clio.csproj
  - Update version in nuspec if needed
  - Generate NuGet package

Documentation:
  - Update Commands.md
  - Add command to help system
  - Generate HTML documentation (if applicable)

Release Steps:
  1. Merge PR
  2. Run release workflow
  3. Create GitHub release tag
  4. Publish to NuGet
  5. Update documentation websites
```

---

This architecture document provides a complete technical view of the GetEnvironmentInfoCommand implementation, suitable for architects, senior developers, and technical reviewers.
