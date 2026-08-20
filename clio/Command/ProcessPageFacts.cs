namespace Clio.Command;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Text.Json.Serialization;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using JsonIgnoreAttribute = System.Text.Json.Serialization.JsonIgnoreAttribute;

/// <summary>
/// One page button that can complete a Pre-configured page process element.
/// </summary>
[DataContract]
public sealed class ProcessPageButton {
	/// <summary>
	/// Gets or sets the button's view-element name on the page — the identity the process element stores.
	/// </summary>
	[DataMember(Name = "name")]
	[JsonProperty("name")]
	[JsonPropertyName("name")]
	public string Name { get; set; }

	/// <summary>
	/// Gets or sets the caption the process designer records: the page button's resolved caption and the element
	/// name, joined as <c>"Save | SaveButton"</c>.
	/// </summary>
	[DataMember(Name = "caption")]
	[JsonProperty("caption")]
	[JsonPropertyName("caption")]
	public string Caption { get; set; }

	/// <summary>
	/// Gets or sets the page event that completes the page. Always <c>clicked</c> — the designer records no other,
	/// and the value also forms the element's stored tag.
	/// </summary>
	[DataMember(Name = "event")]
	[JsonProperty("event")]
	[JsonPropertyName("event")]
	public string Event { get; set; }

	/// <summary>
	/// Gets or sets the page requests the button's click handler issues (for example
	/// <c>crt.SaveRecordRequest</c>). Empty when the button declares no handler.
	/// </summary>
	[DataMember(Name = "requests")]
	[JsonProperty("requests")]
	[JsonPropertyName("requests")]
	public List<string> Requests { get; set; }
}

/// <summary>
/// One page-scoped entity data source of a page. Each becomes an element parameter carrying the id of the record
/// the page added or modified.
/// </summary>
[DataContract]
public sealed class ProcessPageDataSource {
	/// <summary>Gets or sets the data source's name on the page (for example <c>PDS</c>).</summary>
	[DataMember(Name = "name")]
	[JsonProperty("name")]
	[JsonPropertyName("name")]
	public string Name { get; set; }

	/// <summary>Gets or sets the entity the data source reads and writes.</summary>
	[DataMember(Name = "entitySchemaName")]
	[JsonProperty("entitySchemaName")]
	[JsonPropertyName("entitySchemaName")]
	public string EntitySchemaName { get; set; }
}

/// <summary>
/// The facts a Pre-configured page process element needs about a Freedom UI page.
/// </summary>
[DataContract]
public sealed class ProcessPageFactsResponse {
	/// <summary>Gets or sets a value indicating whether the request succeeded.</summary>
	[DataMember(Name = "success")]
	[JsonProperty("success")]
	[JsonPropertyName("success")]
	public bool Success { get; set; }

	/// <summary>Gets or sets the page schema name the facts were read from.</summary>
	[DataMember(Name = "schema-name")]
	[JsonProperty("schema-name")]
	[JsonPropertyName("schema-name")]
	public string SchemaName { get; set; }

	/// <summary>
	/// Gets or sets the buttons eligible to complete the page. A button is eligible when its click handler issues
	/// one of the page-completing requests, or when it declares no requests at all.
	/// </summary>
	[DataMember(Name = "completingButtonCandidates")]
	[JsonProperty("completingButtonCandidates")]
	[JsonPropertyName("completingButtonCandidates")]
	public List<ProcessPageButton> CompletingButtonCandidates { get; set; }

	/// <summary>Gets or sets the page-scoped entity data sources.</summary>
	[DataMember(Name = "dataSources")]
	[JsonProperty("dataSources")]
	[JsonPropertyName("dataSources")]
	public List<ProcessPageDataSource> DataSources { get; set; }

	/// <summary>Gets or sets the error message for failed requests.</summary>
	[DataMember(Name = "error")]
	[JsonProperty("error")]
	[JsonPropertyName("error")]
	public string Error { get; set; }
}

