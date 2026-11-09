using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Clio.Command.EntitySchemaDesigner;
using Clio.Command.Localization;
using Clio.Common;
using Clio.Package;

namespace Clio.Command;

/// <summary>
/// The translations requested for one row of a DB-first data binding.
/// </summary>
/// <param name="RowLabel">How the row is named in messages: its <c>Name</c>, else its <c>Id</c>, else its position.</param>
/// <param name="Cultures">Culture name (for example <c>uk-UA</c>) → column name → value.</param>
public sealed record DataBindingRowLocalization(
	string RowLabel,
	IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Cultures);

/// <summary>
/// What was written and verified for one row in one culture.
/// </summary>
/// <param name="RowId">Identifier of the record.</param>
/// <param name="RowLabel">How the row is named in messages.</param>
/// <param name="Culture">Culture name.</param>
/// <param name="Columns">The columns written in that culture.</param>
public sealed record DataBindingLocalizationWrite(
	string RowId,
	string RowLabel,
	string Culture,
	IReadOnlyList<string> Columns);

/// <summary>
/// A localization write that failed or did not verify, with the rows and cultures already written before it stopped.
/// </summary>
public sealed class DataBindingLocalizationException : InvalidOperationException {

	/// <summary>Initializes a new instance of the <see cref="DataBindingLocalizationException"/> class.</summary>
	/// <param name="message">What failed.</param>
	/// <param name="written">The rows and cultures written before the failure.</param>
	/// <param name="innerException">The underlying failure, if any.</param>
	public DataBindingLocalizationException(
		string message,
		IReadOnlyList<DataBindingLocalizationWrite> written,
		Exception? innerException = null)
		: base(message, innerException) {
		Written = written;
	}

	/// <summary>The rows and cultures that were written before the failure.</summary>
	public IReadOnlyList<DataBindingLocalizationWrite> Written { get; }
}

/// <summary>
/// The context a localization write needs, resolved before any row of the binding is written.
/// </summary>
/// <param name="ProfileCulture">Culture of the connected user's profile: the culture plain inserts and updates
/// write to.</param>
/// <param name="CultureNames">Requested culture name (any letter case) → the site's spelling of it; a culture
/// missing from it is sent as requested.</param>
internal sealed record DataBindingLocalizationPlan(
	string ProfileCulture,
	IReadOnlyDictionary<string, string>? CultureNames = null) {

	/// <summary>Returns the site's spelling of <paramref name="culture"/> (<c>ru-ru</c> → <c>ru-RU</c>).</summary>
	public string ToSiteCultureName(string culture) =>
		CultureNames is not null && CultureNames.TryGetValue(culture, out string? siteName) ? siteName : culture;
}

/// <summary>
/// Writes the per-culture values of localizable columns of DB-first data binding rows.
/// </summary>
/// <remarks>
/// A plain insert or update writes a localizable column in the connected user's profile culture only. The other
/// cultures go through DataService <c>UpdateLocalizationQuery</c>. The package binding keeps one snapshot per
/// culture, taken when the binding is saved, so the caller must save the binding after <see cref="Write"/>.
/// </remarks>
internal interface IDataBindingDbLocalizer {

	/// <summary>
	/// Checks every requested translation before anything is written: each culture must be active on the site,
	/// each column must exist and be localizable, and each value must fit the column.
	/// </summary>
	/// <param name="schemaName">Entity schema of the binding.</param>
	/// <param name="packageUId">Package the binding belongs to; the schema is read as seen from it.</param>
	/// <param name="runtimeColumns">Runtime columns of the schema, used for the text length of each column.</param>
	/// <param name="rows">Requested translations; rows without any are ignored.</param>
	/// <returns>The plan to pass to <see cref="Write"/>, or <see langword="null"/> when no row asks for a
	/// translation.</returns>
	/// <exception cref="InvalidOperationException">Any check failed; the message lists every failure.</exception>
	DataBindingLocalizationPlan? Validate(
		string schemaName,
		Guid packageUId,
		IReadOnlyList<DataBindingSchemaColumn> runtimeColumns,
		IReadOnlyList<DataBindingRowLocalization> rows);

	/// <summary>
	/// Writes the translations of each row, then reads every culture back and compares it with the request.
	/// </summary>
	/// <param name="plan">The plan <see cref="Validate"/> returned.</param>
	/// <param name="schemaName">Entity schema of the binding.</param>
	/// <param name="rows">Record identifier and requested translations of each row.</param>
	/// <returns>One entry per row and culture written.</returns>
	/// <exception cref="DataBindingLocalizationException">A write failed, or a value read back differs from the request
	/// (the message says the values were saved but not verified); it carries the rows and cultures already written.
	/// </exception>
	IReadOnlyList<DataBindingLocalizationWrite> Write(
		DataBindingLocalizationPlan plan,
		string schemaName,
		IReadOnlyList<(string RowId, DataBindingRowLocalization Localization)> rows);
}

