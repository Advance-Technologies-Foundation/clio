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
/// Which configuration inside the profile the columns came from: <see cref="ListedViewType"/> or
/// <see cref="TiledViewType"/>. This reports the configuration ACTUALLY used, which can differ from the active
/// flag when the active one is empty.
/// </param>
/// <param name="Scope">
/// Classifies the GRID-SETTINGS row the columns came from, and nothing else: <see cref="UserScope"/> when the
/// calling user has a personal row for this grid, so the set may be that user's own customization rather than the
/// section's shared default; <see cref="SharedScope"/> when only the product/system row exists;
/// <see cref="UnknownScope"/> when the distinction could not be established. It does NOT classify the
/// active-view profile that selects WHICH view is reported, so a personal active-view selection can still steer a
/// <see cref="SharedScope"/> answer.
/// </param>
/// <param name="Notes">Non-fatal details worth reporting, such as a malformed or empty stored configuration.</param>
public sealed record ClassicListProfileResult(
	IReadOnlyList<ClassicListProfileColumn> Columns,
	string ViewName,
	string ViewType,
	string Scope,
	IReadOnlyList<string> Notes) {

	// The scope and view-type vocabularies belong to this contract rather than to one reader: every
	// implementation of IClassicListProfileReader is required to produce these values, and every consumer reads
	// them off this record, so a second (or test) implementation must not have to reach into a concrete reader.
	/// <summary>Scope value for a profile the calling user has a personal row for.</summary>
	public const string UserScope = "user";

	/// <summary>Scope value for a profile served from the product/system row.</summary>
	public const string SharedScope = "shared";

	/// <summary>Scope value used when personal-vs-shared could not be established.</summary>
	public const string UnknownScope = "unknown";

	/// <summary>View type of the grid's listed (row) configuration.</summary>
	public const string ListedViewType = "listed";

	/// <summary>View type of the grid's tiled configuration.</summary>
	public const string TiledViewType = "tiled";
}

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

	internal const string DefaultViewName = "GridDataView";
	internal const string QueryProfileUrl = "/DataService/json/SyncReply/QueryProfile";

	private const string ActiveViewKeySuffix = "ActiveViewSettingsProfile";
	private const string GridSettingsKeyInfix = "GridSettings";
	private const string SysProfileDataSchemaName = "SysProfileData";
	private const string BindToProperty = "bindTo";

	/// <inheritdoc />
	public ClassicListProfileResult Read(string sectionSchemaName) {
		if (string.IsNullOrWhiteSpace(sectionSchemaName)) {
			return Empty();
		}
		var notes = new List<string>();
		string section = sectionSchemaName.Trim();
		if (!TryReadActiveViewName(section, out string viewName, out string activeViewFailureReason)) {
			// Reporting `view` without this note would be a claim about a read that never happened: the name used
			// below is the platform default assumed on the caller's behalf, not the view the section named.
			notes.Add($"The section's active view could not be read, so the platform default view "
				+ $"'{DefaultViewName}' was assumed; the reported columns may belong to a different view than the "
				+ $"one the section opens with{Describe(activeViewFailureReason)}.");
		}
		string gridKey = $"{section}{GridSettingsKeyInfix}{viewName}";
		if (!TryQueryProfile(gridKey, out JObject profile, out string gridFailureReason)) {
			// A FAILED read is not "no profile". Without this branch a transient 403, an expired session serving
			// an HTML login page or a missing route is byte-identical on the wire to a pristine stand, and the
			// caller adopts the narrow `schema-default` answer this whole change exists to replace.
			notes.Add($"The saved grid profile could not be read — the QueryProfile request failed"
				+ $"{Describe(gridFailureReason)} — so the answer falls back to the section's static declaration "
				+ "and may be narrower than the set the list renders.");
			return new ClassicListProfileResult([], null, null, null, notes);
		}
		// An absent profile is the ordinary case for a section nobody has opened, so it earns no note: the
		// reported source already tells the caller the answer did not come from a profile. Only a profile that
		// EXISTS and still yields nothing is worth explaining, because that one looks like a parser failure.
		if (profile is null || !profile.HasValues) {
			return new ClassicListProfileResult([], null, null, null, notes);
		}
		int notesBeforeParse = notes.Count;
		(IReadOnlyList<ClassicListProfileColumn> columns, string viewType) = ParseColumns(profile, notes);
		if (columns.Count == 0) {
			if (notes.Count == notesBeforeParse) {
				// The invariant stated above, now implemented: a stored payload whose shape the parser does not
				// understand must not be indistinguishable from a stand that simply holds no profile.
				notes.Add("A saved profile exists for this list, but no column configuration could be read out of "
					+ "it, so the answer falls back to the section's static declaration; the stored payload may use "
					+ "a shape this reader does not understand.");
			}
			return new ClassicListProfileResult([], viewName, null, null, notes);
		}
		return new ClassicListProfileResult(columns, viewName, viewType, ResolveScope(gridKey, notes), notes);
	}

	private static ClassicListProfileResult Empty() => new([], null, null, null, []);

	/// <summary>Reads the view the section opens with, falling back to the platform default view name.</summary>
	/// <param name="section">Classic section client-unit schema name.</param>
	/// <param name="viewName">The view the section opens with, or <see cref="DefaultViewName"/> when unread.</param>
	/// <param name="failureReason">The read failure's message, or <see langword="null"/> when the read succeeded.</param>
	/// <returns>
	/// <see langword="false"/> when the read itself failed, so the caller can say that the returned name is an
	/// assumption rather than the section's answer.
	/// </returns>
	private bool TryReadActiveViewName(string section, out string viewName, out string failureReason) {
		viewName = DefaultViewName;
		if (!TryQueryProfile($"{section}{ActiveViewKeySuffix}", out JObject activeView, out failureReason)) {
			return false;
		}
		string name = activeView?["activeViewName"]?.ToString();
		if (!string.IsNullOrWhiteSpace(name)) {
			viewName = name.Trim();
		}
		return true;
	}

	/// <summary>Posts one <c>QueryProfile</c> read.</summary>
	/// <param name="key">Profile key to read.</param>
	/// <param name="profile">The stored payload, or <see langword="null"/> when the stand holds none.</param>
	/// <param name="failureReason">The read failure's message, or <see langword="null"/> when the read succeeded.</param>
	/// <returns>
	/// <see langword="false"/> when the read FAILED — a transport error, an expired session serving an HTML page,
	/// or a stand without the route. A failure still degrades to "no columns" rather than failing the whole
	/// command, because the static declaration remains a usable answer; the caller turns this into a note so the
	/// degradation is never silent.
	/// </returns>
	private bool TryQueryProfile(string key, out JObject profile, out string failureReason) {
		profile = null;
		failureReason = null;
		try {
			string url = serviceUrlBuilder.Build(QueryProfileUrl);
			string body = new JObject { ["key"] = key }.ToString(Newtonsoft.Json.Formatting.None);
			string response = applicationClient.ExecutePostRequest(url, body);
			if (string.IsNullOrWhiteSpace(response)) {
				// An empty body is the route answering "nothing stored for this key", not a failure.
				return true;
			}
			profile = JObject.Parse(response);
			return true;
		}
		catch (Exception exception) {
			// The reason travels back to the caller instead of dying here: without it an expired session, a
			// permission-gated route and a stand without the route are one indistinguishable "no profile", and
			// the operator's only signal is a narrower column set.
			failureReason = exception.Message;
			return false;
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
		string activeType = isTiled
			? ClassicListProfileResult.TiledViewType
			: ClassicListProfileResult.ListedViewType;
		string activeProperty = isTiled ? tiledProperty : listedProperty;
		string fallbackProperty = isTiled ? listedProperty : tiledProperty;
		IReadOnlyList<ClassicListProfileColumn> active =
			TryParseEmbeddedJson(container[activeProperty], activeProperty, notes, out JToken activeConfig)
				? parseItems(activeConfig)
				: [];
		if (active.Count > 0) {
			return (active, activeType);
		}
		IReadOnlyList<ClassicListProfileColumn> fallback =
			TryParseEmbeddedJson(container[fallbackProperty], fallbackProperty, notes, out JToken fallbackConfig)
				? parseItems(fallbackConfig)
				: [];
		if (fallback.Count == 0) {
			return ([], null);
		}
		string fallbackType = isTiled
			? ClassicListProfileResult.ListedViewType
			: ClassicListProfileResult.TiledViewType;
		notes.Add($"The saved profile's active '{activeType}' configuration is empty, so the '{fallbackType}' " +
			"configuration was reported instead; the rendered set may differ from what the section opens with.");
		return (fallback, fallbackType);
	}

	/// <summary>Parses a configuration stored as a JSON string inside the profile payload.</summary>
	/// <param name="value">The stored configuration token.</param>
	/// <param name="propertyName">Property the token was read from, for the note.</param>
	/// <param name="notes">Collects the non-fatal degradation notes.</param>
	/// <param name="parsed">The parsed configuration when this returns <see langword="true"/>.</param>
	/// <returns><see langword="false"/> when the property holds nothing usable.</returns>
	private static bool TryParseEmbeddedJson(
		JToken value,
		string propertyName,
		List<string> notes,
		out JToken parsed) {
		parsed = null;
		if (value is null || value.Type == JTokenType.Null) {
			return false;
		}
		if (value.Type != JTokenType.String) {
			parsed = value;
			return true;
		}
		string text = value.Value<string>();
		if (string.IsNullOrWhiteSpace(text)) {
			return false;
		}
		try {
			parsed = JToken.Parse(text);
			return true;
		}
		catch (Newtonsoft.Json.JsonReaderException) {
			// Loud rather than silent: an unreadable stored configuration and an absent one lead to the same
			// fallback, and only the note tells the two apart.
			notes.Add($"The saved profile's '{propertyName}' value is not valid JSON and was skipped.");
			return false;
		}
	}

	/// <summary>Reads <c>items[]</c> of a modern <c>listedConfig</c> / <c>tiledConfig</c> object.</summary>
	private static IReadOnlyList<ClassicListProfileColumn> ParseModernItems(JToken config) =>
		Distinct((config as JObject)?["items"] as JArray, item => (
			ReadFirstString(item, BindToProperty, "metaPath", "path", "columnName"),
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
			ReadFirstString(cell, "metaPath", "columnName", BindToProperty)
			?? ReadFirstString(ReadKeyCell(cell), BindToProperty, "name"),
			ReadCaption(cell) ?? ReadCaption(ReadKeyCell(cell))));
	}

	/// <summary>Reads a legacy cell's nested <c>key[0]</c>, tolerating cells that are not objects.</summary>
	private static JToken ReadKeyCell(JToken cell) => ((cell as JObject)?["key"] as JArray)?.FirstOrDefault();

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

	/// <summary>Reads the first non-empty string property, tolerating elements that are not objects.</summary>
	/// <remarks>
	/// The cast to <see cref="JObject"/> is load-bearing, not defensive noise: indexing a bare
	/// <see cref="JToken"/> with a string throws for a <c>JValue</c> element (a literal in <c>items[]</c>) and for
	/// a <c>JArray</c> (a legacy row that survived the one-level flatten), which would turn a parseable payload
	/// into a total command failure and lose the static answer the command could still have given.
	/// </remarks>
	private static string ReadFirstString(JToken source, params string[] propertyNames) {
		var container = source as JObject;
		foreach (string propertyName in propertyNames) {
			JToken value = container?[propertyName];
			if (value?.Type == JTokenType.String && !string.IsNullOrWhiteSpace(value.Value<string>())) {
				return value.Value<string>().Trim();
			}
			// A legacy cell can nest the binding one level deeper as `name: { bindTo: … }`.
			if (value is JObject nested) {
				string bindTo = ReadFirstString(nested, BindToProperty);
				if (!string.IsNullOrWhiteSpace(bindTo)) {
					return bindTo;
				}
			}
		}
		return null;
	}

	private static string ReadCaption(JToken source) => ReadFirstString(source, "caption");

	/// <summary>
	/// Establishes whether the returned GRID-SETTINGS profile can be this user's own customization or is the
	/// shared default.
	/// </summary>
	/// <remarks>
	/// <c>QueryProfile</c> answers for the CALLING user and silently falls back to the system row, so the payload
	/// alone cannot say which one it served. The row existence check supplies that, and it is deliberately
	/// non-fatal: losing the distinction is worth a note, not the whole answer. Scope covers the grid-settings row
	/// ONLY — the active-view profile that selects which view is reported is not classified, which is why the
	/// contract words the claim narrowly.
	/// </remarks>
	private string ResolveScope(string gridKey, List<string> notes) {
		string contactId = ReadCurrentUserContactId(out string contactFailureReason);
		if (string.IsNullOrWhiteSpace(contactId)) {
			notes.Add("The current user's contact could not be read, so the profile could not be classified as " +
				$"personal or shared{Describe(contactFailureReason)}.");
			return ClassicListProfileResult.UnknownScope;
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
			return response.Rows.Count > 0
				? ClassicListProfileResult.UserScope
				: ClassicListProfileResult.SharedScope;
		}
		catch (Exception exception) {
			notes.Add($"The saved profile could not be classified as personal or shared ({exception.Message}).");
			return ClassicListProfileResult.UnknownScope;
		}
	}

	private string ReadCurrentUserContactId(out string failureReason) {
		failureReason = null;
		try {
			string url = serviceUrlBuilder.Build(CreatioServicePaths.GetCurrentUserInfo);
			string response = applicationClient.ExecutePostRequest(url, "{}");
			return string.IsNullOrWhiteSpace(response)
				? null
				: JObject.Parse(response)["userInfo"]?["contactId"]?.ToString();
		}
		catch (Exception exception) {
			failureReason = exception.Message;
			return null;
		}
	}

	/// <summary>Renders a read failure's reason for a note, or nothing when the failure carried none.</summary>
	private static string Describe(string failureReason) =>
		string.IsNullOrWhiteSpace(failureReason) ? string.Empty : $" ({failureReason.Trim()})";

	private sealed class SysProfileDataRowsResponse : SelectQueryHelper.SelectQueryResponseBaseDto {

		[JsonPropertyName("rows")]
		public List<SysProfileDataRow> Rows { get; set; } = [];
	}

	private sealed class SysProfileDataRow {

		[JsonPropertyName("Id")]
		public string Id { get; set; }
	}
}
