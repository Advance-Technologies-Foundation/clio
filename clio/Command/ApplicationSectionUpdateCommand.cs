using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Clio.Common;
using Clio.Package;
using Clio.UserEnvironment;
using CommandLine;

namespace Clio.Command;

/// <summary>
/// CLI options for updating metadata of an existing installed application section.
/// </summary>
[Verb("update-app-section", HelpText = "Update metadata of a section inside an existing installed application")]
public sealed class UpdateAppSectionOptions : EnvironmentOptions {
	[Option("application-code", Required = true, HelpText = "Installed application code")]
	public string ApplicationCode { get; set; } = string.Empty;

	[Option("section-code", Required = true, HelpText = "Section code inside the installed application")]
	public string SectionCode { get; set; } = string.Empty;

	[Option("caption", Required = false, HelpText = "Updated section caption")]
	public string? Caption { get; set; }

	[Option("description", Required = false, HelpText = "Updated section description")]
	public string? Description { get; set; }

	[Option("icon-id", Required = false, HelpText = "Updated section icon GUID")]
	public string? IconId { get; set; }

	[Option("icon-background", Required = false, HelpText = "Updated section icon background in #RRGGBB format")]
	public string? IconBackground { get; set; }
}

/// <summary>
/// Updates metadata of an existing installed application section.
/// </summary>
public interface IApplicationSectionUpdateService {
	/// <summary>
	/// Updates metadata of an existing installed application section in the specified environment.
	/// </summary>
	/// <param name="environmentName">Registered clio environment name.</param>
	/// <param name="request">Section update request payload.</param>
	/// <returns>Structured application and section metadata before and after the update.</returns>
	ApplicationSectionUpdateResult UpdateSection(string environmentName, ApplicationSectionUpdateRequest request);
}

