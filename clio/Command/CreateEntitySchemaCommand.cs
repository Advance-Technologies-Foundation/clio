using System;
using System.Collections.Generic;
using Clio.Command.EntitySchemaDesigner;
using Clio.Common;
using CommandLine;

namespace Clio.Command;

[Verb("create-entity-schema", HelpText = "Create an entity schema in a remote Creatio package")]
public class CreateEntitySchemaOptions : RemoteCommandOptions
{
	[Option("package", Required = true, HelpText = "Target package name")]
	public string Package { get; set; }

	[Option("name", Required = true, HelpText = "Schema name")]
	public string SchemaName { get; set; }

	[Option("title", Required = true, HelpText = "Schema title")]
	public string Title { get; set; }

	[Option("parent", Required = false, HelpText = "Parent schema name")]
	public string ParentSchemaName { get; set; }

	[Option("extend-parent", Required = false, Default = false, HelpText = "Create replacement schema")]
	public bool ExtendParent { get; set; }

	[Option("column", Required = false, HelpText = "Column spec <name>:<type>[:<title>[:<refSchema>]]", Separator = ';')]
	public IEnumerable<string> Columns { get; set; }
}

public class CreateEntitySchemaCommand : Command<CreateEntitySchemaOptions>
{
	private readonly IRemoteEntitySchemaCreator _remoteEntitySchemaCreator;
	private readonly ILogger _logger;

	public CreateEntitySchemaCommand(IRemoteEntitySchemaCreator remoteEntitySchemaCreator, ILogger logger)
	{
		_remoteEntitySchemaCreator = remoteEntitySchemaCreator;
		_logger = logger;
	}

	public override int Execute(CreateEntitySchemaOptions options)
	{
		try {
			Validate(options);
			_remoteEntitySchemaCreator.Create(options);
			_logger.WriteInfo("Done");
			return 0;
		} catch (Exception exception) {
			_logger.WriteError(exception.Message);
			return 1;
		}
	}

	private static void Validate(CreateEntitySchemaOptions options)
	{
		if (options == null) {
			throw new InvalidOperationException("Command options are required.");
		}
		if (string.IsNullOrWhiteSpace(options.Package)) {
			throw new InvalidOperationException("Package is required.");
		}
		if (string.IsNullOrWhiteSpace(options.SchemaName)) {
			throw new InvalidOperationException("Schema name is required.");
		}
		if (options.SchemaName.Trim().Length > 22) {
			throw new InvalidOperationException("Schema name must not exceed 22 characters.");
		}
		if (string.IsNullOrWhiteSpace(options.Title)) {
			throw new InvalidOperationException("Schema title is required.");
		}
		if (options.ExtendParent && string.IsNullOrWhiteSpace(options.ParentSchemaName)) {
			throw new InvalidOperationException("--extend-parent requires --parent.");
		}
	}
}
