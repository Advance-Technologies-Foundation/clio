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

## MCP Server (Model Context Protocol)

Clio includes an MCP (Model Context Protocol) server that provides AI agents with tools to interact with Creatio platforms. The MCP server enables automated development, testing, and operations through a standardized protocol.

### Starting MCP Server

```bash
clio mcp-server
```

The server starts on stdio transport and exposes 46+ tools for Creatio operations.

### MCP Tool Categories

#### DB-first Backend Tools (15 tools)
These tools work directly with the Creatio database through the backend MCP endpoint (`/mcp`), providing a DB-first approach without local file system requirements.

**Application Tools:**
- `application-create-db` - Create Creatio applications
- `application-get-info-db` - Get application information
- `application-get-list-db` - List all applications

**Entity Tools:**
- `entity-create-db` - Create entity schemas
- `entity-create-lookup-db` - Create lookup schemas
- `entity-update-db` - Update entity schemas (add/update/delete columns)
- `entity-check-name-db` - Check if entity name is taken
- `entity-list-packages-db` - List available packages
- `entity-get-schema-db` - Get detailed entity schema

**Binding Tools:**
- `binding-create-db` - Create data bindings (insert rows)
- `binding-get-columns-db` - Get columns for binding

**Page Tools (Freedom UI):**
- `page-get-db` - Get Freedom UI page schema
- `page-update-db` - Update Freedom UI page schema
- `page-list-db` - List all Freedom UI pages

#### File-first Legacy Tools (31+ tools)
Traditional tools working with local files and packages (create-entity-schema, create-data-binding, etc.)

### Usage with AI Agents

The MCP server is designed to work with AI agents like Claude Desktop, GitHub Copilot, or any MCP-compatible client:

1. Configure your AI client to connect to clio MCP server
2. AI agent can discover and use all available tools
3. Tools provide structured input/output for reliable automation

### Documentation

- **Tool Prompts:** `clio/Command/McpServer/Prompts/` - Detailed usage guides for each tool category
- **AGENTS.md:** `clio/Command/McpServer/AGENTS.md` - Guidelines for AI agents using MCP tools

### Backend MCP Integration

Clio's DB-first tools require a running Creatio instance with MCP support:
- Backend endpoint: `http://localhost:5001/mcp`
- Protocol: MCP over Streamable HTTP
- Authentication: HTTP Basic Auth

For more information about Creatio backend MCP implementation, see the Terrasoft.Mcp module documentation.

## Workspace Solution Generation (.slnx)

Starting from September 2025, the `createw` command generates a solution file in `.slnx` format. All projects are added in a sorted order by their relative path, ensuring a stable and repeatable solution structure.

- Solution file: `.solution/CreatioPackages.slnx`
- Projects: always sorted by path
- Command: `clio createw`

This change improves consistency for CI/CD and version control.