/// <inheritdoc />
internal sealed class DataBindingDbLocalizer(
	IApplicationClient applicationClient,
	IServiceUrlBuilder serviceUrlBuilder,
	ICreatioCultureCatalog cultureCatalog,
	IRemoteEntitySchemaDesignerClient designerClient,
	ICurrentUserCultureResolverFactory cultureResolverFactory,
	EnvironmentSettings environmentSettings) : IDataBindingDbLocalizer {

	/// <summary>DataService parameter type for a localizable string: a JSON object <c>culture → value</c>.</summary>
	private const int LocalizableStringDataValueType = 19;

	private const string CultureNameAlias = "CultureName";

	/// <summary>Maximum length of the fixed-size runtime text types; the other text types are not limited.</summary>
	private static readonly IReadOnlyDictionary<int, int> TextLengthByDataValueType = new Dictionary<int, int> {
		[27] = 50, // ShortText
		[28] = 250, // MediumText
		[30] = 500 // LongText
	};

	/// <inheritdoc />
	public DataBindingLocalizationPlan? Validate(
		string schemaName,
		Guid packageUId,
		IReadOnlyList<DataBindingSchemaColumn> runtimeColumns,
		IReadOnlyList<DataBindingRowLocalization> rows) {
		List<DataBindingRowLocalization> localizedRows = rows.Where(row => row.Cultures.Count > 0).ToList();
		if (localizedRows.Count == 0) {
			return null;
		}

		List<string> failures = [];
		Dictionary<string, string> cultureNames = ValidateCultures(localizedRows, failures);
		ValidateColumnsAndValues(schemaName, packageUId, runtimeColumns, localizedRows, failures);
		if (failures.Count > 0) {
			throw new InvalidOperationException(
				"Localizations were refused and nothing was written: " + string.Join(" ", failures));
		}

		using IOwnedCurrentUserCultureResolver resolver = cultureResolverFactory.CreateOwned(environmentSettings);
		Clio.Command.EntitySchemaDesigner.CultureResolution profileCulture = resolver.ResolveAsync().GetAwaiter().GetResult();
		if (!profileCulture.Success) {
			throw new InvalidOperationException(
				"Localizations were refused and nothing was written: the culture of the connected user could not "
				+ $"be read ({profileCulture.FailureReason}).");
		}

		return new DataBindingLocalizationPlan(profileCulture.Culture, cultureNames);
	}

	/// <inheritdoc />
	public IReadOnlyList<DataBindingLocalizationWrite> Write(
		DataBindingLocalizationPlan plan,
		string schemaName,
		IReadOnlyList<(string RowId, DataBindingRowLocalization Localization)> rows) {
		ArgumentNullException.ThrowIfNull(plan);
		List<DataBindingLocalizationWrite> written = [];
		List<string> mismatches = [];
		foreach ((string rowId, DataBindingRowLocalization localization) in rows) {
			if (localization.Cultures.Count == 0) {
				continue;
			}

			List<string> columns = localization.Cultures.Values
				.SelectMany(cultureValues => cultureValues.Keys)
				.Distinct(StringComparer.OrdinalIgnoreCase)
				.ToList();
			try {
				IReadOnlyDictionary<string, string?> profileValues = ReadProfileValues(schemaName, rowId, columns);
				WriteRow(plan, schemaName, rowId, localization, columns, profileValues);
				written.AddRange(localization.Cultures.Select(culture => new DataBindingLocalizationWrite(
					rowId, localization.RowLabel, plan.ToSiteCultureName(culture.Key), culture.Value.Keys.ToList())));
				mismatches.AddRange(VerifyRow(plan, schemaName, rowId, localization, columns));
			}
			catch (Exception exception) when (exception is not OperationCanceledException) {
				throw new DataBindingLocalizationException(
					$"Localizing row '{localization.RowLabel}' ({rowId}) failed: {exception.Message}"
					+ DescribeMismatches(mismatches),
					written,
					exception);
			}
		}

		if (mismatches.Count > 0) {
			throw new DataBindingLocalizationException(
				"Localizations were saved, but NOT verified: the values read back differ from the request for "
				+ string.Join(", ", mismatches) + ". Check the records in Creatio before relying on them.",
				written);
		}

		return written;
	}

	private static string DescribeMismatches(IReadOnlyList<string> mismatches) =>
		mismatches.Count == 0
			? string.Empty
			: " Earlier rows were saved, but NOT verified: the values read back differ from the request for "
				+ string.Join(", ", mismatches) + ".";

	/// <returns>Each requested culture that is active on the site → the site's spelling of its name.</returns>
	private Dictionary<string, string> ValidateCultures(IReadOnlyList<DataBindingRowLocalization> rows, List<string> failures) {
		IReadOnlyList<CreatioCulture> siteCultures = cultureCatalog.GetCultures();
		string active = string.Join(", ", siteCultures.Where(culture => culture.Active).Select(culture => culture.Name));
		IEnumerable<string> requested = rows
			.SelectMany(row => row.Cultures.Keys)
			.Distinct(StringComparer.OrdinalIgnoreCase);
		Dictionary<string, string> cultureNames = new(StringComparer.OrdinalIgnoreCase);
		foreach (string culture in requested) {
			CreatioCulture? match = siteCultures.FirstOrDefault(candidate =>
				string.Equals(candidate.Name, culture, StringComparison.OrdinalIgnoreCase));
			if (match is null || !match.Active) {
				failures.Add($"Culture '{culture}' is not active on the site (active cultures: {active}).");
			} else {
				cultureNames[culture] = match.Name;
			}
		}

		return cultureNames;
	}

	private void ValidateColumnsAndValues(
		string schemaName,
		Guid packageUId,
		IReadOnlyList<DataBindingSchemaColumn> runtimeColumns,
		IReadOnlyList<DataBindingRowLocalization> rows,
		List<string> failures) {
		EntityDesignSchemaDto designSchema = designerClient.GetSchemaDesignItem(
				new GetSchemaDesignItemRequestDto {
					Name = schemaName,
					PackageUId = packageUId,
					UseFullHierarchy = true,
					Cultures = [EntitySchemaDesignerSupport.DefaultCultureName]
				},
				new RemoteCommandOptions())
			.Schema
			?? throw new InvalidOperationException($"Entity schema '{schemaName}' was not returned by the designer.");

		HashSet<string> reportedColumns = new(StringComparer.OrdinalIgnoreCase);
		foreach (DataBindingRowLocalization row in rows) {
			foreach ((string culture, IReadOnlyDictionary<string, string> values) in row.Cultures) {
				foreach ((string columnName, string value) in values) {
					EntitySchemaColumnDto? designColumn = EntitySchemaDesignerSupport.FindOwnColumn(designSchema, columnName)
						?? EntitySchemaDesignerSupport.FindInheritedColumn(designSchema, columnName);
					if (designColumn is null || !designColumn.LocalizableText) {
						if (reportedColumns.Add(columnName)) {
							failures.Add(designColumn is null
								? $"Column '{columnName}' is not a column of '{schemaName}'."
								: $"Column '{columnName}' of '{schemaName}' is not localizable.");
						}

						continue;
					}

					int? maxLength = ResolveMaxLength(runtimeColumns, columnName);
					if (maxLength.HasValue && value.Length > maxLength.Value) {
						failures.Add(
							$"Row '{row.RowLabel}' [{culture}] column '{columnName}': {value.Length} characters, the "
							+ $"column allows {maxLength.Value}.");
					}
				}
			}
		}
	}

	private static int? ResolveMaxLength(IReadOnlyList<DataBindingSchemaColumn> runtimeColumns, string columnName) {
		DataBindingSchemaColumn? column = runtimeColumns.FirstOrDefault(candidate =>
			string.Equals(candidate.Name, columnName, StringComparison.OrdinalIgnoreCase));
		return column is not null && TextLengthByDataValueType.TryGetValue(column.DataValueType, out int length)
			? length
			: null;
	}

	/// <summary>
	/// Reads the row's current values of <paramref name="columns"/>. A plain select answers in the connected user's
	/// culture, which is the culture an insert or update of the row wrote.
	/// </summary>
	private IReadOnlyDictionary<string, string?> ReadProfileValues(
		string schemaName,
		string rowId,
		IReadOnlyList<string> columns) {
		JsonArray rows = ReadRows(
			ServiceUrlBuilder.KnownRoute.Select,
			"SelectQuery",
			SelectQueryHelper.BuildSelectQuery(
				schemaName,
				columns.Select(column => new SelectQueryHelper.SelectQueryColumnDefinition(column, column)).ToList(),
				[new SelectQueryHelper.SelectQueryFilterDefinition("Id", rowId, SelectQueryHelper.GuidDataValueType)]));
		JsonObject? row = rows.FirstOrDefault() as JsonObject
			?? throw new InvalidOperationException($"Record '{rowId}' of '{schemaName}' was not found.");
		return columns.ToDictionary(
			column => column,
			column => row[column]?.GetValue<string>(),
			StringComparer.OrdinalIgnoreCase);
	}

	private void WriteRow(
		DataBindingLocalizationPlan plan,
		string schemaName,
		string rowId,
		DataBindingRowLocalization localization,
		IReadOnlyList<string> columns,
		IReadOnlyDictionary<string, string?> profileValues) {
		Dictionary<string, object> items = new(StringComparer.Ordinal);
		foreach (string column in columns) {
			Dictionary<string, string> cultureValues = new(StringComparer.OrdinalIgnoreCase);
			foreach ((string culture, IReadOnlyDictionary<string, string> values) in localization.Cultures) {
				string? value = values.FirstOrDefault(pair =>
					string.Equals(pair.Key, column, StringComparison.OrdinalIgnoreCase)).Value;
				if (value is not null) {
					cultureValues[plan.ToSiteCultureName(culture)] = value;
				}
			}

			// The platform rebuilds the column value from this map, and a required column such as Name rejects
			// the save when the profile culture is missing from it, so the value the row already has goes along.
			if (!cultureValues.ContainsKey(plan.ProfileCulture)
				&& profileValues.TryGetValue(column, out string? profileValue)
				&& !string.IsNullOrEmpty(profileValue)) {
				cultureValues[plan.ProfileCulture] = profileValue;
			}

			items[column] = SelectQueryHelper.BuildParameterExpression(
				LocalizableStringDataValueType, JsonSerializer.Serialize(cultureValues));
		}

		object body = new Dictionary<string, object> {
			["rootSchemaName"] = schemaName,
			["operationType"] = 2,
			["isForceUpdate"] = false,
			["columnValues"] = new { items },
			["filters"] = SelectQueryHelper.BuildIdFilter(rowId, SelectQueryHelper.GuidDataValueType)
		};
		string response = applicationClient.ExecutePostRequest(
			serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.UpdateLocalizationQuery),
			JsonSerializer.Serialize(body));
		DataServiceResponse.ThrowIfUnsuccessful(response, "UpdateLocalizationQuery");
	}

	/// <summary>
	/// Compares the stored value of every requested culture and column with the request.
	/// </summary>
	/// <returns>One label per value that differs, empty when every value matches.</returns>
	private IEnumerable<string> VerifyRow(
		DataBindingLocalizationPlan plan,
		string schemaName,
		string rowId,
		DataBindingRowLocalization localization,
		IReadOnlyList<string> columns) {
		List<SelectQueryHelper.SelectQueryColumnDefinition> selectColumns =
			[new("SysCulture.Name", CultureNameAlias), .. columns.Select(column => new SelectQueryHelper.SelectQueryColumnDefinition(column, column))];
		JsonArray stored = ReadRows(
			ServiceUrlBuilder.KnownRoute.SelectLocalizationQuery,
			"SelectLocalizationQuery",
			SelectQueryHelper.BuildSelectQuery(
				schemaName,
				selectColumns,
				[new SelectQueryHelper.SelectQueryFilterDefinition("Record", rowId, SelectQueryHelper.GuidDataValueType)]));
		IReadOnlyDictionary<string, string?>? profileValues = null;
		List<string> mismatches = [];
		foreach ((string culture, IReadOnlyDictionary<string, string> values) in localization.Cultures) {
			JsonObject? cultureRow = stored.OfType<JsonObject>().FirstOrDefault(row =>
				string.Equals(row[CultureNameAlias]?.GetValue<string>(), culture, StringComparison.OrdinalIgnoreCase));
			// The culture whose value lives in the entity table itself has no localization row; when it is the
			// profile culture, a plain select reads it.
			IReadOnlyDictionary<string, string?>? tableValues =
				cultureRow is null && string.Equals(culture, plan.ProfileCulture, StringComparison.OrdinalIgnoreCase)
					? profileValues ??= ReadProfileValues(schemaName, rowId, columns)
					: null;

			foreach ((string column, string expected) in values) {
				string? actual = cultureRow is not null
					? cultureRow[column]?.GetValue<string>()
					: tableValues?.GetValueOrDefault(column);
				if (!string.Equals(actual, expected, StringComparison.Ordinal)) {
					mismatches.Add($"'{localization.RowLabel}' {column} [{culture}]");
				}
			}
		}

		return mismatches;
	}

	private JsonArray ReadRows(ServiceUrlBuilder.KnownRoute route, string operation, object query) {
		string url = serviceUrlBuilder.Build(route);
		string response = applicationClient.ExecutePostRequest(url, JsonSerializer.Serialize(query));
		DataServiceResponse.ThrowIfUnsuccessful(response, operation);
		return JsonNode.Parse(response)?["rows"] as JsonArray ?? [];
	}
}
