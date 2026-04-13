using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Clio.Common;
using Clio.Package;
using Clio.UserEnvironment;
using CommandLine;
using ConsoleTables;

namespace Clio.Command;

/// <summary>
/// CLI options for listing sections of an existing installed application.
/// </summary>
[Verb("list-app-sections", HelpText = "List sections of an existing installed application")]
public sealed class ApplicationSectionGetListOptions : EnvironmentOptions {
	[Option("application-code", Required = true, HelpText = "Installed application code")]
	public string ApplicationCode { get; set; } = string.Empty;

	[Option("json", Required = false, Default = false, HelpText = "Output as indented JSON instead of a table")]
	public bool JsonFormat { get; set; }
}

/// <summary>
/// Returns the list of sections for an installed application.
/// </summary>
public interface IApplicationSectionGetListService {
	/// <summary>
	/// Returns all sections of the specified installed application.
	/// </summary>
	/// <param name="environmentName">Registered clio environment name.</param>
	/// <param name="request">Section list request payload.</param>
	/// <returns>Structured result with section metadata list.</returns>
	ApplicationSectionGetListResult GetSections(string environmentName, ApplicationSectionGetListRequest request);
}

/// <summary>
/// Default ApplicationSection DataService-backed implementation for existing-app section listing.
/// </summary>
public sealed class ApplicationSectionGetListService(
	ISettingsRepository settingsRepository,
	IApplicationClientFactory applicationClientFactory,
	IServiceUrlBuilder serviceUrlBuilder,
	IApplicationInfoService applicationInfoService)
	: IApplicationSectionGetListService {
	private const string ApplicationSectionSchemaName = "ApplicationSection";
	private static readonly JsonSerializerOptions JsonOptions = new() {
		DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
		PropertyNameCaseInsensitive = true
	};

	/// <inheritdoc />
	public ApplicationSectionGetListResult GetSections(string environmentName, ApplicationSectionGetListRequest request) {
		if (string.IsNullOrWhiteSpace(environmentName)) {
			throw new ArgumentException("Environment name is required.", nameof(environmentName));
		}

		ArgumentNullException.ThrowIfNull(request);
		if (string.IsNullOrWhiteSpace(request.ApplicationCode)) {
			throw new ArgumentException("application-code is required.");
		}

		EnvironmentSettings environmentSettings = settingsRepository.FindEnvironment(environmentName)
			?? throw new InvalidOperationException(
				$"Environment with key '{environmentName}' not found. Check your clio configuration.");
		if (string.IsNullOrWhiteSpace(environmentSettings.Uri)) {
			throw new InvalidOperationException(
				$"Environment with key '{environmentName}' not found. Check your clio configuration.");
		}

		IApplicationClient client = applicationClientFactory.CreateEnvironmentClient(environmentSettings);
		ApplicationInfoResult applicationInfo = applicationInfoService.GetApplicationInfo(
			environmentName,
			null,
			request.ApplicationCode);
		string applicationId = applicationInfo.ApplicationId
			?? throw new InvalidOperationException("Application id was not returned by application-get-info.");
		IReadOnlyList<ApplicationSectionRecord> sections = GetAllSectionRecords(client, environmentSettings, applicationId);
		return new ApplicationSectionGetListResult(
			applicationInfo.PackageUId,
			applicationInfo.PackageName,
			applicationId,
			applicationInfo.ApplicationName ?? string.Empty,
			applicationInfo.ApplicationCode ?? request.ApplicationCode,
			applicationInfo.ApplicationVersion,
			sections.Select(MapSection).ToList());
	}

	private IReadOnlyList<ApplicationSectionRecord> GetAllSectionRecords(
		IApplicationClient client,
		EnvironmentSettings environmentSettings,
		string applicationId) {
		string responseBody = client.ExecutePostRequest(
			serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.Select, environmentSettings),
			JsonSerializer.Serialize(BuildSectionSelectQuery(applicationId), JsonOptions));
		ApplicationSectionSelectQueryResponseDto response =
			JsonSerializer.Deserialize<ApplicationSectionSelectQueryResponseDto>(responseBody, JsonOptions)
			?? throw new InvalidOperationException("ApplicationSection select query returned an empty response.");
		if (!response.Success) {
			throw new InvalidOperationException(response.ErrorInfo?.Message ?? "ApplicationSection select query failed.");
		}

		return response.Rows;
	}

	private static object BuildSectionSelectQuery(string applicationId) =>
		SelectQueryHelper.BuildSelectQuery(
			ApplicationSectionSchemaName,
			[
				new SelectQueryHelper.SelectQueryColumnDefinition("Id", "Id"),
				new SelectQueryHelper.SelectQueryColumnDefinition("ApplicationId", "ApplicationId"),
				new SelectQueryHelper.SelectQueryColumnDefinition("Caption", "Caption"),
				new SelectQueryHelper.SelectQueryColumnDefinition("Code", "Code"),
				new SelectQueryHelper.SelectQueryColumnDefinition("Description", "Description"),
				new SelectQueryHelper.SelectQueryColumnDefinition("EntitySchemaName", "EntitySchemaName"),
				new SelectQueryHelper.SelectQueryColumnDefinition("PackageId", "PackageId"),
				new SelectQueryHelper.SelectQueryColumnDefinition("SectionSchemaUId", "SectionSchemaUId"),
				new SelectQueryHelper.SelectQueryColumnDefinition("LogoId", "LogoId"),
				new SelectQueryHelper.SelectQueryColumnDefinition("IconBackground", "IconBackground"),
				new SelectQueryHelper.SelectQueryColumnDefinition("ClientTypeId", "ClientTypeId")
			],
			[
				new SelectQueryHelper.SelectQueryFilterDefinition(
					"ApplicationId",
					applicationId,
					SelectQueryHelper.GuidDataValueType)
			]);

	private static ApplicationSectionInfoResult MapSection(ApplicationSectionRecord record) =>
		new(
			record.Id,
			record.Code,
			record.Caption ?? string.Empty,
			record.Description,
			record.EntitySchemaName,
			record.PackageId,
			record.SectionSchemaUId,
			record.LogoId,
			record.IconBackground,
			record.ClientTypeId);

	private sealed class ApplicationSectionSelectQueryResponseDto : SelectQueryHelper.SelectQueryResponseBaseDto {
		[JsonPropertyName("rows")]
		public List<ApplicationSectionRecord> Rows { get; set; } = [];
	}
}