/// <summary>
/// Projects a merged Freedom UI page bundle into the facts a Pre-configured page process element needs: the
/// buttons that could complete the page, and the page-scoped entity data sources.
/// <para>This lives in clio rather than in the CrtProcessBuilder package for one reason: the answer is only
/// knowable from the MERGED page (a page inherits its buttons from its template chain), the platform performs that
/// merge client-side in the process designer's Angular bundle, and no server-side merged-view API exists. clio
/// already merges the chain to produce a page bundle, so the projection is a small step on top of work it does
/// anyway.</para>
/// <para>The rules below are not invented — they are transcribed from the shipped process-designer bundle
/// (<c>ProcessPageMetadataService</c> and its <c>crt.Button</c> provider) and verified against a designer-built
/// element. Keeping them here, in code with tests, is the point: expressed as prose in agent guidance instead,
/// every caller would re-derive them and get them subtly wrong.</para>
/// </summary>
public static class ProcessPageFactsProjection {

	#region Constants: Private

	private const string ButtonElementType = "crt.Button";
	private const string EntityDataSourceType = "crt.EntityDataSource";
	private const string PageScope = "page";
	private const string MenuClickMode = "menu";
	private const string ClickedEvent = "clicked";
	private const string DefaultCulture = "en-US";

	/// <summary>
	/// The requests that make a button a page-completing candidate. Mirrors the designer's own allow-list; a button
	/// declaring NO requests is also a candidate, because a custom button that only runs code can still be chosen.
	/// </summary>
	private static readonly string[] CompletingRequests = [
		"crt.SaveRecordRequest", "crt.ClosePageRequest", "crt.CancelRecordChangesRequest"
	];

	#endregion

	#region Methods: Public

	/// <summary>
	/// Projects a merged page bundle into the process-page facts.
	/// </summary>
	/// <param name="bundle">The merged page bundle (as produced for <c>get-page</c>'s <c>bundle.json</c>).</param>
	/// <param name="culture">Culture used to resolve resource-backed captions; defaults to <c>en-US</c>.</param>
	public static (List<ProcessPageButton> Buttons, List<ProcessPageDataSource> DataSources) Project(JObject bundle,
		string culture = null) {
		ArgumentNullException.ThrowIfNull(bundle);
		JObject strings = bundle["resources"]?["strings"] as JObject;
		string effectiveCulture = string.IsNullOrWhiteSpace(culture) ? DefaultCulture : culture;
		List<ProcessPageButton> buttons = [];
		CollectButtons(bundle["viewConfig"], strings, effectiveCulture, buttons);
		return (buttons, CollectDataSources(bundle["modelConfig"]?["dataSources"] as JObject));
	}

	/// <summary>
	/// Whether a button is eligible to complete the page: its handler issues one of the completing requests, or it
	/// declares no requests at all.
	/// </summary>
	public static bool IsCompletingCandidate(ProcessPageButton button) {
		ArgumentNullException.ThrowIfNull(button);
		return button.Requests is null or { Count: 0 }
			|| button.Requests.Any(request => CompletingRequests.Contains(request, StringComparer.Ordinal));
	}

	#endregion

	#region Methods: Private

	/// <summary>
	/// Walks the merged view config for <c>crt.Button</c> nodes. A menu button contributes one entry per leaf menu
	/// item rather than one for itself, because it is the item the user presses that completes the page.
	/// </summary>
	private static void CollectButtons(JToken node, JObject strings, string culture,
		List<ProcessPageButton> collected) {
		switch (node) {
			case JArray array: {
				foreach (JToken item in array) {
					CollectButtons(item, strings, culture, collected);
				}
				break;
			}
			case JObject element: {
				if (string.Equals(element["type"]?.Value<string>(), ButtonElementType, StringComparison.Ordinal)) {
					AppendButton(element, strings, culture, collected);
				}
				foreach (JProperty property in element.Properties()) {
					CollectButtons(property.Value, strings, culture, collected);
				}
				break;
			}
		}
	}

	/// <summary>Appends a button node's entries, expanding a menu button into its leaf items.</summary>
	private static void AppendButton(JObject element, JObject strings, string culture,
		List<ProcessPageButton> collected) {
		string caption = ResolveCaption(element["caption"], strings, culture);
		if (string.Equals(element["clickMode"]?.Value<string>(), MenuClickMode, StringComparison.Ordinal)) {
			AppendMenuItems(element["menuItems"], strings, culture, caption, collected);
			return;
		}
		string name = element["name"]?.Value<string>();
		if (string.IsNullOrWhiteSpace(name)) {
			return;
		}
		collected.Add(BuildButton(name, caption, element[ClickedEvent]));
	}

