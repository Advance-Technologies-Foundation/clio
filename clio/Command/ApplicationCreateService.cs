using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using Clio.Common;
using Clio.UserEnvironment;

namespace Clio.Command;

/// <summary>
/// Creates Creatio applications through the AppInstaller CreateApp endpoint and returns their structured metadata.
/// </summary>
public interface IApplicationCreateService
{
	/// <summary>
	/// Creates a new Creatio application in the specified environment and loads its structured metadata.
	/// </summary>
	/// <param name="environmentName">Registered clio environment name.</param>
	/// <param name="request">Application creation request payload.</param>
	/// <returns>The created application's structured metadata.</returns>
	ApplicationInfoResult CreateApplication(string environmentName, ApplicationCreateRequest request);
}

/// <summary>
/// Default CreateApp-backed implementation for the application MCP tool family.
/// </summary>
public sealed class ApplicationCreateService(
	ISettingsRepository settingsRepository,
	IApplicationClientFactory applicationClientFactory,
	IServiceUrlBuilder serviceUrlBuilder,
	IApplicationInfoService applicationInfoService)
	: IApplicationCreateService
{
	private const string CreateApplicationRoute = "ServiceModel/AppInstallerService.svc/CreateApp";
	private const string SelectQueryRoute = "DataService/json/SyncReply/SelectQuery";
	private const int PollAttempts = 15;
	private static readonly TimeSpan PollDelay = TimeSpan.FromSeconds(2);
	private static readonly Regex TimeoutRegex = new(
		@"(?is)(App Installer CreateApp request failed.*timeout of \d+ms exceeded|timeout of \d+ms exceeded)",
		RegexOptions.None, TimeSpan.FromSeconds(5));
	private static readonly Regex PascalCaseWordRegex = new(
		@"([A-Z]+(?=$|[A-Z][a-z0-9])|[A-Z]?[a-z0-9]+)",
		RegexOptions.Compiled, TimeSpan.FromSeconds(5));
	private static readonly JsonSerializerOptions JsonOptions = new()
	{
		DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
		PropertyNameCaseInsensitive = true
	};

	/// <inheritdoc />
	public ApplicationInfoResult CreateApplication(string environmentName, ApplicationCreateRequest request)
	{
		if (string.IsNullOrWhiteSpace(environmentName))
		{
			throw new ArgumentException("Environment name is required.", nameof(environmentName));
		}

		ArgumentNullException.ThrowIfNull(request);
		ValidateRequest(request);

		EnvironmentSettings environmentSettings = settingsRepository.FindEnvironment(environmentName)
			?? throw new InvalidOperationException(
				$"Environment with key '{environmentName}' not found. Check your clio configuration.");
		if (!IsConfiguredEnvironment(environmentSettings))
		{
			throw new InvalidOperationException(
				$"Environment with key '{environmentName}' not found. Check your clio configuration.");
		}

		IApplicationClient client = applicationClientFactory.CreateEnvironmentClient(environmentSettings);
		ResolvedApplicationCreateRequest resolvedRequest = ResolveRequest(request, client, environmentSettings, serviceUrlBuilder);
		string requestUrl = serviceUrlBuilder.Build(CreateApplicationRoute, environmentSettings);
		string requestBody = JsonSerializer.Serialize(CreateRequestDto.From(resolvedRequest), JsonOptions);

		try
		{
			string responseBody = client.ExecutePostRequest(requestUrl, requestBody);
			CreateApplicationResponseDto response = DeserializeResponse(responseBody);
			if (!response.Success)
			{
				throw new InvalidOperationException(BuildFailureMessage(response));
			}

			if (!Guid.TryParse(response.Value, out _))
			{
				throw new InvalidOperationException("CreateApp returned an invalid application identifier.");
			}

			return LoadCreatedApplication(environmentName, resolvedRequest.Code, response.Value);
		}
		catch (Exception exception) when (IsTimeout(exception))
		{
			return PollApplicationInfo(environmentName, resolvedRequest.Code, exception);
		}
	}

	private static void ValidateRequest(ApplicationCreateRequest request)
	{
		EnsureRequired(request.TemplateCode, nameof(request.TemplateCode), "Template code is required.");
		if (string.IsNullOrWhiteSpace(request.Name) && string.IsNullOrWhiteSpace(request.Code))
		{
			throw new ArgumentException("Either name or code is required.");
		}

		if (!string.IsNullOrWhiteSpace(request.ClientTypeId) && !Guid.TryParse(request.ClientTypeId, out _))
		{
			throw new ArgumentException("Client type id must be a valid GUID.", nameof(request));
		}

		if (!string.IsNullOrWhiteSpace(request.IconId) &&
			!string.Equals(request.IconId, "auto", StringComparison.OrdinalIgnoreCase) &&
			!Guid.TryParse(request.IconId, out _))
		{
			throw new ArgumentException("Icon id must be a valid GUID or 'auto'.", nameof(request));
		}
	}

	private static void EnsureRequired(string value, string paramName, string message)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			throw new ArgumentException(message, paramName);
		}
	}

	private static bool IsConfiguredEnvironment(EnvironmentSettings environmentSettings) =>
		!string.IsNullOrWhiteSpace(environmentSettings.Uri);

	private static CreateApplicationResponseDto DeserializeResponse(string responseBody)
	{
		if (string.IsNullOrWhiteSpace(responseBody))
		{
			throw new InvalidOperationException("CreateApp returned an empty response.");
		}

		return JsonSerializer.Deserialize<CreateApplicationResponseDto>(responseBody, JsonOptions)
			?? throw new InvalidOperationException("CreateApp returned an empty response.");
	}

	private static ResolvedApplicationCreateRequest ResolveRequest(
		ApplicationCreateRequest request,
		IApplicationClient client,
		EnvironmentSettings environmentSettings,
		IServiceUrlBuilder serviceUrlBuilder)
	{
		string? resolvedCode = string.IsNullOrWhiteSpace(request.Code)
			? null
			: SanitizeCode(request.Code);
		string resolvedName = string.IsNullOrWhiteSpace(request.Name)
			? DeriveNameFromCode(resolvedCode!)
			: request.Name.Trim();

		resolvedCode ??= GenerateCodeFromName(resolvedName);
		string resolvedIconBackground = string.IsNullOrWhiteSpace(request.IconBackground)
			? GenerateRandomHexColor()
			: request.IconBackground.Trim();
		string resolvedIconId = string.IsNullOrWhiteSpace(request.IconId) ||
			string.Equals(request.IconId, "auto", StringComparison.OrdinalIgnoreCase)
				? ResolveRandomIconId(client, environmentSettings, serviceUrlBuilder)
				: Guid.Parse(request.IconId).ToString();
		string? resolvedClientTypeId = string.IsNullOrWhiteSpace(request.ClientTypeId)
			? null
			: Guid.Parse(request.ClientTypeId).ToString();

		return new ResolvedApplicationCreateRequest(
			resolvedName,
			resolvedCode,
			request.Description?.Trim(),
			request.TemplateCode.Trim(),
			resolvedIconId,
			resolvedIconBackground,
			resolvedClientTypeId,
			request.OptionalTemplateData);
	}

	private static string GenerateCodeFromName(string name)
	{
		string[] segments = Regex.Split(name.Trim(), @"[^\p{L}\p{Nd}]+", RegexOptions.None, TimeSpan.FromSeconds(5))
			.Where(segment => !string.IsNullOrWhiteSpace(segment))
			.ToArray();
		StringBuilder builder = new("Usr");
		foreach (string rawSegment in segments)
		{
			string segment = new(rawSegment.Where(char.IsLetterOrDigit).ToArray());
			if (string.IsNullOrWhiteSpace(segment))
			{
				continue;
			}

			builder.Append(char.ToUpperInvariant(segment[0]));
			if (segment.Length > 1)
			{
				builder.Append(segment[1..]);
			}
		}

		string generatedCode = builder.ToString();
		if (generatedCode.Length == 3)
		{
			throw new ArgumentException(
				$"Application name '{name}' contains no valid characters for code generation.",
				nameof(name));
		}

		if (generatedCode.Length > 3 && char.IsDigit(generatedCode[3]))
		{
			generatedCode = generatedCode.Insert(3, "_");
		}

		return generatedCode;
	}

	private static string DeriveNameFromCode(string code)
	{
		string trimmedCode = code.Trim();
		if (trimmedCode.StartsWith("Usr", StringComparison.OrdinalIgnoreCase))
		{
			trimmedCode = trimmedCode[3..];
		}

		if (trimmedCode.StartsWith("_", StringComparison.Ordinal))
		{
			trimmedCode = trimmedCode[1..];
		}

		string[] words = PascalCaseWordRegex.Matches(trimmedCode)
			.Select(match => match.Value)
			.Where(value => !string.IsNullOrWhiteSpace(value))
			.ToArray();
		if (words.Length == 0)
		{
			throw new ArgumentException(
				$"Application code '{code}' cannot be converted into a readable application name.",
				nameof(code));
		}

		return string.Join(" ", words);
	}

	private static string SanitizeCode(string code)
	{
		string trimmedCode = code.Trim();
		if (string.IsNullOrWhiteSpace(trimmedCode))
		{
			throw new ArgumentException("Application code is required.", nameof(code));
		}

		string[] words = Regex.Split(trimmedCode, @"[^\p{L}\p{Nd}]+", RegexOptions.None, TimeSpan.FromSeconds(5))
			.Where(segment => !string.IsNullOrWhiteSpace(segment))
			.ToArray();
		if (words.Length == 0)
		{
			throw new ArgumentException(
				$"Application code '{code}' contains no valid characters.",
				nameof(code));
		}

		string firstWord = NormalizeWord(words[0]);
		if (string.IsNullOrWhiteSpace(firstWord))
		{
			throw new ArgumentException(
				$"Application code '{code}' contains no valid characters.",
				nameof(code));
		}

		StringBuilder builder = new();
		bool hasUsrPrefix = firstWord.StartsWith("Usr", StringComparison.OrdinalIgnoreCase);
		if (hasUsrPrefix)
		{
			builder.Append("Usr");
			builder.Append(firstWord.Length > 3 ? NormalizeWord(firstWord[3..]) : string.Empty);
		}
		else
		{
			builder.Append("Usr");
			builder.Append(firstWord);
		}

		foreach (string word in words.Skip(1))
		{
			string normalizedWord = NormalizeWord(word);
			if (!string.IsNullOrWhiteSpace(normalizedWord))
			{
				builder.Append(normalizedWord);
			}
		}

		string sanitizedCode = builder.ToString();
		if (sanitizedCode.Length == 3)
		{
			throw new ArgumentException(
				$"Application code '{code}' contains no valid characters.",
				nameof(code));
		}

		if (char.IsDigit(sanitizedCode[3]))
		{
			sanitizedCode = sanitizedCode.Insert(3, "_");
		}

		return sanitizedCode;
	}

	private static string NormalizeWord(string word)
	{
		string sanitizedWord = new(word.Where(char.IsLetterOrDigit).ToArray());
		if (string.IsNullOrWhiteSpace(sanitizedWord))
		{
			return string.Empty;
		}

		if (sanitizedWord.Length == 1)
		{
			return sanitizedWord.ToUpperInvariant();
		}

		return char.ToUpperInvariant(sanitizedWord[0]) + sanitizedWord[1..];
	}

	private static string GenerateRandomHexColor()
	{
		int red = Random.Shared.Next(50, 200);
		int green = Random.Shared.Next(50, 200);
		int blue = Random.Shared.Next(50, 200);
		return string.Create(7, (red, green, blue), static (span, value) =>
		{
			span[0] = '#';
			value.red.TryFormat(span.Slice(1, 2), out _, "X2");
			value.green.TryFormat(span.Slice(3, 2), out _, "X2");
			value.blue.TryFormat(span.Slice(5, 2), out _, "X2");
		});
	}

	private static string ResolveRandomIconId(
		IApplicationClient client,
		EnvironmentSettings environmentSettings,
		IServiceUrlBuilder serviceUrlBuilder)
	{
		string responseBody = client.ExecutePostRequest(
			serviceUrlBuilder.Build(SelectQueryRoute, environmentSettings),
			JsonSerializer.Serialize(BuildRandomIconQuery()));
		IconSelectQueryResponseDto response = JsonSerializer.Deserialize<IconSelectQueryResponseDto>(responseBody, JsonOptions)
			?? throw new InvalidOperationException("SysAppIcons query returned an empty response.");
		if (!response.Success)
		{
			throw new InvalidOperationException(response.ErrorInfo?.Message ?? "Failed to query SysAppIcons.");
		}

		if (response.Rows.Count == 0)
		{
			throw new InvalidOperationException("No icons found in SysAppIcons.");
		}

		int index = Random.Shared.Next(response.Rows.Count);
		return response.Rows[index].Id;
	}

	private ApplicationInfoResult LoadCreatedApplication(string environmentName, string appCode, string appId)
	{
		return LoadApplicationInfoWithRetry(
			environmentName,
			appId,
			appCode,
			$"Application '{appCode}' was created but its metadata could not be loaded");
	}

	private ApplicationInfoResult PollApplicationInfo(
		string environmentName,
		string appCode,
		Exception timeoutException)
	{
		return LoadApplicationInfoWithRetry(
			environmentName,
			null,
			appCode,
			$"CreateApp request timed out and application '{appCode}' could not be loaded",
			timeoutException);
	}

	private ApplicationInfoResult LoadApplicationInfoWithRetry(
		string environmentName,
		string? appId,
		string appCode,
		string failurePrefix,
		Exception? fallbackException = null)
	{
		InvalidOperationException lastLoadException = null!;
		for (int attempt = 1; attempt <= PollAttempts; attempt++)
		{
			try
			{
				return applicationInfoService.GetApplicationInfo(environmentName, appId, appCode);
			}
			catch (InvalidOperationException exception)
			{
				lastLoadException = exception;
				if (attempt < PollAttempts)
				{
					Thread.Sleep(PollDelay);
				}
			}
		}

		string failureMessage = $"{failurePrefix} after {PollAttempts} attempts.";
		if (!string.IsNullOrWhiteSpace(lastLoadException.Message))
		{
			failureMessage = $"{failureMessage} Last error: {lastLoadException.Message}";
		}

		throw new InvalidOperationException(
			failureMessage,
			lastLoadException);
	}

	private static bool IsTimeout(Exception exception)
	{
		return TimeoutRegex.IsMatch(exception.Message);
	}

	private static string BuildFailureMessage(CreateApplicationResponseDto response)
	{
		string baseMessage = !string.IsNullOrWhiteSpace(response.ErrorInfo?.Message)
			? response.ErrorInfo.Message
			: "Failed to create application.";
		IReadOnlyList<DependencyErrorDto> dependencyErrors = response.DependenciesErrors ?? [];
		if (dependencyErrors.Count == 0)
		{
			return baseMessage;
		}

		string dependencyMessage = string.Join(
			"; ",
			dependencyErrors.Select(FormatDependencyError));
		return $"{baseMessage} Dependencies: {dependencyMessage}";
	}

	private static string FormatDependencyError(DependencyErrorDto error)
	{
		List<string> parts = [];
		if (!string.IsNullOrWhiteSpace(error.Source))
		{
			parts.Add($"source={error.Source}");
		}

		if (!string.IsNullOrWhiteSpace(error.Reference))
		{
			parts.Add($"reference={error.Reference}");
		}

		if (!string.IsNullOrWhiteSpace(error.Package))
		{
			parts.Add($"package={error.Package}");
		}

		return parts.Count == 0 ? "unknown dependency error" : string.Join(", ", parts);
	}

	private static object BuildRandomIconQuery()
	{
		return new
		{
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
			hierarchicalColumnFiltersValue = new
			{
				filterType = 6,
				isEnabled = true,
				items = new Dictionary<string, object>(),
				logicalOperation = 0,
				trimDateTimeParameterToDate = false
			},
			hierarchicalColumnName = (string?)null,
			hierarchicalColumnValue = (object?)null,
			hierarchicalFullDataLoad = false,
			useLocalization = true,
			useRecordDeactivation = false,
			columns = new
			{
				items = new Dictionary<string, object>
				{
					["Id"] = new
					{
						expression = new
						{
							expressionType = 0,
							columnPath = "Id"
						},
						orderDirection = 0,
						orderPosition = -1,
						isVisible = true
					}
				}
			},
			filters = new
			{
				filterType = 6,
				isEnabled = true,
				trimDateTimeParameterToDate = false,
				logicalOperation = 0,
				items = new Dictionary<string, object>()
			},
			__type = "Terrasoft.Nui.ServiceModel.DataContract.SelectQuery",
			queryKind = 0,
			serverESQCacheParameters = new
			{
				cacheLevel = 0,
				cacheGroup = string.Empty,
				cacheItemName = string.Empty
			},
			queryOptimize = false,
			useMetrics = false,
			querySource = 0
		};
	}

	private sealed class CreateRequestDto
	{
		[JsonPropertyName("name")]
		public string Name { get; init; } = string.Empty;

		[JsonPropertyName("iconBackground")]
		public string IconBackground { get; init; } = string.Empty;

		[JsonPropertyName("description")]
		public string Description { get; init; } = string.Empty;

		[JsonPropertyName("templateCode")]
		public string TemplateCode { get; init; } = string.Empty;

		[JsonPropertyName("iconId")]
		public string IconId { get; init; } = string.Empty;

		[JsonPropertyName("code")]
		public string Code { get; init; } = string.Empty;

		[JsonPropertyName("optionalTemplateData")]
		public OptionalTemplateDataDto OptionalTemplateData { get; init; } = new();

		[JsonPropertyName("clientTypeId")]
		public string? ClientTypeId { get; init; }

		public static CreateRequestDto From(ResolvedApplicationCreateRequest request)
		{
			return new CreateRequestDto
			{
				Name = request.Name,
				IconBackground = request.IconBackground,
				Description = request.Description,
				TemplateCode = request.TemplateCode,
				IconId = request.IconId,
				Code = request.Code,
				ClientTypeId = request.ClientTypeId,
				OptionalTemplateData = OptionalTemplateDataDto.From(request.OptionalTemplateData)
			};
		}
	}

	private sealed class OptionalTemplateDataDto
	{
		[JsonPropertyName("entitySchemaName")]
		public string? EntitySchemaName { get; init; }

		[JsonPropertyName("useExistingEntitySchema")]
		public bool? UseExistingEntitySchema { get; init; }

		[JsonPropertyName("useAIContentGeneration")]
		public bool? UseAiContentGeneration { get; init; }

		[JsonPropertyName("appSectionDescription")]
		public string? AppSectionDescription { get; init; }

		public static OptionalTemplateDataDto From(ApplicationOptionalTemplateData? optionalTemplateData)
		{
			if (optionalTemplateData is null)
			{
				return new OptionalTemplateDataDto();
			}

			return new OptionalTemplateDataDto
			{
				EntitySchemaName = optionalTemplateData.EntitySchemaName,
				UseExistingEntitySchema = optionalTemplateData.UseExistingEntitySchema,
				UseAiContentGeneration = optionalTemplateData.UseAiContentGeneration,
				AppSectionDescription = optionalTemplateData.AppSectionDescription
			};
		}
	}

	private sealed class CreateApplicationResponseDto
	{
		[JsonPropertyName("success")]
		public bool Success { get; init; }

		[JsonPropertyName("errorInfo")]
		public ErrorInfoDto? ErrorInfo { get; init; }

		[JsonPropertyName("value")]
		public string? Value { get; init; }

		[JsonPropertyName("dependenciesErrors")]
		public List<DependencyErrorDto>? DependenciesErrors { get; init; } = [];
	}

	private sealed class ErrorInfoDto
	{
		[JsonPropertyName("message")]
		public string? Message { get; init; }
	}

	private sealed class DependencyErrorDto
	{
		[JsonPropertyName("source")]
		public string? Source { get; init; }

		[JsonPropertyName("reference")]
		public string? Reference { get; init; }

		[JsonPropertyName("package")]
		public string? Package { get; init; }
	}

	private sealed class IconSelectQueryResponseDto
	{
		[JsonPropertyName("success")]
		public bool Success { get; init; }

		[JsonPropertyName("errorInfo")]
		public ErrorInfoDto? ErrorInfo { get; init; }

		[JsonPropertyName("rows")]
		public List<IconRowDto> Rows { get; init; } = [];
	}

	private sealed class IconRowDto
	{
		[JsonPropertyName("Id")]
		public string Id { get; init; } = string.Empty;
	}

	private sealed record ResolvedApplicationCreateRequest(
		string Name,
		string Code,
		string? Description,
		string TemplateCode,
		string IconId,
		string IconBackground,
		string? ClientTypeId,
		ApplicationOptionalTemplateData? OptionalTemplateData);
}

