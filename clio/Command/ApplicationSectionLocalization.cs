using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Clio.Common;
using Clio.Package;

namespace Clio.Command;

/// <summary>
/// One row of a section's localization table: the non-default culture values of the section's
/// localizable <c>SysModule</c> columns.
/// </summary>
/// <param name="CultureName">Culture name as stored in <c>SysCulture.Name</c>, for example <c>es-ES</c>.</param>
/// <param name="Caption">Section title in this culture.</param>
/// <param name="Description">Section description in this culture.</param>
/// <param name="ModuleHeader">Section module header in this culture.</param>
public sealed record SectionLocalizationRow(
	string CultureName,
	string? Caption,
	string? Description,
	string? ModuleHeader);

/// <summary>
/// Reads and writes the per-culture values of an application section's title and description, and refreshes the
/// application package's <c>SysModule_&lt;SectionCode&gt;</c> data binding after such a write.
/// </summary>
/// <remarks>
/// The section title is <c>SysModule.Caption</c>: the default culture lives in <c>SysModule</c>, every other culture
/// in the <c>SysModule</c> localization table. The <c>ApplicationSection</c> update path of the platform deletes
/// that table's rows for the section on every update, so a caller that must keep translations snapshots them with
/// <see cref="ReadLocalizations"/> first and writes them back with <see cref="WriteLocalizations"/>.
/// </remarks>
public interface IApplicationSectionLocalizationClient {

	/// <summary>
	/// Reads every localization row of the section through DataService <c>SelectLocalizationQuery</c>.
	/// </summary>
	/// <param name="client">Authenticated client of the target environment.</param>
	/// <param name="environmentSettings">Target environment, used to build the route.</param>
	/// <param name="sectionId">Section identifier (<c>ApplicationSection.Id</c> = <c>SysModule.Id</c>).</param>
	/// <returns>One row per non-default culture that has a stored value.</returns>
	/// <exception cref="InvalidOperationException">The query failed.</exception>
	IReadOnlyList<SectionLocalizationRow> ReadLocalizations(
		IApplicationClient client,
		EnvironmentSettings environmentSettings,
		string sectionId);

	/// <summary>
	/// Writes per-culture values through DataService <c>UpdateLocalizationQuery</c> on <c>SysModule</c>. The server
	/// merges per culture: a culture absent from a map keeps its stored value. A <c>Caption</c> map must include the
	/// connected user's culture, or the platform rejects the save as a missing required title.
	/// </summary>
	/// <param name="client">Authenticated client of the target environment.</param>
	/// <param name="environmentSettings">Target environment, used to build the route.</param>
	/// <param name="sectionId">Section identifier.</param>
	/// <param name="columnValues">Column name (<c>Caption</c>, <c>Description</c>, <c>ModuleHeader</c>) → culture → value.</param>
	/// <exception cref="InvalidOperationException">The server rejected the write.</exception>
	void WriteLocalizations(
		IApplicationClient client,
		EnvironmentSettings environmentSettings,
		string sectionId,
		IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> columnValues);

	/// <summary>
	/// Re-saves the package data binding <c>SysModule_&lt;SectionCode&gt;</c> unchanged, so its per-culture data
	/// snapshot re-reads the values a direct localization write stored. The binding keeps its columns and records.
	/// </summary>
	/// <param name="client">Authenticated client of the target environment.</param>
	/// <param name="environmentSettings">Target environment, used to build the routes.</param>
	/// <param name="packageUId">UId of the application's primary package.</param>
	/// <param name="sectionCode">Section code; the binding name is <c>SysModule_&lt;SectionCode&gt;</c>.</param>
	/// <returns><see langword="true"/> when the binding was found and re-saved; <see langword="false"/> when the
	/// package has no such binding.</returns>
	/// <exception cref="InvalidOperationException">A read or the save failed.</exception>
	bool RefreshSectionPackageBinding(
		IApplicationClient client,
		EnvironmentSettings environmentSettings,
		string packageUId,
		string sectionCode);
}

