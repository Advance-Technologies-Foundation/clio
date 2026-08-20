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

	internal static object BuildSelectQuery(
		string rootSchemaName,
		IReadOnlyList<SelectQueryColumnDefinition> columns,
		IReadOnlyList<SelectQueryFilterDefinition> filters,
		int rowCount = 10000)
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
			rowCount,
			rowsOffset = -1,
			isPageable = false,
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
			}
		};
	}

	/// <summary>
	/// Builds a SelectQuery where all <paramref name="filterValues"/> for <paramref name="filterColumn"/>
	/// are combined with an OR logical operator, producing an IN-style batch filter in a single request.
	/// </summary>
	internal static object BuildSelectQueryWithOrFilter(
		string rootSchemaName,
		IReadOnlyList<SelectQueryColumnDefinition> columns,
		string filterColumn,
		IReadOnlyList<string> filterValues,
		int dataValueType,
		int rowCount = 10000)
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

		return new
		{
			rootSchemaName,
			operationType = 0,
			allColumns = false,
			isDistinct = false,
			ignoreDisplayValues = false,
			rowCount,
			rowsOffset = -1,
			isPageable = false,
			columns = new
			{
				items = columnItems
			},
			filters = new
			{
				filterType = 6,
				isEnabled = true,
				trimDateTimeParameterToDate = false,
				logicalOperation = 1,
				items = filterItems
			}
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
