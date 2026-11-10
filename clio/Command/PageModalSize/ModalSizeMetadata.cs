using System;
using System.Text.Json;
using System.Text.Json.Nodes;
using Clio.Command.AddonSchemaDesigner;

namespace Clio.Command.PageModalSize;

/// <summary>Reads <c>modalSettings.size</c> out of <c>AppearanceSettings</c> add-on metadata.</summary>
internal static class ModalSizeMetadata {
	internal const string AddonName = "AppearanceSettings";
	internal const string ClientUnitSchemaManagerName = "ClientUnitSchemaManager";
	internal const string ModalSettingsKey = "modalSettings";
	internal const string SizeKey = "size";

	/// <summary>Parses the metadata; blank metadata is an empty object (the package has no add-on yet).</summary>
	internal static bool TryParse(string metaData, out JsonObject metadata) {
		if (string.IsNullOrWhiteSpace(metaData)) {
			metadata = new JsonObject();
			return true;
		}
		try {
			metadata = JsonNode.Parse(metaData) as JsonObject;
		} catch (JsonException) {
			metadata = null;
		}
		return metadata is not null;
	}

	/// <summary>The raw <c>modalSettings.size</c>, None (0) included, or <c>null</c> when no size is set.</summary>
	internal static int? ReadRawSize(JsonObject metadata) =>
		metadata[ModalSettingsKey] is JsonObject modalSettings
		&& modalSettings[SizeKey] is JsonValue sizeNode
		&& sizeNode.TryGetValue(out int size)
			? size
			: null;

	/// <summary>The size as stored: None (0) and a missing size both mean "no size".</summary>
	internal static int? ToStoredSize(int? value) => value == PageModalSizeCatalog.NoneValue ? null : value;

	// Mirrors the Page Designer and the page business-rule add-on path: the page is passed as the PARENT and the
	// target is a fresh UId, so the add-on service resolves the page by its parent and keeps the REQUESTED package.
	// Passing the committed page UId as the target would pin the add-on to the page's own (possibly locked) package.
	// useFullHierarchy=false: measured on 10.2.760, true merges the layers of EVERY package of the environment
	// whichever package is requested, so a layer the package does not see would read as its own size and be written
	// back into its layer; false merges the package and its dependencies only.
	/// <summary>The add-on read of the page's <c>AppearanceSettings</c> as the package sees it.</summary>
	internal static AddonGetRequestDto BuildGetRequest(Guid pageSchemaUId, Guid packageUId) =>
		new() {
			AddonName = AddonName,
			TargetSchemaUId = Guid.NewGuid(),
			TargetParentSchemaUId = pageSchemaUId,
			TargetPackageUId = packageUId,
			TargetSchemaManagerName = ClientUnitSchemaManagerName,
			UseFullHierarchy = false
		};
}