/// <inheritdoc />
public sealed class ApplicationSectionLocalizationClient(IServiceUrlBuilder serviceUrlBuilder)
	: IApplicationSectionLocalizationClient {

	/// <summary>DataService parameter type for a localizable string: a JSON object <c>culture → value</c>.</summary>
	private const int LocalizableStringDataValueType = 19;

	private const string SysModuleSchemaName = "SysModule";
	private const string PackageSchemaDataSchemaName = "SysPackageSchemaData";
	private const string BindingNamePrefix = "SysModule_";

	private static readonly JsonSerializerOptions JsonOptions = new() {
		PropertyNameCaseInsensitive = true
	};

	/// <inheritdoc />
	public IReadOnlyList<SectionLocalizationRow> ReadLocalizations(
		IApplicationClient client,
		EnvironmentSettings environmentSettings,
		string sectionId) {
		ArgumentNullException.ThrowIfNull(client);
		object query = SelectQueryHelper.BuildSelectQuery(
			SysModuleSchemaName,
			[
				new SelectQueryHelper.SelectQueryColumnDefinition("SysCulture.Name", "CultureName"),
				new SelectQueryHelper.SelectQueryColumnDefinition("Caption", "Caption"),
				new SelectQueryHelper.SelectQueryColumnDefinition("Description", "Description"),
				new SelectQueryHelper.SelectQueryColumnDefinition("ModuleHeader", "ModuleHeader")
			],
			[
				new SelectQueryHelper.SelectQueryFilterDefinition(
					"Record", sectionId, SelectQueryHelper.GuidDataValueType)
			]);
		string url = serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.SelectLocalizationQuery, environmentSettings);
		string responseBody = client.ExecutePostRequest(url, JsonSerializer.Serialize(query));
		LocalizationSelectResponse response = ServiceResponseJsonGuard.Deserialize<LocalizationSelectResponse>(
			"SelectLocalizationQuery", url, responseBody, JsonOptions);
		if (!response.Success) {
			throw new InvalidOperationException(
				$"SelectLocalizationQuery failed: {DescribeServerText(response.ErrorInfo?.Message ?? responseBody)}");
		}

		return response.Rows
			.Where(row => !string.IsNullOrWhiteSpace(row.CultureName))
			.Select(row => new SectionLocalizationRow(row.CultureName, row.Caption, row.Description, row.ModuleHeader))
			.ToList();
	}

	/// <inheritdoc />
	public void WriteLocalizations(
		IApplicationClient client,
		EnvironmentSettings environmentSettings,
		string sectionId,
		IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> columnValues) {
		ArgumentNullException.ThrowIfNull(client);
		ArgumentNullException.ThrowIfNull(columnValues);
		Dictionary<string, object> items = columnValues
			.Where(column => column.Value.Count > 0)
			.ToDictionary(
				column => column.Key,
				column => SelectQueryHelper.BuildParameterExpression(
					LocalizableStringDataValueType, JsonSerializer.Serialize(column.Value)),
				StringComparer.Ordinal);
		if (items.Count == 0) {
			return;
		}

		object body = new Dictionary<string, object> {
			["rootSchemaName"] = SysModuleSchemaName,
			["operationType"] = 2,
			["isForceUpdate"] = false,
			["columnValues"] = new { items },
			["filters"] = SelectQueryHelper.BuildIdFilter(sectionId, SelectQueryHelper.GuidDataValueType)
		};
		string responseBody = client.ExecutePostRequest(
			serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.UpdateLocalizationQuery, environmentSettings),
			JsonSerializer.Serialize(body));
		DataServiceResponse.ThrowIfUnsuccessful(responseBody, "UpdateLocalizationQuery");
	}

	/// <inheritdoc />
	public bool RefreshSectionPackageBinding(
		IApplicationClient client,
		EnvironmentSettings environmentSettings,
		string packageUId,
		string sectionCode) {
		ArgumentNullException.ThrowIfNull(client);
		string bindingName = BindingNamePrefix + sectionCode;
		string? bindingUId = FindBindingUId(client, environmentSettings, packageUId, bindingName);
		if (bindingUId is null) {
			return false;
		}

		string getSchemaUrl = serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.GetSchemaDataDesignItem, environmentSettings);
		string getSchemaResponse = client.ExecutePostRequest(
			getSchemaUrl, JsonSerializer.Serialize(new { schemaUId = bindingUId }));
		DataServiceResponse.ThrowIfUnsuccessful(getSchemaResponse, "SchemaDataDesignerService.GetSchema");
		JsonObject schema = JsonNode.Parse(getSchemaResponse)?["schema"] as JsonObject
			?? throw new InvalidOperationException(
				$"SchemaDataDesignerService.GetSchema returned no schema for binding '{bindingName}'.");

		string boundDataResponse = client.ExecutePostRequest(
			serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.GetBoundSchemaData, environmentSettings),
			JsonSerializer.Serialize(new { uId = bindingUId }));
		DataServiceResponse.ThrowIfUnsuccessful(boundDataResponse, "SchemaDataDesignerService.GetBoundSchemaData");
		JsonArray boundRecordIds = ReadBoundRecordIds(boundDataResponse, bindingName);
		schema["boundRecordIds"] = boundRecordIds;

		string saveResponse = client.ExecutePostRequest(
			serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.SaveSchemaData, environmentSettings),
			schema.ToJsonString());
		DataServiceResponse.ThrowIfUnsuccessful(saveResponse, "SchemaDataDesignerService.SaveSchema");
		return true;
	}

	private string? FindBindingUId(
		IApplicationClient client,
		EnvironmentSettings environmentSettings,
		string packageUId,
		string bindingName) {
		object query = SelectQueryHelper.BuildSelectQuery(
			PackageSchemaDataSchemaName,
			[new SelectQueryHelper.SelectQueryColumnDefinition("UId", "UId")],
			[
				new SelectQueryHelper.SelectQueryFilterDefinition(
					"Name", bindingName, SelectQueryHelper.TextDataValueType),
				new SelectQueryHelper.SelectQueryFilterDefinition(
					"SysPackage.UId", packageUId, SelectQueryHelper.GuidDataValueType)
			]);
		string url = serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.Select, environmentSettings);
		string responseBody = client.ExecutePostRequest(url, JsonSerializer.Serialize(query));
		BindingSelectResponse response = ServiceResponseJsonGuard.Deserialize<BindingSelectResponse>(
			"SelectQuery", url, responseBody, JsonOptions);
		if (!response.Success) {
			throw new InvalidOperationException(
				$"SelectQuery failed: {DescribeServerText(response.ErrorInfo?.Message ?? responseBody)}");
		}

		return response.Rows
			.Select(row => row.UId)
			.FirstOrDefault(uid => !string.IsNullOrWhiteSpace(uid));
	}

	/// <summary>
	/// Bounds and redacts server-supplied text before it reaches a message: a failed call can answer with a
	/// whole HTML page, and the text can carry URLs or credential values.
	/// </summary>
	/// <param name="text">Raw server text.</param>
	/// <returns>At most a short preview of the text, with sensitive values replaced.</returns>
	internal static string DescribeServerText(string? text) =>
		// Redact before cutting: a cut inside a credential pair would leave half of it unmatched.
		CreatioResponseError.Truncate(SensitiveErrorTextRedactor.Redact(text));

	private static JsonArray ReadBoundRecordIds(string boundDataResponse, string bindingName) {
		string? itemsJson = JsonNode.Parse(boundDataResponse)?["items"]?.GetValue<string>();
		if (string.IsNullOrWhiteSpace(itemsJson)) {
			throw new InvalidOperationException(
				$"SchemaDataDesignerService.GetBoundSchemaData returned no rows for binding '{bindingName}'.");
		}

		JsonArray items = JsonNode.Parse(itemsJson) as JsonArray ?? [];
		JsonNode[] ids = items
			.Select(item => item?["Id"]?.GetValue<string>())
			.Where(id => !string.IsNullOrWhiteSpace(id))
			.Select(id => (JsonNode)JsonValue.Create(id))
			.ToArray();
		if (ids.Length == 0) {
			throw new InvalidOperationException(
				$"SchemaDataDesignerService.GetBoundSchemaData returned no record ids for binding '{bindingName}'.");
		}

		return new JsonArray(ids);
	}

	private sealed class LocalizationSelectResponse : SelectQueryHelper.SelectQueryResponseBaseDto {
		[JsonPropertyName("rows")]
		public List<LocalizationRowDto> Rows { get; set; } = [];
	}

	private sealed class LocalizationRowDto {
		[JsonPropertyName("CultureName")]
		public string? CultureName { get; set; }

		[JsonPropertyName("Caption")]
		public string? Caption { get; set; }

		[JsonPropertyName("Description")]
		public string? Description { get; set; }

		[JsonPropertyName("ModuleHeader")]
		public string? ModuleHeader { get; set; }
	}

	private sealed class BindingSelectResponse : SelectQueryHelper.SelectQueryResponseBaseDto {
		[JsonPropertyName("rows")]
		public List<BindingRowDto> Rows { get; set; } = [];
	}

	private sealed class BindingRowDto {
		[JsonPropertyName("UId")]
		public string? UId { get; set; }
	}
}
