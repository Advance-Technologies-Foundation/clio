using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Clio.Common;
using Clio.Package;

namespace Clio.Command;

/// <summary>
/// One package data binding as the environment stores it: the descriptor, the <c>data.json</c> snapshot and one
/// <c>Localization/data.&lt;culture&gt;.json</c> snapshot per culture that has rows.
/// </summary>
/// <param name="Descriptor">The descriptor, with the registration's UId, name, ModifiedOn, install type, entity
/// schema and columns.</param>
/// <param name="Data">The stored <c>data.json</c> text, verbatim.</param>
/// <param name="Localizations">The stored <c>Localization/data.&lt;culture&gt;.json</c> text, verbatim, keyed by
/// culture name; a culture whose snapshot carries no rows is left out.</param>
internal sealed record DataBindingSnapshot(
	DataBindingDescriptorFile Descriptor,
	string Data,
	IReadOnlyDictionary<string, string> Localizations);

/// <summary>
/// Reads one package data binding from the environment as the platform's package export writes it.
/// </summary>
/// <remarks>
/// The export does not rebuild a binding from the live rows: it serializes what the binding stores. The
/// registration (<c>SysPackageSchemaData</c>) and its columns (<c>SysPackageSchemaDataColumn</c>) make the
/// descriptor, <c>SysPackageSchemaData.Data</c> is the <c>data.json</c> text and <c>SysPackageDataLcz.Data</c> is the
/// <c>Localization/data.&lt;culture&gt;.json</c> text of each culture. Reading those keeps the value shapes the export
/// writes (an empty lookup as stored, a localizable column inline when the binding has no culture snapshot)
/// without re-deriving them.
/// </remarks>
internal interface IDataBindingSnapshotReader {

	/// <summary>Reads the binding <paramref name="bindingName"/> of package <paramref name="packageName"/>.</summary>
	/// <exception cref="InvalidOperationException">The package has no binding by that name on the environment,
	/// the binding has no columns, or a stored snapshot is not package data JSON.</exception>
	DataBindingSnapshot Read(string packageName, string bindingName);
}