/// <summary>
/// Flat application creation request used by the application MCP tool family.
/// </summary>
/// <param name="Name">Optional human-readable application name.</param>
/// <param name="Code">Optional unique application code.</param>
/// <param name="Description">Optional application description.</param>
/// <param name="TemplateCode">Template code used by CreateApp.</param>
/// <param name="IconId">Optional application icon identifier, or the literal <c>auto</c>.</param>
/// <param name="IconBackground">Optional application icon background color.</param>
/// <param name="ClientTypeId">Optional client type identifier.</param>
/// <param name="OptionalTemplateData">Optional CreateApp template payload.</param>
[SuppressMessage("ReSharper", "UnusedAutoPropertyAccessor.Global", Justification = "MCP request contract")]
public sealed record ApplicationCreateRequest(
	string? Name,
	string? Code,
	string? Description,
	string TemplateCode,
	string? IconId = null,
	string? IconBackground = null,
	string? ClientTypeId = null,
	ApplicationOptionalTemplateData? OptionalTemplateData = null);

/// <summary>
/// Optional template data forwarded to the CreateApp endpoint.
/// </summary>
/// <param name="EntitySchemaName">Optional entity schema name.</param>
/// <param name="UseExistingEntitySchema">Optional flag that reuses an existing entity schema.</param>
/// <param name="UseAiContentGeneration">Optional flag that enables AI content generation.</param>
/// <param name="AppSectionDescription">Optional application section description.</param>
public sealed record ApplicationOptionalTemplateData(
	string? EntitySchemaName = null,
	bool? UseExistingEntitySchema = null,
	bool? UseAiContentGeneration = null,
	string? AppSectionDescription = null);
