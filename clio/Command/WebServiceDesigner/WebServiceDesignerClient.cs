using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Clio.Common;

namespace Clio.Command.WebServiceDesigner;

/// <summary>
/// Thin client over the platform endpoints the Studio web service designer uses, plus the DataService reads
/// the web service tools need. Every response here is raw: <see cref="ReadSchema"/> returns the service's
/// authentication values, and stripping them is <see cref="WebServiceSchemaView"/>'s job, never the caller's.
/// </summary>
public interface IWebServiceDesignerClient {
	ServiceSchemaDto ReadSchema(Guid uId, Guid packageUId);
	void SaveSchema(ServiceSchemaDto schema);
	bool IsSchemaNameUnique(string managerName, string schemaName);
	IReadOnlyList<JsonObject> Select(string rootSchemaName, IReadOnlyList<string> columns,
		IReadOnlyDictionary<string, string> equalFilters, int rowCount = -1, int rowsOffset = 0,
		string orderColumn = null);
	ServiceMethodCallResult CallMethod(string serviceName, string methodName,
		IReadOnlyDictionary<string, string> parameterValues);
}

/// <summary>What a test call returned; request and response headers are deliberately not modelled.</summary>
public sealed record ServiceMethodCallResult(bool Success, int? StatusCode, string ResponseBody, string Error);

