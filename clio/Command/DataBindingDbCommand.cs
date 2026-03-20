using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Clio.Command.ProcessModel;
using Clio.Common;
using Clio.Package;
using CommandLine;

namespace Clio.Command;

/// <summary>
/// Options for the <c>create-data-binding-db</c> command.
/// </summary>
[Verb("create-data-binding-db", HelpText = "Create a DB-first package data binding by saving data directly to the remote Creatio database")]
public class CreateDataBindingDbOptions : EnvironmentOptions {
	[Option("environment", Required = false, HelpText = "Environment name")]
	public string? EnvironmentAlias {
		get => Environment;
		set => Environment = value;
	}

	[Option("package", Required = true, HelpText = "Target package name")]
	public string PackageName { get; set; } = string.Empty;

	[Option("schema", Required = true, HelpText = "Entity schema name")]
	public string SchemaName { get; set; } = string.Empty;

	[Option("binding-name", Required = false, HelpText = "Binding folder name; defaults to <schema>")]
	public string? BindingName { get; set; }

	[Option("rows", Required = false, HelpText = "JSON array of row objects, each with a 'values' key containing column values")]
	public string? RowsJson { get; set; }
}

/// <summary>
/// Options for the <c>upsert-data-binding-row-db</c> command.
/// </summary>
[Verb("upsert-data-binding-row-db", HelpText = "Upsert a single row in a remote DB-first data binding")]
public class UpsertDataBindingRowDbOptions : EnvironmentOptions {
	[Option("environment", Required = false, HelpText = "Environment name")]
	public string? EnvironmentAlias {
		get => Environment;
		set => Environment = value;
	}

	[Option("package", Required = true, HelpText = "Target package name")]
	public string PackageName { get; set; } = string.Empty;

	[Option("binding-name", Required = true, HelpText = "Binding folder name")]
	public string BindingName { get; set; } = string.Empty;

	[Option("values", Required = true, HelpText = "Row values as JSON object keyed by column name")]
	public string ValuesJson { get; set; } = string.Empty;
}

/// <summary>
/// Options for the <c>remove-data-binding-row-db</c> command.
/// </summary>
[Verb("remove-data-binding-row-db", HelpText = "Remove a row from a remote DB-first data binding and delete the package schema data record when no bound rows remain")]
public class RemoveDataBindingRowDbOptions : EnvironmentOptions {
	[Option("environment", Required = false, HelpText = "Environment name")]
	public string? EnvironmentAlias {
		get => Environment;
		set => Environment = value;
	}

	[Option("package", Required = true, HelpText = "Target package name")]
	public string PackageName { get; set; } = string.Empty;

	[Option("binding-name", Required = true, HelpText = "Binding folder name")]
	public string BindingName { get; set; } = string.Empty;

	[Option("key-value", Required = true, HelpText = "Primary-key value of the row to remove")]
	public string KeyValue { get; set; } = string.Empty;
}

/// <summary>
/// Creates a DB-first package data binding by persisting data to the remote Creatio database.
/// </summary>
public class CreateDataBindingDbCommand(IDataBindingDbService dataBindingDbService, ILogger logger)
	: Command<CreateDataBindingDbOptions> {
	/// <inheritdoc />
	public override int Execute(CreateDataBindingDbOptions options) {
		try {
			dataBindingDbService.CreateBinding(options);
			logger.WriteInfo("Done");
			return 0;
		}
		catch (Exception exception) {
			logger.WriteError(exception.Message);
			return 1;
		}
	}
}

/// <summary>
/// Upserts a single row in a remote DB-first data binding.
/// </summary>
public class UpsertDataBindingRowDbCommand(IDataBindingDbService dataBindingDbService, ILogger logger)
	: Command<UpsertDataBindingRowDbOptions> {
	/// <inheritdoc />
	public override int Execute(UpsertDataBindingRowDbOptions options) {
		try {
			dataBindingDbService.UpsertRow(options);
			logger.WriteInfo("Done");
			return 0;
		}
		catch (Exception exception) {
			logger.WriteError(exception.Message);
			return 1;
		}
	}
}