/// <inheritdoc />
internal sealed class DataBindingSnapshotReader(
	IApplicationClient applicationClient,
	IServiceUrlBuilder serviceUrlBuilder) : IDataBindingSnapshotReader {

	private const string RegistrationSchemaName = "SysPackageSchemaData";
	private const string ColumnSchemaName = "SysPackageSchemaDataColumn";
	private const string LocalizationSchemaName = "SysPackageDataLcz";

	/// <inheritdoc />
	public DataBindingSnapshot Read(string packageName, string bindingName) {
		RegistrationRow registration = ReadRegistration(packageName, bindingName);
		DataBindingDescriptorFile descriptor = new() {
			Descriptor = new DataBindingDescriptor {
				UId = registration.UId,
				Name = registration.Name,
				ModifiedOnUtc = ReadModifiedOnUtc(registration.Id),
				InstallType = registration.InstallType,
				Schema = new DataBindingSchemaRef {
					UId = registration.SchemaUId,
					Name = registration.SchemaName
				},
				Columns = ReadColumns(registration.Id, bindingName)
			}
		};
		string data = ReadSnapshot(registration.Data, bindingName, "data.json")?.Text
			?? JsonSerializer.Serialize(new DataBindingPackageDataFile(), DataBindingJson.Options);
		return new DataBindingSnapshot(descriptor, data, ReadLocalizations(registration.Id, bindingName));
	}

	/// <summary>
	/// Reads the registration by package and binding name in one select, so a missing package and a missing
	/// binding are the same "not found" answer.
	/// </summary>
	private RegistrationRow ReadRegistration(string packageName, string bindingName) {
		List<RegistrationRow> rows = Select<RegistrationRow>(
			RegistrationSchemaName,
			[
				Column("Id"), Column("UId"), Column("Name"), Column("InstallType"),
				new SelectQueryHelper.SelectQueryColumnDefinition("SysSchema.UId", "SchemaUId"),
				new SelectQueryHelper.SelectQueryColumnDefinition("SysSchema.Name", "SchemaName"),
				Column("Data")
			],
			[
				new SelectQueryHelper.SelectQueryFilterDefinition(
					"Name", bindingName, SelectQueryHelper.TextDataValueType),
				new SelectQueryHelper.SelectQueryFilterDefinition(
					"SysPackage.Name", packageName, SelectQueryHelper.TextDataValueType)
			]);
		return rows.Count switch {
			0 => throw new InvalidOperationException(
				$"Binding '{bindingName}' was not found in package '{packageName}' on the environment."),
			1 => rows[0],
			_ => throw new InvalidOperationException(
				$"Binding '{bindingName}' has {rows.Count} registrations in package '{packageName}', so it is "
				+ "not clear which one to write.")
		};
	}

	private List<DataBindingColumnDefinition> ReadColumns(Guid registrationId, string bindingName) {
		List<DataBindingColumnDefinition> columns = Select<ColumnRow>(
				ColumnSchemaName,
				[
					new SelectQueryHelper.SelectQueryColumnDefinition("PackageSchemaColumnUId", "ColumnUId"),
					Column("ColumnName"), Column("IsForceUpdate"), Column("IsKey"),
					new SelectQueryHelper.SelectQueryColumnDefinition("DataValueTypeUId", "DataTypeValueUId")
				],
				[RegistrationFilter(registrationId)])
			.Select(row => new DataBindingColumnDefinition {
				ColumnUId = row.ColumnUId,
				ColumnName = row.ColumnName,
				IsForceUpdate = row.IsForceUpdate,
				IsKey = row.IsKey,
				DataTypeValueUId = row.DataTypeValueUId
			})
			.OrderBy(column => column.ColumnName, StringComparer.Ordinal)
			.ToList();
		if (columns.Count == 0) {
			throw new InvalidOperationException($"Binding '{bindingName}' has no columns on the environment.");
		}

		return columns;
	}

	private Dictionary<string, string> ReadLocalizations(Guid registrationId, string bindingName) {
		Dictionary<string, string> localizations = new(StringComparer.OrdinalIgnoreCase);
		foreach (LocalizationRow row in Select<LocalizationRow>(
					LocalizationSchemaName,
					[new SelectQueryHelper.SelectQueryColumnDefinition("SysCulture.Name", "CultureName"), Column("Data")],
					[RegistrationFilter(registrationId)])) {
			StoredSnapshot? snapshot = ReadSnapshot(row.Data, bindingName, $"Localization/data.{row.CultureName}.json");
			if (snapshot is { RowCount: > 0 }) {
				localizations[row.CultureName] = snapshot.Text;
			}
		}

		return localizations;
	}

	/// <summary>
	/// Reads the registration's <c>ModifiedOn</c> in UTC, which the descriptor carries as <c>ModifiedOnUtc</c>.
	/// </summary>
	/// <remarks>
	/// Over OData, not DataService: a DataService select returns a date in the connected user's time zone without
	/// an offset, while OData returns it in UTC.
	/// </remarks>
	private string ReadModifiedOnUtc(Guid registrationId) {
		string url = serviceUrlBuilder.Build($"odata/{RegistrationSchemaName}({registrationId})?$select=ModifiedOn");
		string response = applicationClient.ExecuteGetRequest(url);
		string? modifiedOn = JsonNode.Parse(response)?["ModifiedOn"]?.GetValue<string>();
		if (!DateTimeOffset.TryParse(modifiedOn, CultureInfo.InvariantCulture,
				DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out DateTimeOffset modifiedOnUtc)) {
			throw new InvalidOperationException(
				$"The environment returned no readable ModifiedOn for binding registration '{registrationId}'.");
		}

		return $"/Date({modifiedOnUtc.ToUnixTimeMilliseconds()})/";
	}

	/// <summary>
	/// Returns a stored snapshot verbatim with its row count, or <see langword="null"/> when none is stored. A
	/// snapshot that is not package data JSON is refused rather than written.
	/// </summary>
	private static StoredSnapshot? ReadSnapshot(string? text, string bindingName, string fileName) {
		if (string.IsNullOrWhiteSpace(text)) {
			return null;
		}

		try {
			if (JsonNode.Parse(text)?["PackageData"] is JsonArray packageData) {
				return new StoredSnapshot(text, packageData.Count);
			}
		}
		catch (JsonException) {
			// Reported below with the binding and the file it would have become.
		}

		throw new InvalidOperationException(
			$"The stored {fileName} of binding '{bindingName}' is not package data JSON, so it was not written.");
	}

	private List<TRow> Select<TRow>(
		string schemaName,
		IReadOnlyList<SelectQueryHelper.SelectQueryColumnDefinition> columns,
		IReadOnlyList<SelectQueryHelper.SelectQueryFilterDefinition> filters) =>
		SelectQueryHelper.ExecuteSelectQuery<SelectResponse<TRow>>(
				applicationClient, serviceUrlBuilder, SelectQueryHelper.BuildSelectQuery(schemaName, columns, filters))
			.Rows;

	private static SelectQueryHelper.SelectQueryFilterDefinition RegistrationFilter(Guid registrationId) =>
		new("SysPackageSchemaData.Id", registrationId.ToString(), SelectQueryHelper.GuidDataValueType);

	private static SelectQueryHelper.SelectQueryColumnDefinition Column(string path) => new(path, path);

	private sealed record StoredSnapshot(string Text, int RowCount);

	private sealed class SelectResponse<TRow> : SelectQueryHelper.SelectQueryResponseBaseDto {
		[JsonPropertyName("rows")]
		public List<TRow> Rows { get; init; } = [];
	}

	private sealed class RegistrationRow {
		public Guid Id { get; init; }
		public Guid UId { get; init; }
		public string Name { get; init; } = string.Empty;
		public int InstallType { get; init; }
		public Guid SchemaUId { get; init; }
		public string SchemaName { get; init; } = string.Empty;
		public string? Data { get; init; }
	}

	private sealed class ColumnRow {
		public Guid ColumnUId { get; init; }
		public string ColumnName { get; init; } = string.Empty;
		public bool IsForceUpdate { get; init; }
		public bool IsKey { get; init; }
		public Guid DataTypeValueUId { get; init; }
	}

	private sealed class LocalizationRow {
		public string CultureName { get; init; } = string.Empty;
		public string? Data { get; init; }
	}
}
