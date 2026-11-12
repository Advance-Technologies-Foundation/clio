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
	[Option("package", Required = true, HelpText = "Target package name")]
	public string PackageName { get; set; } = string.Empty;

	[Option("schema", Required = true, HelpText = "Entity schema name")]
	public string SchemaName { get; set; } = string.Empty;

	[Option("binding-name", Required = false, HelpText = "Binding folder name; defaults to <schema>")]
	public string? BindingName { get; set; }

	[Option("rows", Required = false, HelpText = "JSON array of row objects, each with a 'values' key containing column values and an optional 'localizations' key (culture -> column -> text)")]
	public string? RowsJson { get; set; }
}

/// <summary>
/// Options for the <c>upsert-data-binding-row-db</c> command.
/// </summary>
[Verb("upsert-data-binding-row-db", HelpText = "Upsert a single row in a remote DB-first data binding")]
public class UpsertDataBindingRowDbOptions : EnvironmentOptions {
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
[Verb("remove-data-binding-row-db", HelpText = "DELETE the live record and remove its row from a remote DB-first data binding (no confirmation, no undo)")]
public class RemoveDataBindingRowDbOptions : EnvironmentOptions {
	[Option("package", Required = true, HelpText = "Target package name")]
	public string PackageName { get; set; } = string.Empty;

	[Option("binding-name", Required = true, HelpText = "Binding folder name")]
	public string BindingName { get; set; } = string.Empty;

	[Option("key-value", Required = true, HelpText = "Primary-key value of the row to remove")]
	public string KeyValue { get; set; } = string.Empty;
}

/// <summary>
///     Options for <see cref="ReadDataBindingDbCommand" />.
/// </summary>
[Verb("read-data-binding-db", Aliases = ["get-data-binding-db"],
	HelpText = "Read which columns a remote DB-first data binding actually ships")]
public class ReadDataBindingDbOptions : EnvironmentOptions {

	[Option("package", Required = true, HelpText = "Target package name")]
	public string PackageName { get; set; } = string.Empty;

