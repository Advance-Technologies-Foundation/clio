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
	IPageSchemaResolver pageSchemaResolver,
	IAppearanceSettingsLayerLocator layerLocator)
	: IPageModalSizeService {

	private const string ModalTemplateName = "BaseMiniPageTemplate";
	private const string ModalSettingsKey = ModalSizeMetadata.ModalSettingsKey;
	private const string SizeKey = ModalSizeMetadata.SizeKey;
	// A runtime that still serves an older size looks exactly like a higher layer.
	private const string StaleCacheHint =
		"If no other package should set it, the runtime may be serving a stale cached size: clear the cache "
		+ "('clio clear-redis-db', or restart the application) and read the size again.";
	// Only modal pages reach this service, so a page with no size opens with the modal default.
	private const int RuntimeDefaultValue = PageModalSizeCatalog.ModalDefaultValue;

	private sealed record PageTarget(string PageSchemaName, string PackageName, Guid PackageId, Guid PageSchemaUId);

	private sealed record StoredSize(int? Value, string SourcePackageName, string Warning);

	// A layer package-name does not see sets the size the window opens with: the package that sets it (null when it
	// could not be told apart) and the sentence that says so.
	private sealed record SizeOverride(string EffectiveSourcePackageName, string Text);

	public PageModalSizeReadResult Get(PageModalSizeReadRequest request) {
		ArgumentNullException.ThrowIfNull(request);
		PageTarget target = ResolveTarget(request.PackageName, request.PageSchemaName);
		AddonSchemaDto schema = addonSchemaDesignerClient.GetSchema(BuildGetRequest(target));
		StoredSize stored = ReadStoredSize(schema, target);
		(int? runtimeValue, string runtimeWarning) = TryReadRuntimeValue(target.PageSchemaName);
		int effectiveValue = EffectiveValue(stored.Value, runtimeValue);
		// An unreadable body has no size to compare with, so any runtime size would read as a false override.
		SizeOverride sizeOverride = stored.Warning is null ? FindOverride(target, stored.Value, runtimeValue) : null;
		string overrideWarning = sizeOverride is null
			? null
			: $"{Capitalize(sizeOverride.Text)}, so a write to '{target.PackageName}' would not change the window. "
				+ StaleCacheHint;
		return new PageModalSizeReadResult(
			target.PageSchemaName,
			target.PackageName,
			stored.Value is int value ? PageModalSizeCatalog.NameOf(value) : null,
			stored.Value,
			stored.SourcePackageName,
			NameValue(effectiveValue),
			sizeOverride?.EffectiveSourcePackageName,
			PageModalSizeCatalog.Names,
			CombineWarnings(stored.Warning, overrideWarning, runtimeWarning));
	}

	public PageModalSizeWriteResult Set(PageModalSizeWriteRequest request) {
		ArgumentNullException.ThrowIfNull(request);
		// Refuse an unsupported size before any remote call, so nothing is read or written for it.
		int newValue = PageModalSizeCatalog.Parse(request.Size);
		PageTarget target = ResolveTarget(request.PackageName, request.PageSchemaName);
		AddonSchemaDto schema = addonSchemaDesignerClient.GetSchema(BuildGetRequest(target));

		// Change only modalSettings.size: every other top-level field and every other modalSettings field is kept.
		// Parsed strictly: a body that is not a JSON object would otherwise be replaced by {"modalSettings":...} and
		// every other AppearanceSettings field would be lost without a word.
		JsonObject metadata = ParseMetadataForWrite(schema.MetaData, target.PageSchemaName);
		StoredSize previous = ReadStoredSize(schema, target);

		// A package above package-name that sets the size wins at runtime, so a write here would report success and
		// leave the window unchanged: refuse before anything is written. A failed check does not block the write;
		// the runtime is read again after the save and reports the size the window opens with.
		int? runtimeBefore = TryReadRuntimeValue(target.PageSchemaName).Value;
		if (FindOverride(target, previous.Value, runtimeBefore) is SizeOverride sizeOverride) {
			throw new InvalidOperationException(
				$"{Capitalize(sizeOverride.Text)}, so writing to '{target.PackageName}' would not change the window. "
				+ $"Nothing was changed. {StaleCacheHint}");
		}

		// The size is already stored in package-name's own layer and the window opens with it: a save, cache reset and
		// rebuild would change nothing, and a rebuild notifies every online user. Nothing is written.
		if (previous.Value == newValue && runtimeBefore == newValue
			&& string.Equals(previous.SourcePackageName, target.PackageName, StringComparison.OrdinalIgnoreCase)) {
			return new PageModalSizeWriteResult(
				target.PageSchemaName,
				target.PackageName,
				PageModalSizeCatalog.NameOf(newValue),
				previous.Value,
				previous.SourcePackageName,
				PageModalSizeCatalog.NameOf(newValue),
				previous.Warning);
		}

		if (metadata[ModalSettingsKey] is not JsonObject modalSettings) {
			modalSettings = new JsonObject();
			metadata[ModalSettingsKey] = modalSettings;
		}
		modalSettings[SizeKey] = newValue;
		schema.MetaData = metadata.ToJsonString(new JsonSerializerOptions { WriteIndented = true });

		addonSchemaDesignerClient.SaveSchema(schema);
		// The save may have created package-name's own layer, which the layers read before it do not hold.
		layerLocator.Forget();
		string resetWarning = addonSchemaDesignerClient.ResetClientScriptCache();
		// The same post-save sequence as the other add-on writers (RelatedPageAddonService, BusinessRuleAddonService).
		// AppearanceSettingsService reflects a SaveSchema at once (measured on 10.2.1027); whether the client bundles
		// pick the size up without a rebuild was not measured, so the rebuild is kept.
		string buildWarning = addonSchemaDesignerClient.BuildConfiguration();
		(int? runtimeValue, string runtimeWarning) = TryReadRuntimeValue(target.PageSchemaName);
		string overrideWarning = FindOverride(target, newValue, runtimeValue) is SizeOverride after
			? $"The size is saved in package '{target.PackageName}', but {after.Text}. {StaleCacheHint}"
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

	// The runtime merges the layers of every package the way the window opens; GetSchema (useFullHierarchy=false)
	// shows the size as package-name sees it. When they differ, a package package-name does not see sets the size.
	// Compared as stored (null and None both mean "no size"), not after the default is applied: a higher layer that
	// stores the default size (small) is an override too. A runtime answer with no size at all proves nothing and is
	// never an override. The package that sets it is the topmost layer of the environment (EC-1); when it cannot be
	// told apart, the text says so. Returns that finding, or null.
	private SizeOverride FindOverride(PageTarget target, int? seenValue, int? runtimeValue) {
		if (runtimeValue is null || ToStoredSize(runtimeValue) == ToStoredSize(seenValue)) {
			return null;
		}
		string seen = ToStoredSize(seenValue) is int seenSize ? NameValue(seenSize) : "no size";
		AppearanceSettingsLayerSource source =
			layerLocator.FindSource(target.PageSchemaName, target.PageSchemaUId, null, runtimeValue);
		string opens = $"the window opens {DescribeValue(ToStoredSize(runtimeValue))}, while package "
			+ $"'{target.PackageName}' sees {seen}";
		if (source.PackageName is string top && layerLocator.Sees(target.PageSchemaName, target.PackageName, top)) {
			// The top layer is one package-name already sees, so no layer above it explains the difference.
			return new SizeOverride(null,
				$"{opens}, although the top layer is in package '{top}', which '{target.PackageName}' sees: the "
				+ "runtime is probably serving a stale cached size");
		}
		string setter = source.PackageName is string packageName
			? $"package '{packageName}' sets the size"
			: $"a package '{target.PackageName}' does not depend on sets the size{DescribeUnknownSource(source)}";
		return new SizeOverride(source.PackageName, $"{opens}: {setter}");
	}

	private static string DescribeUnknownSource(AppearanceSettingsLayerSource source) {
		if (source.CandidatePackageNames.Count > 0) {
			return $" (one of {string.Join(", ", source.CandidatePackageNames.Select(name => $"'{name}'"))})";
		}
		return source.Warning is null ? string.Empty : $" ({source.Warning.TrimEnd('.')})";
	}

	// The size the window opens with: the runtime's merged size when it reported one, else the package's view; a
	// missing or None size opens with the modal default.
	private static int EffectiveValue(int? storedValue, int? runtimeValue) =>
		ToStoredSize(runtimeValue ?? storedValue) ?? RuntimeDefaultValue;

	private static int? ToStoredSize(int? value) => ModalSizeMetadata.ToStoredSize(value);

	private static string DescribeValue(int? value) =>
		value is int size ? NameValue(size) : $"{NameValue(RuntimeDefaultValue)} (no size set)";

	private static string Capitalize(string text) => char.ToUpperInvariant(text[0]) + text[1..];

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
		// The resolved name keeps the stored casing: the add-on layer lookup filters SysSchema.Name, which is
		// case-sensitive on PostgreSQL.
		string pageName = string.IsNullOrWhiteSpace(hierarchy[0].Name) ? normalizedPageName : hierarchy[0].Name;
		return new PageTarget(pageName, normalizedPackageName, packageId, pageSchemaUId);
	}

	private static AddonGetRequestDto BuildGetRequest(PageTarget target) =>
		ModalSizeMetadata.BuildGetRequest(target.PageSchemaUId, target.PackageId);

	// GetSchema returns the metadata merged across the layers package-name sees, and always names the REQUESTED
	// package. The add-on's own uId is persisted only when the package already has a layer, and that layer is the
	// source: a save (designer or clio) stores the merged metadata, size included. Without an own layer the source is
	// the topmost layer package-name sees; parentSchemaUId names the base layer, not that one (measured on 10.2.760).
	// The source is informational, so a failed lookup is a warning and never blocks a write.
	private StoredSize ReadStoredSize(AddonSchemaDto schema, PageTarget target) {
		if (!TryParseMetadata(schema.MetaData, out JsonObject metadata)) {
			return new StoredSize(null, null,
				$"The AppearanceSettings metadata of '{target.PageSchemaName}' is not a JSON object, so the stored "
				+ "size could not be read.");
		}
		int? value = ReadSize(metadata);
		if (value is null) {
			return new StoredSize(null, null, null);
		}
		try {
			if (FindLayerPackageName(ReadAdditionalString(schema, "uId")) is string ownPackageName) {
				return new StoredSize(value, ownPackageName, null);
			}
		} catch (InvalidOperationException exception) {
			return new StoredSize(value, null,
				$"The package the current size comes from could not be determined: {exception.Message}");
		}
		AppearanceSettingsLayerSource source =
			layerLocator.FindSource(target.PageSchemaName, target.PageSchemaUId, target.PackageName, value);
		return new StoredSize(value, source.PackageName, source.Warning);
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
	// a failed check is a warning, never a failed read or write.
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
		// None (0) is kept here: a higher layer that clears the size is an override FindOverride must see.
		return (root["value"] is JsonObject value ? ReadRawSize(value) : null, null);
	}

	private static int? ReadSize(JsonObject metadata) => ToStoredSize(ReadRawSize(metadata));

	private static int? ReadRawSize(JsonObject metadata) => ModalSizeMetadata.ReadRawSize(metadata);

	// Blank metadata is an empty object (the package has no add-on yet); anything else must parse to a JSON object.
	private static bool TryParseMetadata(string metaData, out JsonObject metadata) =>
		ModalSizeMetadata.TryParse(metaData, out metadata);

	private static JsonObject ParseMetadataForWrite(string metaData, string pageSchemaName) =>
		TryParseMetadata(metaData, out JsonObject metadata)
			? metadata
			: throw new InvalidOperationException(
				$"The AppearanceSettings metadata of '{pageSchemaName}' is not a JSON object, so other settings could "
				+ "not be kept. Nothing was changed.");

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
