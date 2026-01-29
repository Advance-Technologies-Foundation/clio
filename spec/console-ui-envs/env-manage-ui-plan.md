# Console UI Environment Manager - Implementation Plan

## Overview

This document outlines the step-by-step implementation plan for the interactive console UI environment manager feature.

## Prerequisites

- Spectre.Console NuGet package installed
- Existing codebase understanding of:
  - `ISettingsRepository` interface
  - `EnvironmentSettings` model
  - Command pattern in clio

## Implementation Phases

### Phase 1: Setup and Infrastructure (2-3 hours)

#### 1.1 Add Dependencies

**Task**: Add Spectre.Console to clio.csproj

**Steps**:
1. Add PackageReference to `clio/clio.csproj`:
   ```xml
   <PackageReference Include="Spectre.Console" Version="0.49.1" />
   ```
2. Restore packages: `dotnet restore`
3. Verify package installation

**Verification**: Build solution successfully

#### 1.2 Create Command Structure

**Task**: Create command files following clio patterns

**Files to Create**:
- `clio/Command/EnvManageUiCommand.cs` - Main command implementation
- `clio/Common/UI/EnvManageUiService.cs` - Business logic service
- `clio/Common/UI/SpectreConsoleHelper.cs` - Helper for Spectre.Console operations

**Command Skeleton**:
```csharp
[Verb("env-ui", Aliases = ["ui"], 
    HelpText = "Interactive console UI for environment management")]
public class EnvManageUiOptions
{
    // Interactive - no options needed
}

public interface IEnvManageUiCommand
{
    int Execute(EnvManageUiOptions options);
}

public class EnvManageUiCommand : Command<EnvManageUiOptions>, IEnvManageUiCommand
{
    private readonly ISettingsRepository _settingsRepository;
    private readonly ILogger _logger;
    
    public EnvManageUiCommand(ISettingsRepository settingsRepository, ILogger logger)
    {
        _settingsRepository = settingsRepository;
        _logger = logger;
    }
    
    public override int Execute(EnvManageUiOptions options)
    {
        // Implementation in Phase 2
        return 0;
    }
}
```

**Verification**: Command registered and shows in help

#### 1.3 Register in DI Container

**Task**: Register new command in BindingsModule

**Steps**:
1. Open `clio/BindingsModule.cs`
2. Add binding for `IEnvManageUiCommand`:
   ```csharp
   Bind<IEnvManageUiCommand>().To<EnvManageUiCommand>();
   ```

**Verification**: `clio env-ui --help` works

### Phase 2: Core UI Components (4-5 hours)

#### 2.1 Main Menu Implementation

**Task**: Create main menu loop with Spectre.Console

**Implementation** in `EnvManageUiCommand.cs`:

```csharp
public override int Execute(EnvManageUiOptions options)
{
    try
    {
        ShowHeader();
        
        while (true)
        {
            var choice = ShowMainMenu();
            
            var result = choice switch
            {
                MenuChoice.ListEnvironments => ListEnvironments(),
                MenuChoice.ViewDetails => ViewEnvironmentDetails(),
                MenuChoice.Create => CreateEnvironment(),
                MenuChoice.Edit => EditEnvironment(),
                MenuChoice.Delete => DeleteEnvironment(),
                MenuChoice.SetActive => SetActiveEnvironment(),
                MenuChoice.Exit => 0,
                _ => 1
            };
            
            if (choice == MenuChoice.Exit)
                return result;
                
            Console.WriteLine("\nPress any key to continue...");
            Console.ReadKey(true);
            Console.Clear();
        }
    }
    catch (Exception ex)
    {
        _logger.WriteError($"Error: {ex.Message}");
        return 1;
    }
}

private void ShowHeader()
{
    var panel = new Panel($"[bold cyan]Clio Environment Manager[/]\n[dim]{_settingsRepository.AppSettingsFilePath}[/]")
    {
        Border = BoxBorder.Double,
        Padding = new Padding(2, 0, 2, 0)
    };
    AnsiConsole.Write(panel);
}

private MenuChoice ShowMainMenu()
{
    return AnsiConsole.Prompt(
        new SelectionPrompt<MenuChoice>()
            .Title("[green]What would you like to do?[/]")
            .AddChoices(Enum.GetValues<MenuChoice>())
            .UseConverter(choice => choice switch
            {
                MenuChoice.ListEnvironments => "List Environments",
                MenuChoice.ViewDetails => "View Environment Details",
                MenuChoice.Create => "Create New Environment",
                MenuChoice.Edit => "Edit Environment",
                MenuChoice.Delete => "Delete Environment",
                MenuChoice.SetActive => "Set Active Environment",
                MenuChoice.Exit => "Exit",
                _ => choice.ToString()
            })
    );
}

private enum MenuChoice
{
    ListEnvironments,
    ViewDetails,
    Create,
    Edit,
    Delete,
    SetActive,
    Exit
}
```

