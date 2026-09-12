using System;
using Clio.Common;
using CommandLine;

namespace Clio.Command;

/// <summary>
/// CLI options for creating a new Creatio application.
/// </summary>
[Verb("create-app", HelpText = "Create a new application in Creatio")]
public sealed class CreateAppOptions : EnvironmentOptions
{
	[Option("name", Required = true, HelpText = "Application display name")]
	public string Name { get; set; } = string.Empty;

	[Option("code", Required = true, HelpText = "Application code (active SchemaNamePrefix is auto-applied; default prefix is Usr)")]
	public string Code { get; set; } = string.Empty;

	[Option("template-code", Required = true, HelpText = "Technical template name (AppFreedomUIv2, AppFreedomUI, AppWithHomePage, EmptyApp)")]
	public string TemplateCode { get; set; } = string.Empty;

	[Option("icon-background", Required = false, HelpText = "Icon background color in #RRGGBB format, e.g. #1F5F8B. Defaults to a random color when omitted.")]
	public string? IconBackground { get; set; }

	[Option("description", Required = false, HelpText = "Application description")]
	public string? Description { get; set; }

	[Option("icon-id", Required = false, HelpText = "Application icon GUID or 'auto' to pick a random icon")]
	public string? IconId { get; set; }

	[Option("with-mobile-pages", Required = false, Default = "true", HelpText = "Create mobile pages (_MobileFormPage, _MobileListPage) for the main entity in addition to web pages (default: true). Pass false for a web-only application.")]
	public string? WithMobilePagesValue { get; set; }

	[Option("entity-schema-name", Required = false, HelpText = "Name of an EXISTING entity schema the application's primary section is built over. The entity must already exist in the environment; Creatio then skips minting a new canonical entity.")]
	public string? EntitySchemaName { get; set; }

	[Option("app-section-description", Required = false, HelpText = "Description applied to the application's primary section.")]
	public string? AppSectionDescription { get; set; }

	/// <summary>
	/// Gets a value indicating whether mobile pages should be generated for the main entity.
	/// </summary>
	public bool WithMobilePages {
		get => string.Equals(WithMobilePagesValue ?? "true", "true", StringComparison.OrdinalIgnoreCase);
		set => WithMobilePagesValue = value ? "true" : "false";
	}

	/// <summary>
	/// Builds the optional CreateApp template payload from the CLI options, or returns
	/// <see langword="null"/> when neither template option was supplied so the request stays
	/// byte-identical to a call that predates these options.
	/// </summary>
	internal ApplicationOptionalTemplateData? BuildOptionalTemplateData() {
		bool hasEntitySchemaName = !string.IsNullOrWhiteSpace(EntitySchemaName);
		bool hasSectionDescription = !string.IsNullOrWhiteSpace(AppSectionDescription);
		if (!hasEntitySchemaName && !hasSectionDescription) {
			return null;
		}

		// --entity-schema-name alone carries the intent, so useExistingEntitySchema is implied here
		// instead of being a second flag the caller can contradict.
		return new ApplicationOptionalTemplateData(
			EntitySchemaName: hasEntitySchemaName ? EntitySchemaName!.Trim() : null,
			UseExistingEntitySchema: hasEntitySchemaName ? true : null,
			AppSectionDescription: hasSectionDescription ? AppSectionDescription!.Trim() : null);
	}

	internal static void ValidateMobilePagesOption(string? value) {
		if (value is null) {
			return;
		}

		if (!string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) &&
			!string.Equals(value, "false", StringComparison.OrdinalIgnoreCase)) {
			throw new ArgumentException($"Invalid value '{value}' for --with-mobile-pages. Allowed values: true, false.");
		}
	}
}

/// <summary>
/// Creates a new Creatio application and prints the result to the logger.
/// </summary>
public sealed class CreateAppCommand(
	IApplicationCreateService applicationCreateService,
	ILogger logger)
	: Command<CreateAppOptions>
{
	/// <inheritdoc />
	public override int Execute(CreateAppOptions options)
	{
		try {
			ArgumentNullException.ThrowIfNull(options);
			if (string.IsNullOrWhiteSpace(options.Environment)) {
				throw new InvalidOperationException("Environment name is required.");
			}
			CreateAppOptions.ValidateMobilePagesOption(options.WithMobilePagesValue);

			ApplicationCreateRequest request = new(
				options.Name,
				options.Code,
				options.Description,
				options.TemplateCode,
				options.IconId,
				options.IconBackground,
				OptionalTemplateData: options.BuildOptionalTemplateData(),
				WithMobilePages: options.WithMobilePages);

			ApplicationInfoResult result = applicationCreateService.CreateApplication(options.Environment, request);

			logger.WriteInfo($"Application created: {result.ApplicationName} ({result.ApplicationCode}) v{result.ApplicationVersion}");
			logger.WriteInfo($"Package: {result.PackageName}");
			return 0;
		} catch (Exception exception) {
			logger.WriteError(exception.Message);
			return 1;
		}
	}
}
