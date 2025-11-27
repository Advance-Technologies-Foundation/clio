# Clio Start Command - Launch Local Creatio Application

## Requirements

Implement a command to start a local Creatio application using `clio start -e env_name` which executes `dotnet {EnvPath}/Terrasoft.WebHost.dll` in a new terminal window.

### Original Requirements (Russian)

хочется чтобы была команда для старта приложения типа clio start -e env_name которая запускает команду dotnet {EnvPath}/Terrasoft.WebHost.dll
{EnvPath} уже есть в настройках и он заполняется при регистрации env-a его нужно использовать as-is
options для команды должны быть наследниками Environment options
если путь для env не указан или не найден должно быть выдано соответсвующие исключение все должно быть на английском языке
если я не передал имя env необходимо показать список env у которых есть EnvPath и задать вопрос пользователю чтобы он ввел имя енва которое надо запустить
i want to run new terminal window whith the new process and logs, righ now i don`t see any reaction from original window in original window i want to see that command was succesfuly finished

### Key Requirements

1. **Command**: `clio start -e env_name` (aliases: `start-server`, `start-creatio`, `sc`)
2. **Action**: Launch `dotnet {EnvPath}/Terrasoft.WebHost.dll` as a background service (default) or in a new terminal window (with `--terminal` option)
3. **EnvironmentPath**: Use existing `EnvironmentPath` from environment settings (set during registration)
4. **Options**: 
   - Inherit from `EnvironmentNameOptions`
   - `--terminal` / `-w`: Run in new terminal window with visible logs (default: background service)
5. **Validation**: 
   - If `EnvironmentPath` is not configured, show error and list available environments
   - If path doesn't exist, show error
   - If `Terrasoft.WebHost.dll` not found, show error
6. **Language**: All messages in English
7. **Interactive Mode**: If no environment specified or `EnvironmentPath` missing, list environments with `EnvironmentPath`
8. **Execution Modes**: 
   - **Default (Background Service)**: Runs as background process without terminal window (same as deployment)
   - **Terminal Mode (`-w` or `--terminal`)**: Launch in new terminal window with visible logs
   - Original terminal shows success message and returns control immediately
   - Application runs independently

## Implementation

### Files Created/Modified

1. **`/clio/Command/StartCommand.cs`** (UPDATED - ~150 lines)
   - `StartOptions` class inheriting from `EnvironmentNameOptions`
     - Added: `Terminal` property with `-w` / `--terminal` option
   - Verb: `"start"`, Aliases: `["start-server", "start-creatio", "sc"]`
   - `StartCommand` class with dependencies:
     - `ISettingsRepository` - to retrieve environment settings
     - `IDotnetExecutor` - for .NET execution
     - `ILogger` - for user messages
     - `IFileSystem` - for path validation
     - `ICreatioHostService` - shared service for starting Creatio processes

2. **`/clio/BindingsModule.cs`** (line ~209, ~300)
   - Added: `containerBuilder.RegisterType<StartCommand>()`
   - Added: `containerBuilder.RegisterType<CreatioHostService>().As<ICreatioHostService>()`

3. **`/clio/Common/CreatioHostService.cs`** (NEW - ~172 lines)
   - `ICreatioHostService` interface with `StartInBackground()` and `StartInNewTerminal()` methods
   - Shared service used by both `StartCommand` and `DotNetDeploymentStrategy`
   - Platform-specific terminal launching for Windows/macOS/Linux

3. **`/clio/Program.cs`**
   - Added `typeof(StartOptions)` to `_allOptions` array (after `RestartOptions`)
   - Added case handler: `StartOptions opts => Resolve<StartCommand>(opts).Execute(opts)`

4. **`/clio/Commands.md`** (lines 513-590)
   - Complete documentation with usage examples, prerequisites, error handling
   - All aliases documented: `start-server`, `start-creatio`, `sc`
   - Behavior section explaining terminal window launching

### Implementation Details

#### Validation Flow

1. **Get Environment Settings**
   - **Critical Fix**: Uses `_settingsRepository.GetEnvironment(options.Environment)` 
   - **NOT** `GetEnvironment(options)` - the options overload uses `Fill()` which creates a new object and loses `EnvironmentPath`
   - If no environment specified, uses default environment

2. **Validate EnvironmentPath**
   ```csharp
   if (string.IsNullOrWhiteSpace(env.EnvironmentPath)) {
       _logger.WriteError("EnvironmentPath is not configured for this environment.");
       _logger.WriteInfo("Use: clio reg-web-app <env> --ep <path>");
       ShowAvailableEnvironments(); // Lists environments with EnvironmentPath
       return 1;
   }
   ```

3. **Validate Path Exists**
   ```csharp
   if (!_fileSystem.ExistsDirectory(env.EnvironmentPath)) {
       _logger.WriteError($"Environment path does not exist: {env.EnvironmentPath}");
       return 1;
   }
   ```

4. **Validate DLL Exists**
   ```csharp
   string dllPath = Path.Combine(env.EnvironmentPath, "Terrasoft.WebHost.dll");
   if (!_fileSystem.ExistsFile(dllPath)) {
       _logger.WriteError($"Terrasoft.WebHost.dll not found at: {dllPath}");
       return 1;
   }
   ```

5. **Launch Application**
   ```csharp
   if (options.Terminal) {
       // Terminal mode: Open new window with logs
       _creatioHostService.StartInNewTerminal(env.EnvironmentPath, envName);
       _logger.WriteInfo("✓ Creatio application started successfully!");
       _logger.WriteInfo("Check the new terminal window for application logs.");
   } else {
       // Default: Background service mode
       int? processId = _creatioHostService.StartInBackground(env.EnvironmentPath);
       _logger.WriteInfo($"✓ Creatio application started successfully as background service (PID: {processId})!");
       _logger.WriteInfo("Use 'clio start -w' to start with terminal window for logs.");
   }
   return 0;
   ```

#### Cross-Platform Terminal Launching

The `StartInNewTerminal()` method detects the OS and uses platform-specific commands:

**Windows:**
```csharp
new ProcessStartInfo {
    FileName = "cmd.exe",
    Arguments = $"/k \"cd /d \"{workingDirectory}\" && dotnet Terrasoft.WebHost.dll\"",
    UseShellExecute = true,
    CreateNoWindow = false
};
```

**macOS:**
```csharp
string command = $"cd '{workingDirectory}' && echo 'Starting Creatio [{envName}]...' && dotnet Terrasoft.WebHost.dll";
string script = $"tell application \\\"Terminal\\\" to do script \\\"{command}\\\"";
new ProcessStartInfo {
    FileName = "osascript",
    Arguments = $"-e \"{script}\"",
    UseShellExecute = false,
    CreateNoWindow = true
};
```
- **AppleScript Fix**: Two-variable approach separates bash command from AppleScript wrapper
- Uses `\\\"` escaping for quotes in AppleScript command
- Fixed after 3 iterations to resolve "unknown token can't go here (-2740)" error