**Verification**: Main menu displays and navigation works

#### 2.2 List Environments View

**Task**: Display environments in table format

**Implementation**:

```csharp
private int ListEnvironments()
{
    var environments = _settingsRepository.GetAllEnvironments();
    var activeEnv = _settingsRepository.GetDefaultEnvironmentName();
    
    if (!environments.Any())
    {
        AnsiConsole.MarkupLine("[yellow]No environments configured.[/]");
        return 0;
    }
    
    var table = new Table()
        .Border(TableBorder.Rounded)
        .AddColumn("#")
        .AddColumn("Name")
        .AddColumn("URL")
        .AddColumn("Login")
        .AddColumn("IsNetCore");
    
    int index = 1;
    foreach (var env in environments)
    {
        var isActive = env.Key == activeEnv;
        var marker = isActive ? "*" : " ";
        
        table.AddRow(
            $"{index}{marker}",
            isActive ? $"[green]{env.Key}[/]" : env.Key,
            env.Value.Uri ?? "[dim]not set[/]",
            env.Value.Login ?? "[dim]not set[/]",
            env.Value.IsNetCore ? "[green]Yes[/]" : "[red]No[/]"
        );
        
        index++;
    }
    
    AnsiConsole.Write(table);
    AnsiConsole.MarkupLine("\n[dim]* - Active Environment[/]");
    
    return 0;
}
```

**Verification**: Environments list displays correctly

#### 2.3 View Details Implementation

**Task**: Display detailed environment information

**Implementation**:

```csharp
private int ViewEnvironmentDetails()
{
    var environments = _settingsRepository.GetAllEnvironments();
    
    if (!environments.Any())
    {
        AnsiConsole.MarkupLine("[yellow]No environments configured.[/]");
        return 0;
    }
    
    var envName = AnsiConsole.Prompt(
        new SelectionPrompt<string>()
            .Title("[green]Select environment to view:[/]")
            .AddChoices(environments.Keys)
    );
    
    var env = environments[envName];
    Console.Clear();
    
    AnsiConsole.Write(new Panel($"[bold]Environment Details: {envName}[/]")
        .Border(BoxBorder.Double));
    
    // Basic Configuration
    var basicTable = CreateDetailsTable("Basic Configuration");
    basicTable.AddRow("Name", envName);
    basicTable.AddRow("URL", env.Uri ?? "[dim]not set[/]");
    basicTable.AddRow("Login", env.Login ?? "[dim]not set[/]");
    basicTable.AddRow("Password", MaskValue(env.Password));
    basicTable.AddRow("Maintainer", env.Maintainer ?? "[dim]not set[/]");
    basicTable.AddRow("IsNetCore", env.IsNetCore ? "[green]Yes[/]" : "[red]No[/]");
    AnsiConsole.Write(basicTable);
    
    // Authentication
    if (!string.IsNullOrEmpty(env.ClientId))
    {
        var authTable = CreateDetailsTable("Authentication");
        authTable.AddRow("ClientId", env.ClientId);
        authTable.AddRow("ClientSecret", MaskValue(env.ClientSecret));
        authTable.AddRow("AuthAppUri", env.AuthAppUri ?? "[dim]not set[/]");
        AnsiConsole.Write(authTable);
    }
    
    // Advanced Settings
    var advancedTable = CreateDetailsTable("Advanced Settings");
    advancedTable.AddRow("Safe Mode", env.Safe ? "[green]Yes[/]" : "[red]No[/]");
    advancedTable.AddRow("Developer Mode", env.DeveloperModeEnabled ? "[green]Yes[/]" : "[red]No[/]");
    advancedTable.AddRow("Workspace Paths", env.WorkspacePathes ?? "[dim]not set[/]");
    advancedTable.AddRow("DB Server Key", env.DbServerKey ?? "[dim]not set[/]");
    AnsiConsole.Write(advancedTable);
    
    // Database Configuration
    if (env.DbServer != null)
    {
        var dbTable = CreateDetailsTable("Database Configuration");
        dbTable.AddRow("DB Name", env.DbName ?? "[dim]not set[/]");
        dbTable.AddRow("DB Server", env.DbServer.Uri ?? "[dim]not set[/]");
        dbTable.AddRow("DB Login", env.DbServer.Login ?? "[dim]not set[/]");
        dbTable.AddRow("DB Password", MaskValue(env.DbServer.Password));
        AnsiConsole.Write(dbTable);
    }
    
    return 0;
}

private Table CreateDetailsTable(string title)
{
    return new Table()
        .Border(TableBorder.Rounded)
        .Title($"[yellow]{title}[/]")
        .AddColumn(new TableColumn("Property").Width(20))
        .AddColumn(new TableColumn("Value"));
}

private string MaskValue(string value)
{
    return string.IsNullOrEmpty(value) ? "[dim]not set[/]" : "[red]****[/]";
}
```

