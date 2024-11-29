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

## MacOS

1. Download [.net core](https://dotnet.microsoft.com/en-us/download/dotnet/6.0) for mac
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

[Commands Reference](clio\Commands.md)