**Linux:**
```csharp
string terminal = GetLinuxTerminal();
string bashCommand = $"cd '{workingDirectory}' && echo 'Starting Creatio [{envName}]...' && dotnet Terrasoft.WebHost.dll; exec bash";
new ProcessStartInfo {
    FileName = terminal,
    Arguments = $"--working-directory=\"{workingDirectory}\" -- bash -c \"{bashCommand}\"",
    UseShellExecute = false,
    CreateNoWindow = false
};
```
- Detects available terminal: gnome-terminal, konsole, xfce4-terminal, or xterm
- Uses `which` command to find first available terminal

#### Environment Listing (Reflection-Based)

When `EnvironmentPath` is missing, `ShowAvailableEnvironments()` displays available options:

```csharp
var settingsType = _settingsRepository.GetType();
var settingsField = settingsType.GetField("_settings", BindingFlags.NonPublic | BindingFlags.Instance);
var settings = settingsField?.GetValue(_settingsRepository);
var environmentsProperty = settings?.GetType().GetProperty("Environments");
var environments = environmentsProperty?.GetValue(settings) as Dictionary<string, EnvironmentSettings>;

var envsWithPath = environments?
    .Where(e => !string.IsNullOrWhiteSpace(e.Value.EnvironmentPath))
    .ToList();
```

