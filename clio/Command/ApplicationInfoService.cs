using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Clio.Common;
using Clio.UserEnvironment;

namespace Clio.Command;

/// <summary>
/// Provides structured application context reads for installed Creatio applications.
/// </summary>
public interface IApplicationInfoService
{
	/// <summary>
	/// Loads application package and entity metadata for the requested installed application.
	/// </summary>
	/// <param name="environmentName">Registered clio environment name.</param>
	/// <param name="appId">Optional installed application identifier.</param>
	/// <param name="appCode">Optional installed application code.</param>
	/// <returns>Structured application package and entity information.</returns>
	ApplicationInfoResult GetApplicationInfo(string environmentName, string? appId, string? appCode);
}

/// <summary>
/// Default application info reader for MCP application metadata tools.
/// </summary>
public sealed class ApplicationInfoService(
	ISettingsRepository settingsRepository,
	IApplicationClientFactory applicationClientFactory)
	: IApplicationInfoService
{
	private const int GuidDataValueType = 0;
	private const int TextDataValueType = 1;

	private static readonly JsonSerializerOptions JsonOptions = new()
	{
		PropertyNameCaseInsensitive = true
	};

	private static readonly IReadOnlyDictionary<int, string> DefaultValueSourceNames =
		new Dictionary<int, string>
		{
			[0] = "None",
			[1] = "Const",
			[2] = "Settings",
			[3] = "SystemValue",
			[4] = "Sequence"
		};

	private static readonly IReadOnlyDictionary<int, string> DataValueTypeNames =
		new Dictionary<int, string>
		{
			[0] = "Guid",
			[1] = "Text",
			[4] = "Integer",
			[5] = "Float",
			[6] = "Money",
			[7] = "DateTime",
			[8] = "Date",
			[9] = "Time",
			[10] = "Lookup",
			[11] = "Enum",
			[12] = "Boolean",
			[13] = "Blob",
			[14] = "Image",
			[15] = "CUSTOM_OBJECT",
			[16] = "IMAGELOOKUP",
			[17] = "COLLECTION",
			[18] = "Color",
			[19] = "LOCALIZABLE_STRING",
			[20] = "ENTITY",
			[21] = "ENTITY_COLLECTION",
			[22] = "ENTITY_COLUMN_MAPPING_COLLECTION",
			[23] = "HASH_TEXT",
			[24] = "SECURE_TEXT",
			[25] = "FILE",
			[26] = "MAPPING",
			[27] = "SHORT_TEXT",
			[28] = "MEDIUM_TEXT",
			[29] = "MAXSIZE_TEXT",
			[30] = "LONG_TEXT",
			[31] = "FLOAT1",
			[32] = "FLOAT2",
			[33] = "FLOAT3",
			[34] = "FLOAT4",
			[35] = "LOCALIZABLE_PARAMETER_VALUES_LIST",
			[36] = "METADATA_TEXT",
			[37] = "STAGE_INDICATOR",
			[38] = "OBJECT_LIST",
			[39] = "COMPOSITE_OBJECT_LIST",
			[40] = "FLOAT8",
			[41] = "FILE_LOCATOR",
			[42] = "PHONE_TEXT",
			[43] = "RICH_TEXT",
			[44] = "WEB_TEXT",
			[45] = "EMAIL_TEXT",
			[46] = "COMPOSITE_OBJECT",
			[47] = "FLOAT0",
			[48] = "MONEY0",
			[49] = "MONEY1",
			[50] = "MONEY3"
		};

	/// <inheritdoc />
	public ApplicationInfoResult GetApplicationInfo(string environmentName, string? appId, string? appCode)
	{
		if (string.IsNullOrWhiteSpace(environmentName))
		{
			throw new ArgumentException("Environment name is required.", nameof(environmentName));
		}

		if (string.IsNullOrWhiteSpace(appId) && string.IsNullOrWhiteSpace(appCode))
		{
			throw new ArgumentException("Either app-id or app-code is required.");
		}

		EnvironmentSettings environmentSettings = settingsRepository.FindEnvironment(environmentName)
			?? throw new InvalidOperationException(
				$"Environment with key '{environmentName}' not found. Check your clio configuration.");
		IApplicationClient client = applicationClientFactory.CreateEnvironmentClient(environmentSettings);
		ServiceUrlBuilder serviceUrlBuilder = new(environmentSettings);

		InstalledApplicationDto application = ResolveApplication(client, serviceUrlBuilder, appId, appCode);
		ApplicationPackageDto primaryPackage = GetPrimaryPackage(client, serviceUrlBuilder, application.Id);
		IReadOnlyList<ApplicationEntityRecordDto> entityRows =
			GetApplicationEntities(client, serviceUrlBuilder, application.Id, primaryPackage.UId);
		IReadOnlyList<ApplicationEntityInfoResult> entities = entityRows
			.GroupBy(entity => entity.UId, StringComparer.OrdinalIgnoreCase)
			.Select(group => LoadEntityInfo(client, serviceUrlBuilder, group.First()))
			.OrderBy(entity => entity.Caption, StringComparer.OrdinalIgnoreCase)
			.ThenBy(entity => entity.Name, StringComparer.OrdinalIgnoreCase)
			.ToList();

		return new ApplicationInfoResult(primaryPackage.UId, primaryPackage.Name, entities);
	}

	private static InstalledApplicationDto ResolveApplication(
		IApplicationClient client,
		ServiceUrlBuilder serviceUrlBuilder,
		string? appId,
		string? appCode)
	{
		SelectQueryResponseDto response = ExecuteSelectQuery<SelectQueryResponseDto>(
			client,
			serviceUrlBuilder,
			BuildInstalledApplicationsQuery(appId, appCode));
		InstalledApplicationDto? application = response.Rows.FirstOrDefault();
		if (application is not null)
		{
			return application;
		}

		string identifier = !string.IsNullOrWhiteSpace(appId) ? appId.Trim() : appCode?.Trim() ?? "unknown";
		throw new InvalidOperationException($"Application '{identifier}' not found.");
	}

	private static ApplicationPackageDto GetPrimaryPackage(
		IApplicationClient client,
		ServiceUrlBuilder serviceUrlBuilder,
		string applicationId)
	{
		string responseJson = client.ExecutePostRequest(
			serviceUrlBuilder.Build("ServiceModel/ApplicationPackagesService.svc/GetApplicationPackages"),
			JsonSerializer.Serialize(applicationId));
		ApplicationPackagesResponseDto response = Deserialize<ApplicationPackagesResponseDto>(
			responseJson,
			"Application packages response was empty.");
		if (!response.Success)
		{
			throw new InvalidOperationException(
				response.ErrorInfo?.Message ?? "Failed to load application packages.");
		}

		ApplicationPackageDto? primaryPackage = response.Packages
			.FirstOrDefault(package => package.IsApplicationPrimaryPackage);
		if (primaryPackage is null)
		{
			throw new InvalidOperationException("Primary package not found in response.");
		}

		return primaryPackage;
	}

	private static IReadOnlyList<ApplicationEntityRecordDto> GetApplicationEntities(
		IApplicationClient client,
		ServiceUrlBuilder serviceUrlBuilder,
		string appId,
		string packageUId)
	{
		ApplicationEntitySelectQueryResponseDto response = ExecuteSelectQuery<ApplicationEntitySelectQueryResponseDto>(
			client,
			serviceUrlBuilder,
			BuildApplicationEntitiesQuery(appId, packageUId));
		return response.Rows;
	}

	private static ApplicationEntityInfoResult LoadEntityInfo(
		IApplicationClient client,
		ServiceUrlBuilder serviceUrlBuilder,
		ApplicationEntityRecordDto entityRow)
	{
		string responseJson = client.ExecutePostRequest(
			serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.RuntimeEntitySchemaRequest),
			JsonSerializer.Serialize(new { uId = entityRow.UId }));
		RuntimeSchemaResponseDto response = Deserialize<RuntimeSchemaResponseDto>(
			responseJson,
			$"Runtime entity schema '{entityRow.UId}' was not returned.");
		if (!response.Success || response.Schema is null)
		{
			throw new InvalidOperationException(
				response.ErrorInfo?.Message ?? $"Runtime entity schema '{entityRow.UId}' was not returned.");
		}

		List<ApplicationColumnInfoResult> columns = (response.Schema.Columns?.Items?.Values ??
			Enumerable.Empty<RuntimeSchemaColumnDto>())
			.Where(column => !column.IsInherited)
			.Select(MapColumn)
			.OrderBy(column => column.Name, StringComparer.OrdinalIgnoreCase)
			.ToList();

		string entityName = !string.IsNullOrWhiteSpace(response.Schema.Name)
			? response.Schema.Name
			: entityRow.Name ?? throw new InvalidOperationException("Entity name was not returned.");
		string fallbackCaption = !string.IsNullOrWhiteSpace(entityRow.Caption) ? entityRow.Caption : entityName;

		return new ApplicationEntityInfoResult(
			entityRow.UId,
			entityName,
			ToLocalizedText(response.Schema.Caption, fallbackCaption),
			columns);
	}

	private static ApplicationColumnInfoResult MapColumn(RuntimeSchemaColumnDto column)
	{
		string name = !string.IsNullOrWhiteSpace(column.Name)
			? column.Name
			: throw new InvalidOperationException("Runtime schema column name was not returned.");
		int? sourceType = column.DefaultValue?.ValueSourceType;
		string? defaultValueSource = sourceType.HasValue &&
			DefaultValueSourceNames.TryGetValue(sourceType.Value, out string? sourceName)
				? sourceName
				: null;
		object? defaultValue = GetDefaultValue(column.DefaultValue);

		return new ApplicationColumnInfoResult(
			name,
			ToLocalizedText(column.Caption, name),
			DataValueTypeNames.TryGetValue(column.DataValueType, out string? dataValueTypeName)
				? dataValueTypeName
				: column.DataValueType.ToString(),
			column.ReferenceSchemaName,
			defaultValueSource,
			defaultValue);
	}

	private static object? GetDefaultValue(DefaultValueDto? defaultValue)
	{
		if (defaultValue is null)
		{
			return null;
		}

		if (defaultValue.ValueSourceType == 1)
		{
			return ConvertJsonElement(defaultValue.Value);
		}

		return ConvertJsonElement(defaultValue.ValueSource);
	}

	private static string ToLocalizedText(
		IReadOnlyDictionary<string, string>? localizedValues,
		string fallback)
	{
		return localizedValues?.Values
			.Select(value => value?.Trim())
			.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))
			?? fallback;
	}

	private static T ExecuteSelectQuery<T>(
		IApplicationClient client,
		ServiceUrlBuilder serviceUrlBuilder,
		object query)
		where T : SelectQueryResponseBaseDto
	{
		string responseJson = client.ExecutePostRequest(
			serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.Select),
			JsonSerializer.Serialize(query));
		T response = Deserialize<T>(
			responseJson,
			"Select query response was empty.");
		if (!response.Success)
		{
			throw new InvalidOperationException(response.ErrorInfo?.Message ?? "Select query failed.");
		}

		return response;
	}

	private static T Deserialize<T>(string responseJson, string emptyMessage)
	{
		if (string.IsNullOrWhiteSpace(responseJson))
		{
			throw new InvalidOperationException(emptyMessage);
		}

		return JsonSerializer.Deserialize<T>(responseJson, JsonOptions)
			?? throw new InvalidOperationException(emptyMessage);
	}

	private static object BuildInstalledApplicationsQuery(string? appId, string? appCode)
	{
		List<SelectQueryFilterDefinition> filters = [];
		if (!string.IsNullOrWhiteSpace(appId))
		{
			filters.Add(new SelectQueryFilterDefinition("Id", appId.Trim(), GuidDataValueType));
		}

		if (!string.IsNullOrWhiteSpace(appCode))
		{
			filters.Add(new SelectQueryFilterDefinition("Code", appCode.Trim(), TextDataValueType));
		}

		return BuildSelectQuery(
			"SysInstalledApp",
			[
				new SelectQueryColumnDefinition("Id", "Id"),
				new SelectQueryColumnDefinition("Code", "Code"),
				new SelectQueryColumnDefinition("Name", "Name"),
				new SelectQueryColumnDefinition("Version", "Version")
			],
			filters);
	}

	private static object BuildApplicationEntitiesQuery(string appId, string packageUId)
	{
		return BuildSelectQuery(
			"ApplicationEntity",
			[
				new SelectQueryColumnDefinition("UId", "UId"),
				new SelectQueryColumnDefinition("Name", "Name"),
				new SelectQueryColumnDefinition("Caption", "Caption")
			],
			[
				new SelectQueryFilterDefinition("Application", appId, GuidDataValueType),
				new SelectQueryFilterDefinition("Package", packageUId, GuidDataValueType)
			]);
	}

	private static object BuildSelectQuery(
		string rootSchemaName,
		IReadOnlyList<SelectQueryColumnDefinition> columns,
		IReadOnlyList<SelectQueryFilterDefinition> filters)
	{
		Dictionary<string, object> columnItems = columns
			.ToDictionary(
				column => column.Alias,
				column => (object)new
				{
					expression = new
					{
						expressionType = 0,
						columnPath = column.Path
					},
					orderDirection = 0,
					orderPosition = -1,
					isVisible = true
				},
				StringComparer.Ordinal);

		Dictionary<string, object> filterItems = filters
			.Select((filter, index) => new { filter, index })
			.ToDictionary(
				item => $"filter{item.index}",
				item => (object)new
				{
					filterType = 1,
					comparisonType = 3,
					isEnabled = true,
					trimDateTimeParameterToDate = false,
					leftExpression = new
					{
						expressionType = 0,
						columnPath = item.filter.ColumnPath
					},
					rightExpression = new
					{
						expressionType = 2,
						parameter = new
						{
							value = item.filter.Value,
							dataValueType = item.filter.DataValueType
						}
					}
				},
				StringComparer.Ordinal);

		return new
		{
			rootSchemaName,
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
				items = columnItems
			},
			filters = new
			{
				filterType = 6,
				isEnabled = true,
				trimDateTimeParameterToDate = false,
				logicalOperation = 0,
				items = filterItems
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

	private static object? ConvertJsonElement(JsonElement? element)
	{
		if (!element.HasValue)
		{
			return null;
		}

		return ConvertJsonElement(element.Value);
	}

	private static object? ConvertJsonElement(JsonElement element)
	{
		return element.ValueKind switch
		{
			JsonValueKind.Undefined => null,
			JsonValueKind.Null => null,
			JsonValueKind.String => element.GetString(),
			JsonValueKind.Number when element.TryGetInt64(out long intValue) => intValue,
			JsonValueKind.Number when element.TryGetDecimal(out decimal decimalValue) => decimalValue,
			JsonValueKind.True => true,
			JsonValueKind.False => false,
			_ => element.ToString()
		};
	}

	private sealed record SelectQueryColumnDefinition(string Path, string Alias);

	private sealed record SelectQueryFilterDefinition(string ColumnPath, object Value, int DataValueType);

	private abstract class SelectQueryResponseBaseDto
	{
		[JsonPropertyName("success")]
		public bool Success { get; set; }

		[JsonPropertyName("errorInfo")]
		public ErrorInfoDto? ErrorInfo { get; set; }
	}

	private sealed class SelectQueryResponseDto : SelectQueryResponseBaseDto
	{

		[JsonPropertyName("rows")]
		public List<InstalledApplicationDto> Rows { get; set; } = [];
	}

	private sealed class ApplicationEntitySelectQueryResponseDto : SelectQueryResponseBaseDto
	{
		[JsonPropertyName("rows")]
		public List<ApplicationEntityRecordDto> Rows { get; set; } = [];
	}

	private sealed class ApplicationPackagesResponseDto
	{
		[JsonPropertyName("success")]
		public bool Success { get; set; }

		[JsonPropertyName("errorInfo")]
		public ErrorInfoDto? ErrorInfo { get; set; }

		[JsonPropertyName("packages")]
		public List<ApplicationPackageDto> Packages { get; set; } = [];
	}

	private sealed class RuntimeSchemaResponseDto
	{
		[JsonPropertyName("success")]
		public bool Success { get; set; }

		[JsonPropertyName("errorInfo")]
		public ErrorInfoDto? ErrorInfo { get; set; }

		[JsonPropertyName("schema")]
		public RuntimeSchemaDto? Schema { get; set; }
	}

	private sealed class ErrorInfoDto
	{
		[JsonPropertyName("message")]
		public string? Message { get; set; }
	}

	private sealed class InstalledApplicationDto
	{
		[JsonPropertyName("Id")]
		public string Id { get; set; } = string.Empty;

		[JsonPropertyName("Code")]
		public string Code { get; set; } = string.Empty;

		[JsonPropertyName("Name")]
		public string Name { get; set; } = string.Empty;

		[JsonPropertyName("Version")]
		public string Version { get; set; } = string.Empty;
	}

	private sealed class ApplicationPackageDto
	{
		[JsonPropertyName("uId")]
		public string UId { get; set; } = string.Empty;

		[JsonPropertyName("name")]
		public string Name { get; set; } = string.Empty;

		[JsonPropertyName("isApplicationPrimaryPackage")]
		public bool IsApplicationPrimaryPackage { get; set; }
	}

	private sealed class ApplicationEntityRecordDto
	{
		[JsonPropertyName("UId")]
		public string UId { get; set; } = string.Empty;

		[JsonPropertyName("Name")]
		public string? Name { get; set; }

		[JsonPropertyName("Caption")]
		public string? Caption { get; set; }
	}

	private sealed class RuntimeSchemaDto
	{
		[JsonPropertyName("uId")]
		public string? UId { get; set; }

		[JsonPropertyName("name")]
		public string? Name { get; set; }

		[JsonPropertyName("caption")]
		public Dictionary<string, string>? Caption { get; set; }

		[JsonPropertyName("columns")]
		public RuntimeSchemaColumnsDto? Columns { get; set; }
	}

	private sealed class RuntimeSchemaColumnsDto
	{
		[JsonPropertyName("Items")]
		public Dictionary<string, RuntimeSchemaColumnDto>? Items { get; set; }
	}

	private sealed class RuntimeSchemaColumnDto
	{
		[JsonPropertyName("name")]
		public string? Name { get; set; }

		[JsonPropertyName("caption")]
		public Dictionary<string, string>? Caption { get; set; }

		[JsonPropertyName("dataValueType")]
		public int DataValueType { get; set; }

		[JsonPropertyName("isInherited")]
		public bool IsInherited { get; set; }

		[JsonPropertyName("referenceSchemaName")]
		public string? ReferenceSchemaName { get; set; }

		[JsonPropertyName("defValue")]
		public DefaultValueDto? DefaultValue { get; set; }
	}

	private sealed class DefaultValueDto
	{
		[JsonPropertyName("valueSourceType")]
		public int ValueSourceType { get; set; }

		[JsonPropertyName("value")]
		public JsonElement? Value { get; set; }

		[JsonPropertyName("valueSource")]
		public JsonElement? ValueSource { get; set; }
	}
}

/// <summary>
/// Structured application package and entity information returned by application MCP tools.
/// </summary>
/// <param name="PackageUId">Primary package identifier.</param>
/// <param name="PackageName">Primary package name.</param>
/// <param name="Entities">Application entity details.</param>
public sealed record ApplicationInfoResult(
	string PackageUId,
	string PackageName,
	IReadOnlyList<ApplicationEntityInfoResult> Entities);

/// <summary>
/// Structured entity information returned as part of application info results.
/// </summary>
/// <param name="UId">Entity identifier.</param>
/// <param name="Name">Entity schema name.</param>
/// <param name="Caption">Entity caption.</param>
/// <param name="Columns">Non-inherited runtime columns.</param>
public sealed record ApplicationEntityInfoResult(
	string UId,
	string Name,
	string Caption,
	IReadOnlyList<ApplicationColumnInfoResult> Columns);

/// <summary>
/// Structured column information returned as part of application info results.
/// </summary>
/// <param name="Name">Column name.</param>
/// <param name="Caption">Column caption.</param>
/// <param name="DataValueType">Creatio data-value-type name.</param>
/// <param name="ReferenceSchema">Optional lookup reference schema.</param>
/// <param name="DefaultValueSource">Optional default-value source name.</param>
/// <param name="DefaultValue">Optional default-value payload.</param>
public sealed record ApplicationColumnInfoResult(
	string Name,
	string Caption,
	string DataValueType,
	string? ReferenceSchema = null,
	object? DefaultValueSource = null,
	object? DefaultValue = null);
