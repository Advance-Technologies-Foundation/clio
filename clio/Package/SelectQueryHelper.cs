using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
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

	/// <summary>
	/// How many times a SelectQuery is sent when the server answers with a transient failure.
	/// </summary>
	/// <remarks>
	/// The transport-level retry inside <c>ExecutePostRequest</c> never applies to these: the server answers
	/// HTTP 200 and reports the failure in the body (<c>success: false</c>), so as far as the transport is
	/// concerned the call succeeded. A SelectQuery is a read and carries no side effect, so re-sending it is
	/// safe. This budget is deliberately separate from the <c>maxAttempts</c> argument, which belongs to the
	/// transport.
	/// </remarks>
	private const int TransientFailureAttempts = 3;

	/// <summary>
	/// Number of sends allowed for a caller that bounded the call with a finite <c>requestTimeout</c>.
	/// </summary>
	/// <remarks>
	/// A finite <c>requestTimeout</c> is the caller's statement of how long THIS call may take, and several
	/// callers budget a whole operation around it - the create-app-section timeout recovery sizes its poll so
	/// the whole MCP call stays under the client's ~180 s ceiling (ENG-91540), counting one in-flight readback
	/// of <c>VerificationTimeoutMs</c>. Retrying underneath such a caller would silently triple its bound and
	/// hand it back the opaque client-side abort the budget exists to prevent, so a bounded call is sent once
	/// and its failure is returned unchanged.
	/// </remarks>
	private const int BoundedCallAttempts = 1;

	/// <summary>
	/// Pause between two sends of the same SelectQuery after a transient server failure.
	/// </summary>
	/// <remarks>
	/// Short on purpose: the conditions retried here (a concurrent collection modification, a deadlock
	/// victim) clear within milliseconds, and a long pause would only add latency to a run that is
	/// already behind.
	/// </remarks>
	private static readonly TimeSpan TransientFailureRetryDelay = TimeSpan.FromMilliseconds(500);

	/// <summary>
	/// Server-reported failures that a re-send can clear: a concurrent modification of a server-side
	/// collection, a database deadlock, or a lock/command timeout.
	/// </summary>
	/// <remarks>
	/// Kept deliberately narrow. Retrying every <c>success: false</c> would also retry - and so delay and
	/// obscure - real answers such as a package that does not exist.
	/// </remarks>
	private static readonly string[] TransientFailureMarkers = [
		"Collection was modified",
		"deadlock",
		"timeout",
		"timed out"
	];

	internal static T ExecuteSelectQuery<T>(
		IApplicationClient client,
		IServiceUrlBuilder serviceUrlBuilder,
		object query,
		int requestTimeout = Timeout.Infinite,
		int maxAttempts = 1,
		int retryDelay = 1)
		where T : SelectQueryResponseBaseDto
	{
		string url = serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.Select);
		string requestBody = JsonSerializer.Serialize(query);
		int allowedAttempts = requestTimeout == Timeout.Infinite
			? TransientFailureAttempts
			: BoundedCallAttempts;
		int attempt = 1;
		while (true)
		{
			string responseJson = client.ExecutePostRequest(
				url,
				requestBody,
				requestTimeout, maxAttempts, retryDelay);
			// ENG-93365: an HTML error/login page or a truncated body must surface as a typed error naming the
			// endpoint and the actual body, never as a raw System.Text.Json parser message.
			T response = ServiceResponseJsonGuard.Deserialize<T>("SelectQuery", url, responseJson, JsonOptions);
			if (response.Success)
			{
				return response;
			}
			string detail = response.ErrorInfo?.Message ?? responseJson;
			// Issue #1119: a server-side "Collection was modified; enumeration operation may not execute."
			// arrived as an HTTP 200 with success:false and took out a whole 45-minute e2e run, because the
			// read that resolves the target package had no retry at all.
			if (attempt >= allowedAttempts || !IsTransientFailure(detail))
			{
				throw new InvalidOperationException($"SelectQuery failed: {detail}");
			}
			Thread.Sleep(TransientFailureRetryDelay);
			attempt++;
		}
	}

	/// <summary>
	/// Whether a server-reported SelectQuery failure is one a re-send can clear.
	/// </summary>
	/// <param name="detail">The failure text the server reported, or the raw response body.</param>
	/// <returns>True when the text names a known transient condition.</returns>
	private static bool IsTransientFailure(string detail)
	{
		return !string.IsNullOrEmpty(detail)
			&& TransientFailureMarkers.Any(marker =>
				detail.Contains(marker, StringComparison.OrdinalIgnoreCase));
	}

	/// <summary>
	/// Builds a SelectQuery that returns one row with the number of records of <paramref name="rootSchemaName"/> the
	/// caller can read, in the column <paramref name="alias"/> (an aggregation COUNT over <c>Id</c>).
	/// </summary>
	internal static object BuildCountQuery(string rootSchemaName, string alias) =>
		new
		{
			rootSchemaName,
			operationType = 0,
			allColumns = false,
			isDistinct = false,
			ignoreDisplayValues = false,
			rowCount = 1,
			rowsOffset = -1,
			isPageable = false,
			columns = new
			{
				items = new Dictionary<string, object>(StringComparer.Ordinal)
				{
					[alias] = new
					{
						expression = new
						{
							// Function → aggregation → count, as Terrasoft.Nui.ServiceModel serializes it.
							expressionType = 1,
							functionType = 2,
							aggregationType = 1,
							functionArgument = new
							{
								expressionType = 0,
								columnPath = "Id"
							}
						},
						isVisible = true
					}
				}
			}
		};

	/// <summary>
	/// Builds a SelectQuery whose filters are combined with AND.
	/// </summary>
	/// <param name="rootSchemaName">Schema to query.</param>
	/// <param name="columns">Columns to return.</param>
	/// <param name="filters">Comparison filters, all of which a row must match.</param>
	/// <param name="rowCount">Maximum number of rows to return.</param>
	/// <param name="isDistinct">Return each distinct combination of the selected columns once, so the row cap counts
	/// distinct values rather than every row that carries them.</param>
	/// <returns>The query, ready for JSON serialization.</returns>
	internal static object BuildSelectQuery(
		string rootSchemaName,
		IReadOnlyList<SelectQueryColumnDefinition> columns,
		IReadOnlyList<SelectQueryFilterDefinition> filters,
		int rowCount = 10000,
		bool isDistinct = false)
	{
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

		return BuildQueryEnvelope(rootSchemaName, columns, 0, filterItems, rowCount, isDistinct);
	}

	/// <summary>
	/// Builds a SelectQuery where all <paramref name="filterValues"/> for <paramref name="filterColumn"/>
	/// are combined with an OR logical operator, one comparison filter per value, in a single request.
	/// </summary>
	/// <remarks>
	/// Do not use it for the virtual <c>ApplicationSection</c> schema; use <see cref="BuildSelectQueryWithInFilter"/>.
	/// </remarks>
	internal static object BuildSelectQueryWithOrFilter(
		string rootSchemaName,
		IReadOnlyList<SelectQueryColumnDefinition> columns,
		string filterColumn,
		IReadOnlyList<string> filterValues,
		int dataValueType,
		int rowCount = 10000)
	{
		Dictionary<string, object> filterItems = filterValues
			.Select((value, index) => new { value, index })
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
						columnPath = filterColumn
					},
					rightExpression = new
					{
						expressionType = 2,
						parameter = new
						{
							value = item.value,
							dataValueType
						}
					}
				},
				StringComparer.Ordinal);

		return BuildQueryEnvelope(rootSchemaName, columns, 1, filterItems, rowCount);
	}

	/// <summary>
	/// Builds a SelectQuery with a single IN filter: <paramref name="filterColumn"/> equals any of
	/// <paramref name="filterValues"/>, all carried as right expressions of one filter.
	/// </summary>
	/// <remarks>
	/// Use it instead of <see cref="BuildSelectQueryWithOrFilter"/> for the virtual <c>ApplicationSection</c>
	/// schema: its query executor reads the values of the FIRST filter on a column and ignores the group's
	/// logical operation, so an OR group returns the rows of the first value only.
	/// </remarks>
	/// <param name="rootSchemaName">Schema to query.</param>
	/// <param name="columns">Columns to return.</param>
	/// <param name="filterColumn">Column the values are compared with.</param>
	/// <param name="filterValues">Values to match; a row matches when it equals any of them.</param>
	/// <param name="dataValueType">DataService data value type of the values.</param>
	/// <param name="rowCount">Maximum number of rows to return.</param>
	/// <returns>The query, ready for JSON serialization.</returns>
	internal static object BuildSelectQueryWithInFilter(
		string rootSchemaName,
		IReadOnlyList<SelectQueryColumnDefinition> columns,
		string filterColumn,
		IReadOnlyList<string> filterValues,
		int dataValueType,
		int rowCount = 10000)
	{
		Dictionary<string, object> filterItems = new(StringComparer.Ordinal)
		{
			["filter0"] = new
			{
				filterType = 4,
				comparisonType = 3,
				isEnabled = true,
				trimDateTimeParameterToDate = false,
				leftExpression = new
				{
					expressionType = 0,
					columnPath = filterColumn
				},
				rightExpressions = filterValues
					.Select(value => BuildParameterExpression(dataValueType, value))
					.ToList()
			}
		};

		return BuildQueryEnvelope(rootSchemaName, columns, 0, filterItems, rowCount);
	}

	/// <summary>
	/// Builds the column items of a SelectQuery: one visible column expression per definition, keyed by alias, with
	/// the definition's order (unordered by default).
	/// </summary>
	private static Dictionary<string, object> BuildColumnItems(IReadOnlyList<SelectQueryColumnDefinition> columns)
	{
		return columns
			.ToDictionary(
				column => column.Alias,
				column => (object)new
				{
					expression = new
					{
						expressionType = 0,
						columnPath = column.Path
					},
					orderDirection = column.OrderDirection,
					orderPosition = column.OrderPosition,
					isVisible = true
				},
				StringComparer.Ordinal);
	}

	/// <summary>
	/// Builds the DataService SelectQuery wire envelope shared by every builder in this class: the root
	/// schema, the columns and one filter group (<c>filterType: 6</c>) with the given logical operation.
	/// </summary>
	private static object BuildQueryEnvelope(
		string rootSchemaName,
		IReadOnlyList<SelectQueryColumnDefinition> columns,
		int logicalOperation,
		Dictionary<string, object> filterItems,
		int rowCount,
		bool isDistinct = false)
	{
		return new
		{
			rootSchemaName,
			operationType = 0,
			allColumns = false,
			isDistinct,
			ignoreDisplayValues = false,
			rowCount,
			rowsOffset = -1,
			isPageable = false,
			columns = new
			{
				items = BuildColumnItems(columns)
			},
			filters = new
			{
				filterType = 6,
				isEnabled = true,
				trimDateTimeParameterToDate = false,
				logicalOperation,
				items = filterItems
			}
		};
	}

	/// <summary>
	/// Builds a DataService parameter expression (<c>expressionType: 2</c>) that carries a literal value, as used
	/// in <c>columnValues</c> of an update query and on the right side of a comparison filter.
	/// </summary>
	/// <param name="dataValueType">DataService data value type of <paramref name="value"/>.</param>
	/// <param name="value">The literal value.</param>
	/// <returns>The expression, ready for JSON serialization.</returns>
	internal static object BuildParameterExpression(int dataValueType, object value) =>
		new
		{
			expressionType = 2,
			parameter = new
			{
				dataValueType,
				value
			}
		};

	/// <summary>
	/// Builds the filter group of an update query that targets one record by its <c>Id</c> column.
	/// </summary>
	/// <param name="id">Record identifier.</param>
	/// <param name="dataValueType">DataService data value type the identifier is sent as. Callers keep the type
	/// the endpoint was verified with; the value is not converted.</param>
	/// <returns>The filter group, ready for JSON serialization.</returns>
	internal static object BuildIdFilter(string id, int dataValueType) =>
		new
		{
			filterType = 6,
			isEnabled = true,
			trimDateTimeParameterToDate = false,
			logicalOperation = 0,
			items = new
			{
				primaryFilter = new
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
					rightExpression = BuildParameterExpression(dataValueType, id)
				}
			}
		};

	/// <summary>A selected column. <paramref name="OrderDirection"/>: 0 = none, 1 = ascending, 2 = descending;
	/// <paramref name="OrderPosition"/>: -1 = not ordered. Ordering matters whenever a row cap could cut off the
	/// row the caller wants, because <c>rowCount</c> is applied to an otherwise unordered result.</summary>
	internal sealed record SelectQueryColumnDefinition(string Path, string Alias, int OrderDirection = 0,
		int OrderPosition = -1);

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
