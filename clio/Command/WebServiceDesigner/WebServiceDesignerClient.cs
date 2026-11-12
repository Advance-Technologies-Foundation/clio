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
	/// <summary>Reads a web service schema through <c>ServiceSchemaRequest</c>, credential values included.</summary>
	/// <param name="uId">The schema UId.</param>
	/// <param name="packageUId">The UId of the package that owns it.</param>
	/// <returns>The raw schema.</returns>
	ServiceSchemaDto ReadSchema(Guid uId, Guid packageUId);

	/// <summary>
	/// Saves a schema through <c>ServiceDataContract</c>. The platform validates nothing here and replaces an
	/// existing schema of the same name, so callers check the name first.
	/// </summary>
	/// <param name="schema">The complete schema to store.</param>
	void SaveSchema(ServiceSchemaDto schema);

	/// <summary>Asks the platform whether no schema of a manager uses the name yet, case-insensitively.</summary>
	/// <param name="managerName">The schema manager.</param>
	/// <param name="schemaName">The name to check.</param>
	/// <returns><c>true</c> when the name is free.</returns>
	bool IsSchemaNameUnique(string managerName, string schemaName);

	/// <summary>Reads rows through DataService with equality filters.</summary>
	/// <param name="rootSchemaName">The entity to read.</param>
	/// <param name="columns">Column paths to return.</param>
	/// <param name="equalFilters">Column path to value; all must match.</param>
	/// <param name="rowCount">The page size, or -1 for all rows.</param>
	/// <param name="rowsOffset">Rows to skip.</param>
	/// <param name="orderColumn">The column to sort by, needed for stable paging.</param>
	/// <returns>One JSON object per row.</returns>
	IReadOnlyList<JsonObject> Select(string rootSchemaName, IReadOnlyList<string> columns,
		IReadOnlyDictionary<string, string> equalFilters, int rowCount = -1, int rowsOffset = 0,
		string orderColumn = null);

	/// <summary>Returns which of the system-setting codes exist, in one DataService read.</summary>
	/// <param name="codes">Codes to look up.</param>
	/// <returns>The codes that exist, as the platform spells them.</returns>
	IReadOnlyCollection<string> FindExistingSysSettingCodes(IReadOnlyCollection<string> codes);

	/// <summary>Calls one method through <c>CallServiceSchemaService.Execute</c>, as Studio's test request does.</summary>
	/// <param name="serviceName">The service code.</param>
	/// <param name="methodName">The method code.</param>
	/// <param name="parameterValues">Values for top-level request parameters, by code.</param>
	/// <returns>The raw outcome, including what the platform reports as sent.</returns>
	ServiceMethodCallResult CallMethod(string serviceName, string methodName,
		IReadOnlyDictionary<string, string> parameterValues);
}

/// <summary>
/// The outcome of a test call. The request fields stay inside clio: they are read only to mask secrets.
/// <see cref="ParameterValues"/> is the platform's JSON text of the parsed response parameters by code.
/// </summary>
public sealed record ServiceMethodCallResult(bool Success, int? StatusCode, string ResponseBody, string Error,
	string RequestRawData = null, string RequestBody = null, string ParameterValues = null);

