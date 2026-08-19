namespace Clio.Command;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using Clio.Common;
using Clio.Package;
using Newtonsoft.Json.Linq;

/// <summary>One Classic list column as the saved grid profile declares it.</summary>
/// <param name="Path">Column path the profile binds the cell to.</param>
/// <param name="Caption">Caption the profile stores for the column; <see langword="null"/> when it stores none.</param>
public sealed record ClassicListProfileColumn(string Path, string Caption);

/// <summary>Columns read out of a Classic section's saved grid profile.</summary>
/// <param name="Columns">
/// Ordered columns of the view the section actually opens with; empty when the stand holds no usable profile
/// for the section, which is the normal case for a section nobody has ever opened.
/// </param>
/// <param name="ViewName">Active view name the profile named, for example <c>GridDataView</c>.</param>
/// <param name="ViewType">
/// Which configuration inside the profile the columns came from: <c>listed</c> or <c>tiled</c>. This reports the
/// configuration ACTUALLY used, which can differ from the active flag when the active one is empty.
/// </param>
/// <param name="Scope">
/// <c>user</c> when the calling user has a personal profile row for this grid, so the set may be that user's
/// own customization rather than the section's shared default; <c>shared</c> when only the product/system row
/// exists; <c>unknown</c> when the distinction could not be established.
/// </param>
/// <param name="Notes">Non-fatal details worth reporting, such as a malformed or empty stored configuration.</param>
public sealed record ClassicListProfileResult(
	IReadOnlyList<ClassicListProfileColumn> Columns,
	string ViewName,
	string ViewType,
	string Scope,
	IReadOnlyList<string> Notes);

/// <summary>Reads a Classic section's saved list-column profile through read-only Creatio APIs.</summary>
public interface IClassicListProfileReader {

	/// <summary>Reads the columns of the view the Classic section opens with.</summary>
	/// <param name="sectionSchemaName">Classic section client-unit schema name.</param>
	/// <returns>
	/// The profile columns and their provenance. Never <see langword="null"/>: a stand with no profile for the
	/// section returns an empty <see cref="ClassicListProfileResult.Columns"/> so the caller can fall through to
	/// the static declaration instead of treating an absent profile as a failure.
	/// </returns>
	ClassicListProfileResult Read(string sectionSchemaName);
}