	[Option("binding-name", Required = true, HelpText = "Binding folder name")]
	public string BindingName { get; set; } = string.Empty;

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
			foreach (DataBindingLocalizationWrite localization in result.Localizations) {
				logger.WriteInfo(
					$"Localized row: {localization.RowId} ({localization.RowLabel}) [{localization.Culture}]: "
					+ string.Join(", ", localization.Columns));
			}
			foreach (DataBindingLocalizationWrite localization in result.Localizations.Where(write => !write.Verified)) {
				logger.WriteWarning(
					$"Row {localization.RowId} ({localization.RowLabel}) [{localization.Culture}] was written, but not "
					+ "verified: the site's default culture could not be read"
					+ (string.IsNullOrWhiteSpace(localization.UnverifiedReason) ? "" : $" ({localization.UnverifiedReason})")
					+ ", so the value could not be read back. Check the record in Creatio.");
			}
			if (result.Localizations.Count > 0) {
				logger.WriteInfo(
					$"Translations are stored on the records and in the per-culture data of binding "
					+ $"'{result.BindingName}' of package '{options.PackageName}'. A binding of another package that "
					+ "also carries these records keeps its previous translations until that binding is saved again.");
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
///     Reports which columns a remote DB-first data binding ships, so the transfer contract can be checked without
///     exporting and unpacking the whole package.
/// </summary>
public class ReadDataBindingDbCommand(IDataBindingDbService dataBindingDbService, ILogger logger)
	: Command<ReadDataBindingDbOptions> {

	/// <inheritdoc />
	public override int Execute(ReadDataBindingDbOptions options) {
		try {
			BoundBindingProjection projection = dataBindingDbService.ReadBinding(options);
			logger.WriteInfo($"binding: {projection.BindingName}");
			logger.WriteInfo($"schema:  {projection.EntitySchemaName}");
			logger.WriteInfo($"uId:     {projection.BindingUId}");
			logger.WriteInfo($"rows:    {projection.Rows.Count}");
			IReadOnlyList<string> columns = projection.GetColumns();
			logger.WriteInfo($"columns ({columns.Count}): {string.Join(", ", columns)}");
			for (int index = 0; index < projection.Rows.Count; index++) {
				IReadOnlyDictionary<string, string> row = projection.Rows[index];
				string values = string.Join(", ", row.OrderBy(pair => pair.Key, StringComparer.Ordinal)
					.Select(pair => $"{pair.Key}={pair.Value}"));
				logger.WriteInfo($"row[{index}]: {values}");
			}
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

	/// <summary>
	/// Reads what a remote DB-first binding actually ships: its entity schema and, per row, the exact set of
	/// bound columns.
	/// </summary>
	/// <remarks>
	///     A binding ships ONLY the columns it was created with, and that projection — not the live record — is what
	///     transfers to the next environment.
	/// </remarks>
	/// <exception cref="InvalidOperationException">The package or the binding does not exist on the environment.</exception>
	BoundBindingProjection ReadBinding(ReadDataBindingDbOptions options);
}

/// <summary>
///     What a DB-first data binding ships, as read back from the environment.
/// </summary>
/// <param name="BindingName">Binding folder name, i.e. the <c>SysPackageSchemaData.Name</c>.</param>
/// <param name="EntitySchemaName">Entity schema the binding carries rows for.</param>
/// <param name="BindingUId">Identifier of the package-schema-data record.</param>
/// <param name="Rows">One entry per bound row: the column names mapped to their bound values.</param>
public sealed record BoundBindingProjection(
	string BindingName,
	string EntitySchemaName,
	Guid BindingUId,
	IReadOnlyList<IReadOnlyDictionary<string, string>> Rows) {

	/// <summary>
	///     Every column name the binding ships, across all rows, in stable order.
	/// </summary>
	public IReadOnlyList<string> GetColumns() =>
		Rows.SelectMany(row => row.Keys).Distinct(StringComparer.Ordinal).OrderBy(name => name, StringComparer.Ordinal)
			.ToArray();
}

public interface ILookupRegistrationService {
	void EnsureLookupRegistration(string packageName, string lookupSchemaName, string lookupTitle);
}

internal sealed class LookupRegistrationService(
	IApplicationClient applicationClient,
	IServiceUrlBuilder serviceUrlBuilder,
	IPackageDataBindingWriter bindingWriter,
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
		PackageRef packageRef = bindingWriter.ResolvePackage(packageName);
		DataBindingDbSchema lookupBindingSchema =
			BuildLookupBindingSchema(bindingWriter.FetchSchema(LookupSectionSchemaName));
		DataBindingDbSchema registeredLookupSchema = bindingWriter.FetchSchema(lookupSchemaName);
		LookupRegistrationRow? lookupRegistrationRow = FindLookupRegistrationRow(registeredLookupSchema.EntitySchemaUId);
		string lookupRegistrationRowId = EnsureLookupRegistrationRow(
			lookupBindingSchema.SchemaColumns,
			registeredLookupSchema.EntitySchemaUId,
			resolvedLookupTitle,
			lookupRegistrationRow);
		string bindingName = BuildBindingName(lookupSchemaName);
		PackageDataBindingRef? existingBinding = bindingWriter.FindBinding(packageRef.UId, bindingName);
		if (existingBinding is not null &&
			!string.Equals(existingBinding.EntitySchemaName, LookupSectionSchemaName, StringComparison.OrdinalIgnoreCase)) {
			throw new InvalidOperationException(
				string.IsNullOrWhiteSpace(existingBinding.EntitySchemaName)
					? $"Package schema data '{bindingName}' already exists, but the environment did not report "
						+ "which entity schema it delivers, so it cannot be confirmed as the Lookup binding."
					: $"Package schema data '{bindingName}' already exists for schema "
						+ $"'{existingBinding.EntitySchemaName}'.");
		}

		bindingWriter.SaveBinding(
			packageRef,
			bindingName,
			LookupSectionSchemaName,
			lookupBindingSchema,
			[lookupRegistrationRowId],
			existingBinding?.UId);
		logger.WriteInfo($"Lookup '{lookupSchemaName}' registered in Lookups.");
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
			DataServiceResponse.ThrowIfUnsuccessful(response, "InsertQuery");
			return rowId;
		}
		if (!string.Equals(existingRow.Name, lookupTitle, StringComparison.Ordinal)) {
			string response = applicationClient.ExecutePostRequest(
				serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.Update),
				BuildLookupUpdateBody(existingRow.RowId.ToString(), lookupTitle, lookupSchemaColumns));
			DataServiceResponse.ThrowIfUnsuccessful(response, "UpdateQuery");
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

}

internal sealed class DataBindingDbService(
	IApplicationClient applicationClient,
	IServiceUrlBuilder serviceUrlBuilder,
	IPackageDataBindingWriter bindingWriter,
	IDataBindingDbLocalizer localizer) : IDataBindingDbService {

	/// <summary>Creatio runtime <c>dataValueType</c> of a native Color column.</summary>
	private const int ColorRuntimeDataValueType = 18;

	public DataBindingResult CreateBinding(CreateDataBindingDbOptions options) {
		ArgumentNullException.ThrowIfNull(options);
		ValidateEnvironment(options);

		PackageRef packageRef = bindingWriter.ResolvePackage(options.PackageName);
		string bindingName = string.IsNullOrWhiteSpace(options.BindingName)
			? options.SchemaName
			: options.BindingName.Trim();

		DataBindingDbSchema schema = bindingWriter.FetchSchema(options.SchemaName);
		List<DataBindingDbRowInput>? rowInputs = ParseRowsJson(options.RowsJson);
		List<Dictionary<string, JsonNode?>>? rows = rowInputs?.Select(input => input.Values).ToList();
		ValidateRequestedBindingColumnsSupported(schema, rows);
		List<DataBindingRowLocalization> localizations = rowInputs?.Select(input => input.Localization).ToList() ?? [];
		DataBindingLocalizationPlan? localizationPlan = localizer.Validate(
			options.SchemaName, packageRef.UId, schema.SchemaColumns, localizations);
		if (localizationPlan is not null) {
			ValidateProfileCultureValues(rowInputs ?? [], localizationPlan.ProfileCulture);
		}

		Guid? existingBindingUId = bindingWriter.FindBinding(packageRef.UId, bindingName)?.UId;
		List<Dictionary<string, JsonNode?>> existingBoundRows = existingBindingUId.HasValue
			? bindingWriter.FetchBoundRows(existingBindingUId.Value)
			: [];
		List<string> boundRecordIds = ExtractBoundRecordIds(existingBoundRows);
		RefuseSecretSettingValueRows(options.SchemaName, existingBoundRows, rows);

		Dictionary<string, string> existingNameToId = ShouldFetchExistingNames(schema, rows)
			? FetchExistingEntityNameToId(options.SchemaName)
			: new(StringComparer.OrdinalIgnoreCase);

		(List<DataBindingCreatedRow> createdRows, List<DataBindingCreatedRow> skippedRows, List<string> rowIds) =
			ProcessRows(options.SchemaName, rows, schema, existingNameToId, boundRecordIds);
		// Written before the binding is saved: the save takes the per-culture snapshot the package ships.
		IReadOnlyList<DataBindingLocalizationWrite> localizationWrites = [];
		Exception? localizationFailure = null;
		if (localizationPlan is not null) {
			try {
				localizationWrites = localizer.Write(localizationPlan, options.SchemaName, PairRowIds(rowIds, localizations));
			}
			catch (Exception exception) when (exception is not OperationCanceledException) {
				// The rows are already in the table. Saving the binding anyway keeps them in the package, so a
				// failed translation never leaves inserted rows that no package ships.
				localizationFailure = exception;
			}
		}
		try {
			DataBindingDbSchema bindingSchema = BuildBindingSchemaProjection(
				schema, existingBoundRows, rows, LocalizedColumnRowSet(localizations));
			bindingWriter.SaveBinding(
				packageRef, bindingName, options.SchemaName, bindingSchema, boundRecordIds, existingBindingUId);
		}
		catch (Exception exception) when (localizationFailure is not null && exception is not OperationCanceledException) {
			throw new InvalidOperationException(
				$"{localizationFailure.Message}{DescribeWrittenLocalizations(localizationFailure)}"
				+ $"{DescribeCreatedRows(createdRows)} Saving binding "
				+ $"'{bindingName}' of package '{options.PackageName}' then failed too, so the binding was NOT saved: "
				+ exception.Message,
				exception);
		}

		if (localizationFailure is not null) {
			throw new InvalidOperationException(
				$"{localizationFailure.Message}{DescribeWrittenLocalizations(localizationFailure)}"
				+ $"{DescribeCreatedRows(createdRows)} The rows and binding '{bindingName}' of package "
				+ $"'{options.PackageName}' were saved; run the same call again to retry the translations.",
				localizationFailure);
		}

		return new DataBindingResult(bindingName, createdRows, skippedRows, localizationWrites);
	}

	/// <summary>
	/// Names the rows this call inserted, so a caller whose translation write failed knows which records are new.
	/// </summary>
	private static string DescribeCreatedRows(IReadOnlyList<DataBindingCreatedRow> createdRows) =>
		createdRows.Count == 0
			? string.Empty
			: " Rows created by this call: " + string.Join(", ", createdRows.Select(row => row.Id)) + ".";

	/// <summary>
	/// Lists the rows and cultures a failed localization write had already written, so the caller knows which
	/// records now carry translations.
	/// </summary>
	private static string DescribeWrittenLocalizations(Exception localizationFailure) {
		if (localizationFailure is not DataBindingLocalizationException { Written.Count: > 0 } failure) {
			return string.Empty;
		}

		return " Translations already written: "
			+ string.Join("; ", failure.Written.Select(write =>
				$"{write.RowId} ({write.RowLabel}) [{write.Culture}]: {string.Join(", ", write.Columns)}"))
			+ ".";
	}

	/// <summary>
	/// Refuses a profile-culture translation that differs from the row's own value of the same column. The insert
	/// writes <c>values</c> in the profile culture, so a different translation would rename the record after the
	/// insert, and a re-run of the same call - which matches existing rows by <c>Name</c> - would insert a duplicate.
	/// A profile-culture translation of a column missing from <c>values</c> is refused too: on a row matched by
	/// <c>Id</c> it would rewrite the stored value, and translating adds cultures without rewriting the record.
	/// </summary>
	/// <exception cref="InvalidOperationException">A row's profile-culture translation differs from its value or has no
	/// value to match.</exception>
	private static void ValidateProfileCultureValues(IEnumerable<DataBindingDbRowInput> rowInputs, string profileCulture) {
		List<string> failures = [];
		foreach (DataBindingDbRowInput input in rowInputs) {
			KeyValuePair<string, IReadOnlyDictionary<string, string>> profileTranslations = input.Localization.Cultures
				.FirstOrDefault(culture => string.Equals(culture.Key, profileCulture, StringComparison.OrdinalIgnoreCase));
			if (profileTranslations.Value is null) {
				continue;
			}

			foreach ((string column, string translation) in profileTranslations.Value) {
				KeyValuePair<string, JsonNode?> value = input.Values.FirstOrDefault(pair =>
					string.Equals(pair.Key, column, StringComparison.OrdinalIgnoreCase));
				if (value.Key is null) {
					failures.Add(
						$"Row '{input.Localization.RowLabel}' [{profileCulture}] column '{column}': the profile culture "
						+ $"'{profileCulture}' takes its value from 'values', and 'values' has no '{column}'; drop it from "
						+ "'localizations' or pass the same value in 'values'.");
				} else if (!string.Equals(value.Value?.ToString(), translation, StringComparison.Ordinal)) {
					failures.Add(
						$"Row '{input.Localization.RowLabel}' [{profileCulture}] column '{column}': the translation "
						+ $"'{translation}' differs from the row value '{value.Value}'; the profile culture '{profileCulture}' "
						+ "takes its value from 'values', so drop it from 'localizations' or make the two equal.");
				}
			}
		}

		if (failures.Count > 0) {
			throw new InvalidOperationException(
				"Localizations were refused and nothing was written: " + string.Join(" ", failures));
		}
	}

	/// <summary>
	/// Pairs each requested row's translations with the record identifier <see cref="ProcessRows"/> resolved for it.
	/// </summary>
	private static List<(string RowId, DataBindingRowLocalization Localization)> PairRowIds(
		List<string> rowIds,
		List<DataBindingRowLocalization> localizations) {
		if (rowIds.Count != localizations.Count) {
			throw new InvalidOperationException(
				$"Resolved {rowIds.Count} record identifiers for {localizations.Count} requested rows.");
		}

		return rowIds.Zip(localizations).ToList();
	}

	/// <summary>
	/// A localized column ships only when the binding carries it, so a column that appears only in
	/// <c>localizations</c> joins the projection as if a row had referenced it.
	/// </summary>
	private static IEnumerable<Dictionary<string, JsonNode?>> LocalizedColumnRowSet(
		IEnumerable<DataBindingRowLocalization> localizations) {
		Dictionary<string, JsonNode?> columns = localizations
			.SelectMany(localization => localization.Cultures.Values)
			.SelectMany(values => values.Keys)
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.ToDictionary(column => column, _ => (JsonNode?)null, StringComparer.OrdinalIgnoreCase);
		return SingleRowSet(columns);
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

	/// <returns>The created and the skipped rows, and the record identifier of every requested row, in request
	/// order.</returns>
	private (List<DataBindingCreatedRow> CreatedRows, List<DataBindingCreatedRow> SkippedRows, List<string> RowIds) ProcessRows(
		string schemaName,
		List<Dictionary<string, JsonNode?>>? rows,
		DataBindingDbSchema schema,
		Dictionary<string, string> existingNameToId,
		List<string> boundRecordIds) {
		List<DataBindingCreatedRow> createdRows = [];
		List<DataBindingCreatedRow> skippedRows = [];
		List<string> rowIds = [];
		if (rows is not { Count: > 0 }) {
			return (createdRows, skippedRows, rowIds);
		}
		foreach (Dictionary<string, JsonNode?> row in rows) {
			string rowId = EnsureRowId(row);
			rowIds.Add(rowId);
			string? rowName = row.TryGetValue("Name", out JsonNode? nameNode)
				? nameNode?.ToString()
				: null;
			if (rowName is not null && existingNameToId.TryGetValue(rowName, out string? existingId)) {
				rowIds[^1] = existingId;
				AddToBoundIds(boundRecordIds, existingId);
				Dictionary<string, string?> skippedValues = row.ToDictionary(kv => kv.Key, kv => kv.Value?.ToString());
				skippedValues["Id"] = existingId;
				skippedRows.Add(new DataBindingCreatedRow(existingId, skippedValues));
			} else if (RowExistsInTable(schemaName, rowId)) {
				// Row already exists in the table (matched by Id): register the binding for it without
				// rewriting the row. Lets a row with no Name column (e.g. SysSchemaAdminUnitRight) be bound
				// by Id, the same way a Named row is adopted above, instead of inserting a duplicate.
				if (rowName is not null) {
					existingNameToId[rowName] = rowId;
				}
				AddToBoundIds(boundRecordIds, rowId);
				skippedRows.Add(new DataBindingCreatedRow(
					rowId,
					row.ToDictionary(kv => kv.Key, kv => kv.Value?.ToString())));
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
		return (createdRows, skippedRows, rowIds);
	}

	private static void AddToBoundIds(List<string> boundRecordIds, string id) {
		if (!boundRecordIds.Contains(id, StringComparer.OrdinalIgnoreCase)) {
			boundRecordIds.Add(id);
		}
	}

	public void UpsertRow(UpsertDataBindingRowDbOptions options) {
		ArgumentNullException.ThrowIfNull(options);
		ValidateEnvironment(options);

		PackageRef packageRef = bindingWriter.ResolvePackage(options.PackageName);
		(string entitySchemaName, Guid bindingUId) = LookupBindingInfo(packageRef.UId, options.BindingName);
		DataBindingDbSchema schema = bindingWriter.FetchSchema(entitySchemaName);
		Dictionary<string, JsonNode?> values = ParseValues(options.ValuesJson);
		ValidateRequestedBindingColumnsSupported(schema, [values]);

		string rowId = EnsureRowId(values);
		List<Dictionary<string, JsonNode?>> existingBoundRows = bindingWriter.FetchBoundRows(bindingUId);
		List<string> existingIds = ExtractBoundRecordIds(existingBoundRows);
		bool rowAlreadyBound = existingIds.Contains(rowId, StringComparer.OrdinalIgnoreCase);
		RefuseSecretSettingValueRows(entitySchemaName, existingBoundRows, SingleRowSet(values));

		if (rowAlreadyBound || RowExistsInTable(entitySchemaName, rowId)) {
			// Update whether the row is already bound to this package OR exists in the table but is not yet
			// bound: only a row that exists in neither place is a genuine insert.
			UpdateEntityRow(entitySchemaName, rowId, values, schema.SchemaColumns);
		} else {
			InsertEntityRow(entitySchemaName, values, schema.SchemaColumns);
		}
		if (!rowAlreadyBound) {
			existingIds.Add(rowId);
		}
			DataBindingDbSchema bindingSchema = BuildBindingSchemaProjection(schema, existingBoundRows, SingleRowSet(values));

		bindingWriter.SaveBinding(
			packageRef, options.BindingName, schema.SchemaName, bindingSchema, existingIds, bindingUId);
	}

	/// <inheritdoc />
	public BoundBindingProjection ReadBinding(ReadDataBindingDbOptions options) {
		ArgumentNullException.ThrowIfNull(options);
		ValidateEnvironment(options);

		PackageRef packageRef = bindingWriter.ResolvePackage(options.PackageName);
		(string entitySchemaName, Guid bindingUId) = LookupBindingInfo(packageRef.UId, options.BindingName);
		List<Dictionary<string, JsonNode?>> boundRows = bindingWriter.FetchBoundRows(bindingUId);
		IReadOnlyList<IReadOnlyDictionary<string, string>> rows = boundRows
			.Select(row => (IReadOnlyDictionary<string, string>)row.ToDictionary(
				pair => pair.Key,
				pair => FormatBoundValue(pair.Value),
				StringComparer.Ordinal))
			.ToArray();
		return new BoundBindingProjection(options.BindingName, entitySchemaName, bindingUId, rows);
	}

	/// <summary>
	///     Renders a bound value as text, collapsing a lookup envelope to <c>displayValue (value)</c>.
	/// </summary>
	private static string FormatBoundValue(JsonNode? value) {
		if (value is null) {
			return string.Empty;
		}
		if (value is not JsonObject envelope) {
			return value.ToString();
		}
		bool hasDisplayValue = envelope.TryGetPropertyValue("displayValue", out JsonNode? display);
		bool hasRawValue = envelope.TryGetPropertyValue("value", out JsonNode? raw);
		if (!hasDisplayValue && !hasRawValue) {
			return envelope.ToJsonString();
		}
		string displayValue = display?.ToString() ?? string.Empty;
		string rawValue = raw?.ToString() ?? string.Empty;
		if (string.IsNullOrEmpty(displayValue)) {
			return rawValue;
		}
		return string.IsNullOrEmpty(rawValue) ? displayValue : $"{displayValue} ({rawValue})";
	}

	/// <inheritdoc />
	public void RemoveRow(RemoveDataBindingRowDbOptions options) {
		ArgumentNullException.ThrowIfNull(options);
		ValidateEnvironment(options);

		PackageRef packageRef = bindingWriter.ResolvePackage(options.PackageName);
		(string entitySchemaName, Guid bindingUId) = LookupBindingInfo(packageRef.UId, options.BindingName);
		List<Dictionary<string, JsonNode?>> boundRows = bindingWriter.FetchBoundRows(bindingUId);
		List<string> boundIds = ExtractBoundRecordIds(boundRows);

		if (!boundIds.Contains(options.KeyValue, StringComparer.OrdinalIgnoreCase)) {
			throw new InvalidOperationException(
				$"Row with key '{options.KeyValue}' was not found in binding '{options.BindingName}'.");
		}

		boundIds.RemoveAll(id => string.Equals(id, options.KeyValue, StringComparison.OrdinalIgnoreCase));
		boundRows.RemoveAll(row =>
			row.TryGetValue("Id", out JsonNode? idNode) &&
			string.Equals(idNode?.ToString(), options.KeyValue, StringComparison.OrdinalIgnoreCase));
		if (boundIds.Count > 0) {
			RefuseSecretSettingValueRows(entitySchemaName, boundRows);
		}
		DeleteEntityRow(entitySchemaName, options.KeyValue);

		if (boundIds.Count == 0) {
			bindingWriter.DeleteBinding(packageRef, options.BindingName);
		}
		else {
			DataBindingDbSchema schema = bindingWriter.FetchSchema(entitySchemaName);
			DataBindingDbSchema bindingSchema = BuildBindingSchemaProjection(schema, boundRows);
			bindingWriter.SaveBinding(
				packageRef, options.BindingName, entitySchemaName, bindingSchema, boundIds, bindingUId);
		}
	}

	private static void ValidateEnvironment(EnvironmentOptions options) {
		if (string.IsNullOrWhiteSpace(options.Environment) && string.IsNullOrWhiteSpace(options.Uri)) {
			throw new InvalidOperationException("--environment or --uri is required.");
		}
	}

	/// <summary>
	///     Builds a DataService DeleteQuery body for one row of a schema.
	/// </summary>
	/// <remarks>
	///     Both arguments are caller-controlled and never validated as a GUID, so they are JSON-escaped: a crafted
	///     value could otherwise close the string and inject sibling properties into the filter.
	/// </remarks>
	private static string BuildDeleteQueryBody(string rootSchemaName, string keyValue) {
		string escapedRootSchemaName = JsonEncodedText.Encode(rootSchemaName ?? string.Empty).ToString();
		string escapedKeyValue = JsonEncodedText.Encode(keyValue ?? string.Empty).ToString();
		return $$"""
			{
			  "__type":"Terrasoft.Nui.ServiceModel.DataContract.DeleteQuery",
			  "rootSchemaName":"{{escapedRootSchemaName}}",
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
			            "value":"{{escapedKeyValue}}"
			          }
			        }
			      }
			    }
			  }
			}
			""";
	}

	private (string EntitySchemaName, Guid BindingUId) LookupBindingInfo(Guid packageUId, string bindingName) {
		PackageDataBindingRef? binding = bindingWriter.FindBinding(packageUId, bindingName);
		if (binding is null) {
			throw new InvalidOperationException(
				$"Binding '{bindingName}' was not found in the remote environment.");
		}

		return (string.IsNullOrWhiteSpace(binding.EntitySchemaName) ? bindingName : binding.EntitySchemaName,
			binding.UId);
	}

	private void DeleteEntityRow(string schemaName, string keyValue) {
		string response = applicationClient.ExecutePostRequest(
			serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.Delete),
			BuildDeleteQueryBody(schemaName, keyValue));
		DataServiceResponse.ThrowIfUnsuccessful(response, "DeleteQuery");
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
					value = BuildRowParameterValue(kv.Key, kv.Value, dataValueType)
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
		DataServiceResponse.ThrowIfUnsuccessful(response, "UpdateQuery");
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
					value = BuildRowParameterValue(kv.Key, kv.Value, dataValueType)
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
		DataServiceResponse.ThrowIfUnsuccessful(response, "InsertQuery");
	}

	/// <summary>
	/// Shapes a row value for an Insert/Update query parameter, applying the Color rules at the DataService boundary.
	/// </summary>
	/// <remarks>
	/// Every other column is written the way it always was - the JSON node stringified, null collapsed to an
	/// empty string. Color cannot use that rule: the parameter keeps <c>dataValueType</c> 18, so a number
	/// would be POSTed as "123", an object as its JSON text, and a null as "", none of which is a valid
	/// "#RRGGBB" literal. The local-file converter already refuses those shapes; this is the same rule for
	/// the DB-first create and upsert paths.
	/// </remarks>
	private static string? BuildRowParameterValue(string columnName, JsonNode? value, int dataValueType) =>
		dataValueType == ColorRuntimeDataValueType
			? ConvertColorValue(columnName, value)
			: value?.ToString() ?? "";

	/// <summary>
	/// Returns the hex literal of a Color value, preserving null, and rejects any other wire shape.
	/// </summary>
	private static string? ConvertColorValue(string columnName, JsonNode? value) {
		if (value is null) {
			//Preserved as null rather than "": an empty string is not a Color, and writing one turns an
			//explicit "no color" into malformed data on the column.
			return null;
		}

		if (value is JsonValue jsonValue && jsonValue.TryGetValue(out string? hexLiteral)) {
			return hexLiteral;
		}

		throw new InvalidOperationException(
			$"Column '{columnName}' value '{value.ToJsonString()}' is not valid for data type 'Color'.");
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

	private static List<string> ExtractBoundRecordIds(IEnumerable<Dictionary<string, JsonNode?>> rows) =>
		rows
			.Where(row => row.TryGetValue("Id", out JsonNode? idNode) && idNode is not null)
			.Select(row => row["Id"]!.GetValue<string>())
			.Where(id => !string.IsNullOrWhiteSpace(id))
			.ToList();

	private static void ValidateRequestedBindingColumnsSupported(
		DataBindingDbSchema schema,
		IEnumerable<Dictionary<string, JsonNode?>>? rows) {
		if (rows is null) {
			return;
		}

		foreach (Dictionary<string, JsonNode?> row in rows) {
			foreach (KeyValuePair<string, JsonNode?> requested in row) {
				if (string.IsNullOrWhiteSpace(requested.Key)) {
					continue;
				}

				DataBindingSchemaColumn? column = schema.SchemaColumns
					.FirstOrDefault(col => string.Equals(col.Name, requested.Key, StringComparison.OrdinalIgnoreCase));
				if (column is null) {
					continue;
				}

				PackageDataBindingWriter.ResolveBindingDataTypeValueUId(column);
				if (column.DataValueType == ColorRuntimeDataValueType) {
					//Checked here as well as at the write parameter so the whole request is rejected before
					//the first POST: validating only inside InsertEntityRow would leave earlier rows of the
					//same create already inserted on the stand when a later one carries a malformed Color.
					ConvertColorValue(requested.Key, requested.Value);
				}
			}
		}
	}

	/// <summary>
	/// Columns the platform reads while it saves a binding of the given schema, whether or not a row references them.
	/// Saving a <c>SysSettingsValue</c> binding runs <c>PackageSchemaDataUtilities.GetIsSecureSettings</c> (the
	/// SecureText re-encryption, on by default through <c>ReEncryptSecureSettingsValuesInPackageData</c>), which calls
	/// <c>GetIsColumnValueLoaded("SysSettingsId")</c> and <c>GetIsColumnValueLoaded("TextValue")</c>. Both throw
	/// <c>Value "..." was not found.</c> when the column is not in the binding, so they are always projected.
	/// </summary>
	private static readonly Dictionary<string, string[]> PlatformRequiredBindingColumns =
		new(StringComparer.OrdinalIgnoreCase) {
			["SysSettingsValue"] = ["SysSettings", "TextValue"]
		};

	private const string SysSettingsValueSchemaName = "SysSettingsValue";
	private const string SecureTextValueTypeName = "SecureText";

	/// <summary>
	/// Refuses a <c>SysSettingsValue</c> binding that would hold the value of a SecureText setting. The projection
	/// always carries <c>TextValue</c>, so such a row would ship the secret; <c>PackageDataBinder</c> refuses the same
	/// row for the same reason. Rows are matched by their own <c>Id</c> and by the <c>SysSettings</c> they name, because
	/// an adopted row may carry only its <c>Id</c> and a new row has no stored setting to read yet.
	/// </summary>
	/// <exception cref="InvalidOperationException">A row belongs to a SecureText setting.</exception>
	private void RefuseSecretSettingValueRows(string schemaName,
		params IEnumerable<Dictionary<string, JsonNode?>>?[] rowSets) {
		if (!string.Equals(schemaName, SysSettingsValueSchemaName, StringComparison.OrdinalIgnoreCase)) {
			return;
		}

		List<Dictionary<string, JsonNode?>> rows = rowSets.Where(set => set is not null).SelectMany(set => set).ToList();
		List<string> valueRowIds = ReadGuidValues(rows, "Id");
		List<string> settingIds = ReadGuidValues(rows, "SysSettings");
		HashSet<string> secretCodes = new(StringComparer.OrdinalIgnoreCase);
		if (valueRowIds.Count > 0) {
			secretCodes.UnionWith(FindSecureTextCodes(SysSettingsValueSchemaName, "SysSettings.", valueRowIds));
		}
		if (settingIds.Count > 0) {
			secretCodes.UnionWith(FindSecureTextCodes("SysSettings", string.Empty, settingIds));
		}
		if (secretCodes.Count == 0) {
			return;
		}

		throw new InvalidOperationException(
			$"System setting(s) {string.Join(", ", secretCodes.Order(StringComparer.OrdinalIgnoreCase).Select(code => $"'{code}'"))} "
			+ $"are {SecureTextValueTypeName}, and a secret value is never shipped in a package, so no "
			+ $"{SysSettingsValueSchemaName} row of them is bound. Ship only the definition (SysSettings_<Code>) and set "
			+ "the value on each environment with update-sys-setting.");
	}

	private IEnumerable<string> FindSecureTextCodes(string rootSchemaName, string settingPathPrefix,
		IReadOnlyList<string> ids) {
		SettingValueTypeResponse response = SelectQueryHelper.ExecuteSelectQuery<SettingValueTypeResponse>(
			applicationClient,
			serviceUrlBuilder,
			SelectQueryHelper.BuildSelectQueryWithInFilter(
				rootSchemaName,
				[
					new SelectQueryHelper.SelectQueryColumnDefinition($"{settingPathPrefix}Code", "Code"),
					new SelectQueryHelper.SelectQueryColumnDefinition($"{settingPathPrefix}ValueTypeName", "ValueTypeName")
				],
				"Id",
				ids,
				SelectQueryHelper.GuidDataValueType));
		return response.Rows
			.Where(row => string.Equals(row.ValueTypeName, SecureTextValueTypeName, StringComparison.OrdinalIgnoreCase))
			.Select(row => row.Code ?? string.Empty);
	}

	private static List<string> ReadGuidValues(IEnumerable<Dictionary<string, JsonNode?>> rows, string columnName) =>
		rows
			.Select(row => row.FirstOrDefault(pair => string.Equals(pair.Key, columnName, StringComparison.OrdinalIgnoreCase)).Value)
			.Select(node => node is JsonObject envelope && envelope.TryGetPropertyValue("value", out JsonNode? raw)
				? raw?.ToString()
				: node?.ToString())
			.Where(value => Guid.TryParse(value, out _))
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.ToList();

	private static DataBindingDbSchema BuildBindingSchemaProjection(
		DataBindingDbSchema runtimeSchema,
		params IEnumerable<Dictionary<string, JsonNode?>>?[] rowSets) {
		HashSet<string> referencedColumns = CollectReferencedColumnNames(rowSets);
		if (PlatformRequiredBindingColumns.TryGetValue(runtimeSchema.SchemaName, out string[]? requiredColumns)) {
			referencedColumns.UnionWith(requiredColumns);
		}
		List<DataBindingSchemaColumn> projectedColumns = SelectBindingSchemaColumns(runtimeSchema.SchemaColumns, referencedColumns);
		ValidateBindingSchemaColumnsSupported(projectedColumns);
		return new DataBindingDbSchema(
			runtimeSchema.EntitySchemaUId,
			runtimeSchema.SchemaName,
			projectedColumns.Select(col => col.Name).ToList(),
			projectedColumns);
	}

	private static IEnumerable<Dictionary<string, JsonNode?>> SingleRowSet(Dictionary<string, JsonNode?> row) {
		yield return row;
	}

	private static HashSet<string> CollectReferencedColumnNames(IEnumerable<Dictionary<string, JsonNode?>>?[] rowSets) {
		HashSet<string> referencedColumns = new(StringComparer.OrdinalIgnoreCase) { "Id" };
		foreach (IEnumerable<Dictionary<string, JsonNode?>>? rowSet in rowSets) {
			if (rowSet is null) {
				continue;
			}

			foreach (Dictionary<string, JsonNode?> row in rowSet) {
				foreach (string columnName in row.Keys.Where(name => !string.IsNullOrWhiteSpace(name))) {
					referencedColumns.Add(columnName);
				}
			}
		}

		return referencedColumns;
	}

	private static List<DataBindingSchemaColumn> SelectBindingSchemaColumns(
		IReadOnlyList<DataBindingSchemaColumn> runtimeSchemaColumns,
		HashSet<string> referencedColumns) =>
		runtimeSchemaColumns
			.Where(column => referencedColumns.Contains(column.Name))
			.ToList();

	private static void ValidateBindingSchemaColumnsSupported(IEnumerable<DataBindingSchemaColumn> schemaColumns) {
		foreach (DataBindingSchemaColumn schemaColumn in schemaColumns) {
			PackageDataBindingWriter.ResolveBindingDataTypeValueUId(schemaColumn);
		}
	}

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

	private bool RowExistsInTable(string schemaName, string rowId) {
		return Guid.TryParse(rowId, out Guid parsedRowId) && bindingWriter.RowExists(schemaName, parsedRowId);
	}

	private static List<DataBindingDbRowInput>? ParseRowsJson(string? json) {
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

		List<DataBindingDbRowInput> result = [];
		foreach (JsonNode? item in array) {
			if (item is JsonObject rowObj &&
				rowObj.TryGetPropertyValue("values", out JsonNode? valuesNode) &&
				valuesNode is JsonObject valuesObj) {
				Dictionary<string, JsonNode?> values = valuesObj.ToDictionary(kv => kv.Key, kv => kv.Value);
				string rowLabel = DescribeRow(values, result.Count);
				rowObj.TryGetPropertyValue("localizations", out JsonNode? localizationsNode);
				result.Add(new DataBindingDbRowInput(
					values,
					new DataBindingRowLocalization(rowLabel, ParseRowLocalizations(localizationsNode, rowLabel))));
			}
		}

		return result;
	}

	private static string DescribeRow(Dictionary<string, JsonNode?> values, int index) {
		foreach (string column in new[] { "Name", "Id" }) {
			if (values.TryGetValue(column, out JsonNode? node) && !string.IsNullOrWhiteSpace(node?.ToString())) {
				return node.ToString();
			}
		}

		return $"#{index + 1}";
	}

	/// <summary>
	/// Parses a row's <c>localizations</c>: culture → column → text value, the shape <c>create-data-binding</c>
	/// takes for its single row.
	/// </summary>
	[System.Diagnostics.CodeAnalysis.SuppressMessage("Minor Code Smell", "S1481:Unused local variables should be removed",
		Justification = "False positive: cultureObject is enumerated by the deconstructing foreach below.")]
	private static IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> ParseRowLocalizations(
		JsonNode? node,
		string rowLabel) {
		Dictionary<string, IReadOnlyDictionary<string, string>> cultures = new(StringComparer.OrdinalIgnoreCase);
		if (node is null) {
			return cultures;
		}

		JsonObject cultureObject = node as JsonObject
			?? throw new InvalidOperationException(
				$"Row '{rowLabel}': 'localizations' must be a JSON object keyed by culture, then column name.");

		foreach ((string culture, JsonNode? columnsNode) in cultureObject) {
			JsonObject? columnObject = string.IsNullOrWhiteSpace(culture) ? null : columnsNode as JsonObject;
			if (columnObject is null) {
				throw new InvalidOperationException(
					$"Row '{rowLabel}': 'localizations' entry '{culture}' must be a JSON object keyed by column name.");
			}

			Dictionary<string, string> values = new(StringComparer.OrdinalIgnoreCase);
			foreach ((string column, JsonNode? valueNode) in columnObject) {
				if (valueNode is not JsonValue jsonValue || !jsonValue.TryGetValue(out string? text)) {
					throw new InvalidOperationException(
						$"Row '{rowLabel}': the '{culture}' value of column '{column}' must be a string.");
				}

				values[column] = text;
			}

			if (values.Count > 0) {
				cultures[culture.Trim()] = values;
			}
		}

		return cultures;
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

	private sealed class SettingValueTypeResponse : SelectQueryHelper.SelectQueryResponseBaseDto {
		[JsonPropertyName("rows")]
		public List<SettingValueTypeRowDto> Rows { get; init; } = [];
	}

	private sealed class SettingValueTypeRowDto {
		[JsonPropertyName("Code")]
		public string? Code { get; init; }

		[JsonPropertyName("ValueTypeName")]
		public string? ValueTypeName { get; init; }
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
/// Per-column install-time matching policy for a DB-first binding: <see cref="KeyColumns"/> are the columns
/// the platform matches on when installing the binding onto a target environment, and
/// <see cref="ForceUpdateColumns"/> are the columns whose values overwrite the target row on install.
/// Without a policy a binding keys on the primary <c>Id</c> column and force-updates nothing — correct for
/// clio-generated rows whose Ids have no counterpart on the target. A policy is required to deliver an
/// environment-random row (for example a <c>SysSettingsValue</c> All-Users default row, whose Id differs per
/// environment) by its natural key with a forced value update, so install merges the existing row instead of
/// inserting a duplicate.
/// </summary>
internal sealed record DataBindingColumnPolicy(
	IReadOnlyCollection<string> KeyColumns,
	IReadOnlyCollection<string> ForceUpdateColumns);

/// <summary>
/// Result of a <see cref="DataBindingDbService.CreateBinding"/> operation.
/// </summary>
public sealed record DataBindingResult(
	string BindingName,
	IReadOnlyList<DataBindingCreatedRow> CreatedRows,
	IReadOnlyList<DataBindingCreatedRow> SkippedRows,
	IReadOnlyList<DataBindingLocalizationWrite> Localizations);

/// <summary>
/// One requested row of <c>create-data-binding-db</c>: its column values and its translations.
/// </summary>
internal sealed record DataBindingDbRowInput(
	Dictionary<string, JsonNode?> Values,
	DataBindingRowLocalization Localization);

/// <summary>
/// Represents a single row created by <see cref="DataBindingDbService.CreateBinding"/>.
/// </summary>
public sealed record DataBindingCreatedRow(
	string Id,
	IReadOnlyDictionary<string, string?> Values);
