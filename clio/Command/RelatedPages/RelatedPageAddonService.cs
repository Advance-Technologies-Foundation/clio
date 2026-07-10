using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Clio.Command.AddonSchemaDesigner;

namespace Clio.Command.RelatedPages;

/// <summary>
/// Upserts a related-page binding into an entity's related-pages add-on schema. Works for BOTH the web
/// <c>RelatedPage</c> add-on and the <c>MobileRelatedPage</c> add-on — the add-on kind is carried by
/// <see cref="AddonGetRequestDto.AddonName"/>, the page-collection manipulation is identical.
/// Mirrors <see cref="Clio.Command.BusinessRules.BusinessRuleAddonService"/>: GetSchema → modify the
/// add-on <c>MetaData</c> JSON (<c>Pages</c> collection) → SaveSchema → reset client cache → rebuild.
/// </summary>
internal interface IRelatedPageAddonService {
	void UpsertRelatedPage(AddonGetRequestDto request, Guid pageSchemaUId, bool isDefault);
}

[SuppressMessage("Minor Code Smell", "S1192:String literals should not be duplicated", Justification = "The 'IsDefault' add-on metadata key reads more clearly inline than behind a constant in this small upsert.")]
internal sealed class RelatedPageAddonService(
	IAddonSchemaDesignerClient addonSchemaDesignerClient)
	: IRelatedPageAddonService {

	private const string PagesPropertyName = "Pages";

	private static readonly JsonSerializerOptions JsonOptions = new() {
		DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
		PropertyNameCaseInsensitive = true,
		WriteIndented = true
	};

	public void UpsertRelatedPage(AddonGetRequestDto request, Guid pageSchemaUId, bool isDefault) {
		ArgumentNullException.ThrowIfNull(request);
		if (pageSchemaUId == Guid.Empty) {
			throw new ArgumentException("Page schema UId is required.", nameof(pageSchemaUId));
		}

		AddonSchemaDto schema = addonSchemaDesignerClient.GetSchema(request);
		JsonObject metadata = ParseMetadata(schema.MetaData);
		JsonArray pages = GetOrCreatePages(metadata);
		UpsertPage(pages, pageSchemaUId, isDefault);

		schema.MetaData = metadata.ToJsonString(JsonOptions);

		addonSchemaDesignerClient.SaveSchema(schema);
		// Make the saved add-on immediately visible (same sequence as business-rule creation).
		addonSchemaDesignerClient.ResetClientScriptCache();
		addonSchemaDesignerClient.BuildConfiguration();
	}

	/// <summary>
	/// Adds (or reuses) the page entry for <paramref name="pageSchemaUId"/>. When <paramref name="isDefault"/>
	/// is true it becomes the single default page (clearing <c>IsDefault</c> on every other page); when
	/// false it is added/kept without changing which page is default.
	/// </summary>
	internal static void UpsertPage(JsonArray pages, Guid pageSchemaUId, bool isDefault) {
		string uid = pageSchemaUId.ToString();
		JsonObject target = null;
		foreach (JsonObject page in pages.OfType<JsonObject>()) {
			bool isMatch = string.Equals(page["PageSchemaUId"]?.ToString(), uid, StringComparison.OrdinalIgnoreCase);
			if (isMatch) {
				target = page;
			}
			if (isDefault) {
				page["IsDefault"] = isMatch;
			}
		}
		if (target is null) {
			pages.Add(new JsonObject {
				["UId"] = Guid.NewGuid().ToString(),
				["PageSchemaUId"] = uid,
				["IsDefault"] = isDefault
			});
		} else if (!isDefault && target["IsDefault"] is null) {
			target["IsDefault"] = false;
		}
	}

	private static JsonObject ParseMetadata(string metaData) {
		if (string.IsNullOrWhiteSpace(metaData)) {
			return new JsonObject { [PagesPropertyName] = new JsonArray() };
		}
		try {
			return JsonNode.Parse(metaData) as JsonObject
				?? throw new InvalidOperationException("Related-page add-on metadata root must be a JSON object.");
		} catch (JsonException exception) {
			throw new InvalidOperationException("Related-page add-on metadata is not valid JSON.", exception);
		}
	}

	private static JsonArray GetOrCreatePages(JsonObject metadata) {
		if (!metadata.TryGetPropertyValue(PagesPropertyName, out JsonNode pagesNode) || pagesNode is null) {
			JsonArray created = [];
			metadata[PagesPropertyName] = created;
			return created;
		}
		return pagesNode as JsonArray
			?? throw new InvalidOperationException($"Related-page add-on metadata '{PagesPropertyName}' must be a JSON array.");
	}
}