/// <summary>
/// Default ApplicationSection DataService-backed implementation for existing-app section updates.
/// </summary>
public sealed class ApplicationSectionUpdateService(
	ISettingsRepository settingsRepository,
	IApplicationClientFactory applicationClientFactory,
	IServiceUrlBuilder serviceUrlBuilder,
	IApplicationInfoService applicationInfoService)
	: IApplicationSectionUpdateService {
	private const string ApplicationSectionSchemaName = "ApplicationSection";
	private static readonly JsonSerializerOptions JsonOptions = new() {
		DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
		PropertyNameCaseInsensitive = true
	};
	private static readonly Regex HexColorRegex = new("^#[0-9A-Fa-f]{6}$", RegexOptions.None, TimeSpan.FromSeconds(5));

	/// <inheritdoc />
	public ApplicationSectionUpdateResult UpdateSection(string environmentName, ApplicationSectionUpdateRequest request) {
		if (string.IsNullOrWhiteSpace(environmentName)) {
			throw new ArgumentException("Environment name is required.", nameof(environmentName));
		}

		ArgumentNullException.ThrowIfNull(request);
		ValidateRequest(request);
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
			?? throw new InvalidOperationException("Application id was not returned by get-app-info.");
		ApplicationSectionRecord previousSection = GetSectionRecord(
			client,
			environmentSettings,
			applicationId,
			request.SectionCode);
		ResolvedApplicationSectionUpdateRequest resolvedRequest = ResolveRequest(request);
		string requestBody = JsonSerializer.Serialize(BuildUpdateBody(previousSection, resolvedRequest), JsonOptions);
		string responseBody = client.ExecutePostRequest(
			serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.Update, environmentSettings),
			requestBody);
		UpdateQueryResponseDto response = JsonSerializer.Deserialize<UpdateQueryResponseDto>(responseBody, JsonOptions)
			?? throw new InvalidOperationException("UpdateQuery returned an empty response.");
		if (!response.Success) {
			throw new InvalidOperationException(response.ErrorInfo?.Message ?? "UpdateQuery failed.");
		}

		ApplicationSectionRecord updatedSection = GetSectionRecord(
			client,
			environmentSettings,
			applicationId,
			request.SectionCode);
		return new ApplicationSectionUpdateResult(
			applicationInfo.PackageUId,
			applicationInfo.PackageName,
			applicationId,
			applicationInfo.ApplicationName ?? string.Empty,
			applicationInfo.ApplicationCode ?? request.ApplicationCode,
			applicationInfo.ApplicationVersion,
			MapSection(previousSection),
			MapSection(updatedSection));
	}

	private static void ValidateRequest(ApplicationSectionUpdateRequest request) {
		if (string.IsNullOrWhiteSpace(request.ApplicationCode)) {
			throw new ArgumentException("application-code is required.");
		}

		if (string.IsNullOrWhiteSpace(request.SectionCode)) {
			throw new ArgumentException("section-code is required.");
		}

		bool hasCaption = request.Caption is not null;
		bool hasDescription = request.Description is not null;
		bool hasIconId = request.IconId is not null;
		bool hasIconBackground = request.IconBackground is not null;
		if (!hasCaption && !hasDescription && !hasIconId && !hasIconBackground) {
			throw new ArgumentException("At least one mutable field is required: caption, description, icon-id, or icon-background.");
		}

		if (hasCaption && string.IsNullOrWhiteSpace(request.Caption)) {
			throw new ArgumentException("caption cannot be empty.");
		}

		if (hasDescription && string.IsNullOrWhiteSpace(request.Description)) {
			throw new ArgumentException("description cannot be empty.");
		}

		if (hasIconId && (!Guid.TryParse(request.IconId, out _))) {
			throw new ArgumentException("icon-id must be a valid GUID.");
		}

		if (hasIconBackground) {
			if (string.IsNullOrWhiteSpace(request.IconBackground)) {
				throw new ArgumentException("icon-background cannot be empty.");
			}

			if (!HexColorRegex.IsMatch(request.IconBackground.Trim())) {
				throw new ArgumentException("icon-background must use #RRGGBB format.");
			}
		}
	}

	private static ResolvedApplicationSectionUpdateRequest ResolveRequest(ApplicationSectionUpdateRequest request) {
		return new ResolvedApplicationSectionUpdateRequest(
			request.Caption?.Trim(),
			request.Description?.Trim(),
			request.IconId?.Trim(),
			request.IconBackground?.Trim(),
			request.Caption is not null,
			request.Description is not null,
			request.IconId is not null,
			request.IconBackground is not null);
	}

	private ApplicationSectionRecord GetSectionRecord(
		IApplicationClient client,
		EnvironmentSettings environmentSettings,
		string applicationId,
		string sectionCode) {
		string responseBody = client.ExecutePostRequest(
			serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.Select, environmentSettings),
			JsonSerializer.Serialize(BuildSectionSelectQuery(applicationId), JsonOptions));
		ApplicationSectionSelectQueryResponseDto response = JsonSerializer.Deserialize<ApplicationSectionSelectQueryResponseDto>(
				responseBody,
				JsonOptions)
			?? throw new InvalidOperationException("ApplicationSection select query returned an empty response.");
		if (!response.Success) {
			throw new InvalidOperationException(response.ErrorInfo?.Message ?? "ApplicationSection select query failed.");
		}

		return response.Rows
				.FirstOrDefault(row => string.Equals(row.Code, sectionCode, StringComparison.OrdinalIgnoreCase))
			?? throw new InvalidOperationException(
				$"Section '{sectionCode}' was not found in application '{applicationId}'.");
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

	private static object BuildUpdateBody(ApplicationSectionRecord previousSection, ResolvedApplicationSectionUpdateRequest request) {
		Dictionary<string, object> items = new(StringComparer.Ordinal) {
			["Id"] = CreateParameterExpression(SelectQueryHelper.GuidDataValueType, previousSection.Id),
			["ApplicationId"] = CreateParameterExpression(SelectQueryHelper.GuidDataValueType, previousSection.ApplicationId),
			["LogoId"] = CreateParameterExpression(SelectQueryHelper.GuidDataValueType,
				request.ShouldUpdateIconId && request.IconId is not null ? request.IconId : previousSection.LogoId ?? string.Empty),
			["PackageId"] = CreateParameterExpression(SelectQueryHelper.GuidDataValueType, previousSection.PackageId ?? string.Empty),
			["IconBackground"] = CreateParameterExpression(SelectQueryHelper.TextDataValueType,
				request.ShouldUpdateIconBackground && request.IconBackground is not null ? request.IconBackground : previousSection.IconBackground ?? string.Empty)
		};
		if (request.ShouldUpdateCaption && request.Caption is not null) {
			items["Caption"] = CreateParameterExpression(SelectQueryHelper.TextDataValueType, request.Caption);
		}

		if (request.ShouldUpdateDescription && request.Description is not null) {
			items["Description"] = CreateParameterExpression(SelectQueryHelper.TextDataValueType, request.Description);
		}

		return new {
			rootSchemaName = ApplicationSectionSchemaName,
			columnValues = new {
				items
			},
			filters = BuildPrimaryKeyFilter(previousSection.Id)
		};
	}

	private static object CreateParameterExpression(int dataValueType, object value) =>
		new {
			expressionType = 2,
			parameter = new {
				dataValueType,
				value
			}
		};

	private static object BuildPrimaryKeyFilter(string keyValue) =>
		new {
			filterType = 6,
			isEnabled = true,
			trimDateTimeParameterToDate = false,
			logicalOperation = 0,
			items = new {
				primaryFilter = new {
					filterType = 1,
					comparisonType = 3,
					isEnabled = true,
					trimDateTimeParameterToDate = false,
					leftExpression = new {
						expressionType = 0,
						columnPath = "Id"
					},
					rightExpression = new {
						expressionType = 2,
						parameter = new {
							dataValueType = SelectQueryHelper.TextDataValueType,
							value = keyValue
						}
					}
				}
			}
		};

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

	private sealed record ResolvedApplicationSectionUpdateRequest(
		string? Caption,
		string? Description,
		string? IconId,
		string? IconBackground,
		bool ShouldUpdateCaption,
		bool ShouldUpdateDescription,
		bool ShouldUpdateIconId,
		bool ShouldUpdateIconBackground);

	private sealed class ApplicationSectionSelectQueryResponseDto : SelectQueryHelper.SelectQueryResponseBaseDto {
		[JsonPropertyName("rows")]
		public List<ApplicationSectionRecord> Rows { get; set; } = [];
	}

	private sealed class UpdateQueryResponseDto {
		[JsonPropertyName("success")]
		public bool Success { get; set; }

		[JsonPropertyName("errorInfo")]
		public ErrorInfoDto? ErrorInfo { get; set; }
	}

	private sealed class ErrorInfoDto {
		[JsonPropertyName("message")]
		public string? Message { get; set; }
	}
}

