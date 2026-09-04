using System;
using System.Collections.Generic;
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
	/// <summary>
	/// Single source of truth for the "no settable property supplied" error, shared by the command-level
	/// <see cref="SetEntitySchemaPropertiesCommand.ValidateOptions"/> pre-check and the manager-level defensive
	/// re-check so the two layers cannot drift.
	/// </summary>
	internal const string NoPropertyToSetError =
		"At least one schema property to set is required " +
		"(for example --primary-display-column, --title or --title-localizations).";

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

	/// <summary>
	/// Gets or sets the new schema caption for a single culture. Ignored when
	/// <see cref="TitleLocalizations"/> is supplied.
	/// </summary>
	[Option("title", Required = false,
		HelpText = "New schema caption for the effective caption culture (see --caption-culture)")]
	public string? Title { get; set; }

	/// <summary>
	/// Gets or sets the new schema caption per culture, as a JSON object such as
	/// <c>{"en-US":"Mention language"}</c>. Cultures that are not listed are left untouched.
	/// </summary>
	[Option("title-localizations", Required = false,
		HelpText = "New schema caption per culture as JSON, e.g. '{\"en-US\":\"Mention language\"}'")]
	public string? TitleLocalizations { get; set; }

	/// <summary>
	/// Gets or sets the culture used when only the scalar <see cref="Title"/> is supplied.
	/// Precedence: this override, then the connected user's profile culture, then <c>en-US</c>.
	/// </summary>
	[Option("caption-culture", Required = false,
		HelpText = "Culture used for a scalar --title (e.g. en-US). Precedence: this override > profile culture > en-US")]
	public string? CaptionCulture { get; set; }

	/// <summary>
	/// Gets the parsed <see cref="TitleLocalizations"/> map, or <c>null</c> when none was supplied.
	/// Set by the MCP tool and by <see cref="SetEntitySchemaPropertiesCommand.ValidateOptions"/>.
	/// </summary>
	public IReadOnlyDictionary<string, string>? ParsedTitleLocalizations { get; set; }

	/// <summary>
	/// Gets a value indicating whether any settable schema-level property was supplied.
	/// </summary>
	/// <remarks>
	/// Deliberately does NOT look at the raw <see cref="TitleLocalizations"/> JSON string: the write path
	/// acts on <see cref="ParsedTitleLocalizations"/> and <see cref="Title"/> only, so counting the
	/// unparsed string would let this guard pass on a request the manager then saves without any change.
	/// <see cref="SetEntitySchemaPropertiesCommand.ValidateOptions"/> populates the map before checking.
	/// </remarks>
	internal bool HasAnyPropertyToSet =>
		!string.IsNullOrWhiteSpace(PrimaryDisplayColumn)
		|| !string.IsNullOrWhiteSpace(Title)
		|| ParsedTitleLocalizations is { Count: > 0 };
}

/// <summary>
/// Sets schema-level properties (the primary-display column and the schema caption per culture) on a remote entity schema through the
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
			throw new ArgumentException("Package is required.", nameof(options));
		}
		if (string.IsNullOrWhiteSpace(options.SchemaName)) {
			throw new ArgumentException("Schema name is required.", nameof(options));
		}
		if (!string.IsNullOrWhiteSpace(options.TitleLocalizations) && options.ParsedTitleLocalizations is null) {
			options.ParsedTitleLocalizations =
				EntitySchemaDesignerSupport.ParseLocalizationJson(options.TitleLocalizations, "title-localizations");
		} else if (options.ParsedTitleLocalizations is not null) {
			// The MCP tool hands the map over already deserialized. Normalize it through the SAME entry
			// point as the CLI's JSON string - including the culture-name check and the ENG-91044
			// script/culture guard - so an empty culture name, an unknown culture or a caption in the
			// wrong script is rejected up front on both surfaces instead of reaching the designer save
			// and failing only at the readback check.
			options.ParsedTitleLocalizations = EntitySchemaDesignerSupport.NormalizeSchemaCaptionLocalizations(
				options.ParsedTitleLocalizations, "title-localizations");
		}
		if (!options.HasAnyPropertyToSet) {
			throw new ArgumentException(SetEntitySchemaPropertiesOptions.NoPropertyToSetError, nameof(options));
		}
	}
}
