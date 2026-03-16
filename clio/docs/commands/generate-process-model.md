# generate-process-model

## Purpose
Generates a strongly typed C# process model for ATF.Repository from a Creatio process schema.

## Usage
```bash
clio generate-process-model <Code> [options]
```

## Arguments

| Argument | Position | Required | Description | Example |
|----------|----------|----------|-------------|---------|
| `Code` | 0 | Yes | Process code as it appears in the Creatio process designer | `UsrStartOrder` |

## Options

| Argument | Short | Default | Description |
|----------|-------|---------|-------------|
| `--DestinationPath` | `-d` | `.` | Destination folder or explicit `.cs` file path |
| `--Namespace` | `-n` | `AtfTIDE.ProcessModels` | Namespace for the generated process model class |
| `--Culture` | `-x` | `en-US` | Culture used to resolve localized captions and descriptions |

## Inherited Environment Arguments

| Argument | Short | Description |
|----------|-------|-------------|
| `--Environment` | `-e` | Registered environment name |
| `--uri` |  | Application URI |
| `--Login` | `-l` | User login |
| `--Password` | `-p` | User password |
| `--clientId` |  | OAuth client ID |
| `--clientSecret` |  | OAuth client secret |
| `--authAppUri` |  | OAuth authentication app URI |

## Output Behavior

When `DestinationPath` points to a folder, the command writes `<Code>.cs` into that folder.

When `DestinationPath` points to an explicit `.cs` file path, the command writes the generated model to that exact file.

## Examples

Generate into the current directory:

```bash
clio generate-process-model UsrStartOrder -e dev
```

Generate into a folder:

```bash
clio generate-process-model UsrStartOrder -e dev -d C:\Models
```

Generate into an explicit file:

```bash
clio generate-process-model UsrStartOrder -e dev -d C:\Models\OrderStart.cs
```

Generate with a custom namespace and culture:

```bash
clio generate-process-model UsrStartOrder -e dev -n Contoso.ProcessModels -x uk-UA
```
