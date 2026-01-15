---
description: Create help file for {command name} command
name: doc-command
model: Claude Sonnet 4.5 (copilot)
tools: ['read', 'edit', 'search', 'web', 'agent', 'todo']
---

Create help file for ${input:command_name} command. 
User may reference command by one of it many aliases, documentation should use the command name and list aliases as optional. 
For instance when a user asks to document `ver` command you should find that the main command name is `info` and document it as `info` command because info command is aliased as `ver`.


To identify the main command name and its aliases, look for the `[Verb]` attribute in the options class file. It will look similar to this:
```csharp
	[Verb("command-name", Aliases = ["alias-one","alias-two","another-alias"], HelpText = "Optional text that may or may not exist describing the command.")]
```

## IMPORTANT
- Create or update help file for command in `clio\help\en` directory. Filename must follow the command name (never alias), if the file already exists update it to reflect the current state of the command.


## HELP FILE FORMAT

## Objective
Generate comprehensive, user-friendly and AI Friendly documentation for clio CLI commands by analyzing source code. 
The documentation should help users understand command purpose, arguments, and usage patterns without requiring knowledge of the internal implementation.

## Quick Start Workflow
1. **Locate the command mapping** in `Program.cs` `ExecuteCommandWithOptions` method
2. **Find the options class** (e.g., `RestartOptions`, `PushPkgOptions`)
3. **Find the corresponding command class** (e.g., `RestartCommand`, `PushPackageCommand`)
4. **Analyze inheritance chain** for all available parameters
5. **Document arguments** with required/optional status and defaults
6. **Create usage examples** based on real-world scenarios
7. **Save documentation** in `clio/docs/commands/[command-name].md` (path from the repository root).
8. **Update ./clio/Commands.md** file to reference new or updated command documentation.
9. Command references should be maintained as correct navigational links as [`command`](./path-to-command-doc.md). For instance ping command should be refed as [`ping`](./PingCommand.md)
10. Create or update help file for command in `clio\help\en` directory.


## Detailed Instructions

### 1. Analyze Source Code Structure

#### Command Discovery Process
Commands in clio are registered via a large switch expression in `ExecuteCommandWithOptions` method located in `Program.cs`. To document a command:

1. **Find the options type** in the `CommandOption` array
2. **Locate the mapping** in `ExecuteCommandWithOptions` method
3. **Identify the command class** from the mapping (e.g., `RestartOptions` → `RestartCommand`)

#### Command Mapping Examples
```csharp
// From ExecuteCommandWithOptions method:
RestartOptions opts => CreateRemoteCommand<RestartCommand>(opts).Execute(opts),
PushPkgOptions opts => Resolve<PushPackageCommand>(opts).Execute(opts),
NewPkgOptions opts => CreateCommand<NewPkgCommand>(...).Execute(opts),
```

#### File Locations
- **Options classes**: Can be in various subdirectories under `clio/Command/`
- **Command classes**: Usually in the same directory as their options
- **Base classes**: Often in `clio/Command/` or `clio/Common/`
- **Main registration**: `clio/Program.cs`

#### Key Areas to Examine
```csharp
// Look for option properties like:
[Option('e', "environment", Required = true, HelpText = "Environment name")]
public string EnvironmentName { get; set; }

[Option('u', "uri", Required = false, Default = "localhost", HelpText = "Server URI")]
public string Uri { get; set; } = "localhost";


// Look for positional parameters like:
[Value(0, MetaName = "App name", Required = false, HelpText = "Name of application")]
public string Name { get; set; }

```

### clio-gate requirements
Check if the command is calling api implemented in the cliogate package. 
When `cliogate` is required include this in the documentation.


#### Inheritance Analysis
- Check if options inherit from base classes (e.g., `EnvironmentOptions`, `RemoteCommandOptions`)
- Document inherited properties alongside command-specific ones
- Common base classes include:
	- `EnvironmentOptions` - provides environment-related arguments
	- `RemoteCommandOptions` - provides remote connection arguments

### 2. Command Documentation Structure

Create documentation following this template:

