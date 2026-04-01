using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
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
			DataBindingResult result = dataBindingDbService.CreateBinding(options);
			foreach (DataBindingCreatedRow row in result.CreatedRows) {
				string valuesPreview = string.Join(", ",
					row.Values
						.Where(kv => !string.Equals(kv.Key, "Id", StringComparison.OrdinalIgnoreCase))
						.Select(kv => $"{kv.Key}={kv.Value}"));
				logger.WriteInfo($"Created row: {row.Id} ({valuesPreview})");
			}
			foreach (DataBindingCreatedRow row in result.SkippedRows) {
				string valuesPreview = string.Join(", ",
					row.Values
						.Where(kv => !string.Equals(kv.Key, "Id", StringComparison.OrdinalIgnoreCase))
						.Select(kv => $"{kv.Key}={kv.Value}"));
				logger.WriteInfo($"Skipped existing row: {row.Id} ({valuesPreview})");
			}
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
	DataBindingResult CreateBinding(CreateDataBindingDbOptions options);

	/// <summary>
	/// Upserts a single row in a remote DB-first binding.
	/// </summary>
	void UpsertRow(UpsertDataBindingRowDbOptions options);

	/// <summary>
	/// Removes a row from a remote DB-first binding and deletes the package schema data record when empty.
	/// </summary>
	void RemoveRow(RemoveDataBindingRowDbOptions options);
}

internal interface ILookupRegistrationService {
	void EnsureLookupRegistration(string packageName, string lookupSchemaName, string lookupTitle);
}