/// <summary>
/// Lists sections of an existing installed application and prints the structured result to the logger.
/// </summary>
public sealed class GetAppSectionsCommand(
	IApplicationSectionGetListService applicationSectionGetListService,
	ILogger logger)
	: Command<ApplicationSectionGetListOptions> {
	private static readonly JsonSerializerOptions WriteIndentedOptions = new() {
		WriteIndented = true,
		DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
	};

	/// <inheritdoc />
	public override int Execute(ApplicationSectionGetListOptions options) {
		try {
			ArgumentNullException.ThrowIfNull(options);
			if (string.IsNullOrWhiteSpace(options.Environment)) {
				throw new InvalidOperationException("Environment name is required.");
			}

			ApplicationSectionGetListResult result = applicationSectionGetListService.GetSections(
				options.Environment,
				new ApplicationSectionGetListRequest(options.ApplicationCode));

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

	private void PrintAsTable(ApplicationSectionGetListResult result) {
		string version = string.IsNullOrWhiteSpace(result.ApplicationVersion)
			? string.Empty
			: $" v{result.ApplicationVersion}";
		logger.WriteInfo($"Application: {result.ApplicationName} ({result.ApplicationCode}){version}");
		logger.WriteLine();
		ConsoleTable table = new("Code", "Caption", "EntitySchemaName", "Description");
		foreach (ApplicationSectionInfoResult section in result.Sections) {
			table.AddRow(
				section.Code,
				section.Caption,
				section.EntitySchemaName ?? string.Empty,
				section.Description ?? string.Empty);
		}
		logger.PrintTable(table);
	}
}

/// <summary>
/// Request payload for listing sections of an installed application.
/// </summary>
/// <param name="ApplicationCode">Installed application code.</param>
public sealed record ApplicationSectionGetListRequest(string ApplicationCode);

/// <summary>
/// Structured result for listing sections of an installed application.
/// </summary>
/// <param name="PackageUId">Primary package identifier.</param>
/// <param name="PackageName">Primary package name.</param>
/// <param name="ApplicationId">Installed application identifier.</param>
/// <param name="ApplicationName">Installed application display name.</param>
/// <param name="ApplicationCode">Installed application code.</param>
/// <param name="ApplicationVersion">Installed application version.</param>
/// <param name="Sections">List of section metadata records.</param>
public sealed record ApplicationSectionGetListResult(
	string PackageUId,
	string PackageName,
	string ApplicationId,
	string ApplicationName,
	string ApplicationCode,
	string? ApplicationVersion,
	IReadOnlyList<ApplicationSectionInfoResult> Sections);
