using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Clio.Common;
using Clio.Package;
using CommandLine;

namespace Clio.Command;

/// <summary>Options for a single bounded DataService batch.</summary>
[Verb("execute-dataservice-batch", HelpText = "Write explicit records in one native DataService batch")]
public sealed class DataServiceBatchOptions : RemoteCommandOptions {
	/// <summary>JSON file containing an array of explicit operations.</summary>
	[Option("input", Required = true, HelpText = "JSON operations file (1–100 operations, at most 200000 UTF-8 bytes)")]
	public string Input { get; set; }
}

/// <summary>A typed DataService scalar value.</summary>
public sealed record DataServiceBatchValue(
	[property: JsonPropertyName("data-value-type")] int DataValueType,
	[property: JsonPropertyName("value")] JsonElement Value);

/// <summary>One insert, update or delete targeting an explicit record UUID.</summary>
public sealed record DataServiceBatchOperation(
	[property: JsonPropertyName("operation")] string Operation,
	[property: JsonPropertyName("schema-name")] string SchemaName,
	[property: JsonPropertyName("record-id")] Guid RecordId,
	[property: JsonPropertyName("values")] Dictionary<string, DataServiceBatchValue> Values);

/// <summary>Native outcome correlated to a zero-based input index.</summary>
public sealed record DataServiceBatchItemResult(
	[property: JsonPropertyName("index")] int Index,
	[property: JsonPropertyName("record-id")] Guid RecordId,
	[property: JsonPropertyName("state")] string State,
	[property: JsonPropertyName("rows-affected")] int? RowsAffected,
	[property: JsonPropertyName("error")] string Error);

/// <summary>Batch outcomes without an atomicity or independent-readback claim.</summary>
public sealed record DataServiceBatchResult(
	[property: JsonPropertyName("success")] bool Success,
	[property: JsonPropertyName("completed-count")] int CompletedCount,
	[property: JsonPropertyName("failed-count")] int FailedCount,
	[property: JsonPropertyName("unknown-count")] int UnknownCount,
	[property: JsonPropertyName("items")] IReadOnlyList<DataServiceBatchItemResult> Items,
	[property: JsonPropertyName("next-step")] string NextStep);

/// <summary>Validates and submits native batches without replay.</summary>
public interface IDataServiceBatchService {
	/// <summary>Submits 1–100 explicit operations once and correlates native outcomes.</summary>
	DataServiceBatchResult Execute(IReadOnlyList<DataServiceBatchOperation> operations);
}

/// <summary>CLI and MCP command entry point for native batch writes.</summary>
public sealed class DataServiceBatchCommand(IDataServiceBatchService service, IFileSystem files, ILogger logger)
	: Command<DataServiceBatchOptions> {
	/// <summary>Executes an already decoded operation list.</summary>
	public DataServiceBatchResult ExecuteBatch(IReadOnlyList<DataServiceBatchOperation> operations) => service.Execute(operations);
	/// <inheritdoc />
	public override int Execute(DataServiceBatchOptions options) {
		try {
			if (files.GetFileSize(options.Input) > DataServiceBatchService.MaximumBytes) {
				throw new ArgumentException("Input exceeds the 200000-byte limit.");
			}
			string input = files.ReadAllText(options.Input);
			if (Encoding.UTF8.GetByteCount(input) > DataServiceBatchService.MaximumBytes) {
				throw new ArgumentException("Input exceeds the 200000-byte limit.");
			}
			DataServiceBatchResult result = ExecuteBatch(JsonSerializer.Deserialize<DataServiceBatchOperation[]>(input));
			logger.WriteInfo(JsonSerializer.Serialize(result));
			return result.Success ? 0 : 1;
		} catch (Exception exception) {
			logger.WriteError(SensitiveErrorTextRedactor.RedactUntrustedOrNull(exception.Message));
			return 1;
		}
	}
}