**Verification**: Environment details display correctly

### Phase 3: CRUD Operations (6-8 hours)

#### 3.1 Create Environment

**Task**: Interactive environment creation

**Implementation**:

```csharp
private int CreateEnvironment()
{
    Console.Clear();
    AnsiConsole.Write(new Panel("[bold]Create New Environment[/]")
        .Border(BoxBorder.Double));
    
    try
    {
        // Environment name
        var name = AnsiConsole.Prompt(
            new TextPrompt<string>("[green]Environment name:[/]")
                .Validate(name =>
                {
                    if (string.IsNullOrWhiteSpace(name))
                        return ValidationResult.Error("[red]Name cannot be empty[/]");
                    if (!System.Text.RegularExpressions.Regex.IsMatch(name, @"^[a-zA-Z0-9_-]+$"))
                        return ValidationResult.Error("[red]Name can only contain letters, numbers, underscores, and hyphens[/]");
                    if (_settingsRepository.IsEnvironmentExists(name))
                        return ValidationResult.Error($"[red]Environment '{name}' already exists[/]");
                    return ValidationResult.Success();
                })
        );
        
        // URL
        var url = AnsiConsole.Prompt(
            new TextPrompt<string>("[green]URL:[/]")
                .Validate(url =>
                {
                    if (string.IsNullOrWhiteSpace(url))
                        return ValidationResult.Error("[red]URL cannot be empty[/]");
                    if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || 
                        (uri.Scheme != "http" && uri.Scheme != "https"))
                        return ValidationResult.Error("[red]Invalid URL format (must be http:// or https://)[/]");
                    return ValidationResult.Success();
                })
        );
        
        // Login
        var login = AnsiConsole.Prompt(
            new TextPrompt<string>("[green]Login:[/]")
                .Validate(login =>
                {
                    if (string.IsNullOrWhiteSpace(login))
                        return ValidationResult.Error("[red]Login cannot be empty[/]");
                    return ValidationResult.Success();
                })
        );
        
        // Password
        var password = AnsiConsole.Prompt(
            new TextPrompt<string>("[green]Password (optional):[/]")
                .Secret()
                .AllowEmpty()
        );
        
        // IsNetCore
        var isNetCore = AnsiConsole.Confirm("[green]Is this a .NET Core environment?[/]", defaultValue: true);
        
        // Advanced settings
        var configureAdvanced = AnsiConsole.Confirm(
            "[green]Configure advanced settings?[/]", 
            defaultValue: false
        );
        
        // Create environment object
        var env = new EnvironmentSettings
        {
            Uri = url.TrimEnd('/'),
            Login = login,
            Password = password,
            IsNetCore = isNetCore
        };
        
        if (configureAdvanced)
        {
            ConfigureAdvancedSettings(env);
        }
        
        // Save
        _settingsRepository.ConfigureEnvironment(name, env);
        
        AnsiConsole.MarkupLine($"\n[green]✓[/] Environment '[cyan]{name}[/]' created successfully!");
        
        // Set as active?
        if (AnsiConsole.Confirm($"Set '[cyan]{name}[/]' as active environment?", defaultValue: false))
        {
            _settingsRepository.SetActiveEnvironment(name);
            AnsiConsole.MarkupLine($"[green]✓[/] '[cyan]{name}[/]' is now the active environment");
        }
        
        return 0;
    }
    catch (Exception ex)
    {
        _logger.WriteError($"Failed to create environment: {ex.Message}");
        return 1;
    }
}

private void ConfigureAdvancedSettings(EnvironmentSettings env)
{
    env.Maintainer = AnsiConsole.Ask<string>("[green]Maintainer (optional):[/]", string.Empty);
    
    env.ClientId = AnsiConsole.Ask<string>("[green]Client ID (optional):[/]", string.Empty);
    
    if (!string.IsNullOrEmpty(env.ClientId))
    {
        env.ClientSecret = AnsiConsole.Prompt(
            new TextPrompt<string>("[green]Client Secret:[/]")
                .Secret()
                .AllowEmpty()
        );
        
        env.AuthAppUri = AnsiConsole.Ask<string>("[green]Auth App URI (optional):[/]", string.Empty);
    }
    
    env.Safe = AnsiConsole.Confirm("[green]Enable Safe Mode?[/]", defaultValue: false);
    env.DeveloperModeEnabled = AnsiConsole.Confirm("[green]Enable Developer Mode?[/]", defaultValue: false);
    
    env.WorkspacePathes = AnsiConsole.Ask<string>("[green]Workspace Paths (optional):[/]", string.Empty);
}
```