```markdown
# [Command Name]

## Purpose
Brief description of what the command does and when to use it.

## Usage
```bash
clio [command] [options]
```

## Arguments

### Required Arguments
| Argument | Short | Description | Example        |
|----------|-------|-------------|----------------|
| --name   | -n    | Description | `--name MyApp` |

### Optional Arguments
| Argument | Short | Default   | Description | Example                    |
|----------|-------|-----------|-------------|----------------------------|
| --uri    | -u    | localhost | Server URI  | `--uri https://mysite.com` |

## Examples

### Basic Usage
```bash
clio command --required-arg value
```

### Advanced Usage
```bash
clio command --required-arg value --optional-arg value
```

## Output
Description of what the command produces (console output, files created, etc.)

## Notes
Additional information, warnings, or tips for users.


### 3. Argument Documentation Guidelines

#### Required vs Optional
- **Required**: Arguments that must be provided or the command fails
- **Optional**: Arguments with default values or that can be omitted
- Check for `Required = true` attribute or validation logic in the command

#### Default Values
- Look for `Default = "value"` in attributes
- Check property initializers: `public string Uri { get; set; } = "localhost";`
- Note any defaults set in command logic

#### Argument Types
- **String**: Text values (most common)
- **Boolean**: Flags (true/false, often just presence indicates true)
- **Numeric**: Integer or decimal values
- **Enum**: Predefined set of values

### 4. Creating Realistic Examples

#### Basic Example
Show the simplest way to use the command with only required arguments.

#### Common Scenarios
Include examples for typical use cases:
- Development environment usage
- Production environment usage
- Different configuration scenarios

#### Complex Examples
Show advanced usage with multiple optional arguments.

### 5. Output Documentation

Describe what users can expect:
- **Console Messages**: Success/error messages, progress indicators
- **Files Created**: Location and format of generated files
- **Side Effects**: Changes to environment, configuration, or external systems

## File Naming and Location

### Documentation Files
- Save in `docs/commands/` directory
- Use kebab-case naming: `restart-command.md`, `install-app.md`
- Match the command name as users would type it

### Cross-References
- Link to related commands when relevant
- Reference the main `Commands.md` file for command lists
- Include links to external documentation when applicable

## Quality Checklist

Before finalizing documentation:

- [ ] All required arguments are clearly marked
- [ ] Default values are documented where applicable
- [ ] Inheritance from base classes is properly handled
- [ ] Examples are realistic and tested
- [ ] Output description matches actual command behavior
- [ ] Language is user-friendly (no internal jargon)
- [ ] File is saved in correct location with proper naming

## Common Patterns in Clio Commands

### Environment Commands
Most commands that interact with Creatio require:
- `--environment` or `-e` (required) - Creatio environment name
- Often inherit from `EnvironmentOptions`

### Remote Commands
Commands that connect to remote instances typically have:
- `--uri` or `-u` - Server URI (often has default)
- `--login` or `-l` - Username (for basic auth)
- `--password` or `-p` - Password (for basic auth)
- `--clientid` - OAuth Client ID (for OAuth authentication)
- `--clientsecret` - OAuth Client Secret (for OAuth authentication)
- `--authappuri` - OAuth Authentication App URI (for OAuth authentication)
- Often inherit from `RemoteCommandOptions`

**Note**: Remote commands support two authentication methods:
1. **Environment-based** (recommended): Use `-e` or `--environment` to reference pre-configured environment
2. **Direct authentication**: Either username/password OR OAuth credentials (clientid, clientsecret, authappuri)

**Important**: All examples in documentation should use the `-e` option as the primary method, with OAuth and username/password mentioned as alternatives.

### Package Commands
Commands working with packages usually include:
- `--name` or `-n` - Package name
- `--path` or `-p` - File system path
- `--force` or `-f` - Force overwrite (boolean flag)

## Example Command Analysis

For reference, here's how to analyze the `RestartCommand`:

1. **Find files**: `RestartCommand.cs` and `RestartOptions.cs`
2. **Check inheritance**: `RestartOptions : EnvironmentOptions`
3. **Document inherited**: Environment name from base class
4. **Check specific options**: Any restart-specific arguments
5. **Create examples**: Restarting different environments
6. **Save as**: `docs/commands/restart.md`

## Troubleshooting

### Missing Information
- If argument purpose is unclear, check the command's Execute method
- Look for validation logic to understand constraints
- Check unit tests for usage examples

### Complex Inheritance
- Map out the full inheritance chain
- Document all properties from all base classes
- Group related properties together in documentation