/// <summary>Uses native continuation and query IDs to preserve per-record outcomes.</summary>
public sealed class DataServiceBatchService(IApplicationClient client, IServiceUrlBuilder urls) : IDataServiceBatchService {
	internal const int MaximumBytes = 200_000;
	private const string Completed = "completed";
	private const string Failed = "failed";
	private const string Unknown = "unknown";
	private const string InsertOperation = "insert";
	private const string Advice = "One batch was submitted without retry. Continuation is enabled; atomicity is not guaranteed. Results are native acknowledgements, not independent readback. Verify affected records before resubmitting unknown items. Use native sequence enrollment for lifecycle changes.";

	/// <inheritdoc />
	public DataServiceBatchResult Execute(IReadOnlyList<DataServiceBatchOperation> operations) {
		if (operations is null || operations.Count is < 1 or > 100) {
			throw new ArgumentException("Provide 1–100 explicit operations.");
		}
		foreach (DataServiceBatchOperation operation in operations) {
			Validate(operation);
		}
		Guid[] queryIds = operations.Select(_ => Guid.NewGuid()).ToArray();
		JsonArray queries = [];
		for (int index = 0; index < operations.Count; index++) {
			queries.Add(BuildQuery(operations[index], queryIds[index]));
		}
		string body = new JsonObject { ["items"] = queries, ["continueIfError"] = true }.ToJsonString();
		if (Encoding.UTF8.GetByteCount(body) > MaximumBytes) {
			throw new ArgumentException("Encoded batch exceeds the 200000-byte limit.");
		}
		DataServiceBatchItemResult[] results;
		try {
			string response = client.ExecuteNonReplayablePostRequest(urls.Build(ServiceUrlBuilder.KnownRoute.BatchQuery), body, 30_000, 1, 1);
			if (Encoding.UTF8.GetByteCount(response) > MaximumBytes) {
				throw new InvalidOperationException("Batch response exceeded the 200000-byte limit.");
			}
			using JsonDocument document = JsonDocument.Parse(response);
			results = ReadResults(document.RootElement, operations, queryIds);
		} catch (Exception exception) {
			string error = SensitiveErrorTextRedactor.RedactUntrustedOrNull(exception.Message) ?? "Batch response unavailable.";
			results = operations.Select((operation, index) => new DataServiceBatchItemResult(index, operation.RecordId, Unknown, null, error)).ToArray();
		}
		int completed = results.Count(item => item.State == Completed);
		int failed = results.Count(item => item.State == Failed);
		return new(completed == operations.Count, completed, failed, results.Length - completed - failed, results, Advice);
	}

	private static void Validate(DataServiceBatchOperation operation) {
		if (operation is null || operation.RecordId == Guid.Empty || !IsIdentifier(operation.SchemaName)
			|| operation.Operation is not (InsertOperation or "update" or "delete")) {
			throw new ArgumentException("Each operation needs insert/update/delete, a schema-name and a non-empty record-id.");
		}
		int count = operation.Values?.Count ?? 0;
		if (operation.Operation == "delete" ? count != 0 : count is < 1 or > 50) {
			throw new ArgumentException("Insert/update require 1–50 values; delete accepts no values.");
		}
		if ((operation.Values ?? []).Any(pair => !IsIdentifier(pair.Key) || pair.Key.Equals("Id", StringComparison.OrdinalIgnoreCase)
			|| pair.Value is null || !IsScalar(pair.Value))) {
			throw new ArgumentException("Values require column identifiers other than Id and compatible scalar DataService types (0–12).");
		}
	}

	private static bool IsIdentifier(string value) => value is not null && Regex.IsMatch(value, "^[A-Za-z_][A-Za-z0-9_]{0,127}$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
	private static bool IsScalar(DataServiceBatchValue value) {
		if (value.DataValueType is < 0 or > 12) { return false; }
		JsonElement element = value.Value;
		if (element.ValueKind == JsonValueKind.Null) { return true; }
		return value.DataValueType switch {
			0 or 10 => element.ValueKind == JsonValueKind.String && element.TryGetGuid(out _),
			1 or 2 or 3 or 7 or 8 or 9 => element.ValueKind == JsonValueKind.String,
			4 or 11 => element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out _),
			5 or 6 => element.ValueKind == JsonValueKind.Number && element.TryGetDecimal(out _),
			12 => element.ValueKind is JsonValueKind.True or JsonValueKind.False,
			_ => false
		};
	}

