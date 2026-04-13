using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Clio.Common;
using Clio.Package;
using Clio.UserEnvironment;
using CommandLine;

namespace Clio.Command;
	
/// <summary>
/// CLI options for creating a section inside an existing installed application.
/// </summary>
[Verb("create-app-section", HelpText = "Create a section inside an existing installed application")]
public sealed class CreateAppSectionOptions : EnvironmentOptions {
	[Option("application-code", Required = true, HelpText = "Installed application code")]
	public string ApplicationCode { get; set; } = string.Empty;

	[Option("caption", Required = true, HelpText = "Section caption")]
	public string Caption { get; set; } = string.Empty;

	[Option("description", Required = false, HelpText = "Section description")]
	public string? Description { get; set; }

	[Option("entity-schema-name", Required = false, HelpText = "Existing entity schema name")]
	public string? EntitySchemaName { get; set; }

	[Option("with-mobile-pages", Required = false, Default = "true", HelpText = "Create mobile pages in addition to web pages (default: true)")]
	public string? WithMobilePagesValue { get; set; }

	public bool WithMobilePages {
		get => string.Equals(WithMobilePagesValue ?? "true", "true", StringComparison.OrdinalIgnoreCase);
		set => WithMobilePagesValue = value ? "true" : "false";
	}

	internal static void ValidateMobilePagesOption(string? value) {
		if (value is null) return;
		if (!string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) &&
			!string.Equals(value, "false", StringComparison.OrdinalIgnoreCase)) {
			throw new ArgumentException($"Invalid value '{value}' for --with-mobile-pages. Allowed values: true, false.");
		}
	}
}

/// <summary>
/// Creates a section inside an existing installed application and returns structured readback data.
/// </summary>
public interface IApplicationSectionCreateService {
	/// <summary>
	/// Creates a section inside an existing installed application in the specified environment.
	/// </summary>
	/// <param name="environmentName">Registered clio environment name.</param>
	/// <param name="request">Section creation request payload.</param>
	/// <returns>Structured data for the created section, entity, and pages.</returns>
	ApplicationSectionCreateResult CreateSection(string environmentName, ApplicationSectionCreateRequest request);
}