**Verification**: Can create new environment successfully

#### 3.2 Edit Environment

**Task**: Modify existing environment

**Implementation**:

```csharp
private int EditEnvironment()
{
    var environments = _settingsRepository.GetAllEnvironments();
    
    if (!environments.Any())
    {
        AnsiConsole.MarkupLine("[yellow]No environments configured.[/]");
        return 0;
    }
    
    var envName = AnsiConsole.Prompt(
        new SelectionPrompt<string>()
            .Title("[green]Select environment to edit:[/]")
            .AddChoices(environments.Keys)
    );
    
    var currentEnv = environments[envName];
    
    Console.Clear();
    AnsiConsole.Write(new Panel($"[bold]Edit Environment: {envName}[/]")
        .Border(BoxBorder.Double));
    
    try
    {
        var fields = AnsiConsole.Prompt(
            new MultiSelectionPrompt<string>()
                .Title("[green]Select fields to edit:[/]")
                .Required()
                .AddChoices(new[]
                {
                    "URL",
                    "Login",
                    "Password",
                    "IsNetCore",
                    "Maintainer",
                    "ClientId",
                    "ClientSecret",
                    "Safe Mode",
                    "Developer Mode",
                    "All Fields"
                })
        );
        
        var editAll = fields.Contains("All Fields");
        
        var updatedEnv = currentEnv; // Clone would be better
        
        if (editAll || fields.Contains("URL"))
        {
            updatedEnv.Uri = AnsiConsole.Ask(
                "[green]URL:[/]", 
                currentEnv.Uri ?? string.Empty
            ).TrimEnd('/');
        }
        
        if (editAll || fields.Contains("Login"))
        {
            updatedEnv.Login = AnsiConsole.Ask(
                "[green]Login:[/]", 
                currentEnv.Login ?? string.Empty
            );
        }
        
        if (editAll || fields.Contains("Password"))
        {
            var changePassword = AnsiConsole.Confirm(
                "[green]Change password?[/]", 
                defaultValue: false
            );
            
            if (changePassword)
            {
                updatedEnv.Password = AnsiConsole.Prompt(
                    new TextPrompt<string>("[green]New Password:[/]")
                        .Secret()
                        .AllowEmpty()
                );
            }
        }
        
        if (editAll || fields.Contains("IsNetCore"))
        {
            updatedEnv.IsNetCore = AnsiConsole.Confirm(
                "[green]Is this a .NET Core environment?[/]", 
                defaultValue: currentEnv.IsNetCore
            );
        }
        
        if (editAll || fields.Contains("Maintainer"))
        {
            updatedEnv.Maintainer = AnsiConsole.Ask(
                "[green]Maintainer:[/]", 
                currentEnv.Maintainer ?? string.Empty
            );
        }
        
        // Save changes
        _settingsRepository.ConfigureEnvironment(envName, updatedEnv);
        
        AnsiConsole.MarkupLine($"\n[green]✓[/] Environment '[cyan]{envName}[/]' updated successfully!");
        
        return 0;
    }
    catch (Exception ex)
    {
        _logger.WriteError($"Failed to edit environment: {ex.Message}");
        return 1;
    }
}
```

