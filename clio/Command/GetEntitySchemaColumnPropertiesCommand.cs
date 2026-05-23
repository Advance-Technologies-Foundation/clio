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
	[Option("package", Required = false, HelpText = "Target package name")]
	public string Package { get; set; }

	[Option("package-name", Required = false, Hidden = true, HelpText = "Alias for --package")]
	public string? PackageNameAlias {
		get => Package;
		set { if (!string.IsNullOrEmpty(value)) Package = value; }
	}

	[Option("schema-name", Required = false, HelpText = "Entity schema name")]
	public string SchemaName { get; set; }

	[Option("column-name", Required = false, HelpText = "Column name")]
	public string ColumnName { get; set; }

	[Option("column", Required = false, Hidden = true, HelpText = "Alias for --column-name")]
	public string? ColumnAlias {
		get => ColumnName;
		set { if (!string.IsNullOrEmpty(value)) ColumnName = value; }
	}
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
			EntitySchemaColumnPropertiesInfo properties = GetColumnProperties(options);
			WriteColumnProperties(properties);
			return 0;
		} catch (Exception exception) {
			_logger.WriteError(exception.Message);
			return 1;
		}
	}

	internal EntitySchemaColumnPropertiesInfo GetColumnProperties(GetEntitySchemaColumnPropertiesOptions options) {
		Validate(options);
		return _columnManager.GetColumnProperties(options);
	}

	private static void Validate(GetEntitySchemaColumnPropertiesOptions options) {
		ArgumentNullException.ThrowIfNull(options);
		if (string.IsNullOrWhiteSpace(options.Package)) {
			throw new ArgumentException("Package is required.", nameof(options.Package));
		}
		if (string.IsNullOrWhiteSpace(options.SchemaName)) {
			throw new ArgumentException("Schema name is required.", nameof(options.SchemaName));
		}
		if (string.IsNullOrWhiteSpace(options.ColumnName)) {
			throw new ArgumentException("Column name is required.", nameof(options.ColumnName));
		}
	}

	private void WriteColumnProperties(EntitySchemaColumnPropertiesInfo properties) {
		_logger.WriteInfo("Entity schema column properties");
		_logger.WriteInfo($"Schema: {properties.SchemaName}");
		_logger.WriteInfo($"Package: {properties.PackageName}");
		_logger.WriteInfo($"Column: {properties.ColumnName}");
		_logger.WriteInfo($"Source: {properties.Source}");
		_logger.WriteInfo($"Title: {FormatText(properties.Title)}");
		_logger.WriteInfo($"Description: {FormatText(properties.Description)}");
		_logger.WriteInfo($"Type: {properties.Type}");
		_logger.WriteInfo($"Required: {FormatBoolean(properties.Required)}");
		_logger.WriteInfo($"Indexed: {FormatBoolean(properties.Indexed)}");
		_logger.WriteInfo($"Cloneable: {FormatBoolean(properties.Cloneable)}");
		_logger.WriteInfo($"Track changes: {FormatBoolean(properties.TrackChanges)}");
		_logger.WriteInfo($"Default value source: {FormatText(properties.DefaultValueSource)}");
		_logger.WriteInfo($"Default value: {FormatText(properties.DefaultValue)}");
		_logger.WriteInfo($"Reference schema: {FormatText(properties.ReferenceSchemaName)}");
		_logger.WriteInfo($"Simple lookup: {FormatBoolean(properties.SimpleLookup)}");
		_logger.WriteInfo($"Cascade: {FormatBoolean(properties.Cascade)}");
		_logger.WriteInfo($"Do not control integrity: {FormatBoolean(properties.DoNotControlIntegrity)}");
		_logger.WriteInfo($"Multiline text: {FormatBoolean(properties.MultilineText)}");
		_logger.WriteInfo($"Localizable text: {FormatBoolean(properties.LocalizableText)}");
		_logger.WriteInfo($"Accent insensitive: {FormatBoolean(properties.AccentInsensitive)}");
		_logger.WriteInfo($"Masked: {FormatBoolean(properties.Masked)}");
		_logger.WriteInfo($"Format validated: {FormatBoolean(properties.FormatValidated)}");
		_logger.WriteInfo($"Use seconds: {FormatBoolean(properties.UseSeconds)}");
	}

	private static string FormatBoolean(bool value) {
		return value ? "true" : "false";
	}

	private static string FormatText(string? value) {
		return string.IsNullOrWhiteSpace(value) ? "<none>" : value;
	}
}
