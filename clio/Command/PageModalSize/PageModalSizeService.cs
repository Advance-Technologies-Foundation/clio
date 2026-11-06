using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Clio.Command.AddonSchemaDesigner;
using Clio.Common;

namespace Clio.Command.PageModalSize;

/// <inheritdoc cref="IPageModalSizeService" />
internal sealed class PageModalSizeService(
	IApplicationClient applicationClient,
	IServiceUrlBuilder serviceUrlBuilder,
	IAddonSchemaDesignerClient addonSchemaDesignerClient,
	IPageSchemaResolver pageSchemaResolver)
	: IPageModalSizeService {

	private const string AddonName = "AppearanceSettings";
	private const string ClientUnitSchemaManagerName = "ClientUnitSchemaManager";
	private const string ModalTemplateName = "BaseMiniPageTemplate";
	private const string ModalSettingsKey = "modalSettings";
	private const string SizeKey = "size";
	// Only modal pages reach this service, so a page with no size opens with the modal default.
	private const int RuntimeDefaultValue = PageModalSizeCatalog.ModalDefaultValue;

	private sealed record PageTarget(string PageSchemaName, string PackageName, Guid PackageId, Guid PageSchemaUId);

	private sealed record StoredSize(int? Value, string SourcePackageName, string Warning);

	public PageModalSizeReadResult Get(PageModalSizeReadRequest request) {
		ArgumentNullException.ThrowIfNull(request);
		PageTarget target = ResolveTarget(request.PackageName, request.PageSchemaName);
		AddonSchemaDto schema = addonSchemaDesignerClient.GetSchema(BuildGetRequest(target));
		StoredSize stored = ReadStoredSize(schema);
		(int? runtimeValue, string runtimeWarning) = TryReadRuntimeValue(target.PageSchemaName);
		int seenValue = stored.Value ?? RuntimeDefaultValue;
		int effectiveValue = runtimeValue ?? seenValue;
		string overrideWarning = runtimeValue.HasValue && runtimeValue != seenValue
			? $"The window opens {NameValue(runtimeValue.Value)}, not {NameValue(seenValue)} as package "
				+ $"'{target.PackageName}' sees it: a package above '{target.PackageName}' sets the size, so a write to "
				+ $"'{target.PackageName}' would not change the window."
			: null;
		return new PageModalSizeReadResult(
			target.PageSchemaName,
			target.PackageName,
			stored.Value is int value ? PageModalSizeCatalog.NameOf(value) : null,
			stored.Value,
			stored.SourcePackageName,
			NameValue(effectiveValue),
			PageModalSizeCatalog.Names,
			CombineWarnings(stored.Warning, overrideWarning, runtimeWarning));
	}

	public PageModalSizeWriteResult Set(PageModalSizeWriteRequest request) {
		ArgumentNullException.ThrowIfNull(request);
		// Refuse an unsupported size before any remote call, so nothing is read or written for it.
		int newValue = PageModalSizeCatalog.Parse(request.Size);
		PageTarget target = ResolveTarget(request.PackageName, request.PageSchemaName);
		AddonSchemaDto schema = addonSchemaDesignerClient.GetSchema(BuildGetRequest(target));
		StoredSize previous = ReadStoredSize(schema);

		// Change only modalSettings.size: every other top-level field and every other modalSettings field is kept.
		// Parsed strictly: a body that is not a JSON object would otherwise be replaced by {"modalSettings":...} and
		// every other AppearanceSettings field would be lost without a word.
		JsonObject metadata = ParseMetadataForWrite(schema.MetaData, target.PageSchemaName);
		if (metadata[ModalSettingsKey] is not JsonObject modalSettings) {
			modalSettings = new JsonObject();
			metadata[ModalSettingsKey] = modalSettings;
		}
		modalSettings[SizeKey] = newValue;
		schema.MetaData = metadata.ToJsonString(new JsonSerializerOptions { WriteIndented = true });

		addonSchemaDesignerClient.SaveSchema(schema);
		string resetWarning = addonSchemaDesignerClient.ResetClientScriptCache();
		string buildWarning = addonSchemaDesignerClient.BuildConfiguration();
		(int? runtimeValue, string runtimeWarning) = TryReadRuntimeValue(target.PageSchemaName);
		string overrideWarning = runtimeValue.HasValue && runtimeValue != newValue
			? $"The size is saved in package '{target.PackageName}', but the window still opens "
				+ $"{NameValue(runtimeValue.Value)}: a package above '{target.PackageName}' sets the size."
			: null;

		return new PageModalSizeWriteResult(
			target.PageSchemaName,
			target.PackageName,
			previous.Value is int value ? PageModalSizeCatalog.NameOf(value) : null,
			previous.Value,
			previous.SourcePackageName,
			PageModalSizeCatalog.NameOf(newValue),
			CombineWarnings(overrideWarning, runtimeWarning, previous.Warning, resetWarning, buildWarning));
	}

	private PageTarget ResolveTarget(string packageName, string pageSchemaName) {
		if (string.IsNullOrWhiteSpace(pageSchemaName)) {
			throw new ArgumentException("page-schema-name is required.");
		}
		if (string.IsNullOrWhiteSpace(packageName)) {
			throw new ArgumentException("package-name is required.");
		}
		string normalizedPackageName = packageName.Trim();
		(string packageUId, string packageError) = PageSchemaMetadataHelper.QueryPackageUId(
			applicationClient, serviceUrlBuilder, normalizedPackageName);
		if (packageError != null) {
			throw new InvalidOperationException(packageError);
		}
		if (!Guid.TryParse(packageUId, out Guid packageId)) {
			throw new InvalidOperationException(
				$"Resolved package '{normalizedPackageName}' UId '{packageUId}' is not a valid GUID.");
		}
		string normalizedPageName = pageSchemaName.Trim();
		IReadOnlyList<PageDesignerHierarchySchema> hierarchy =
			pageSchemaResolver.ResolveHierarchy(normalizedPageName, packageId);
		// The Page Designer shows the size only for a page it opens in the modal page designer: a page inheriting
		// BaseMiniPageTemplate. Writing a size to any other page would change nothing the user can see or select.
		if (!hierarchy.Any(schema => string.Equals(schema.Name, ModalTemplateName, StringComparison.OrdinalIgnoreCase))) {
			throw new InvalidOperationException(
				$"Page '{normalizedPageName}' is not a modal page: it does not inherit {ModalTemplateName}, so it has no "
				+ "modal window size. Nothing was changed.");
		}
		if (!Guid.TryParse(hierarchy[0].UId, out Guid pageSchemaUId)) {
			throw new InvalidOperationException($"Page schema '{normalizedPageName}' has no valid UId.");
		}
		return new PageTarget(normalizedPageName, normalizedPackageName, packageId, pageSchemaUId);
	}

	// Mirrors the Page Designer and the page business-rule add-on path: the page is passed as the PARENT and the
	// target is a fresh UId, so the add-on service resolves the page by its parent and keeps the REQUESTED package.
	// Passing the committed page UId as the target would pin the add-on to the page's own (possibly locked) package.
	private static AddonGetRequestDto BuildGetRequest(PageTarget target) =>
		new() {
			AddonName = AddonName,
			TargetSchemaUId = Guid.NewGuid(),
			TargetParentSchemaUId = target.PageSchemaUId,
			TargetPackageUId = target.PackageId,
			TargetSchemaManagerName = ClientUnitSchemaManagerName,
			UseFullHierarchy = true
		};

	// GetSchema returns the metadata merged across the layers visible from the package, and always names the
	// REQUESTED package. The layer the size comes from is found by UId: the add-on's own uId is persisted only when
	// the package already has a layer; otherwise parentSchemaUId is the topmost existing layer. An own layer is taken
	// as the source because a save (designer or clio) stores the merged metadata, size included; the platform offers
	// no read of one layer alone (useFullHierarchy=false still merges). The source is informational, so a failed
	// lookup is a warning and never blocks a write.
	private StoredSize ReadStoredSize(AddonSchemaDto schema) {
		int? value = ReadSize(ParseMetadata(schema.MetaData));
		if (value is null) {
			return new StoredSize(null, null, null);
		}
		try {
			string sourcePackageName = FindLayerPackageName(ReadAdditionalString(schema, "uId"))
				?? FindLayerPackageName(ReadAdditionalString(schema, "parentSchemaUId"));
			return new StoredSize(value, sourcePackageName, null);
		} catch (InvalidOperationException exception) {
			return new StoredSize(value, null,
				$"The package the current size comes from could not be determined: {exception.Message}");
		}
	}

	private string FindLayerPackageName(string addonSchemaUId) {
		if (!Guid.TryParse(addonSchemaUId, out Guid uId) || uId == Guid.Empty) {
			return null;
		}
		(string packageName, string error) = PageSchemaMetadataHelper.QueryAddonSchemaPackageName(
			applicationClient, serviceUrlBuilder, uId.ToString());
		return error != null ? throw new InvalidOperationException(error) : packageName;
	}

	// The runtime read merges every layer the way the window opens, so it shows a layer above the package that
	// GetSchema (scoped to the package) cannot see. The value is null when the runtime reports no size. Never throws:
	// on a write it runs after the save is committed, so a failed check is a warning, not a failed write.
	private (int? Value, string Warning) TryReadRuntimeValue(string pageSchemaName) {
		string unverified = $"The size the window opens with could not be checked for '{pageSchemaName}'";
		JsonObject root;
		try {
			string body = new JsonObject { ["schemaName"] = pageSchemaName }.ToJsonString();
			string response = applicationClient.ExecutePostRequest(
				serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.GetAppearanceSettings), body);
			root = string.IsNullOrWhiteSpace(response) ? null : JsonNode.Parse(response) as JsonObject;
		} catch (Exception exception) {
			return (null, $"{unverified}: {exception.Message}");
		}
		if (root is null || root["success"]?.GetValueKind() != JsonValueKind.True) {
			return (null, $"{unverified}: AppearanceSettingsService.GetAppearanceSettings returned no result.");
		}
		return (root["value"] is JsonObject value ? ReadSize(value) : null, null);
	}

	private static int? ReadSize(JsonObject metadata) {
		if (metadata[ModalSettingsKey] is not JsonObject modalSettings
			|| modalSettings[SizeKey] is not JsonValue sizeNode
			|| !sizeNode.TryGetValue(out int size)
			|| size == PageModalSizeCatalog.NoneValue) {
			return null;
		}
		return size;
	}

	private static JsonObject ParseMetadata(string metaData) {
		if (string.IsNullOrWhiteSpace(metaData)) {
			return new JsonObject();
		}
		try {
			return JsonNode.Parse(metaData) as JsonObject ?? new JsonObject();
		} catch (JsonException) {
			return new JsonObject();
		}
	}

	private static JsonObject ParseMetadataForWrite(string metaData, string pageSchemaName) {
		if (string.IsNullOrWhiteSpace(metaData)) {
			return new JsonObject();
		}
		try {
			if (JsonNode.Parse(metaData) is JsonObject metadata) {
				return metadata;
			}
		} catch (JsonException) {
			// Reported below with the same message as a non-object body.
		}
		throw new InvalidOperationException(
			$"The AppearanceSettings metadata of '{pageSchemaName}' is not a JSON object, so other settings could not be "
			+ "kept. Nothing was changed.");
	}

	private static string ReadAdditionalString(AddonSchemaDto schema, string key) =>
		schema.AdditionalData is not null
		&& schema.AdditionalData.TryGetValue(key, out JsonElement value)
		&& value.ValueKind == JsonValueKind.String
			? value.GetString()
			: null;

	// The size name, or "unsupported (N)" for a stored value the designer does not offer.
	private static string NameValue(int value) => PageModalSizeCatalog.NameOf(value) ?? $"unsupported ({value})";

	private static string CombineWarnings(params string[] warnings) {
		string[] present = warnings.Where(warning => !string.IsNullOrWhiteSpace(warning)).ToArray();
		return present.Length == 0 ? null : string.Join(" ", present);
	}
}