	private static JsonNode BuildQuery(DataServiceBatchOperation operation, Guid queryId) {
		string type = operation.Operation switch { InsertOperation => "InsertQuery", "update" => "UpdateQuery", _ => "DeleteQuery" };
		JsonObject query = new() {
			["__type"] = "Terrasoft.Nui.ServiceModel.DataContract." + type,
			["queryId"] = queryId, ["rootSchemaName"] = operation.SchemaName,
			["operationType"] = operation.Operation switch { InsertOperation => 1, "update" => 2, _ => 3 }
		};
		if (operation.Operation != InsertOperation) {
			object select = SelectQueryHelper.BuildSelectQuery(operation.SchemaName, [], [new("Id", operation.RecordId, 0)]);
			query["filters"] = JsonSerializer.SerializeToNode(select)["filters"].DeepClone();
		}
		if (operation.Operation != "delete") {
			JsonObject values = [];
			foreach (var pair in operation.Values) {
				values[pair.Key] = JsonSerializer.SerializeToNode(new { expressionType = 2, parameter = new { dataValueType = pair.Value.DataValueType, value = pair.Value.Value } });
			}
			if (operation.Operation == InsertOperation) {
				values["Id"] = JsonSerializer.SerializeToNode(new { expressionType = 2, parameter = new { dataValueType = 0, value = operation.RecordId } });
			}
			query["columnValues"] = new JsonObject { ["items"] = values };
		}
		return query;
	}

	private static DataServiceBatchItemResult[] ReadResults(JsonElement root, IReadOnlyList<DataServiceBatchOperation> operations, Guid[] ids) {
		JsonElement[] rows = root.GetProperty("queryResults").EnumerateArray().ToArray();
		if (rows.Any(row => !row.TryGetProperty("queryId", out JsonElement id) || !id.TryGetGuid(out Guid parsed) || !ids.Contains(parsed))) {
			throw new InvalidOperationException("Batch response contained an uncorrelated query result.");
		}
		return operations.Select((operation, index) => {
			JsonElement[] matches = rows.Where(row => row.GetProperty("queryId").GetGuid() == ids[index]).ToArray();
			if (matches.Length != 1 || !matches[0].TryGetProperty("success", out JsonElement success)
				|| success.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) {
				return new DataServiceBatchItemResult(index, operation.RecordId, Unknown, null, "Missing, duplicate or malformed native result; verify the record before resubmission.");
			}
			JsonElement match = matches[0];
			int? affected = match.TryGetProperty("rowsAffected", out JsonElement count) && count.ValueKind == JsonValueKind.Number && count.TryGetInt32(out int number) && number >= 0 ? number : null;
			string error = ReadError(match) ?? (success.GetBoolean() ? null : ServerReportedFailureText.NoServerTextCause);
			return new DataServiceBatchItemResult(index, operation.RecordId, success.GetBoolean() ? Completed : Failed, affected, error);
		}).ToArray();
	}

	private static string ReadError(JsonElement match) {
		string error = null;
		if (match.TryGetProperty("responseStatus", out JsonElement status) && status.ValueKind == JsonValueKind.Object
			&& (status.TryGetProperty("Message", out JsonElement message) || status.TryGetProperty("message", out message))
			&& message.ValueKind == JsonValueKind.String) {
			error = SensitiveErrorTextRedactor.RedactUntrustedOrNull(message.GetString());
		}
		if (error is null && match.TryGetProperty("errorInfo", out JsonElement info) && info.ValueKind == JsonValueKind.Object
			&& info.TryGetProperty("message", out JsonElement detail) && detail.ValueKind == JsonValueKind.String) {
			error = SensitiveErrorTextRedactor.RedactUntrustedOrNull(detail.GetString());
		}
		return error;
	}
}