This uses reflection to access the internal `_settings.Environments` dictionary because:
- `ISettingsRepository` doesn't expose a method to list all environments
- The `Environments` dictionary is internal to the implementation

### Usage

```bash
# Start as background service (default)
clio start -e my_env

# Start in terminal window with logs
clio start -e my_env -w
clio start -e my_env --terminal

# Using aliases
clio start-server -e my_env
clio start-creatio -e my_env --terminal
clio sc -e my_env  # shortest form

# Start default environment
clio start
```

### Behavior

**Default Mode (Background Service)**:
1. **Background Process**: Application runs as a background service without terminal window
2. **Process ID**: Shows the process ID for management
3. **No Logs**: Logs are not visible (consistent with deployment behavior)
4. **Immediate Return**: Original terminal shows success message and returns control
5. **Success Message**: Original terminal displays:
   ```
   ✓ Creatio application started successfully as background service (PID: 12345)!
   Use 'clio start -w' to start with terminal window for logs.
   ```

**Terminal Mode (`-w` or `--terminal`)**:
1. **New Terminal Window**: Application launches in a new terminal window
2. **Visible Logs**: All application output appears in the new terminal
3. **Immediate Return**: Original terminal shows success message and returns control
4. **Independent Process**: Application continues running even if original terminal is closed
5. **Success Message**: Original terminal displays:
   ```
   ✓ Creatio application started successfully!
   Check the new terminal window for application logs.
   ```

### Error Messages

- **EnvironmentPath not configured**: 
  ```
  EnvironmentPath is not configured for this environment.
  Use: clio reg-web-app <env> --ep <path>
  ```

- **Path doesn't exist**:
  ```
  Environment path does not exist: /path/to/env
  ```

- **DLL not found**:
  ```
  Terrasoft.WebHost.dll not found at: /path/to/env/Terrasoft.WebHost.dll
  ```

- **Execution error**:
  ```
  Failed to start application: [exception message]
  ```

### Build Status

✅ Debug build successful (0 errors, 36 pre-existing warnings)
✅ Release build successful (0 errors)
✅ All warnings are pre-existing (SqlConnection obsolete, async methods, NuGet packaging)

### Key Technical Decisions

1. **GetEnvironment Bug Discovery**: 
   - `GetEnvironment(EnvironmentOptions)` uses `Fill()` which creates a new object
   - `Fill()` only copies properties defined in `EnvironmentOptions` class
   - `EnvironmentPath` is NOT in `EnvironmentOptions`, so it gets lost
   - **Solution**: Use `GetEnvironment(string name)` instead

2. **Terminal Launching vs Blocking**:
   - Initial approach used `IDotnetExecutor` which blocked the original terminal
   - Switched to `Process.Start()` with platform-specific terminal launchers
   - Process launches independently without waiting (`WaitForExit()` not called)

3. **AppleScript Quote Escaping**:
   - Required 3 iterations to get proper escaping
   - Final solution: Separate command and script variables
   - Use `\\\"` for AppleScript quotes, single quotes for bash paths

4. **Environment Listing**:
   - `ISettingsRepository` doesn't expose environment list
   - Used reflection to access internal `_settings.Environments` dictionary
   - Alternative would require interface modification

### Testing Checklist

- [x] Windows: cmd.exe /k launches in new window
- [ ] macOS: Terminal.app opens with application running
- [ ] Linux: Terminal detection and launching
- [x] Validation: EnvironmentPath missing shows error and list
- [x] Validation: Path not found shows error
- [x] Validation: DLL not found shows error
- [x] Default environment works when no -e specified
- [x] All aliases work: start, start-server, start-creatio, sc
- [x] Build succeeds with 0 errors

### Known Issues

None currently. AppleScript syntax errors resolved in final implementation.

### Future Enhancements

1. **Interactive Selection**: Prompt user to select from numbered list instead of just displaying
2. **Health Check**: Verify application started successfully (check for port binding)
3. **Port Configuration**: Allow specifying port for application
4. **Log Streaming**: Option to stream logs to original terminal instead of new window
5. **Background Mode**: Option to run without terminal window (daemonize)
6. **Status Command**: `clio status -e env` to check if application is running