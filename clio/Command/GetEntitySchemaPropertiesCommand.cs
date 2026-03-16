using System;
using Clio.Command.EntitySchemaDesigner;
using Clio.Common;
using CommandLine;

namespace Clio.Command;

/// <summary>
/// CLI options for reading remote entity schema properties.
/// </summary>
[Verb("get-entity-schema-properties", HelpText = "Get properties from a remote Creatio entity schema")]
public class GetEntitySchemaPropertiesOptions : RemoteCommandOptions
{
	[Option("package", Required = true, HelpText = "Target package name")]
	public string Package { get; set; }

	[Option("schema-name", Required = true, HelpText = "Entity schema name")]
	public string SchemaName { get; set; }
}

/// <summary>
/// Prints summary properties for a remote entity schema.
/// </summary>
public class GetEntitySchemaPropertiesCommand : Command<GetEntitySchemaPropertiesOptions>
{
	private readonly IRemoteEntitySchemaColumnManager _columnManager;
	private readonly ILogger _logger;

	public GetEntitySchemaPropertiesCommand(IRemoteEntitySchemaColumnManager columnManager, ILogger logger) {
		_columnManager = columnManager;
		_logger = logger;
	}

	public override int Execute(GetEntitySchemaPropertiesOptions options) {
		try {
			Validate(options);
			_columnManager.PrintSchemaProperties(options);
			return 0;
		} catch (Exception exception) {
			_logger.WriteError(exception.Message);
			return 1;
		}
	}

	private static void Validate(GetEntitySchemaPropertiesOptions options) {
		ArgumentNullException.ThrowIfNull(options);
		if (string.IsNullOrWhiteSpace(options.Package)) {
			throw new InvalidOperationException("Package is required.");
		}
		if (string.IsNullOrWhiteSpace(options.SchemaName)) {
			throw new InvalidOperationException("Schema name is required.");
		}
	}
}