internal sealed class LookupRegistrationService(
	IApplicationClient applicationClient,
	IServiceUrlBuilder serviceUrlBuilder,
	IApplicationPackageListProvider packageListProvider,
	IDataBindingSchemaClient schemaClient,
	ILogger logger) : ILookupRegistrationService {
	private const string LookupSectionSchemaName = "Lookup";
	private static readonly HashSet<string> SkippedLookupBindingColumns = new(StringComparer.OrdinalIgnoreCase) {
		"CreatedBy",
		"ModifiedBy"
	};

	public void EnsureLookupRegistration(string packageName, string lookupSchemaName, string lookupTitle) {
		if (string.IsNullOrWhiteSpace(packageName)) {
			throw new InvalidOperationException("Package name is required for lookup registration.");
		}
		if (string.IsNullOrWhiteSpace(lookupSchemaName)) {
			throw new InvalidOperationException("Lookup schema name is required for lookup registration.");
		}

		string resolvedLookupTitle = string.IsNullOrWhiteSpace(lookupTitle)
			? lookupSchemaName
			: lookupTitle.Trim();
		PackageRef packageRef = ResolvePackageRef(packageName);
		DataBindingDbSchema lookupBindingSchema = BuildLookupBindingSchema(FetchSchema(LookupSectionSchemaName));
		DataBindingDbSchema registeredLookupSchema = FetchSchema(lookupSchemaName);
		LookupRegistrationRow? lookupRegistrationRow = FindLookupRegistrationRow(registeredLookupSchema.EntitySchemaUId);
		string lookupRegistrationRowId = EnsureLookupRegistrationRow(
			lookupBindingSchema.SchemaColumns,
			registeredLookupSchema.EntitySchemaUId,
			resolvedLookupTitle,
			lookupRegistrationRow);
		string bindingName = BuildBindingName(lookupSchemaName);
		PackageSchemaDataBinding? existingBinding = FindPackageSchemaDataBinding(packageRef.UId, bindingName);
		if (existingBinding is not null &&
			!string.Equals(existingBinding.EntitySchemaName, LookupSectionSchemaName, StringComparison.OrdinalIgnoreCase)) {
			throw new InvalidOperationException(
				$"Package schema data '{bindingName}' already exists for schema '{existingBinding.EntitySchemaName}'.");
		}

		string requestBody = DataBindingDbService.BuildSaveSchemaDataRequest(
			packageRef,
			bindingName,
			LookupSectionSchemaName,
			lookupBindingSchema,
			[lookupRegistrationRowId],
			existingBinding?.UId);
		string response = applicationClient.ExecutePostRequest(
			serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.SaveSchemaData),
			requestBody);
		DataBindingDbService.ThrowIfUnsuccessful(response, "SaveSchema");
		logger.WriteInfo($"Lookup '{lookupSchemaName}' registered in Lookups.");
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

	private LookupRegistrationRow? FindLookupRegistrationRow(Guid lookupSchemaUId) {
		LookupRegistrationSelectResponse response = SelectQueryHelper.ExecuteSelectQuery<LookupRegistrationSelectResponse>(
			applicationClient,
			serviceUrlBuilder,
			SelectQueryHelper.BuildSelectQuery(
				LookupSectionSchemaName,
				[
					new SelectQueryHelper.SelectQueryColumnDefinition("Id", "Id"),
					new SelectQueryHelper.SelectQueryColumnDefinition("Name", "Name")
				],
				[
					new SelectQueryHelper.SelectQueryFilterDefinition(
						"SysEntitySchemaUId",
						lookupSchemaUId.ToString(),
						SelectQueryHelper.GuidDataValueType)
				]));
		if (response.Rows.Count > 1) {
			throw new InvalidOperationException(
				$"Lookup '{lookupSchemaUId}' already has multiple registrations in Lookup.");
		}

		LookupRegistrationRowDto? row = response.Rows.SingleOrDefault();
		if (row is null) {
			return null;
		}
		if (!Guid.TryParse(row.Id, out Guid parsedRowId)) {
			throw new InvalidOperationException(
				$"Lookup registration row for schema '{lookupSchemaUId}' returned an invalid Id.");
		}

		return new LookupRegistrationRow(parsedRowId, row.Name ?? string.Empty);
	}

	private PackageSchemaDataBinding? FindPackageSchemaDataBinding(Guid packageUId, string bindingName) {
		PackageSchemaDataSelectResponse response =
			SelectQueryHelper.ExecuteSelectQuery<PackageSchemaDataSelectResponse>(
				applicationClient,
				serviceUrlBuilder,
				SelectQueryHelper.BuildSelectQuery(
					"SysPackageSchemaData",
					[
						new SelectQueryHelper.SelectQueryColumnDefinition("UId", "UId"),
						new SelectQueryHelper.SelectQueryColumnDefinition("SysSchema.Name", "EntitySchemaName")
					],
					[
						new SelectQueryHelper.SelectQueryFilterDefinition(
							"Name",
							bindingName,
							SelectQueryHelper.TextDataValueType),
						new SelectQueryHelper.SelectQueryFilterDefinition(
							"SysPackage.UId",
							packageUId.ToString(),
							SelectQueryHelper.GuidDataValueType)
					]));
		if (response.Rows.Count > 1) {
			throw new InvalidOperationException(
				$"Package schema data '{bindingName}' already has multiple registrations in package '{packageUId}'.");
		}

		PackageSchemaDataBindingDto? row = response.Rows.SingleOrDefault();
		if (row is null) {
			return null;
		}
		if (!Guid.TryParse(row.UId, out Guid parsedUId)) {
			throw new InvalidOperationException(
				$"Package schema data '{bindingName}' returned an invalid UId.");
		}

		return new PackageSchemaDataBinding(parsedUId, row.EntitySchemaName ?? string.Empty);
	}

	private string EnsureLookupRegistrationRow(
		IReadOnlyList<DataBindingSchemaColumn> lookupSchemaColumns,
		Guid lookupSchemaUId,
		string lookupTitle,
		LookupRegistrationRow? existingRow) {
		if (existingRow is null) {
			string rowId = Guid.NewGuid().ToString();
			string response = applicationClient.ExecutePostRequest(
				serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.Insert),
				BuildLookupInsertBody(rowId, lookupTitle, lookupSchemaUId, lookupSchemaColumns));
			DataBindingDbService.ThrowIfUnsuccessful(response, "InsertQuery");
			return rowId;
		}
		if (!string.Equals(existingRow.Name, lookupTitle, StringComparison.Ordinal)) {
			string response = applicationClient.ExecutePostRequest(
				serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.Update),
				BuildLookupUpdateBody(existingRow.RowId.ToString(), lookupTitle, lookupSchemaColumns));
			DataBindingDbService.ThrowIfUnsuccessful(response, "UpdateQuery");
		}

		return existingRow.RowId.ToString();
	}

	private static DataBindingDbSchema BuildLookupBindingSchema(DataBindingDbSchema schema) {
		List<DataBindingSchemaColumn> bindingColumns = schema.SchemaColumns
			.Where(column => !SkippedLookupBindingColumns.Contains(column.Name))
			.ToList();
		return new DataBindingDbSchema(
			schema.EntitySchemaUId,
			schema.SchemaName,
			bindingColumns.Select(column => column.Name).ToList(),
			bindingColumns);
	}

	private static string BuildLookupInsertBody(
		string rowId,
		string lookupTitle,
		Guid lookupSchemaUId,
		IReadOnlyList<DataBindingSchemaColumn> lookupSchemaColumns) {
		return JsonSerializer.Serialize(new {
			rootSchemaName = LookupSectionSchemaName,
			columnValues = new {
				items = new Dictionary<string, object> {
					["Id"] = CreateColumnValueExpression(
						ResolveInsertDataValueType("Id", lookupSchemaColumns),
						rowId),
					["Name"] = CreateColumnValueExpression(
						ResolveInsertDataValueType("Name", lookupSchemaColumns),
						lookupTitle),
					["SysEntitySchemaUId"] = CreateColumnValueExpression(
						ResolveInsertDataValueType("SysEntitySchemaUId", lookupSchemaColumns),
						lookupSchemaUId.ToString())
				}
			}
		});
	}

	private static string BuildLookupUpdateBody(
		string rowId,
		string lookupTitle,
		IReadOnlyList<DataBindingSchemaColumn> lookupSchemaColumns) {
		return JsonSerializer.Serialize(new {
			rootSchemaName = LookupSectionSchemaName,
			columnValues = new {
				items = new Dictionary<string, object> {
					["Name"] = CreateColumnValueExpression(
						ResolveInsertDataValueType("Name", lookupSchemaColumns),
						lookupTitle)
				}
			},
			filters = BuildPrimaryKeyFilter(rowId)
		});
	}

	private static object CreateColumnValueExpression(int dataValueType, string value) {
		return new {
			expressionType = 2,
			parameter = new {
				dataValueType,
				value
			}
		};
	}

	private static int ResolveInsertDataValueType(
		string columnName,
		IReadOnlyList<DataBindingSchemaColumn> schemaColumns) {
		DataBindingSchemaColumn? column = schemaColumns
			.FirstOrDefault(c => string.Equals(c.Name, columnName, StringComparison.OrdinalIgnoreCase));
		int dataValueType = column?.DataValueType ?? 1;
		if (dataValueType is 26 or 27 or 28 or 29 or 30) {
			dataValueType = 1;
		}

		return dataValueType;
	}

	private static object BuildPrimaryKeyFilter(string keyValue) {
		return new {
			filterType = 6,
			isEnabled = true,
			trimDateTimeParameterToDate = false,
			logicalOperation = 0,
			items = new {
				primaryFilter = new {
					filterType = 1,
					comparisonType = 3,
					isEnabled = true,
					trimDateTimeParameterToDate = false,
					leftExpression = new {
						expressionType = 0,
						columnPath = "Id"
					},
					rightExpression = new {
						expressionType = 2,
						parameter = new {
							dataValueType = 0,
							value = keyValue
						}
					}
				}
			}
		};
	}

	private static string BuildBindingName(string lookupSchemaName) {
		return $"Lookup_{lookupSchemaName}";
	}

	private sealed record LookupRegistrationRow(Guid RowId, string Name);

	private sealed record PackageSchemaDataBinding(Guid UId, string EntitySchemaName);

	private sealed class LookupRegistrationSelectResponse : SelectQueryHelper.SelectQueryResponseBaseDto {
		[JsonPropertyName("rows")]
		public List<LookupRegistrationRowDto> Rows { get; init; } = [];
	}

	private sealed class LookupRegistrationRowDto {
		[JsonPropertyName("Id")]
		public string? Id { get; init; }

		[JsonPropertyName("Name")]
		public string? Name { get; init; }
	}

	private sealed class PackageSchemaDataSelectResponse : SelectQueryHelper.SelectQueryResponseBaseDto {
		[JsonPropertyName("rows")]
		public List<PackageSchemaDataBindingDto> Rows { get; init; } = [];
	}

	private sealed class PackageSchemaDataBindingDto {
		[JsonPropertyName("UId")]
		public string? UId { get; init; }

		[JsonPropertyName("EntitySchemaName")]
		public string? EntitySchemaName { get; init; }
	}
}