public sealed class WebServiceDesignerClient(IApplicationClient applicationClient, IServiceUrlBuilder urlBuilder)
	: CreatioServiceClient(applicationClient, urlBuilder), IWebServiceDesignerClient {

	// A save or a page of process metadata can take far longer than a lookup on a loaded stand.
	private static readonly CreatioRequestOptions RequestOptions = new() { TimeOut = 300_000, MaxAttempts = 1 };

	public ServiceSchemaDto ReadSchema(Guid uId, Guid packageUId) {
		SchemaReadResponse response = EnsureSucceeded(PostAndDeserialize<SchemaReadResponse>(
			ServiceUrlBuilder.KnownRoute.ServiceSchemaRead, new { uId, packageUId }, RequestOptions),
			"read the web service");
		return response.Schema ?? throw new InvalidOperationException("The platform returned no web service schema.");
	}

	public void SaveSchema(ServiceSchemaDto schema) {
		EnsureSucceeded(PostAndDeserialize<PlatformResponse>(
			ServiceUrlBuilder.KnownRoute.ServiceSchemaSave, schema, RequestOptions), "save the web service");
	}

	public bool IsSchemaNameUnique(string managerName, string schemaName) {
		UniqueNameResponse response = EnsureSucceeded(PostAndDeserialize<UniqueNameResponse>(
			ServiceUrlBuilder.KnownRoute.CheckUniqueSchemaName,
			new { managerName, schemaName, excludeUId = Guid.Empty }, RequestOptions), "check the web service code");
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
			// The primary key is a Guid column (DataValueType 0); every other filter here is on text.
			int dataValueType = column == "Id" ? 0 : 1;
			filters[column] = new JsonObject {
				["filterType"] = 1, ["comparisonType"] = 3, ["isEnabled"] = true, ["dataValueType"] = dataValueType,
				["leftExpression"] = new JsonObject { ["expressionType"] = 0, ["columnPath"] = column },
				["rightExpression"] = new JsonObject {
					["expressionType"] = 2,
					["parameter"] = new JsonObject { ["dataValueType"] = dataValueType, ["value"] = value }
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
		return Read(query, rootSchemaName);
	}

	public IReadOnlyCollection<string> FindExistingSysSettingCodes(IReadOnlyCollection<string> codes) {
		if (codes is not { Count: > 0 }) {
			return [];
		}
		var query = new JsonObject {
			["rootSchemaName"] = "SysSettings", ["operationType"] = 0, ["allColumns"] = false, ["rowCount"] = -1,
			["columns"] = new JsonObject { ["items"] = new JsonObject {
				["Code"] = new JsonObject { ["expression"] = new JsonObject { ["expressionType"] = 0, ["columnPath"] = "Code" } }
			} },
			["filters"] = new JsonObject {
				["rootSchemaName"] = "SysSettings", ["filterType"] = 6, ["logicalOperation"] = 0, ["isEnabled"] = true,
				["items"] = new JsonObject {
					["codes"] = new JsonObject {
						["filterType"] = 4, ["comparisonType"] = 3, ["isEnabled"] = true,
						["leftExpression"] = new JsonObject { ["expressionType"] = 0, ["columnPath"] = "Code" },
						["rightExpressions"] = new JsonArray(codes.Select(code => (JsonNode)new JsonObject {
							["expressionType"] = 2,
							["parameter"] = new JsonObject { ["dataValueType"] = 1, ["value"] = code }
						}).ToArray())
					}
				}
			}
		};
		return Read(query, "SysSettings").Select(row => row[ColumnAlias("Code")]?.ToString())
			.Where(code => code is not null).ToList();
	}

	/// <summary>
	/// Sends a read and re-sends it while the server answers with a transient failure, the same budget the shared
	/// DataService reads use; a read has no side effect to repeat.
	/// </summary>
	private List<JsonObject> Read(JsonObject query, string rootSchemaName) {
		SelectResponse response = Clio.Package.SelectQueryHelper.SendWithTransientRetry(
			() => PostAndDeserialize<SelectResponse>(ServiceUrlBuilder.KnownRoute.Select, query, RequestOptions),
			answer => answer is { Success: false }
				&& Clio.Package.SelectQueryHelper.IsTransientServerFailure(answer.ErrorInfo?.Message, string.Empty));
		return EnsureSucceeded(response, $"read {rootSchemaName}").Rows?.OfType<JsonObject>().ToList() ?? [];
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
			result.ErrorInfo?.Message, result.RequestRawData, result.RequestBody, result.ParameterValuesText);
	}

	/// <summary>The row key a selected column comes back under: a column path without its dots.</summary>
	public static string ColumnAlias(string columnPath) => columnPath.Replace(".", string.Empty);

	private static T EnsureSucceeded<T>(T response, string action) where T : PlatformResponse {
		if (response is { Success: true }) {
			return response;
		}
		// The platform's errorInfo carries a stack trace and, for a failed save, can echo request values.
		string message = response?.ErrorInfo?.Message;
		throw new InvalidOperationException(string.IsNullOrWhiteSpace(message)
			? $"The platform refused to {action}."
			: $"The platform refused to {action}: {SensitiveErrorTextRedactor.Redact(message)}");
	}

	private sealed class PlatformErrorInfo {
		[JsonPropertyName("message")] public string Message { get; init; }
	}

	private class PlatformResponse {
		[JsonPropertyName("success")] public bool Success { get; init; }
		[JsonPropertyName("errorInfo")] public PlatformErrorInfo ErrorInfo { get; init; }
	}

	private sealed class SchemaReadResponse : PlatformResponse {
		[JsonPropertyName("schema")] public ServiceSchemaDto Schema { get; init; }
	}

	private sealed class UniqueNameResponse : PlatformResponse {
		[JsonPropertyName("value")] public bool Value { get; init; }
	}

	private sealed class SelectResponse : PlatformResponse {
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
		[JsonPropertyName("RequestRawData")] public string RequestRawData { get; init; }
		[JsonPropertyName("RequestBody")] public string RequestBody { get; init; }
		// A JSON text on the stands measured; an object is accepted too, so a platform that sends one does not
		// fail the whole call.
		[JsonPropertyName("ParameterValues")] public JsonElement? ParameterValues { get; init; }

		public string ParameterValuesText => ParameterValues is not JsonElement value
			? null
			: value.ValueKind switch {
				JsonValueKind.String => value.GetString(),
				JsonValueKind.Object or JsonValueKind.Array => value.GetRawText(),
				_ => null
			};
	}
}