/// <summary>
/// Updates metadata of an existing installed application section and prints the structured readback payload.
/// </summary>
public sealed class UpdateAppSectionCommand(
	IApplicationSectionUpdateService applicationSectionUpdateService,
	ILogger logger)
	: Command<UpdateAppSectionOptions> {
	/// <inheritdoc />
	public override int Execute(UpdateAppSectionOptions options) {
		try {
			ArgumentNullException.ThrowIfNull(options);
			if (string.IsNullOrWhiteSpace(options.Environment)) {
				throw new InvalidOperationException("Environment name is required.");
			}

			ApplicationSectionUpdateResult result = applicationSectionUpdateService.UpdateSection(
				options.Environment,
				new ApplicationSectionUpdateRequest(
					options.ApplicationCode,
					options.SectionCode,
					options.Caption,
					options.Description,
					options.IconId,
					options.IconBackground));
			logger.WriteInfo(JsonSerializer.Serialize(result));
			return 0;
		} catch (Exception exception) {
			logger.WriteError(exception.Message);
			return 1;
		}
	}
}

/// <summary>
/// Request payload for existing-app section updates.
/// </summary>
/// <param name="ApplicationCode">Installed application code.</param>
/// <param name="SectionCode">Section code inside the installed application.</param>
/// <param name="Caption">Updated section caption.</param>
/// <param name="Description">Updated section description.</param>
/// <param name="IconId">Updated icon identifier.</param>
/// <param name="IconBackground">Updated icon background color.</param>
public sealed record ApplicationSectionUpdateRequest(
	string ApplicationCode,
	string SectionCode,
	string? Caption = null,
	string? Description = null,
	string? IconId = null,
	string? IconBackground = null);

/// <summary>
/// Structured result for existing-app section updates.
/// </summary>
/// <param name="PackageUId">Primary package identifier.</param>
/// <param name="PackageName">Primary package name.</param>
/// <param name="ApplicationId">Installed application identifier.</param>
/// <param name="ApplicationName">Installed application display name.</param>
/// <param name="ApplicationCode">Installed application code.</param>
/// <param name="ApplicationVersion">Installed application version.</param>
/// <param name="PreviousSection">Section metadata before the update.</param>
/// <param name="Section">Section metadata after the update.</param>
public sealed record ApplicationSectionUpdateResult(
	string PackageUId,
	string PackageName,
	string ApplicationId,
	string ApplicationName,
	string ApplicationCode,
	string? ApplicationVersion,
	ApplicationSectionInfoResult PreviousSection,
	ApplicationSectionInfoResult Section);
