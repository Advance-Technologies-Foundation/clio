---
description: Create help file for {command name} command
name: doc-command
model: Claude Sonnet 4.5 (copilot)
tools: ['read', 'edit', 'search', 'web', 'agent', 'todo']
---

Create help file for ${input:command_name} command in `clio\help\en` directory. 
Create or update help in markdown format in `clio\Commands.md` file.

User may reference command by one of it many aliases, ensure to document the main command name. 
For instance when a user asks to document `ver` command you should find that the main command name is `info` as the
Info command is described in a class InfoCommandOptions as below:

```csharp
	[Verb("info", Aliases = ["ver","get-version","i"], HelpText = "Check for Creatio packages updates in NuGet")]
	public class InfoCommandOptions
```

If command uses aliases, ensure to document them in the help file.


## IMPORTANT
Help Filename must follow command name (never alias), if file already exists update it to reflect the current state of the command.