**Verification**: Can edit environment fields

#### 3.3 Delete Environment

**Task**: Remove environment with confirmation

**Implementation**:

```csharp
private int DeleteEnvironment()
{
    var environments = _settingsRepository.GetAllEnvironments();
    
    if (!environments.Any())
    {
        AnsiConsole.MarkupLine("[yellow]No environments configured.[/]");
        return 0;
    }
    
    var envName = AnsiConsole.Prompt(
        new SelectionPrompt<string>()
            .Title("[green]Select environment to delete:[/]")
            .AddChoices(environments.Keys)
    );
    
    var env = environments[envName];
    var activeEnv = _settingsRepository.GetDefaultEnvironmentName();
    var isActive = envName == activeEnv;
    
    Console.Clear();
    
    // Warning panel
    var warningPanel = new Panel(
        $"You are about to delete environment: [red]{envName}[/]\n" +
        $"URL: {env.Uri}\n\n" +
        (isActive ? "[yellow]⚠ This is the ACTIVE environment![/]\n\n" : "") +
        "[red]This action cannot be undone.[/]"
    )
    {
        Header = new PanelHeader("[red bold]⚠ WARNING ⚠[/]", Justify.Center),
        Border = BoxBorder.Double,
        BorderStyle = new Style(Color.Red)
    };
    
    AnsiConsole.Write(warningPanel);
    
    if (isActive)
    {
        AnsiConsole.MarkupLine("\n[yellow]Note: You will need to set a new active environment after deletion.[/]");
    }
    
    var confirmed = AnsiConsole.Confirm(
        $"\n[red]Are you absolutely sure you want to delete '{envName}'?[/]", 
        defaultValue: false
    );
    
    if (!confirmed)
    {
        AnsiConsole.MarkupLine("[yellow]Deletion cancelled.[/]");
        return 0;
    }
    
    try
    {
        _settingsRepository.RemoveEnvironment(envName);
        AnsiConsole.MarkupLine($"\n[green]✓[/] Environment '[cyan]{envName}[/]' deleted successfully!");
        
        // If deleted active environment, prompt to set new one
        if (isActive)
        {
            var remaining = _settingsRepository.GetAllEnvironments();
            if (remaining.Any())
            {
                var newActive = AnsiConsole.Prompt(
                    new SelectionPrompt<string>()
                        .Title("[yellow]Select new active environment:[/]")
                        .AddChoices(remaining.Keys)
                );
                
                _settingsRepository.SetActiveEnvironment(newActive);
                AnsiConsole.MarkupLine($"[green]✓[/] '[cyan]{newActive}[/]' is now the active environment");
            }
        }
        
        return 0;
    }
    catch (Exception ex)
    {
        _logger.WriteError($"Failed to delete environment: {ex.Message}");
        return 1;
    }
}
```

**Verification**: Can delete environment with confirmation

#### 3.4 Set Active Environment

**Task**: Change active environment

**Implementation**:

```csharp
private int SetActiveEnvironment()
{
    var environments = _settingsRepository.GetAllEnvironments();
    var currentActive = _settingsRepository.GetDefaultEnvironmentName();
    
    if (!environments.Any())
    {
        AnsiConsole.MarkupLine("[yellow]No environments configured.[/]");
        return 0;
    }
    
    var choices = environments.Keys.ToList();
    var prompt = new SelectionPrompt<string>()
        .Title("[green]Select active environment:[/]")
        .AddChoices(choices)
        .UseConverter(env => env == currentActive ? $"{env} [dim](current)[/]" : env);
    
    var selected = AnsiConsole.Prompt(prompt);
    
    if (selected == currentActive)
    {
        AnsiConsole.MarkupLine($"[yellow]'{selected}' is already the active environment.[/]");
        return 0;
    }
    
    try
    {
        _settingsRepository.SetActiveEnvironment(selected);
        AnsiConsole.MarkupLine($"[green]✓[/] '[cyan]{selected}[/]' is now the active environment");
        return 0;
    }
    catch (Exception ex)
    {
        _logger.WriteError($"Failed to set active environment: {ex.Message}");
        return 1;
    }
}
```

**Verification**: Can change active environment

### Phase 4: Testing (4-5 hours)

#### 4.1 Unit Tests

**File**: `clio.tests/Command/EnvManageUiCommandTests.cs`

