using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
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
/// writes (an empty lookup as <c>"null"</c>, a localizable column inline when the binding has no culture snapshot)
/// without re-deriving them.
/// </remarks>
internal interface IDataBindingSnapshotReader {

	/// <summary>Reads the binding <paramref name="bindingName"/> of package <paramref name="packageName"/>.</summary>
	/// <exception cref="InvalidOperationException">The package or the binding does not exist on the environment,
	/// the binding has no columns, or a stored snapshot is not package data JSON.</exception>
	DataBindingSnapshot Read(string packageName, string bindingName);
}

/// <inheritdoc />
internal sealed class DataBindingSnapshotReader(
	IApplicationClient applicationClient,
	IServiceUrlBuilder serviceUrlBuilder,
	IPackageDataBindingWriter bindingWriter) : IDataBindingSnapshotReader {

	private const string RegistrationSchemaName = "SysPackageSchemaData";
	private const string ColumnSchemaName = "SysPackageSchemaDataColumn";
	private const string LocalizationSchemaName = "SysPackageDataLcz";
	private const string RowsProperty = "rows";

	/// <inheritdoc />
	public DataBindingSnapshot Read(string packageName, string bindingName) {
		PackageRef package = bindingWriter.ResolvePackage(packageName);
		JsonObject registration = ReadRegistration(package, bindingName);
		string registrationId = ReadString(registration, "Id");
		List<DataBindingColumnDefinition> columns = ReadColumns(registrationId, bindingName);
		DataBindingDescriptorFile descriptor = new() {
			Descriptor = new DataBindingDescriptor {
				UId = Guid.Parse(ReadString(registration, "UId")),
				Name = ReadString(registration, "Name"),
				ModifiedOnUtc = ReadModifiedOnUtc(registrationId),
				InstallType = registration["InstallType"]?.GetValue<int>() ?? 0,
				Schema = new DataBindingSchemaRef {
					UId = Guid.Parse(ReadString(registration, "SchemaUId")),
					Name = ReadString(registration, "SchemaName")
				},
				Columns = columns
			}
		};
		string data = ReadSnapshot(registration["Data"], bindingName, "data.json")
			?? JsonSerializer.Serialize(new DataBindingPackageDataFile(), DataBindingJson.Options);
		return new DataBindingSnapshot(descriptor, data, ReadLocalizations(registrationId, bindingName));
	}

	private JsonObject ReadRegistration(PackageRef package, string bindingName) {
		JsonArray rows = ReadRows(SelectQueryHelper.BuildSelectQuery(
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
					"SysPackage.UId", package.UId.ToString(), SelectQueryHelper.GuidDataValueType)
			]));
		return rows.Count switch {
			0 => throw new InvalidOperationException(
				$"Binding '{bindingName}' was not found in package '{package.Name}' on the environment."),
			1 => rows[0] as JsonObject ?? throw new InvalidOperationException(
				$"The environment returned an unreadable registration for binding '{bindingName}'."),
			_ => throw new InvalidOperationException(
				$"Binding '{bindingName}' has {rows.Count} registrations in package '{package.Name}', so it is "
				+ "not clear which one to write.")
		};
	}

	private List<DataBindingColumnDefinition> ReadColumns(string registrationId, string bindingName) {
		JsonArray rows = ReadRows(SelectQueryHelper.BuildSelectQuery(
			ColumnSchemaName,
			[
				new SelectQueryHelper.SelectQueryColumnDefinition("PackageSchemaColumnUId", "ColumnUId"),
				Column("ColumnName"), Column("IsForceUpdate"), Column("IsKey"),
				new SelectQueryHelper.SelectQueryColumnDefinition("DataValueTypeUId", "DataTypeValueUId")
			],
			[
				new SelectQueryHelper.SelectQueryFilterDefinition(
					"SysPackageSchemaData.Id", registrationId, SelectQueryHelper.GuidDataValueType)
			]));
		List<DataBindingColumnDefinition> columns = rows.OfType<JsonObject>()
			.Select(row => new DataBindingColumnDefinition {
				ColumnUId = Guid.Parse(ReadString(row, "ColumnUId")),
				ColumnName = ReadString(row, "ColumnName"),
				IsForceUpdate = row["IsForceUpdate"]?.GetValue<bool>() ?? false,
				IsKey = row["IsKey"]?.GetValue<bool>() ?? false,
				DataTypeValueUId = Guid.Parse(ReadString(row, "DataTypeValueUId"))
			})
			.OrderBy(column => column.ColumnName, StringComparer.Ordinal)
			.ToList();
		if (columns.Count == 0) {
			throw new InvalidOperationException($"Binding '{bindingName}' has no columns on the environment.");
		}

		return columns;
	}

	private Dictionary<string, string> ReadLocalizations(string registrationId, string bindingName) {
		JsonArray rows = ReadRows(SelectQueryHelper.BuildSelectQuery(
			LocalizationSchemaName,
			[new SelectQueryHelper.SelectQueryColumnDefinition("SysCulture.Name", "CultureName"), Column("Data")],
			[
				new SelectQueryHelper.SelectQueryFilterDefinition(
					"SysPackageSchemaData.Id", registrationId, SelectQueryHelper.GuidDataValueType)
			]));
		Dictionary<string, string> localizations = new(StringComparer.OrdinalIgnoreCase);
		foreach (JsonObject row in rows.OfType<JsonObject>()) {
			string culture = ReadString(row, "CultureName");
			string? snapshot = ReadSnapshot(row["Data"], bindingName, $"Localization/data.{culture}.json");
			if (snapshot is not null && HasRows(snapshot)) {
				localizations[culture] = snapshot;
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
	private string ReadModifiedOnUtc(string registrationId) {
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
	/// Returns a stored snapshot verbatim, or <see langword="null"/> when none is stored. A snapshot that is not
	/// package data JSON is refused rather than written.
	/// </summary>
	private static string? ReadSnapshot(JsonNode? node, string bindingName, string fileName) {
		string? text = node?.GetValue<string>();
		if (string.IsNullOrWhiteSpace(text)) {
			return null;
		}

		try {
			if (JsonNode.Parse(text)?["PackageData"] is JsonArray) {
				return text;
			}
		}
		catch (JsonException) {
			// Reported below with the binding and the file it would have become.
		}

		throw new InvalidOperationException(
			$"The stored {fileName} of binding '{bindingName}' is not package data JSON, so it was not written.");
	}

	private static bool HasRows(string snapshot) =>
		JsonNode.Parse(snapshot)?["PackageData"] is JsonArray { Count: > 0 };

	private JsonArray ReadRows(object query) {
		string response = applicationClient.ExecutePostRequest(
			serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.Select),
			JsonSerializer.Serialize(query));
		DataServiceResponse.ThrowIfUnsuccessful(response, "SelectQuery");
		return JsonNode.Parse(response)?[RowsProperty] as JsonArray ?? [];
	}

	private static string ReadString(JsonObject row, string property) =>
		row[property]?.GetValue<string>()
		?? throw new InvalidOperationException($"The environment returned no '{property}' for a binding read.");

	private static SelectQueryHelper.SelectQueryColumnDefinition Column(string path) => new(path, path);
}
