using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using Clio.Common;
using CommandLine;
using ConsoleTables;

namespace Clio.Command;

/// <summary>
/// CLI options for retrieving information about an installed Creatio application.
/// </summary>
[Verb("get-app-info", HelpText = "Get information about an installed Creatio application")]
public sealed class GetAppInfoOptions : EnvironmentOptions
{
	[Option("code", Required = false, HelpText = "Installed application code")]
	public string? Code { get; set; }

	[Option("id", Required = false, HelpText = "Installed application identifier (GUID)")]
	public string? Id { get; set; }

	[Option("json", Required = false, Default = false, HelpText = "Output as indented JSON instead of a table")]
	public bool JsonFormat { get; set; }
}

/// <summary>
/// Retrieves information about an installed Creatio application and prints it to the logger.
/// </summary>
public sealed class GetAppInfoCommand(
	IApplicationInfoService applicationInfoService,
	ILogger logger)
	: Command<GetAppInfoOptions>
{
	private static readonly JsonSerializerOptions WriteIndentedOptions = new() {
		WriteIndented = true,
		DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
	};

	/// <inheritdoc />
	public override int Execute(GetAppInfoOptions options)
	{
		try {
			ArgumentNullException.ThrowIfNull(options);
			if (string.IsNullOrWhiteSpace(options.Environment)) {
				throw new InvalidOperationException("Environment name is required.");
			}
			bool hasId = !string.IsNullOrWhiteSpace(options.Id);
			bool hasCode = !string.IsNullOrWhiteSpace(options.Code);
			if (!hasId && !hasCode) {
				throw new InvalidOperationException("Either --code or --id must be provided.");
			}

			ApplicationInfoResult result = applicationInfoService.GetApplicationInfo(
				options.Environment, options.Id, options.Code);

			if (options.JsonFormat) {
				logger.WriteInfo(JsonSerializer.Serialize(result, WriteIndentedOptions));
			} else {
				PrintAsTable(result);
			}
			return 0;
		} catch (Exception exception) {
			logger.WriteError(exception.Message);
			return 1;
		}
	}

	private void PrintAsTable(ApplicationInfoResult result)
	{
		string version = string.IsNullOrWhiteSpace(result.ApplicationVersion)
			? string.Empty
			: $" v{result.ApplicationVersion}";
		logger.WriteInfo($"Application: {result.ApplicationName} ({result.ApplicationCode}){version}");
		logger.WriteInfo($"Package: {result.PackageName}");
		if (result.Entities.Count == 0) {
			return;
		}
		logger.WriteLine();
		ConsoleTable table = new("Entity", "Caption", "Columns");
		foreach (ApplicationEntityInfoResult entity in result.Entities) {
			table.AddRow(entity.Name, entity.Caption, entity.Columns.Count);
		}
		logger.PrintTable(table);
	}
}
