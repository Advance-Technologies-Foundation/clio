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

	[Option("code", Required = true, HelpText = "Application code starting with Usr prefix, e.g. UsrMyApp")]
	public string Code { get; set; } = string.Empty;

	[Option("template-code", Required = true, HelpText = "Technical template name (AppFreedomUIv2, AppFreedomUI, AppWithHomePage, EmptyApp)")]
	public string TemplateCode { get; set; } = string.Empty;

	[Option("icon-background", Required = false, HelpText = "Icon background color in #RRGGBB format, e.g. #1F5F8B. Defaults to a random color when omitted.")]
	public string? IconBackground { get; set; }

	[Option("description", Required = false, HelpText = "Application description")]
	public string? Description { get; set; }

	[Option("icon-id", Required = false, HelpText = "Application icon GUID or 'auto' to pick a random icon")]
	public string? IconId { get; set; }
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

			ApplicationCreateRequest request = new(
				options.Name,
				options.Code,
				options.Description,
				options.TemplateCode,
				options.IconId,
				options.IconBackground);

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