**Test Cases**:
- Environment listing with no environments
- Environment listing with multiple environments
- Environment creation validation
- Environment deletion confirmation
- Setting active environment

**Example Test**:

```csharp
[Test]
[Description("Lists all environments when multiple exist")]
public void ListEnvironments_WithMultipleEnvironments_DisplaysAllEnvironments()
{
    // Arrange
    var environments = new Dictionary<string, EnvironmentSettings>
    {
        ["dev"] = new() { Uri = "https://dev.local", Login = "admin", IsNetCore = true },
        ["prod"] = new() { Uri = "https://prod.local", Login = "admin", IsNetCore = true }
    };
    
    _settingsRepository.GetAllEnvironments().Returns(environments);
    _settingsRepository.GetDefaultEnvironmentName().Returns("dev");
    
    var command = new EnvManageUiCommand(_settingsRepository, _logger);
    
    // Act
    var result = command.Execute(new EnvManageUiOptions());
    
    // Assert
    result.Should().Be(0, because: "listing environments should succeed");
    _settingsRepository.Received(1).GetAllEnvironments();
}
```

#### 4.2 Integration Tests

**Test Scenarios**:
- Create, edit, delete workflow
- Multiple operations in sequence
- Error recovery

#### 4.3 Manual Testing Checklist

- [ ] Main menu navigation works
- [ ] List displays all environments
- [ ] View details shows all fields
- [ ] Create validates inputs
- [ ] Edit updates only selected fields
- [ ] Delete requires confirmation
- [ ] Active environment indicator works
- [ ] Passwords are masked
- [ ] Error messages are clear
- [ ] Works on Windows
- [ ] Works on macOS
- [ ] Works on Linux
- [ ] Empty config handled gracefully
- [ ] Large number of environments (100+)

### Phase 5: Documentation (2 hours)

#### 5.1 Update Commands.md

Add section for `env-ui` command with examples and screenshots (ASCII art).

#### 5.2 Create User Guide

Document all features with examples in `/docs/commands/EnvManageUiCommand.md`.

#### 5.3 Update README

Mention new interactive UI feature in project README.

## Estimated Timeline

| Phase | Task | Hours | Dependencies |
|-------|------|-------|--------------|
| 1.1 | Add Dependencies | 0.5 | None |
| 1.2 | Create Command Structure | 1 | 1.1 |
| 1.3 | Register in DI | 0.5 | 1.2 |
| 2.1 | Main Menu | 2 | Phase 1 |
| 2.2 | List Environments | 1.5 | 2.1 |
| 2.3 | View Details | 1.5 | 2.1 |
| 3.1 | Create Environment | 2.5 | Phase 2 |
| 3.2 | Edit Environment | 2 | 3.1 |
| 3.3 | Delete Environment | 1.5 | 3.1 |
| 3.4 | Set Active | 1 | Phase 2 |
| 4.1 | Unit Tests | 3 | Phase 3 |
| 4.2 | Integration Tests | 1 | Phase 3 |
| 4.3 | Manual Testing | 1 | Phase 3 |
| 5.1-5.3 | Documentation | 2 | Phase 4 |
| **Total** | | **21.5 hours** | |

## Risk Mitigation

### Technical Risks

1. **Spectre.Console Compatibility Issues**
   - Mitigation: Test on all target platforms early
   - Fallback: Use basic Console API if needed

2. **Configuration File Corruption**
   - Mitigation: Create backup before modifications
   - Validation: Validate JSON before saving

3. **Concurrent Access**
   - Mitigation: Add file locking if needed
   - Note: Current SettingsRepository handles this

### Schedule Risks

1. **Underestimated Complexity**
   - Buffer: 20% time buffer included
   - Mitigation: Regular progress reviews

2. **Testing Issues**
   - Mitigation: Start testing early in Phase 2
   - Parallel: Run manual tests alongside development

## Success Criteria

- [ ] All functional requirements met
- [ ] All unit tests passing
- [ ] Manual testing completed on 3 platforms
- [ ] Documentation complete
- [ ] Code reviewed and approved
- [ ] No regressions in existing commands

## Rollout Plan

1. **Alpha**: Internal testing (dev team)
2. **Beta**: Selected power users
3. **RC**: All users, gather feedback
4. **GA**: Include in next release

## Post-Implementation

- Monitor for bug reports
- Gather user feedback
- Plan future enhancements from "Out of Scope" list