public sealed class WebServiceDesignerClient(IApplicationClient applicationClient, IServiceUrlBuilder urlBuilder)
	: CreatioServiceClient(applicationClient, urlBuilder), IWebServiceDesignerClient {

	// A save or a page of process metadata can take far longer than a lookup on a loaded stand.
	private static readonly CreatioRequestOptions RequestOptions = new() { TimeOut = 300_000, MaxAttempts = 1 };

	public ServiceSchemaDto ReadSchema(Guid uId, Guid packageUId) {
		SchemaReadResponse response = PostAndDeserialize<SchemaReadResponse>(
			ServiceUrlBuilder.KnownRoute.ServiceSchemaRead, new { uId, packageUId }, RequestOptions);
		ThrowIfFailed(response?.Success, response?.ErrorInfo, "read the web service");
		return response.Schema ?? throw new InvalidOperationException("The platform returned no web service schema.");
	}

	public void SaveSchema(ServiceSchemaDto schema) {
		SaveResponse response = PostAndDeserialize<SaveResponse>(
			ServiceUrlBuilder.KnownRoute.ServiceSchemaSave, schema, RequestOptions);
		ThrowIfFailed(response?.Success, response?.ErrorInfo, "save the web service");
	}

	public bool IsSchemaNameUnique(string managerName, string schemaName) {
		UniqueNameResponse response = PostAndDeserialize<UniqueNameResponse>(
			ServiceUrlBuilder.KnownRoute.CheckUniqueSchemaName,
			new { managerName, schemaName, excludeUId = Guid.Empty }, RequestOptions);
		ThrowIfFailed(response?.Success, response?.ErrorInfo, "check the web service code");
		return response.Value;
	}

	public IReadOnlyList<JsonObject> Select(string rootSchemaName, IReadOnlyList<string> columns,
		IReadOnlyDictionary<string, string> equalFilters, int rowCount = -1, int rowsOffset = 0,
		string orderColumn = null) {
		var items = new JsonObject();
		foreach (string column in columns) {
			var item = new JsonObject {
				["expression"] = new JsonObject { ["expressionType"] = 0, ["columnPath"] = column }
			};
			if (column == orderColumn) {
				item["orderDirection"] = 1;
				item["orderPosition"] = 0;
			}
			items[ColumnAlias(column)] = item;
		}
		var filters = new JsonObject();
		foreach ((string column, string value) in equalFilters ?? new Dictionary<string, string>()) {
			filters[column] = new JsonObject {
				["filterType"] = 1, ["comparisonType"] = 3, ["isEnabled"] = true, ["dataValueType"] = 1,
				["leftExpression"] = new JsonObject { ["expressionType"] = 0, ["columnPath"] = column },
				["rightExpression"] = new JsonObject {
					["expressionType"] = 2,
					["parameter"] = new JsonObject { ["dataValueType"] = 1, ["value"] = value }
				}
			};
		}
		var query = new JsonObject {
			["rootSchemaName"] = rootSchemaName,
			["operationType"] = 0,
			["allColumns"] = false,
			["rowCount"] = rowCount,
			["rowsOffset"] = rowsOffset,
			["isPageable"] = rowsOffset > 0 || rowCount > 0,
			["columns"] = new JsonObject { ["items"] = items },
			["filters"] = new JsonObject {
				["rootSchemaName"] = rootSchemaName, ["filterType"] = 6, ["logicalOperation"] = 0,
				["isEnabled"] = true, ["items"] = filters
			}
		};
		SelectResponse response = PostAndDeserialize<SelectResponse>(
			ServiceUrlBuilder.KnownRoute.Select, query, RequestOptions);
		ThrowIfFailed(response?.Success, response?.ErrorInfo, $"read {rootSchemaName}");
		return response.Rows?.OfType<JsonObject>().ToList() ?? [];
	}

	public ServiceMethodCallResult CallMethod(string serviceName, string methodName,
		IReadOnlyDictionary<string, string> parameterValues) {
		var parameters = (parameterValues ?? new Dictionary<string, string>())
			.Select(pair => new { code = pair.Key, value = pair.Value })
			.ToList();
		CallMethodEnvelope envelope = PostAndDeserialize<CallMethodEnvelope>(
			ServiceUrlBuilder.KnownRoute.CallServiceSchemaMethod,
			new { serviceName, methodName, parameters }, RequestOptions);
		CallMethodResponse result = envelope?.Result
			?? throw new InvalidOperationException("The test call returned no result.");
		return new ServiceMethodCallResult(result.Success, result.StatusCode, result.ResponseBody,
			result.ErrorInfo?.Message);
	}

	/// <summary>The row key a selected column comes back under: a column path without its dots.</summary>
	public static string ColumnAlias(string columnPath) => columnPath.Replace(".", string.Empty);

	private static void ThrowIfFailed(bool? success, PlatformErrorInfo errorInfo, string action) {
		if (success == true) {
			return;
		}
		// The platform's errorInfo carries a stack trace and, for a failed save, can echo request values.
		string message = errorInfo?.Message;
		throw new InvalidOperationException(string.IsNullOrWhiteSpace(message)
			? $"The platform refused to {action}."
			: $"The platform refused to {action}: {SensitiveErrorTextRedactor.Redact(message)}");
	}

	private sealed class PlatformErrorInfo {
		[JsonPropertyName("message")] public string Message { get; init; }
	}

	private sealed class SchemaReadResponse {
		[JsonPropertyName("success")] public bool Success { get; init; }
		[JsonPropertyName("errorInfo")] public PlatformErrorInfo ErrorInfo { get; init; }
		[JsonPropertyName("schema")] public ServiceSchemaDto Schema { get; init; }
	}

	private sealed class SaveResponse {
		[JsonPropertyName("success")] public bool Success { get; init; }
		[JsonPropertyName("errorInfo")] public PlatformErrorInfo ErrorInfo { get; init; }
	}

	private sealed class UniqueNameResponse {
		[JsonPropertyName("success")] public bool Success { get; init; }
		[JsonPropertyName("errorInfo")] public PlatformErrorInfo ErrorInfo { get; init; }
		[JsonPropertyName("value")] public bool Value { get; init; }
	}

	private sealed class SelectResponse {
		[JsonPropertyName("success")] public bool Success { get; init; }
		[JsonPropertyName("errorInfo")] public PlatformErrorInfo ErrorInfo { get; init; }
		[JsonPropertyName("rows")] public JsonArray Rows { get; init; }
	}

	private sealed class CallMethodEnvelope {
		[JsonPropertyName("ExecuteResult")] public CallMethodResponse Result { get; init; }
	}

	private sealed class CallMethodResponse {
		[JsonPropertyName("success")] public bool Success { get; init; }
		[JsonPropertyName("errorInfo")] public PlatformErrorInfo ErrorInfo { get; init; }
		[JsonPropertyName("StatusCode")] public int? StatusCode { get; init; }
		[JsonPropertyName("ResponseBody")] public string ResponseBody { get; init; }
	}
}