/// <summary>Reads Classic grid profiles over the platform's own <c>QueryProfile</c> DataService route.</summary>
internal sealed class ClassicListProfileReader(
	IApplicationClient applicationClient,
	IServiceUrlBuilder serviceUrlBuilder) : IClassicListProfileReader {

	internal const string UserScope = "user";
	internal const string SharedScope = "shared";
	internal const string UnknownScope = "unknown";
	internal const string ListedViewType = "listed";
	internal const string TiledViewType = "tiled";
	internal const string DefaultViewName = "GridDataView";
	internal const string QueryProfileUrl = "/DataService/json/SyncReply/QueryProfile";

	private const string ActiveViewKeySuffix = "ActiveViewSettingsProfile";
	private const string GridSettingsKeyInfix = "GridSettings";
	private const string SysProfileDataSchemaName = "SysProfileData";

	/// <inheritdoc />
	public ClassicListProfileResult Read(string sectionSchemaName) {
		if (string.IsNullOrWhiteSpace(sectionSchemaName)) {
			return Empty();
		}
		var notes = new List<string>();
		string section = sectionSchemaName.Trim();
		string viewName = ReadActiveViewName(section);
		string gridKey = $"{section}{GridSettingsKeyInfix}{viewName}";
		JObject profile = QueryProfile(gridKey);
		// An absent profile is the ordinary case for a section nobody has opened, so it earns no note: the
		// reported source already tells the caller the answer did not come from a profile. Only a profile that
		// EXISTS and still yields nothing is worth explaining, because that one looks like a parser failure.
		if (profile is null || !profile.HasValues) {
			return Empty();
		}
		(IReadOnlyList<ClassicListProfileColumn> columns, string viewType) = ParseColumns(profile, notes);
		if (columns.Count == 0) {
			return new ClassicListProfileResult([], viewName, null, null, notes);
		}
		return new ClassicListProfileResult(columns, viewName, viewType, ResolveScope(gridKey, notes), notes);
	}

	private static ClassicListProfileResult Empty() => new([], null, null, null, []);

	/// <summary>Reads the view the section opens with, falling back to the platform default view name.</summary>
	private string ReadActiveViewName(string section) {
		JObject activeView = QueryProfile($"{section}{ActiveViewKeySuffix}");
		string name = activeView?["activeViewName"]?.ToString();
		return string.IsNullOrWhiteSpace(name) ? DefaultViewName : name.Trim();
	}

	/// <summary>Posts one <c>QueryProfile</c> read; returns <see langword="null"/> when it cannot be read.</summary>
	private JObject QueryProfile(string key) {
		try {
			string url = serviceUrlBuilder.Build(QueryProfileUrl);
			string body = new JObject { ["key"] = key }.ToString(Newtonsoft.Json.Formatting.None);
			string response = applicationClient.ExecutePostRequest(url, body);
			return string.IsNullOrWhiteSpace(response) ? null : JObject.Parse(response);
		}
		catch {
			// A transport failure, an expired session serving an HTML page, or a stand without the route must
			// degrade to "no profile answer" rather than fail the whole command: the static declaration is still
			// a usable answer, and the reported source names it honestly.
			return null;
		}
	}

	/// <summary>Extracts the columns of the configuration the grid actually renders.</summary>
	private static (IReadOnlyList<ClassicListProfileColumn> columns, string viewType) ParseColumns(
		JObject profile,
		List<string> notes) {
		// `DataGrid.isTiled` is the authoritative flag. The TOP-LEVEL `isTiled` of the same payload disagrees
		// with it on stock sections (AccountSectionV2 ships top-level true / DataGrid false, and the rendered
		// list is the listed one), so reading the outer flag silently returns the wrong view.
		if (profile["DataGrid"] is JObject dataGrid) {
			return ParseGridConfigs(dataGrid, "tiledConfig", "listedConfig", ParseModernItems, notes);
		}
		// Older profile keys keep the two configurations at the top level as JSON arrays instead.
		return ParseGridConfigs(profile, "tiledColumnsConfig", "listedColumnsConfig", ParseLegacyItems, notes);
	}

	private static (IReadOnlyList<ClassicListProfileColumn> columns, string viewType) ParseGridConfigs(
		JObject container,
		string tiledProperty,
		string listedProperty,
		Func<JToken, IReadOnlyList<ClassicListProfileColumn>> parseItems,
		List<string> notes) {
		bool isTiled = container["isTiled"]?.Type == JTokenType.Boolean && container["isTiled"].Value<bool>();
		string activeType = isTiled ? TiledViewType : ListedViewType;
		string activeProperty = isTiled ? tiledProperty : listedProperty;
		string fallbackProperty = isTiled ? listedProperty : tiledProperty;
		IReadOnlyList<ClassicListProfileColumn> active =
			parseItems(ParseEmbeddedJson(container[activeProperty], activeProperty, notes));
		if (active.Count > 0) {
			return (active, activeType);
		}
		IReadOnlyList<ClassicListProfileColumn> fallback =
			parseItems(ParseEmbeddedJson(container[fallbackProperty], fallbackProperty, notes));
		if (fallback.Count == 0) {
			return ([], null);
		}
		string fallbackType = isTiled ? ListedViewType : TiledViewType;
		notes.Add($"The saved profile's active '{activeType}' configuration is empty, so the '{fallbackType}' " +
			"configuration was reported instead; the rendered set may differ from what the section opens with.");
		return (fallback, fallbackType);
	}

	/// <summary>Parses a configuration stored as a JSON string inside the profile payload.</summary>
	private static JToken ParseEmbeddedJson(JToken value, string propertyName, List<string> notes) {
		if (value is null || value.Type == JTokenType.Null) {
			return null;
		}
		if (value.Type != JTokenType.String) {
			return value;
		}
		string text = value.Value<string>();
		if (string.IsNullOrWhiteSpace(text)) {
			return null;
		}
		try {
			return JToken.Parse(text);
		}
		catch (Newtonsoft.Json.JsonReaderException) {
			// Loud rather than silent: an unreadable stored configuration and an absent one lead to the same
			// fallback, and only the note tells the two apart.
			notes.Add($"The saved profile's '{propertyName}' value is not valid JSON and was skipped.");
			return null;
		}
	}

	/// <summary>Reads <c>items[]</c> of a modern <c>listedConfig</c> / <c>tiledConfig</c> object.</summary>
	private static IReadOnlyList<ClassicListProfileColumn> ParseModernItems(JToken config) =>
		Distinct((config as JObject)?["items"] as JArray, item => (
			ReadFirstString(item, "bindTo", "metaPath", "path", "columnName"),
			ReadCaption(item)));

	/// <summary>Reads a legacy configuration, which is an array of cells or an array of tiled rows.</summary>
	private static IReadOnlyList<ClassicListProfileColumn> ParseLegacyItems(JToken config) {
		if (config is not JArray array) {
			return [];
		}
		// A legacy tiled configuration nests one array per rendered row; a listed one is flat. Flattening one
		// level covers both without having to know which shape a given key uses.
		JArray cells = array.Any(item => item is JArray)
			? new JArray(array.SelectMany<JToken, JToken>(item => item is JArray row ? row.Children() : [item]))
			: array;
		return Distinct(cells, cell => (
			ReadFirstString(cell, "metaPath", "columnName", "bindTo")
			?? ReadFirstString((cell["key"] as JArray)?.FirstOrDefault(), "bindTo", "name"),
			ReadCaption(cell) ?? ReadCaption((cell["key"] as JArray)?.FirstOrDefault())));
	}

	private static IReadOnlyList<ClassicListProfileColumn> Distinct(
		JArray items,
		Func<JToken, (string path, string caption)> select) {
		if (items is null) {
			return [];
		}
		var result = new List<ClassicListProfileColumn>();
		var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (JToken item in items) {
			(string path, string caption) = select(item);
			if (string.IsNullOrWhiteSpace(path) || !seen.Add(path)) {
				continue;
			}
			result.Add(new ClassicListProfileColumn(path, caption));
		}
		return result;
	}

	private static string ReadFirstString(JToken source, params string[] propertyNames) {
		foreach (string propertyName in propertyNames) {
			JToken value = source?[propertyName];
			if (value?.Type == JTokenType.String && !string.IsNullOrWhiteSpace(value.Value<string>())) {
				return value.Value<string>().Trim();
			}
			// A legacy cell can nest the binding one level deeper as `name: { bindTo: … }`.
			if (value is JObject nested) {
				string bindTo = ReadFirstString(nested, "bindTo");
				if (!string.IsNullOrWhiteSpace(bindTo)) {
					return bindTo;
				}
			}
		}
		return null;
	}

	private static string ReadCaption(JToken source) => ReadFirstString(source, "caption");

	/// <summary>
	/// Establishes whether the returned profile can be this user's own customization or is the shared default.
	/// </summary>
	/// <remarks>
	/// <c>QueryProfile</c> answers for the CALLING user and silently falls back to the system row, so the payload
	/// alone cannot say which one it served. The row existence check supplies that, and it is deliberately
	/// non-fatal: losing the distinction is worth a note, not the whole answer.
	/// </remarks>
	private string ResolveScope(string gridKey, List<string> notes) {
		string contactId = ReadCurrentUserContactId();
		if (string.IsNullOrWhiteSpace(contactId)) {
			notes.Add("The current user's contact could not be read, so the profile could not be classified as " +
				"personal or shared.");
			return UnknownScope;
		}
		try {
			object query = SelectQueryHelper.BuildSelectQuery(
				SysProfileDataSchemaName,
				[new SelectQueryHelper.SelectQueryColumnDefinition("Id", "Id")],
				[
					new SelectQueryHelper.SelectQueryFilterDefinition("Key", gridKey,
						SelectQueryHelper.TextDataValueType),
					new SelectQueryHelper.SelectQueryFilterDefinition("Contact.Id", contactId,
						SelectQueryHelper.GuidDataValueType)
				],
				1);
			SysProfileDataRowsResponse response =
				SelectQueryHelper.ExecuteSelectQuery<SysProfileDataRowsResponse>(
					applicationClient, serviceUrlBuilder, query);
			return response.Rows.Count > 0 ? UserScope : SharedScope;
		}
		catch (Exception exception) {
			notes.Add($"The saved profile could not be classified as personal or shared ({exception.Message}).");
			return UnknownScope;
		}
	}

	private string ReadCurrentUserContactId() {
		try {
			string url = serviceUrlBuilder.Build(CreatioServicePaths.GetCurrentUserInfo);
			string response = applicationClient.ExecutePostRequest(url, "{}");
			return string.IsNullOrWhiteSpace(response)
				? null
				: JObject.Parse(response)["userInfo"]?["contactId"]?.ToString();
		}
		catch {
			return null;
		}
	}

	private sealed class SysProfileDataRowsResponse : SelectQueryHelper.SelectQueryResponseBaseDto {

		[JsonPropertyName("rows")]
		public List<SysProfileDataRow> Rows { get; set; } = [];
	}

	private sealed class SysProfileDataRow {

		[JsonPropertyName("Id")]
		public string Id { get; set; }
	}
}