	/// <summary>
	/// Appends a menu's leaf items, carrying the caption path down as <c>"parent | item"</c> exactly as the
	/// designer composes it.
	/// </summary>
	private static void AppendMenuItems(JToken menuItems, JObject strings, string culture, string parentCaption,
		List<ProcessPageButton> collected) {
		if (menuItems is not JArray items) {
			return;
		}
		foreach (JToken item in items) {
			if (item is not JObject menuItem) {
				continue;
			}
			string caption = $"{parentCaption} | {ResolveCaption(menuItem["caption"], strings, culture)}";
			if (menuItem["items"] is JArray { Count: > 0 } nested) {
				AppendMenuItems(nested, strings, culture, caption, collected);
				continue;
			}
			string name = menuItem["name"]?.Value<string>();
			if (!string.IsNullOrWhiteSpace(name)) {
				collected.Add(BuildButton(name, caption, menuItem[ClickedEvent]));
			}
		}
	}

	/// <summary>Builds one button entry, including the designer's <c>"caption | name"</c> composition.</summary>
	private static ProcessPageButton BuildButton(string name, string caption, JToken clicked) => new() {
		Name = name,
		// The designer stores the resolved caption AND the element name together; the element name is what
		// disambiguates two buttons that happen to share a caption.
		Caption = $"{caption} | {name}",
		Event = ClickedEvent,
		Requests = ReadRequests(clicked)
	};

	/// <summary>
	/// Reads the requests a click handler issues. The designer takes exactly the handler's own
	/// <c>request</c> — one request, or none — so a handler shape it does not recognise yields an empty list, which
	/// still leaves the button eligible.
	/// </summary>
	private static List<string> ReadRequests(JToken clicked) {
		string request = clicked?["request"]?.Value<string>();
		return string.IsNullOrWhiteSpace(request) ? [] : [request];
	}

	/// <summary>
	/// Resolves a caption that may be a resource macro. Both shipped forms are handled —
	/// <c>#ResourceString(Key)#</c> and <c>$Resources.Strings.Key</c> — against the merged bundle's own resource
	/// strings, falling back to the raw text when the key is absent so a caption is never reported as empty.
	/// </summary>
	private static string ResolveCaption(JToken caption, JObject strings, string culture) {
		string raw = caption?.Type == JTokenType.String ? caption.Value<string>() : null;
		if (string.IsNullOrWhiteSpace(raw)) {
			return string.Empty;
		}
		string key = null;
		if (raw.StartsWith("#ResourceString(", StringComparison.Ordinal) && raw.EndsWith(")#", StringComparison.Ordinal)) {
			key = raw.Substring("#ResourceString(".Length, raw.Length - "#ResourceString(".Length - ")#".Length);
		} else if (raw.StartsWith("$Resources.Strings.", StringComparison.Ordinal)) {
			key = raw["$Resources.Strings.".Length..];
		}
		if (string.IsNullOrWhiteSpace(key) || strings?[key] is not JObject localized) {
			return raw;
		}
		return localized[culture]?.Value<string>()
			?? localized[DefaultCulture]?.Value<string>()
			?? localized.Properties().FirstOrDefault()?.Value?.Value<string>()
			?? raw;
	}

	/// <summary>
	/// Collects the PAGE-scoped entity data sources. The scope filter is load-bearing and easy to miss: the
	/// designer requests <c>scope === Page</c> only, so the view-element-scoped sources behind lists and detail
	/// grids are deliberately excluded — including them would generate element parameters the page never fills.
	/// </summary>
	private static List<ProcessPageDataSource> CollectDataSources(JObject dataSources) {
		List<ProcessPageDataSource> collected = [];
		if (dataSources is null) {
			return collected;
		}
		foreach (JProperty property in dataSources.Properties()) {
			if (property.Value is not JObject dataSource) {
				continue;
			}
			if (!string.Equals(dataSource["scope"]?.Value<string>(), PageScope, StringComparison.Ordinal)
				|| !string.Equals(dataSource["type"]?.Value<string>(), EntityDataSourceType, StringComparison.Ordinal)) {
				continue;
			}
			string entitySchemaName = dataSource["config"]?["entitySchemaName"]?.Value<string>();
			if (string.IsNullOrWhiteSpace(entitySchemaName)) {
				continue;
			}
			collected.Add(new ProcessPageDataSource {
				Name = property.Name,
				EntitySchemaName = entitySchemaName
			});
		}
		return collected;
	}

	#endregion

}