internal sealed class DataBindingDbService(
	IApplicationClient applicationClient,
	IServiceUrlBuilder serviceUrlBuilder,
	IApplicationPackageListProvider packageListProvider,
	IDataBindingSchemaClient schemaClient) : IDataBindingDbService {

	public DataBindingResult CreateBinding(CreateDataBindingDbOptions options) {
		ArgumentNullException.ThrowIfNull(options);
		ValidateEnvironment(options);

		PackageRef packageRef = ResolvePackageRef(options.PackageName);
		string bindingName = string.IsNullOrWhiteSpace(options.BindingName)
			? options.SchemaName
			: options.BindingName.Trim();

		DataBindingDbSchema schema = FetchSchema(options.SchemaName);
		List<Dictionary<string, JsonNode?>>? rows = ParseRowsJson(options.RowsJson);

		Guid? existingBindingUId = TryLookupBindingUId(packageRef.UId, bindingName);
		List<string> boundRecordIds = existingBindingUId.HasValue
			? FetchExistingBoundRecordIds(existingBindingUId.Value)
			: [];

		Dictionary<string, string> existingNameToId = ShouldFetchExistingNames(schema, rows)
			? FetchExistingEntityNameToId(options.SchemaName)
			: new(StringComparer.OrdinalIgnoreCase);

		(List<DataBindingCreatedRow> createdRows, List<DataBindingCreatedRow> skippedRows) =
			ProcessRows(options.SchemaName, rows, schema, existingNameToId, boundRecordIds);

		string requestBody = BuildSaveSchemaDataRequest(
			packageRef, bindingName, options.SchemaName, schema, boundRecordIds,
			existingBindingUId);
		string response = applicationClient.ExecutePostRequest(
			serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.SaveSchemaData),
			requestBody);
		ThrowIfUnsuccessful(response, "SaveSchema");

		return new DataBindingResult(bindingName, createdRows, skippedRows);
	}

	private static bool ShouldFetchExistingNames(DataBindingDbSchema schema, List<Dictionary<string, JsonNode?>>? rows) {
		if (rows is not { Count: > 0 }) {
			return false;
		}
		bool schemaHasNameColumn = schema.SchemaColumns
			.Any(c => string.Equals(c.Name, "Name", StringComparison.OrdinalIgnoreCase));
		bool hasNamedRows = rows.Any(r => r.ContainsKey("Name"));
		return schemaHasNameColumn && hasNamedRows;
	}

	private (List<DataBindingCreatedRow> CreatedRows, List<DataBindingCreatedRow> SkippedRows) ProcessRows(
		string schemaName,
		List<Dictionary<string, JsonNode?>>? rows,
		DataBindingDbSchema schema,
		Dictionary<string, string> existingNameToId,
		List<string> boundRecordIds) {
		List<DataBindingCreatedRow> createdRows = [];
		List<DataBindingCreatedRow> skippedRows = [];
		if (rows is not { Count: > 0 }) {
			return (createdRows, skippedRows);
		}
		foreach (Dictionary<string, JsonNode?> row in rows) {
			string rowId = EnsureRowId(row);
			string? rowName = row.TryGetValue("Name", out JsonNode? nameNode)
				? nameNode?.ToString()
				: null;
			if (rowName is not null && existingNameToId.TryGetValue(rowName, out string? existingId)) {
				AddToBoundIds(boundRecordIds, existingId);
				Dictionary<string, string?> skippedValues = row.ToDictionary(kv => kv.Key, kv => kv.Value?.ToString());
				skippedValues["Id"] = existingId;
				skippedRows.Add(new DataBindingCreatedRow(existingId, skippedValues));
			} else {
				InsertEntityRow(schemaName, row, schema.SchemaColumns);
				if (rowName is not null) {
					existingNameToId[rowName] = rowId;
				}
				AddToBoundIds(boundRecordIds, rowId);
				createdRows.Add(new DataBindingCreatedRow(
					rowId,
					row.ToDictionary(kv => kv.Key, kv => kv.Value?.ToString())));
			}
		}
		return (createdRows, skippedRows);
	}

	private static void AddToBoundIds(List<string> boundRecordIds, string id) {
		if (!boundRecordIds.Contains(id, StringComparer.OrdinalIgnoreCase)) {
			boundRecordIds.Add(id);
		}
	}

	public void UpsertRow(UpsertDataBindingRowDbOptions options) {
		ArgumentNullException.ThrowIfNull(options);
		ValidateEnvironment(options);

		PackageRef packageRef = ResolvePackageRef(options.PackageName);
		(string entitySchemaName, Guid bindingUId) = LookupBindingInfo(packageRef.UId, options.BindingName);
		DataBindingDbSchema schema = FetchSchema(entitySchemaName);
		Dictionary<string, JsonNode?> values = ParseValues(options.ValuesJson);

		string rowId = EnsureRowId(values);
		List<string> existingIds = FetchExistingBoundRecordIds(bindingUId);
		bool rowAlreadyBound = existingIds.Contains(rowId, StringComparer.OrdinalIgnoreCase);

		if (rowAlreadyBound) {
			UpdateEntityRow(entitySchemaName, rowId, values, schema.SchemaColumns);
		} else {
			InsertEntityRow(entitySchemaName, values, schema.SchemaColumns);
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

	private static void ValidateEnvironment(EnvironmentOptions options) {
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

	internal static string BuildSaveSchemaDataRequest(
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

	private (JsonElement FirstRow, bool Found) QueryBindingRow(Guid packageUId, string bindingName) {
		string response = applicationClient.ExecutePostRequest(
			serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.Select),
			BuildLookupBindingRequestBody(packageUId, bindingName));
		using JsonDocument document = JsonDocument.Parse(response);
		JsonElement root = document.RootElement;
		if (root.TryGetProperty("success", out JsonElement successElement) && !successElement.GetBoolean()) {
			string errorMessage = root.TryGetProperty("responseStatus", out JsonElement status)
				&& status.TryGetProperty("Message", out JsonElement msg)
				? msg.GetString() ?? "Unknown error"
				: "Unknown error";
			throw new InvalidOperationException(
				$"Failed to query binding '{bindingName}': {errorMessage}");
		}
		if (!root.TryGetProperty("rows", out JsonElement rows) ||
			rows.ValueKind != JsonValueKind.Array ||
			rows.GetArrayLength() == 0) {
			return (default, false);
		}
		return (rows[0].Clone(), true);
	}

	private (string EntitySchemaName, Guid BindingUId) LookupBindingInfo(Guid packageUId, string bindingName) {
		(JsonElement firstRow, bool found) = QueryBindingRow(packageUId, bindingName);
		if (!found) {
			throw new InvalidOperationException(
				$"Binding '{bindingName}' was not found in the remote environment.");
		}

		string entitySchemaName = firstRow.TryGetProperty("EntitySchemaName", out JsonElement schemaNameElement)
			? schemaNameElement.GetString() ?? bindingName
			: bindingName;

		Guid bindingUId = firstRow.TryGetProperty("UId", out JsonElement uidElement)
			&& Guid.TryParse(uidElement.GetString(), out Guid parsed)
			? parsed
			: Guid.Empty;

		return (entitySchemaName, bindingUId);
	}

	private Guid? TryLookupBindingUId(Guid packageUId, string bindingName) {
		(JsonElement firstRow, bool found) = QueryBindingRow(packageUId, bindingName);
		if (!found) {
			return null;
		}

		return firstRow.TryGetProperty("UId", out JsonElement uidElement)
			&& Guid.TryParse(uidElement.GetString(), out Guid parsed)
			? parsed
			: null;
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

	private void UpdateEntityRow(
		string rootSchemaName,
		string rowId,
		Dictionary<string, JsonNode?> values,
		IReadOnlyList<DataBindingSchemaColumn> schemaColumns) {
		var columnItems = new Dictionary<string, object>();
		foreach (KeyValuePair<string, JsonNode?> kv in values) {
			if (string.Equals(kv.Key, "Id", StringComparison.OrdinalIgnoreCase)) {
				continue;
			}

			int dataValueType = ResolveInsertDataValueType(kv.Key, schemaColumns);
			columnItems[kv.Key] = new {
				expressionType = 2,
				parameter = new {
					dataValueType,
					value = kv.Value?.ToString() ?? ""
				}
			};
		}

		if (columnItems.Count == 0) {
			return;
		}

		var body = new {
			rootSchemaName,
			columnValues = new { items = columnItems },
			filters = BuildPrimaryKeyFilter(rowId)
		};

		string response = applicationClient.ExecutePostRequest(
			serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.Update),
			JsonSerializer.Serialize(body));
		ThrowIfUnsuccessful(response, "UpdateQuery");
	}

	private void InsertEntityRow(
		string rootSchemaName,
		Dictionary<string, JsonNode?> values,
		IReadOnlyList<DataBindingSchemaColumn> schemaColumns) {
		var columnItems = new Dictionary<string, object>();
		foreach (KeyValuePair<string, JsonNode?> kv in values) {
			int dataValueType = ResolveInsertDataValueType(kv.Key, schemaColumns);
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

	/// <summary>
	/// Resolves the Creatio <c>dataValueType</c> integer suitable for Insert/Update query parameters.
	/// Text subtypes are normalized to <c>1</c> (Text) because the DataService does not accept sub-type integers.
	/// </summary>
	private static int ResolveInsertDataValueType(
		string columnName,
		IReadOnlyList<DataBindingSchemaColumn> schemaColumns) {
		DataBindingSchemaColumn? col = schemaColumns
			.FirstOrDefault(c => string.Equals(c.Name, columnName, StringComparison.OrdinalIgnoreCase));
		int dataValueType = col?.DataValueType ?? 1;
		// Normalize text subtypes to 1 (Text) for Insert/Update query parameters
		if (dataValueType is 26 or 27 or 28 or 29 or 30) {
			dataValueType = 1;
		}

		return dataValueType;
	}

	private static object BuildPrimaryKeyFilter(string keyValue) => new {
		filterType = 6,
		isEnabled = true,
		trimDateTimeParameterToDate = false,
		logicalOperation = 0,
		items = new {
			primaryFilter = new {
				filterType = 1,
				comparisonType = 3,
				isEnabled = true,
				trimDateTimeParameterToDate = false,
				leftExpression = new {
					expressionType = 0,
					columnPath = "Id"
				},
				rightExpression = new {
					expressionType = 2,
					parameter = new {
						dataValueType = 0,
						value = keyValue
					}
				}
			}
		}
	};

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

	private List<string> FetchExistingBoundRecordIds(Guid bindingUId) =>
		FetchBoundRows(bindingUId)
			.Where(row => row.TryGetValue("Id", out JsonNode? idNode) && idNode is not null)
			.Select(row => row["Id"]!.GetValue<string>())
			.Where(id => !string.IsNullOrWhiteSpace(id))
			.ToList();

	private Dictionary<string, string> FetchExistingEntityNameToId(string schemaName) {
		EntityNameSelectResponse response = SelectQueryHelper.ExecuteSelectQuery<EntityNameSelectResponse>(
			applicationClient,
			serviceUrlBuilder,
			SelectQueryHelper.BuildSelectQuery(
				schemaName,
				[
					new SelectQueryHelper.SelectQueryColumnDefinition("Name", "Name"),
					new SelectQueryHelper.SelectQueryColumnDefinition("Id", "Id")
				],
				[]));
		Dictionary<string, string> result = new(StringComparer.OrdinalIgnoreCase);
		foreach (EntityNameRowDto row in response.Rows.Where(r => !string.IsNullOrWhiteSpace(r.Name) && !string.IsNullOrWhiteSpace(r.Id))) {
			result.TryAdd(row.Name, row.Id);
		}
		return result;
	}

	private void DeletePackageSchemaData(Guid packageUId, string bindingName) {
		string response = applicationClient.ExecutePostRequest(
			serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.DeletePackageSchemaData),
			BuildDeletePackageSchemaDataBody(packageUId, bindingName));
		ThrowIfUnsuccessful(response, "DeletePackageSchemaDataRequest");
	}

	internal static void ThrowIfUnsuccessful(string response, string operationName) {
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

	private sealed class EntityNameSelectResponse : SelectQueryHelper.SelectQueryResponseBaseDto {
		[JsonPropertyName("rows")]
		public List<EntityNameRowDto> Rows { get; init; } = [];
	}

	private sealed class EntityNameRowDto {
		[JsonPropertyName("Name")]
		public string? Name { get; init; }

		[JsonPropertyName("Id")]
		public string? Id { get; init; }
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

/// <summary>
/// Result of a <see cref="DataBindingDbService.CreateBinding"/> operation.
/// </summary>
public sealed record DataBindingResult(
	string BindingName,
	IReadOnlyList<DataBindingCreatedRow> CreatedRows,
	IReadOnlyList<DataBindingCreatedRow> SkippedRows);

/// <summary>
/// Represents a single row created by <see cref="DataBindingDbService.CreateBinding"/>.
/// </summary>
public sealed record DataBindingCreatedRow(
	string Id,
	IReadOnlyDictionary<string, string?> Values);
