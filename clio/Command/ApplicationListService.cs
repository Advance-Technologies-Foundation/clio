using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Clio.Common;
using Clio.UserEnvironment;

namespace Clio.Command;

/// <summary>
/// Provides structured installed-application reads through Creatio backend services.
/// </summary>
public interface IApplicationListService
{
	/// <summary>
	/// Loads installed applications for the requested environment and optional filters.
	/// </summary>
	/// <param name="environmentName">Registered clio environment name.</param>
	/// <param name="appId">Optional installed application identifier.</param>
	/// <param name="appCode">Optional installed application code.</param>
	/// <returns>Installed application list items.</returns>
	IReadOnlyList<InstalledApplicationListItem> GetApplications(
		string environmentName,
		string? appId,
		string? appCode);
}

/// <summary>
/// Default SelectQuery-backed application list reader for MCP application tools.
/// </summary>
public sealed class ApplicationListService(
	ISettingsRepository settingsRepository,
	IApplicationClientFactory applicationClientFactory)
	: IApplicationListService
{
	private const int GuidDataValueType = 0;
	private const int TextDataValueType = 1;

	private static readonly JsonSerializerOptions JsonOptions = new()
	{
		PropertyNameCaseInsensitive = true
	};

	/// <inheritdoc />
	public IReadOnlyList<InstalledApplicationListItem> GetApplications(
		string environmentName,
		string? appId,
		string? appCode)
	{
		if (string.IsNullOrWhiteSpace(environmentName))
		{
			throw new ArgumentException("Environment name is required.", nameof(environmentName));
		}

		EnvironmentSettings environmentSettings = settingsRepository.FindEnvironment(environmentName)
			?? throw new InvalidOperationException(
				$"Environment with key '{environmentName}' not found. Check your clio configuration.");
		IApplicationClient client = applicationClientFactory.CreateEnvironmentClient(environmentSettings);
		ServiceUrlBuilder serviceUrlBuilder = new(environmentSettings);
		string responseJson = client.ExecutePostRequest(
			serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.Select),
			JsonSerializer.Serialize(BuildInstalledApplicationsQuery(appId, appCode)));
		SelectQueryResponseDto response = Deserialize<SelectQueryResponseDto>(
			responseJson,
			"Installed application query response was empty.");
		if (!response.Success)
		{
			throw new InvalidOperationException(
				response.ErrorInfo?.Message ?? "Installed application query failed.");
		}

		return response.Rows
			.Select(application => new InstalledApplicationListItem(
				ParseGuid(application.Id, "Installed application id is invalid."),
				application.Name,
				application.Code,
				application.Version,
				application.Description))
			.OrderBy(application => application.Name, StringComparer.OrdinalIgnoreCase)
			.ThenBy(application => application.Code, StringComparer.OrdinalIgnoreCase)
			.ToList();
	}

	private static Guid ParseGuid(string value, string message)
	{
		if (Guid.TryParse(value, out Guid parsedValue))
		{
			return parsedValue;
		}

		throw new InvalidOperationException(message);
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
		List<object> filters = [];
		if (!string.IsNullOrWhiteSpace(appId))
		{
			filters.Add(new
			{
				filterType = 1,
				comparisonType = 3,
				isEnabled = true,
				trimDateTimeParameterToDate = false,
				leftExpression = new
				{
					expressionType = 0,
					columnPath = "Id"
				},
				rightExpression = new
				{
					expressionType = 2,
					parameter = new
					{
						value = appId.Trim(),
						dataValueType = GuidDataValueType
					}
				}
			});
		}

		if (!string.IsNullOrWhiteSpace(appCode))
		{
			filters.Add(new
			{
				filterType = 1,
				comparisonType = 3,
				isEnabled = true,
				trimDateTimeParameterToDate = false,
				leftExpression = new
				{
					expressionType = 0,
					columnPath = "Code"
				},
				rightExpression = new
				{
					expressionType = 2,
					parameter = new
					{
						value = appCode.Trim(),
						dataValueType = TextDataValueType
					}
				}
			});
		}

		Dictionary<string, object> filterItems = filters
			.Select((filter, index) => new KeyValuePair<string, object>($"filter{index}", filter))
			.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);

		return new
		{
			rootSchemaName = "SysInstalledApp",
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
					["Id"] = CreateColumn("Id"),
					["Code"] = CreateColumn("Code"),
					["Name"] = CreateColumn("Name"),
					["Version"] = CreateColumn("Version"),
					["Description"] = CreateColumn("Description")
				}
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

	private static object CreateColumn(string columnPath)
	{
		return new
		{
			expression = new
			{
				expressionType = 0,
				columnPath
			},
			orderDirection = 0,
			orderPosition = -1,
			isVisible = true
		};
	}

	private sealed class SelectQueryResponseDto
	{
		[JsonPropertyName("success")]
		public bool Success { get; set; }

		[JsonPropertyName("errorInfo")]
		public ErrorInfoDto? ErrorInfo { get; set; }

		[JsonPropertyName("rows")]
		public List<InstalledApplicationDto> Rows { get; set; } = [];
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

		[JsonPropertyName("Description")]
		public string Description { get; set; } = string.Empty;
	}

	private sealed class ErrorInfoDto
	{
		[JsonPropertyName("message")]
		public string? Message { get; set; }
	}
}
