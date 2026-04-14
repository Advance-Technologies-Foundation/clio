[![Build](https://github.com/Advance-Technologies-Foundation/clio/actions/workflows/build.yml/badge.svg)](https://github.com/Advance-Technologies-Foundation/clio/actions/workflows/build.yml)

# Introduction

Command Line Interface clio is the utility for integration Creatio platform with development and CI/CD tools.

Please give **[clio-explorer](https://marketplace.visualstudio.com/items?itemName=AdvanceTechnologiesFoundation.clio-explorer)**, a Visual Studio code extension for **clio** a try! This extension provides user interface over clio commands.

# Installation and features

## Windows

To register clio as the global tool, run the command:

```
dotnet tool install clio
```

you can register clio for all users:

```
dotnet tool install clio -g
```

To unregister clio as the global tool, run the command:

```
dotnet tool uninstall clio
```

or for all users:

```
dotnet tool uninstall clio -g
```

More information you can see in [.NET Core Global Tools overview](https://docs.microsoft.com/en-US/dotnet/core/tools/global-tools).

## Context menu

```
clio register
```
https://user-images.githubusercontent.com/26967647/169416137-351674ca-0bd2-44f1-83af-df4557bd02fd.mp4

```
clio unregister
```

## MacOS / Linux

1. Download [.net 8](https://dotnet.microsoft.com/en-us/download/dotnet/8.0) for Mac/Linux

2. Register clio as the global tool, with the command:

```
dotnet tool install clio
```

More information you can see in [.NET Core Global Tools overview](https://docs.microsoft.com/en-US/dotnet/core/tools/global-tools).

Execute command in terminal for success check

```
clio help
```

## Help and examples

To display available commands use:

```
clio help
```

For display command help use:

```
clio <COMMAND_NAME> --help
```

## Run with docker

### Build

```
docker build -f ./install/Dockerfile -t clio .
```

### Run

```
docker run -it --rm clio help
docker run -it --rm clio reg-web-app -help
```

## Commands Reference
[Explore clio commands](clio/Commands.md)

## MCP Server

Clio supports Model Context Protocol (MCP) for integration with AI assistants.

### Testing with MCP Inspector

```bash
npx @modelcontextprotocol/inspector dotnet run --project ~/Projects/clio/clio mcp-server
```

Or configure manually:
- **Command:** `dotnet`
- **Arguments:** `run --project /path/to/clio/clio mcp-server`
- **Transport Type:** STDIO

This opens a browser interface to test MCP tools.

### Available MCP Tools

**list-pages** - List Freedom UI pages
- `packageName` (optional) - Filter by package
- `searchPattern` (optional) - Filter by name pattern
- `limit` (optional) - Max results (default: 50)
- `environmentName` / `uri+login+password` - Connection

**get-page** - Get page schema body
- `schemaName` (required) - Page schema name
- `environmentName` / `uri+login+password` - Connection

**update-page** - Update page body (Destructive)
- `schemaName` (required) - Page schema name
- `body` (required) - New JSON body
- `dryRun` (optional) - Validate only
- `environmentName` / `uri+login+password` - Connection

**install-application** - Install application package

## Workspace Solution Generation (.slnx)

Starting from September 2025, the `createw` command generates a solution file in `.slnx` format. All projects are added in a sorted order by their relative path, ensuring a stable and repeatable solution structure.

- Solution file: `.solution/CreatioPackages.slnx`
- Projects: always sorted by path
- Command: `clio createw`

This change improves consistency for CI/CD and version control.
