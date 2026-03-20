using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Clio.Common;

namespace Clio.Package;

/// <summary>
/// Builds Creatio DataService SelectQuery request bodies and executes them via <see cref="IApplicationClient"/>.
/// </summary>
internal static class SelectQueryHelper
{
	internal const int GuidDataValueType = 0;
	internal const int TextDataValueType = 1;
	internal const int IntDataValueType = 4;

	private static readonly JsonSerializerOptions JsonOptions = new()
	{
		PropertyNameCaseInsensitive = true
	};

	internal static T ExecuteSelectQuery<T>(
		IApplicationClient client,
		IServiceUrlBuilder serviceUrlBuilder,
		object query)
		where T : SelectQueryResponseBaseDto
	{
		string responseJson = client.ExecutePostRequest(
			serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.Select),
			JsonSerializer.Serialize(query));
		if (string.IsNullOrWhiteSpace(responseJson))
		{
			throw new InvalidOperationException("SelectQuery returned an empty response.");
		}
		T response = JsonSerializer.Deserialize<T>(responseJson, JsonOptions)
			?? throw new InvalidOperationException("SelectQuery returned an empty response.");
		if (!response.Success)
		{
			throw new InvalidOperationException(response.ErrorInfo?.Message ?? "SelectQuery failed.");
		}
		return response;
	}

	internal static object BuildSelectQuery(
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
					comparisonType = item.filter.ComparisonType,
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

	internal sealed record SelectQueryColumnDefinition(string Path, string Alias);

	internal sealed record SelectQueryFilterDefinition(
		string ColumnPath,
		object Value,
		int DataValueType,
		int ComparisonType = 3);

	internal abstract class SelectQueryResponseBaseDto
	{
		[JsonPropertyName("success")]
		public bool Success { get; set; }

		[JsonPropertyName("errorInfo")]
		public ErrorInfoDto? ErrorInfo { get; set; }
	}

	internal sealed class ErrorInfoDto
	{
		[JsonPropertyName("message")]
		public string? Message { get; set; }
	}
}