/// <summary>
/// Removes a row from a remote DB-first data binding, and deletes the package schema data record when no
/// bound rows remain.
/// </summary>
public class RemoveDataBindingRowDbCommand(IDataBindingDbService dataBindingDbService, ILogger logger)
	: Command<RemoveDataBindingRowDbOptions> {
	/// <inheritdoc />
	public override int Execute(RemoveDataBindingRowDbOptions options) {
		try {
			dataBindingDbService.RemoveRow(options);
			logger.WriteInfo("Done");
			return 0;
		}
		catch (Exception exception) {
			logger.WriteError(exception.Message);
			return 1;
		}
	}
}

/// <summary>
/// Shared DB-first data-binding service used by the CLI commands and MCP tools.
/// </summary>
public interface IDataBindingDbService {
	/// <summary>
	/// Creates a remote DB-first binding for the specified schema.
	/// </summary>
	void CreateBinding(CreateDataBindingDbOptions options);

	/// <summary>
	/// Upserts a single row in a remote DB-first binding.
	/// </summary>
	void UpsertRow(UpsertDataBindingRowDbOptions options);

	/// <summary>
	/// Removes a row from a remote DB-first binding and deletes the package schema data record when empty.
	/// </summary>
	void RemoveRow(RemoveDataBindingRowDbOptions options);
}