/// <summary>
/// Default ApplicationSection DataService-backed implementation for existing-app section creation.
/// </summary>
public sealed class ApplicationSectionCreateService(
	ISettingsRepository settingsRepository,
	IApplicationClientFactory applicationClientFactory,
	IServiceUrlBuilder serviceUrlBuilder,
	IApplicationInfoService applicationInfoService)
	: IApplicationSectionCreateService {
	private const string ApplicationSectionSchemaName = "ApplicationSection";
	private const string ApplicationIdJsonField = "ApplicationId";
	private const string WebClientTypeId = "195785B4-F55A-4E72-ACE3-6480B54C8FA5";
	private const string SelectQueryRoute = "DataService/json/SyncReply/SelectQuery";
	private const int SectionTypeNormal = 0;
	private const int PollAttempts = 15;
	private static readonly TimeSpan PollDelay = TimeSpan.FromSeconds(2);
	private static readonly Regex CodeWordRegex = new(
		@"[^\p{L}\p{Nd}]+",
		RegexOptions.None,
		TimeSpan.FromSeconds(5));
	private static readonly JsonSerializerOptions JsonOptions = new() {
		DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
		PropertyNameCaseInsensitive = true
	};

	/// <inheritdoc />
	public ApplicationSectionCreateResult CreateSection(string environmentName, ApplicationSectionCreateRequest request) {
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
		ApplicationInfoResult beforeInfo = applicationInfoService.GetApplicationInfo(
			environmentName,
			null,
			request.ApplicationCode);
		ResolvedApplicationSectionCreateRequest resolvedRequest = ResolveRequest(
			request,
			beforeInfo,
			client,
			environmentSettings);
		string requestBody = JsonSerializer.Serialize(BuildInsertBody(resolvedRequest), JsonOptions);
		if (string.IsNullOrWhiteSpace(resolvedRequest.EntitySchemaName)) {
			CheckEntitySchemaDoesNotExist(client, environmentSettings, resolvedRequest.SectionCode, request.Caption);
		}
		string responseBody = client.ExecutePostRequest(
			serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.Insert, environmentSettings),
			requestBody);
		InsertQueryResponseDto response = JsonSerializer.Deserialize<InsertQueryResponseDto>(responseBody, JsonOptions)
			?? throw new InvalidOperationException("InsertQuery returned an empty response.");
		if (!response.Success) {
			throw new InvalidOperationException(response.ErrorInfo?.Message ?? "InsertQuery failed.");
		}

		return LoadCreatedSection(environmentName, beforeInfo, resolvedRequest, client, environmentSettings);
	}

	private static void ValidateRequest(ApplicationSectionCreateRequest request) {
		if (string.IsNullOrWhiteSpace(request.ApplicationCode)) {
			throw new ArgumentException("application-code is required.");
		}

		if (string.IsNullOrWhiteSpace(request.Caption)) {
			throw new ArgumentException("caption is required.");
		}
	}

	private ResolvedApplicationSectionCreateRequest ResolveRequest(
		ApplicationSectionCreateRequest request,
		ApplicationInfoResult applicationInfo,
		IApplicationClient client,
		EnvironmentSettings environmentSettings) {
		string sectionCode = GenerateCodeFromCaption(request.Caption);
		string iconBackground = GenerateRandomHexColor();
		string iconId = ResolveRandomIconId(client, environmentSettings);
		return new ResolvedApplicationSectionCreateRequest(
			Guid.NewGuid().ToString(),
			applicationInfo.ApplicationId ?? throw new InvalidOperationException("Application id was not returned by get-app-info."),
			applicationInfo.ApplicationName ?? string.Empty,
			applicationInfo.ApplicationCode ?? string.Empty,
			applicationInfo.ApplicationVersion,
			applicationInfo.PackageUId,
			applicationInfo.PackageName,
			request.Caption.Trim(),
			sectionCode,
			request.Description?.Trim(),
			request.EntitySchemaName?.Trim(),
			request.WithMobilePages,
			iconId,
			iconBackground,
			request.WithMobilePages ? null : WebClientTypeId);
	}

	private void CheckEntitySchemaDoesNotExist(
		IApplicationClient client,
		EnvironmentSettings environmentSettings,
		string schemaName,
		string caption) {
		try {
			SysSchemaExistsResponseDto response = SelectQueryHelper.ExecuteSelectQuery<SysSchemaExistsResponseDto>(
				client,
				new ServiceUrlBuilder(environmentSettings),
				SelectQueryHelper.BuildSelectQuery(
					"SysSchema",
					[new SelectQueryHelper.SelectQueryColumnDefinition("Name", "Name")],
					[new SelectQueryHelper.SelectQueryFilterDefinition("Name", schemaName, SelectQueryHelper.TextDataValueType)]));
			if (response.Rows.Count > 0) {
				throw new InvalidOperationException(
					$"Entity schema '{schemaName}' already exists. "
					+ $"To create section '{caption}' reusing the existing entity, add: --entity-schema-name {schemaName}");
			}
		} catch (InvalidOperationException) {
			throw;
		} catch {
		}
	}

	private ApplicationSectionCreateResult LoadCreatedSection(
		string environmentName,
		ApplicationInfoResult beforeInfo,
		ResolvedApplicationSectionCreateRequest request,
		IApplicationClient client,
		EnvironmentSettings environmentSettings) {
		Exception? lastError = null;
		for (int attempt = 1; attempt <= PollAttempts; attempt++) {
			try {
				ApplicationInfoResult afterInfo = applicationInfoService.GetApplicationInfo(
					environmentName,
					null,
					request.ApplicationCode);
				ApplicationSectionRecord createdSection = GetSectionRecord(
					client,
					environmentSettings,
					request.ApplicationId,
					request.SectionCode);
				string? entitySchemaName = string.IsNullOrWhiteSpace(createdSection.EntitySchemaName)
					? request.EntitySchemaName
					: createdSection.EntitySchemaName;
				ApplicationEntityInfoResult? entity = ResolveEntity(afterInfo, beforeInfo, entitySchemaName);
				IReadOnlyList<PageListItem> createdPages = ResolveCreatedPages(beforeInfo, afterInfo);
				return new ApplicationSectionCreateResult(
					afterInfo.PackageUId,
					afterInfo.PackageName,
					afterInfo.ApplicationId ?? request.ApplicationId,
					afterInfo.ApplicationName ?? request.ApplicationName,
					afterInfo.ApplicationCode ?? request.ApplicationCode,
					afterInfo.ApplicationVersion ?? request.ApplicationVersion,
					new ApplicationSectionInfoResult(
						createdSection.Id,
						createdSection.Code,
						ResolveLocalizedCaption(createdSection.Caption, request.Caption),
						createdSection.Description,
						entitySchemaName,
						createdSection.PackageId,
						createdSection.SectionSchemaUId,
						createdSection.LogoId,
						createdSection.IconBackground,
						createdSection.ClientTypeId),
					entity,
					createdPages);
			} catch (Exception exception) {
				lastError = exception;
				if (attempt < PollAttempts) {
					System.Threading.Thread.Sleep(PollDelay);
				}
			}
		}

		throw new InvalidOperationException(
			$"Section '{request.SectionCode}' was created but its metadata could not be loaded after {PollAttempts} attempts. Last error: {lastError!.Message}",
			lastError);
	}

	private static ApplicationSectionRecord GetSectionRecord(
		IApplicationClient client,
		EnvironmentSettings environmentSettings,
		string applicationId,
		string sectionCode) {
		ApplicationSectionSelectQueryResponseDto response = SelectQueryHelper.ExecuteSelectQuery<ApplicationSectionSelectQueryResponseDto>(
			client,
			new ServiceUrlBuilder(environmentSettings),
			SelectQueryHelper.BuildSelectQuery(
				ApplicationSectionSchemaName,
				[
					new SelectQueryHelper.SelectQueryColumnDefinition("Id", "Id"),
					new SelectQueryHelper.SelectQueryColumnDefinition(ApplicationIdJsonField, ApplicationIdJsonField),
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
						ApplicationIdJsonField,
						applicationId,
						SelectQueryHelper.GuidDataValueType)
				]));
		return response.Rows
				.FirstOrDefault(row => string.Equals(row.Code, sectionCode, StringComparison.OrdinalIgnoreCase))
			?? throw new InvalidOperationException(
				$"Section '{sectionCode}' was not found in application '{applicationId}'.");
	}

	private static ApplicationEntityInfoResult? ResolveEntity(
		ApplicationInfoResult afterInfo,
		ApplicationInfoResult beforeInfo,
		string? entitySchemaName) {
		if (!string.IsNullOrWhiteSpace(entitySchemaName)) {
			return afterInfo.Entities.FirstOrDefault(entity =>
				string.Equals(entity.Name, entitySchemaName, StringComparison.OrdinalIgnoreCase));
		}

		HashSet<string> previousEntities = beforeInfo.Entities
			.Select(entity => entity.Name)
			.Where(name => !string.IsNullOrWhiteSpace(name))
			.ToHashSet(StringComparer.OrdinalIgnoreCase);
		return afterInfo.Entities.FirstOrDefault(entity => !previousEntities.Contains(entity.Name));
	}

	private static IReadOnlyList<PageListItem> ResolveCreatedPages(
		ApplicationInfoResult beforeInfo,
		ApplicationInfoResult afterInfo) {
		HashSet<string> previousPageKeys = (beforeInfo.Pages ?? [])
			.Select(CreatePageIdentity)
			.ToHashSet(StringComparer.OrdinalIgnoreCase);
		return (afterInfo.Pages ?? [])
			.Where(page => !previousPageKeys.Contains(CreatePageIdentity(page)))
			.ToList();
	}

	private static string CreatePageIdentity(PageListItem page) =>
		$"{page.SchemaName}|{page.UId}|{page.PackageName}";

	private static string ResolveLocalizedCaption(string? value, string fallbackCaption) {
		if (string.IsNullOrWhiteSpace(value)) {
			return fallbackCaption;
		}

		try {
			Dictionary<string, string>? localizedValues = JsonSerializer.Deserialize<Dictionary<string, string>>(
				value,
				new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
			if (localizedValues is null || localizedValues.Count == 0) {
				return value;
			}

			if (localizedValues.TryGetValue("en-US", out string? enUsCaption) &&
				!string.IsNullOrWhiteSpace(enUsCaption)) {
				return enUsCaption;
			}

			string? firstValue = localizedValues.Values.FirstOrDefault(item => !string.IsNullOrWhiteSpace(item));
			return string.IsNullOrWhiteSpace(firstValue) ? fallbackCaption : firstValue;
		} catch (JsonException) {
			return value;
		}
	}

	private object BuildInsertBody(ResolvedApplicationSectionCreateRequest request) {
		Dictionary<string, object> items = new(StringComparer.Ordinal) {
			["Id"] = CreateParameterExpression(SelectQueryHelper.GuidDataValueType, request.Id),
			["Caption"] = CreateParameterExpression(
				SelectQueryHelper.TextDataValueType,
				request.Caption),
			[ApplicationIdJsonField] = CreateParameterExpression(SelectQueryHelper.GuidDataValueType, request.ApplicationId),
			["PackageId"] = CreateParameterExpression(SelectQueryHelper.GuidDataValueType, request.PackageUId),
			["LogoId"] = CreateParameterExpression(SelectQueryHelper.GuidDataValueType, request.IconId),
			["IconBackground"] = CreateParameterExpression(SelectQueryHelper.TextDataValueType, request.IconBackground),
			["Type"] = CreateParameterExpression(SelectQueryHelper.IntDataValueType, SectionTypeNormal),
			["Code"] = CreateParameterExpression(SelectQueryHelper.TextDataValueType, request.SectionCode)
		};
		if (!string.IsNullOrWhiteSpace(request.Description)) {
			items["Description"] = CreateParameterExpression(SelectQueryHelper.TextDataValueType, request.Description);
		}

		if (!string.IsNullOrWhiteSpace(request.EntitySchemaName)) {
			items["EntitySchemaName"] = CreateParameterExpression(
				SelectQueryHelper.TextDataValueType,
				request.EntitySchemaName);
		}

		if (!string.IsNullOrWhiteSpace(request.ClientTypeId)) {
			items["ClientTypeId"] = CreateParameterExpression(SelectQueryHelper.GuidDataValueType, request.ClientTypeId);
		}

		return new {
			rootSchemaName = ApplicationSectionSchemaName,
			columnValues = new {
				items
			}
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

	private string ResolveRandomIconId(IApplicationClient client, EnvironmentSettings environmentSettings) {
		string responseBody = client.ExecutePostRequest(
			serviceUrlBuilder.Build(SelectQueryRoute, environmentSettings),
			JsonSerializer.Serialize(BuildRandomIconQuery()));
		IconSelectQueryResponseDto response = JsonSerializer.Deserialize<IconSelectQueryResponseDto>(responseBody, JsonOptions)
			?? throw new InvalidOperationException("SysAppIcons query returned an empty response.");
		if (!response.Success) {
			throw new InvalidOperationException(response.ErrorInfo?.Message ?? "Failed to query SysAppIcons.");
		}

		if (response.Rows.Count == 0) {
			throw new InvalidOperationException("No icons found in SysAppIcons.");
		}

		int index = Random.Shared.Next(response.Rows.Count);
		return response.Rows[index].Id;
	}

	private static object BuildRandomIconQuery() =>
		new {
			rootSchemaName = "SysAppIcons",
			operationType = 0,
			allColumns = false,
			isDistinct = false,
			ignoreDisplayValues = false,
			rowCount = -1,
			rowsOffset = -1,
			isPageable = false,
			conditionalValues = (object?)null,
			isHierarchical = false,
			hierarchicalMaxDepth = 0,
			hierarchicalColumnFiltersValue = new {
				filterType = 6,
				isEnabled = true,
				items = new Dictionary<string, object>(),
				logicalOperation = 0,
				trimDateTimeParameterToDate = false
			},
			hierarchicalColumnName = (string?)null,
			hierarchicalColumnValue = (object?)null,
			hierarchicalFullDataLoad = false,
			useLocalization = false,
			useRecordDeactivation = false,
			columns = new {
				items = new Dictionary<string, object> {
					["Id"] = new {
						expression = new {
							expressionType = 0,
							columnPath = "Id"
						},
						orderDirection = 0,
						orderPosition = -1,
						isVisible = true
					}
				}
			},
			filters = new {
				filterType = 6,
				isEnabled = true,
				logicalOperation = 0,
				trimDateTimeParameterToDate = false,
				items = new Dictionary<string, object>()
			},
			__type = "Terrasoft.Nui.ServiceModel.DataContract.SelectQuery",
			queryKind = 0,
			serverESQCacheParameters = new {
				cacheLevel = 0,
				cacheGroup = string.Empty,
				cacheItemName = string.Empty
			},
			queryOptimize = false,
			useMetrics = false,
			querySource = 0
		};

	private static string GenerateCodeFromCaption(string caption) {
		string[] words = CodeWordRegex.Split(caption.Trim())
			.Where(item => !string.IsNullOrWhiteSpace(item))
			.ToArray();
		if (words.Length == 0) {
			throw new ArgumentException(
				$"Section caption '{caption}' contains no valid characters for code generation.",
				nameof(caption));
		}

		StringBuilder builder = new("Usr");
		foreach (string word in words) {
			string normalizedWord = NormalizeWord(word);
			if (!string.IsNullOrWhiteSpace(normalizedWord)) {
				builder.Append(normalizedWord);
			}
		}

		if (builder.Length == 3) {
			throw new ArgumentException(
				$"Section caption '{caption}' contains no valid characters for code generation.",
				nameof(caption));
		}

		if (char.IsDigit(builder[3])) {
			builder.Insert(3, "_");
		}

		return builder.ToString();
	}

	private static string NormalizeWord(string value) {
		string sanitizedValue = new(value.Where(char.IsLetterOrDigit).ToArray());
		if (string.IsNullOrWhiteSpace(sanitizedValue)) {
			return string.Empty;
		}

		return sanitizedValue.Length == 1
			? sanitizedValue.ToUpperInvariant()
			: char.ToUpperInvariant(sanitizedValue[0]) + sanitizedValue[1..];
	}

	private static string GenerateRandomHexColor() {
		int red = Random.Shared.Next(50, 200);
		int green = Random.Shared.Next(50, 200);
		int blue = Random.Shared.Next(50, 200);
		return string.Create(7, (red, green, blue), static (span, value) => {
			span[0] = '#';
			value.red.TryFormat(span.Slice(1, 2), out _, "X2");
			value.green.TryFormat(span.Slice(3, 2), out _, "X2");
			value.blue.TryFormat(span.Slice(5, 2), out _, "X2");
		});
	}

	private sealed class InsertQueryResponseDto {
		[JsonPropertyName("success")]
		public bool Success { get; set; }

		[JsonPropertyName("errorInfo")]
		public ErrorInfoDto? ErrorInfo { get; set; }
	}

	private sealed class ErrorInfoDto {
		[JsonPropertyName("message")]
		public string? Message { get; set; }
	}

	private sealed class SysSchemaExistsResponseDto : SelectQueryHelper.SelectQueryResponseBaseDto {
		[JsonPropertyName("rows")]
		public List<SysSchemaNameRowDto> Rows { get; set; } = [];
	}

	private sealed class SysSchemaNameRowDto {
		[JsonPropertyName("Name")]
		public string? Name { get; set; }
	}

	private sealed class IconSelectQueryResponseDto {
		[JsonPropertyName("success")]
		public bool Success { get; set; }

		[JsonPropertyName("errorInfo")]
		public ErrorInfoDto? ErrorInfo { get; set; }

		[JsonPropertyName("rows")]
		public List<IconRowDto> Rows { get; set; } = [];
	}

	private sealed class IconRowDto {
		[JsonPropertyName("Id")]
		public string Id { get; set; } = string.Empty;
	}

	private sealed class ApplicationSectionSelectQueryResponseDto : SelectQueryHelper.SelectQueryResponseBaseDto {
		[JsonPropertyName("rows")]
		public List<ApplicationSectionRecord> Rows { get; set; } = [];
	}

	private sealed record ResolvedApplicationSectionCreateRequest(
		string Id,
		string ApplicationId,
		string ApplicationName,
		string ApplicationCode,
		string? ApplicationVersion,
		string PackageUId,
		string PackageName,
		string Caption,
		string SectionCode,
		string? Description,
		string? EntitySchemaName,
		bool WithMobilePages,
		string IconId,
		string IconBackground,
		string? ClientTypeId);
}

/// <summary>
/// Creates a section inside an existing installed application and prints the structured readback payload.
/// </summary>
public sealed class CreateAppSectionCommand(
	IApplicationSectionCreateService applicationSectionCreateService,
	ILogger logger)
	: Command<CreateAppSectionOptions> {
	/// <inheritdoc />
	public override int Execute(CreateAppSectionOptions options) {
		try {
			ArgumentNullException.ThrowIfNull(options);
			if (string.IsNullOrWhiteSpace(options.Environment)) {
				throw new InvalidOperationException("Environment name is required.");
			}
			CreateAppSectionOptions.ValidateMobilePagesOption(options.WithMobilePagesValue);

			ApplicationSectionCreateResult result = applicationSectionCreateService.CreateSection(
				options.Environment,
				new ApplicationSectionCreateRequest(
					options.ApplicationCode,
					options.Caption,
					options.Description,
					options.EntitySchemaName,
					options.WithMobilePages));
			logger.WriteInfo(JsonSerializer.Serialize(result));
			return 0;
		} catch (Exception exception) {
			logger.WriteError(exception.Message);
			return 1;
		}
	}
}

/// <summary>
/// Request payload for existing-app section creation.
/// </summary>
/// <param name="ApplicationCode">Installed application code.</param>
/// <param name="Caption">Section caption.</param>
/// <param name="Description">Optional section description.</param>
/// <param name="EntitySchemaName">Optional existing entity schema name. When provided, the section reuses that entity.</param>
/// <param name="WithMobilePages">Whether to create mobile pages.</param>
public sealed record ApplicationSectionCreateRequest(
	string ApplicationCode,
	string Caption,
	string? Description = null,
	string? EntitySchemaName = null,
	bool WithMobilePages = true);

/// <summary>
/// Structured result for existing-app section creation.
/// </summary>
/// <param name="PackageUId">Primary package identifier.</param>
/// <param name="PackageName">Primary package name.</param>
/// <param name="ApplicationId">Installed application identifier.</param>
/// <param name="ApplicationName">Installed application display name.</param>
/// <param name="ApplicationCode">Installed application code.</param>
/// <param name="ApplicationVersion">Installed application version.</param>
/// <param name="Section">Created section metadata.</param>
/// <param name="Entity">Created or targeted entity metadata.</param>
/// <param name="Pages">Pages created by the section flow when available.</param>
public sealed record ApplicationSectionCreateResult(
	string PackageUId,
	string PackageName,
	string ApplicationId,
	string ApplicationName,
	string ApplicationCode,
	string? ApplicationVersion,
	ApplicationSectionInfoResult Section,
	ApplicationEntityInfoResult? Entity,
	IReadOnlyList<PageListItem> Pages);

/// <summary>
/// Structured section metadata returned by existing-app section creation.
/// </summary>
/// <param name="Id">Section identifier.</param>
/// <param name="Code">Section code.</param>
/// <param name="Caption">Section caption.</param>
/// <param name="Description">Optional section description.</param>
/// <param name="EntitySchemaName">Target entity schema name.</param>
/// <param name="PackageId">Package identifier used for the section.</param>
/// <param name="SectionSchemaUId">Generated section schema identifier when available.</param>
/// <param name="IconId">Resolved icon identifier.</param>
/// <param name="IconBackground">Resolved icon background color.</param>
/// <param name="ClientTypeId">Optional client type selector used for page generation.</param>
public sealed record ApplicationSectionInfoResult(
	string Id,
	string Code,
	string Caption,
	string? Description,
	string? EntitySchemaName,
	string? PackageId,
	string? SectionSchemaUId,
	string? IconId,
	string? IconBackground,
	string? ClientTypeId);

/// <summary>
/// Section readback row from the ApplicationSection virtual object.
/// </summary>
/// <param name="Id">Section identifier.</param>
/// <param name="ApplicationId">Installed application identifier.</param>
/// <param name="Caption">Localized caption payload.</param>
/// <param name="Code">Section code.</param>
/// <param name="Description">Optional section description.</param>
/// <param name="EntitySchemaName">Target entity schema name.</param>
/// <param name="PackageId">Package identifier.</param>
/// <param name="SectionSchemaUId">Section list page schema UId.</param>
/// <param name="LogoId">Icon identifier.</param>
/// <param name="IconBackground">Icon background color.</param>
/// <param name="ClientTypeId">Optional client type selector.</param>
/// <param name="CardSchemaUId">Section form page schema UId.</param>
/// <param name="SysModuleEntityId">Identifier of the associated SysModuleEntity record.</param>
public sealed record ApplicationSectionRecord(
	[property: JsonPropertyName("Id")] string Id,
	[property: JsonPropertyName("ApplicationId")] string ApplicationId,
	[property: JsonPropertyName("Caption")] string? Caption,
	[property: JsonPropertyName("Code")] string Code,
	[property: JsonPropertyName("Description")] string? Description,
	[property: JsonPropertyName("EntitySchemaName")] string? EntitySchemaName,
	[property: JsonPropertyName("PackageId")] string? PackageId,
	[property: JsonPropertyName("SectionSchemaUId")] string? SectionSchemaUId,
	[property: JsonPropertyName("LogoId")] string? LogoId,
	[property: JsonPropertyName("IconBackground")] string? IconBackground,
	[property: JsonPropertyName("ClientTypeId")] string? ClientTypeId,
	[property: JsonPropertyName("CardSchemaUId")] string? CardSchemaUId,
	[property: JsonPropertyName("SysModuleEntityId")] string? SysModuleEntityId);
