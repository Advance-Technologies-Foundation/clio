using System;
using Clio.Command.EntitySchemaDesigner;
using Clio.Common;
using CommandLine;

namespace Clio.Command;

/// <summary>
/// CLI options for setting schema-level properties on a remote Creatio entity schema.
/// </summary>
/// <remarks>
/// This is an extensible property bag: each settable schema-level property is a separate optional
/// <see cref="OptionAttribute"/>, and only the supplied ones are applied. New schema-level properties can be
/// added as further optional options without breaking the existing command/tool contract (ENG-93040 FR-11).
/// </remarks>
[Verb("set-entity-schema-properties", HelpText = "Set schema-level properties on a remote Creatio entity schema")]
public class SetEntitySchemaPropertiesOptions : RemoteCommandOptions
{
	// Required is enforced in ValidateOptions (not via CommandLineParser's Required=true) so the hidden
	// --package-name / --name aliases work when used standalone — the parser enforces Required on the
	// canonical token's presence, which would reject an alias-only invocation. Mirrors ModifyEntitySchemaColumnOptions.
	[Option("package", Required = false, HelpText = "Target package name")]
	public string Package { get; set; }

	[Option("package-name", Required = false, Hidden = true, HelpText = "Alias for --package")]
	public string? PackageNameAlias {
		get => Package;
		set { if (!string.IsNullOrEmpty(value)) Package = value; }
	}

	[Option("schema-name", Required = false, HelpText = "Entity schema name")]
	public string SchemaName { get; set; }

	[Option("name", Required = false, Hidden = true, HelpText = "Alias for --schema-name")]
	public string? SchemaNameAlias {
		get => SchemaName;
		set { if (!string.IsNullOrEmpty(value)) SchemaName = value; }
	}

	[Option("primary-display-column", Required = false,
		HelpText = "Column name (own or inherited) to set as the primary-display column")]
	public string? PrimaryDisplayColumn { get; set; }
}

/// <summary>
/// Sets schema-level properties (v1: the primary-display column) on a remote entity schema through the
/// Entity Schema Designer save pipeline, then verifies the change was persisted.
/// </summary>
public class SetEntitySchemaPropertiesCommand : Command<SetEntitySchemaPropertiesOptions>
{
	private readonly IRemoteEntitySchemaColumnManager _columnManager;
	private readonly ILogger _logger;

	/// <summary>
	/// Initializes a new instance of the <see cref="SetEntitySchemaPropertiesCommand"/> class.
	/// </summary>
	/// <param name="columnManager">Manager that applies and persists the schema-property change.</param>
	/// <param name="logger">Logger for progress and error output.</param>
	public SetEntitySchemaPropertiesCommand(IRemoteEntitySchemaColumnManager columnManager, ILogger logger) {
		_columnManager = columnManager;
		_logger = logger;
	}

	/// <inheritdoc />
	public override int Execute(SetEntitySchemaPropertiesOptions options) {
		try {
			ValidateOptions(options);
			_columnManager.SetSchemaProperties(options);
			_logger.WriteInfo("Done");
			return 0;
		} catch (Exception exception) {
			_logger.WriteError(exception.Message);
			return 1;
		}
	}

	internal static void ValidateOptions(SetEntitySchemaPropertiesOptions options) {
		ArgumentNullException.ThrowIfNull(options);
		if (string.IsNullOrWhiteSpace(options.Package)) {
			throw new ArgumentException("Package is required.", nameof(options.Package));
		}
		if (string.IsNullOrWhiteSpace(options.SchemaName)) {
			throw new ArgumentException("Schema name is required.", nameof(options.SchemaName));
		}
		if (string.IsNullOrWhiteSpace(options.PrimaryDisplayColumn)) {
			throw new ArgumentException(
				"At least one schema property to set is required (for example --primary-display-column).",
				nameof(options.PrimaryDisplayColumn));
		}
	}
}