internal sealed class DataBindingDbService(
	IApplicationClient applicationClient,
	IServiceUrlBuilder serviceUrlBuilder,
	IApplicationPackageListProvider packageListProvider,
	IDataBindingSchemaClient schemaClient,
	EnvironmentSettings environmentSettings) : IDataBindingDbService {

	public void CreateBinding(CreateDataBindingDbOptions options) {
		ArgumentNullException.ThrowIfNull(options);
		ValidateEnvironment(options);

		PackageRef packageRef = ResolvePackageRef(options.PackageName);
		string bindingName = string.IsNullOrWhiteSpace(options.BindingName)
			? options.SchemaName
			: options.BindingName.Trim();

		DataBindingDbSchema schema = FetchSchema(options.SchemaName);
		List<Dictionary<string, JsonNode?>>? rows = ParseRowsJson(options.RowsJson);

		List<string> boundRecordIds = [];
		if (rows is { Count: > 0 }) {
			foreach (Dictionary<string, JsonNode?> row in rows) {
				string rowId = EnsureRowId(row);
				InsertEntityRow(options.SchemaName, row, schema.SchemaColumns);
				boundRecordIds.Add(rowId);
			}
		}

		string requestBody = BuildSaveSchemaDataRequest(
			packageRef, bindingName, options.SchemaName, schema, boundRecordIds);
		string response = applicationClient.ExecutePostRequest(
			serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.SaveSchemaData),
			requestBody);
		ThrowIfUnsuccessful(response, "SaveSchema");
	}

	public void UpsertRow(UpsertDataBindingRowDbOptions options) {
		ArgumentNullException.ThrowIfNull(options);
		ValidateEnvironment(options);

		PackageRef packageRef = ResolvePackageRef(options.PackageName);
		(string entitySchemaName, Guid bindingUId) = LookupBindingInfo(packageRef.UId, options.BindingName);
		DataBindingDbSchema schema = FetchSchema(entitySchemaName);
		Dictionary<string, JsonNode?> values = ParseValues(options.ValuesJson);

		string rowId = EnsureRowId(values);
		InsertEntityRow(entitySchemaName, values, schema.SchemaColumns);

		List<string> existingIds = FetchExistingBoundRecordIds(bindingUId);
		if (!existingIds.Contains(rowId, StringComparer.OrdinalIgnoreCase)) {
			existingIds.Add(rowId);
		}

		string requestBody = BuildSaveSchemaDataRequest(
			packageRef, options.BindingName, schema.SchemaName, schema,
			existingIds,
			bindingUId != Guid.Empty ? bindingUId : null);
		string response = applicationClient.ExecutePostRequest(
			serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.SaveSchemaData),
			requestBody);
		ThrowIfUnsuccessful(response, "SaveSchema");
	}

	public void RemoveRow(RemoveDataBindingRowDbOptions options) {
		ArgumentNullException.ThrowIfNull(options);
		ValidateEnvironment(options);

		PackageRef packageRef = ResolvePackageRef(options.PackageName);
		(string entitySchemaName, Guid bindingUId) = LookupBindingInfo(packageRef.UId, options.BindingName);
		List<string> boundIds = FetchExistingBoundRecordIds(bindingUId);

		if (!boundIds.Contains(options.KeyValue, StringComparer.OrdinalIgnoreCase)) {
			throw new InvalidOperationException(
				$"Row with key '{options.KeyValue}' was not found in binding '{options.BindingName}'.");
		}

		DeleteEntityRow(entitySchemaName, options.KeyValue);
		boundIds.RemoveAll(id => string.Equals(id, options.KeyValue, StringComparison.OrdinalIgnoreCase));

		if (boundIds.Count == 0) {
			DeletePackageSchemaData(packageRef.UId, options.BindingName);
		}
		else {
			DataBindingDbSchema schema = FetchSchema(entitySchemaName);
			string requestBody = BuildSaveSchemaDataRequest(
				packageRef, options.BindingName, entitySchemaName, schema,
				boundIds,
				bindingUId != Guid.Empty ? bindingUId : null);
			string response = applicationClient.ExecutePostRequest(
				serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.SaveSchemaData),
				requestBody);
			ThrowIfUnsuccessful(response, "SaveSchema");
		}
	}

	private void ValidateEnvironment(EnvironmentOptions options) {
		if (string.IsNullOrWhiteSpace(options.Environment) && string.IsNullOrWhiteSpace(options.Uri)) {
			throw new InvalidOperationException("--environment or --uri is required.");
		}
	}

	private PackageRef ResolvePackageRef(string packageName) {
		PackageInfo? package = packageListProvider.GetPackages()
			.FirstOrDefault(pkg =>
				string.Equals(pkg.Descriptor.Name, packageName, StringComparison.OrdinalIgnoreCase));
		if (package is null) {
			throw new InvalidOperationException($"Package '{packageName}' was not found in the remote environment.");
		}

		return new PackageRef(package.Descriptor.UId, package.Descriptor.Name);
	}

	private DataBindingDbSchema FetchSchema(string schemaName) {
		DataBindingSchema schema = schemaClient.Fetch(schemaName);
		return new DataBindingDbSchema(
			schema.UId,
			schema.Name,
			schema.Columns.Select(c => c.Name).ToList(),
			schema.Columns);
	}

	private static string BuildSaveSchemaDataRequest(
		PackageRef packageRef,
		string bindingName,
		string entitySchemaName,
		DataBindingDbSchema schema,
		List<string> boundRecordIds,
		Guid? existingBindingUId = null) {
		string schemaDataUId = (existingBindingUId ?? Guid.NewGuid()).ToString();

		var columnsArray = schema.SchemaColumns.Select(col => new {
			id = Guid.NewGuid().ToString(),
			uId = col.UId.ToString(),
			isForceUpdate = false,
			isKey = string.Equals(col.Name, "Id", StringComparison.OrdinalIgnoreCase),
			name = col.Name,
			caption = col.Name,
			dataValueTypeUId = DataValueTypeMap.FromRuntimeValueType(col.DataValueType).ToString()
		}).ToArray();

		var payload = new {
			uId = schemaDataUId,
			name = bindingName,
			package = new {
				uId = packageRef.UId.ToString(),
				name = packageRef.Name
			},
			entitySchemaUId = schema.EntitySchemaUId.ToString(),
			entitySchemaName,
			installType = 0,
			columns = columnsArray,
			boundRecordIds = boundRecordIds.ToArray()
		};

		return JsonSerializer.Serialize(payload);
	}

	private static string BuildDeleteQueryBody(string rootSchemaName, string keyValue) {
		return $$"""
			{
			  "__type":"Terrasoft.Nui.ServiceModel.DataContract.DeleteQuery",
			  "rootSchemaName":"{{rootSchemaName}}",
			  "filters":{
			    "isEnabled":true,
			    "filterType":6,
			    "logicalOperation":0,
			    "trimDateTimeParameterToDate":false,
			    "items":{
			      "primaryFilter":{
			        "filterType":1,
			        "comparisonType":3,
			        "isEnabled":true,
			        "trimDateTimeParameterToDate":false,
			        "leftExpression":{
			          "expressionType":0,
			          "columnPath":"Id"
			        },
			        "rightExpression":{
			          "expressionType":2,
			          "parameter":{
			            "dataValueType":0,
			            "value":"{{keyValue}}"
			          }
			        }
			      }
			    }
			  }
			}
			""";
	}

	private static string BuildDeletePackageSchemaDataBody(Guid packageUId, string packageSchemaDataName) =>
		$$"""{"packageUId":"{{packageUId}}","packageSchemaDataName":"{{packageSchemaDataName}}"}""";

	private static string BuildLookupBindingRequestBody(Guid packageUId, string bindingName) {
		return $$"""
			{
			  "rootSchemaName":"SysPackageSchemaData",
			  "columns":{
			    "items":{
			      "Id":{
			        "expression":{
			          "expressionType":0,
			          "columnPath":"Id"
			        }
			      },
			      "UId":{
			        "expression":{
			          "expressionType":0,
			          "columnPath":"UId"
			        }
			      },
			      "Name":{
			        "expression":{
			          "expressionType":0,
			          "columnPath":"Name"
			        }
			      },
			      "EntitySchemaName":{
			        "expression":{
			          "expressionType":0,
			          "columnPath":"SysSchema.Name"
			        }
			      }
			    }
			  },
			  "filters":{
			    "filterType":6,
			    "isEnabled":true,
			    "trimDateTimeParameterToDate":false,
			    "logicalOperation":0,
			    "items":{
			      "byName":{
			        "filterType":1,
			        "comparisonType":3,
			        "isEnabled":true,
			        "trimDateTimeParameterToDate":false,
			        "leftExpression":{
			          "expressionType":0,
			          "columnPath":"Name"
			        },
			        "rightExpression":{
			          "expressionType":2,
			          "parameter":{
			            "dataValueType":28,
			            "value":"{{bindingName}}"
			          }
			        }
			      },
			      "byPackage":{
			        "filterType":1,
			        "comparisonType":3,
			        "isEnabled":true,
			        "trimDateTimeParameterToDate":false,
			        "leftExpression":{
			          "expressionType":0,
			          "columnPath":"SysPackage.UId"
			        },
			        "rightExpression":{
			          "expressionType":2,
			          "parameter":{
			            "dataValueType":0,
			            "value":"{{packageUId}}"
			          }
			        }
			      }
			    }
			  }
			}
			""";
	}

	private static string BuildGetBoundSchemaDataBody(Guid bindingUId) =>
		$$"""{"uId":"{{bindingUId}}"}""";

	private (string EntitySchemaName, Guid BindingUId) LookupBindingInfo(Guid packageUId, string bindingName) {
		string response = applicationClient.ExecutePostRequest(
			serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.Select),
			BuildLookupBindingRequestBody(packageUId, bindingName));

		using JsonDocument document = JsonDocument.Parse(response);
		if (!document.RootElement.TryGetProperty("rows", out JsonElement rows) ||
			rows.ValueKind != JsonValueKind.Array ||
			rows.GetArrayLength() == 0) {
			throw new InvalidOperationException(
				$"Binding '{bindingName}' was not found in the remote environment.");
		}

		JsonElement firstRow = rows[0];
		string entitySchemaName = firstRow.TryGetProperty("EntitySchemaName", out JsonElement schemaNameElement)
			? schemaNameElement.GetString() ?? bindingName
			: bindingName;

		Guid bindingUId = firstRow.TryGetProperty("UId", out JsonElement uidElement)
			&& Guid.TryParse(uidElement.GetString(), out Guid parsed)
			? parsed
			: Guid.Empty;

		return (entitySchemaName, bindingUId);
	}

	private List<Dictionary<string, JsonNode?>> FetchBoundRows(Guid bindingUId) {
		string response = applicationClient.ExecutePostRequest(
			serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.GetBoundSchemaData),
			BuildGetBoundSchemaDataBody(bindingUId));

		using JsonDocument document = JsonDocument.Parse(response);
		if (!document.RootElement.TryGetProperty("items", out JsonElement itemsElement)) {
			return [];
		}

		// items may be a JSON-encoded string (double-encoded) or an inline array/object
		List<Dictionary<string, JsonNode?>> result = [];
		if (itemsElement.ValueKind == JsonValueKind.String) {
			string? itemsJson = itemsElement.GetString();
			if (string.IsNullOrWhiteSpace(itemsJson)) {
				return result;
			}

			using JsonDocument itemsDocument = JsonDocument.Parse(itemsJson);
			ParseBoundRowsFromArray(itemsDocument.RootElement, result);
		}
		else if (itemsElement.ValueKind == JsonValueKind.Array) {
			ParseBoundRowsFromArray(itemsElement, result);
		}

		return result;
	}

	private static void ParseBoundRowsFromArray(
		JsonElement arrayElement,
		List<Dictionary<string, JsonNode?>> result) {
		if (arrayElement.ValueKind != JsonValueKind.Array) {
			return;
		}

		foreach (JsonElement item in arrayElement.EnumerateArray()) {
			Dictionary<string, JsonNode?> row = [];
			foreach (JsonProperty property in item.EnumerateObject()) {
				row[property.Name] = JsonNode.Parse(property.Value.GetRawText());
			}

			result.Add(row);
		}
	}

	private void DeleteEntityRow(string schemaName, string keyValue) {
		string response = applicationClient.ExecutePostRequest(
			serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.Delete),
			BuildDeleteQueryBody(schemaName, keyValue));
		ThrowIfUnsuccessful(response, "DeleteQuery");
	}

	private void InsertEntityRow(
		string rootSchemaName,
		Dictionary<string, JsonNode?> values,
		IReadOnlyList<DataBindingSchemaColumn> schemaColumns) {
		var columnItems = new Dictionary<string, object>();
		foreach (KeyValuePair<string, JsonNode?> kv in values) {
			DataBindingSchemaColumn? col = schemaColumns
				.FirstOrDefault(c => string.Equals(c.Name, kv.Key, StringComparison.OrdinalIgnoreCase));
			int dataValueType = col?.DataValueType ?? 1;
			// InsertQuery parameter dataValueType: normalize text subtypes to 1 (Text)
			if (dataValueType is 28 or 26 or 27 or 29 or 30) {
				dataValueType = 1;
			}

			columnItems[kv.Key] = new {
				expressionType = 2,
				parameter = new {
					dataValueType,
					value = kv.Value?.ToString() ?? ""
				}
			};
		}

		var body = new {
			rootSchemaName,
			columnValues = new { items = columnItems }
		};

		string response = applicationClient.ExecutePostRequest(
			serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.Insert),
			JsonSerializer.Serialize(body));
		ThrowIfUnsuccessful(response, "InsertQuery");
	}

	private static string EnsureRowId(Dictionary<string, JsonNode?> values) {
		if (values.TryGetValue("Id", out JsonNode? idNode) && idNode is not null) {
			string? existing = idNode.ToString();
			if (!string.IsNullOrWhiteSpace(existing)) {
				return existing;
			}
		}

		string newId = Guid.NewGuid().ToString();
		values["Id"] = JsonValue.Create(newId);
		return newId;
	}

	private List<string> FetchExistingBoundRecordIds(Guid bindingUId) {
		List<Dictionary<string, JsonNode?>> boundRows = FetchBoundRows(bindingUId);
		List<string> ids = [];
		foreach (Dictionary<string, JsonNode?> row in boundRows) {
			if (row.TryGetValue("Id", out JsonNode? idNode) && idNode is not null) {
				string? id = idNode.GetValue<string>();
				if (!string.IsNullOrWhiteSpace(id)) {
					ids.Add(id);
				}
			}
		}

		return ids;
	}

	private void DeletePackageSchemaData(Guid packageUId, string bindingName) {
		string response = applicationClient.ExecutePostRequest(
			serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.DeletePackageSchemaData),
			BuildDeletePackageSchemaDataBody(packageUId, bindingName));
		ThrowIfUnsuccessful(response, "DeletePackageSchemaDataRequest");
	}

	private static void ThrowIfUnsuccessful(string response, string operationName) {
		if (string.IsNullOrWhiteSpace(response)) {
			return;
		}

		try {
			using JsonDocument document = JsonDocument.Parse(response);
			if (document.RootElement.TryGetProperty("success", out JsonElement successElement) &&
				!successElement.GetBoolean()) {
				string errorMessage = "Unknown error";
				if (document.RootElement.TryGetProperty("errorInfo", out JsonElement errorInfo) &&
					errorInfo.TryGetProperty("message", out JsonElement messageElement)) {
					errorMessage = messageElement.GetString() ?? errorMessage;
				}
				else if (document.RootElement.TryGetProperty("responseStatus", out JsonElement responseStatus) &&
						 responseStatus.TryGetProperty("Message", out JsonElement rsMessage)) {
					errorMessage = rsMessage.GetString() ?? errorMessage;
				}

				throw new InvalidOperationException(
					$"{operationName} failed: {errorMessage}");
			}
		}
		catch (JsonException) {
			// Response is not JSON — nothing to validate
		}
	}

	private static List<Dictionary<string, JsonNode?>>? ParseRowsJson(string? json) {
		if (string.IsNullOrWhiteSpace(json)) {
			return null;
		}

		JsonNode? node;
		try {
			node = JsonNode.Parse(json);
		}
		catch (JsonException exception) {
			throw new InvalidOperationException($"--rows must contain valid JSON. {exception.Message}");
		}

		if (node is not JsonArray array) {
			throw new InvalidOperationException("--rows must be a JSON array.");
		}

		List<Dictionary<string, JsonNode?>> result = [];
		foreach (JsonNode? item in array) {
			if (item is JsonObject rowObj &&
				rowObj.TryGetPropertyValue("values", out JsonNode? valuesNode) &&
				valuesNode is JsonObject valuesObj) {
				result.Add(valuesObj.ToDictionary(kv => kv.Key, kv => kv.Value));
			}
		}

		return result;
	}

	private static Dictionary<string, JsonNode?> ParseValues(string json) {
		if (string.IsNullOrWhiteSpace(json)) {
			throw new InvalidOperationException("--values is required.");
		}

		JsonNode? node;
		try {
			node = JsonNode.Parse(json);
		}
		catch (JsonException exception) {
			throw new InvalidOperationException($"--values must contain valid JSON. {exception.Message}");
		}

		if (node is not JsonObject jsonObject) {
			throw new InvalidOperationException("--values must be a JSON object keyed by column name.");
		}

		return jsonObject.ToDictionary(kv => kv.Key, kv => kv.Value);
	}
}

/// <summary>
/// Minimal schema descriptor used by the DB-first binding service.
/// </summary>
internal sealed record DataBindingDbSchema(
	Guid EntitySchemaUId,
	string SchemaName,
	IReadOnlyList<string> ColumnNames,
	IReadOnlyList<DataBindingSchemaColumn> SchemaColumns);

/// <summary>
/// Holds resolved package identity — UId and Name — needed by the SaveSchema endpoint.
/// </summary>
internal sealed record PackageRef(Guid UId, string Name);
