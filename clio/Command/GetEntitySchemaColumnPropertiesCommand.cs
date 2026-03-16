using System;
using Clio.Command.EntitySchemaDesigner;
using Clio.Common;
using CommandLine;

namespace Clio.Command;

/// <summary>
/// CLI options for reading remote entity schema column properties.
/// </summary>
[Verb("get-entity-schema-column-properties",
	HelpText = "Get column properties from a remote Creatio entity schema")]
public class GetEntitySchemaColumnPropertiesOptions : RemoteCommandOptions
{
	[Option("package", Required = true, HelpText = "Target package name")]
	public string Package { get; set; }

	[Option("schema-name", Required = true, HelpText = "Entity schema name")]
	public string SchemaName { get; set; }

	[Option("column-name", Required = true, HelpText = "Column name")]
	public string ColumnName { get; set; }
}

/// <summary>
/// Prints properties for a remote entity schema column.
/// </summary>
public class GetEntitySchemaColumnPropertiesCommand : Command<GetEntitySchemaColumnPropertiesOptions>
{
	private readonly IRemoteEntitySchemaColumnManager _columnManager;
	private readonly ILogger _logger;

	public GetEntitySchemaColumnPropertiesCommand(IRemoteEntitySchemaColumnManager columnManager, ILogger logger) {
		_columnManager = columnManager;
		_logger = logger;
	}

	public override int Execute(GetEntitySchemaColumnPropertiesOptions options) {
		try {
			Validate(options);
			_columnManager.PrintColumnProperties(options);
			return 0;
		} catch (Exception exception) {
			_logger.WriteError(exception.Message);
			return 1;
		}
	}

	private static void Validate(GetEntitySchemaColumnPropertiesOptions options) {
		ArgumentNullException.ThrowIfNull(options);
		if (string.IsNullOrWhiteSpace(options.Package)) {
			throw new InvalidOperationException("Package is required.");
		}
		if (string.IsNullOrWhiteSpace(options.SchemaName)) {
			throw new InvalidOperationException("Schema name is required.");
		}
		if (string.IsNullOrWhiteSpace(options.ColumnName)) {
			throw new InvalidOperationException("Column name is required.");
		}
	}
}
