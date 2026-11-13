namespace Clio.Command;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Clio.Common;
using Clio.Package;
using Newtonsoft.Json;

/// <summary>
/// Finds the replacing layers of a Freedom UI page that a write could go to, so that a page customized in more
/// than one editable package is never written to one of them without the user choosing it.
/// </summary>
public interface IPageLayerInspector {
	/// <summary>
	/// Reads the lock state of every package in the chain with one <c>SysPackage</c> query.
	/// </summary>
	/// <param name="chain">The page chain, in any order.</param>
	/// <returns>
	/// Lock state by package UId (case-insensitive), or <c>null</c> when the query failed, plus a warning for every
	/// lock state left unknown: a failed query, a package <c>SysPackage</c> did not return, or a chain entry without
	/// a package UId. A failed read never throws; the caller reports the lock state as unknown.
	/// </returns>
	PagePackageLockRead ReadPackageLocks(IReadOnlyList<PageDesignerHierarchySchema> chain);

	/// <summary>
	/// Classifies the replacing layers of the page and decides whether a write must name its target package.
	/// </summary>
	/// <param name="schemaName">The page schema name; entries with this name are the page's own schemas.</param>
	/// <param name="effectiveFirstChain">
	/// The full chain as the designer service returns it: element [0] is the effective (leaf) schema, the rest
	/// ascend to the root template. It must be resolved from the root variant of the page, as <c>get-page</c> does,
	/// or replacing layers are missing from it.
	/// </param>
	/// <param name="defaultTargetPackageUId">
	/// The package a write without <c>target-package-uid</c> goes to (<c>GetDesignPackageUId</c>).
	/// </param>
	/// <returns>The package choice; <see cref="PagePackageChoice.Required"/> tells whether the user must choose.</returns>
	PagePackageChoice Inspect(
		string schemaName,
		IReadOnlyList<PageDesignerHierarchySchema> effectiveFirstChain,
		string defaultTargetPackageUId);
}

/// <summary>
/// Package lock state read for a page chain.
/// </summary>
/// <param name="Locks">Lock state by package UId (case-insensitive); <c>null</c> when the query failed.</param>
/// <param name="Warning">Why some lock states are unknown; <c>null</c> when every one was read.</param>
public sealed record PagePackageLockRead(IReadOnlyDictionary<string, bool?> Locks, string Warning) {
	/// <summary>
	/// Gets the lock state of a package, or <c>null</c> when it is unknown.
	/// </summary>
	/// <param name="packageUId">The package UId.</param>
	/// <returns>Whether the package is locked; <c>null</c> when unknown.</returns>
	public bool? Get(string packageUId) =>
		packageUId is not null && Locks is not null && Locks.TryGetValue(packageUId, out bool? locked) ? locked : null;
}

/// <summary>
/// Which packages a write to a Freedom UI page could change, and whether the user has to pick one.
/// </summary>
public sealed class PagePackageChoice {
	/// <summary>
	/// Gets whether a write must name its target package: a replacing layer outside the default target is in an
	/// unlocked package, or its lock state is unknown. Ask the user which package to change before writing, then
	/// pass the chosen <c>packageUId</c> as <c>target-package-uid</c> — the default target's UId when the user keeps it.
	/// </summary>
	[JsonProperty("required")]
	[JsonPropertyName("required")]
	public bool Required { get; init; }

	/// <summary>Gets the package a write without <c>target-package-uid</c> goes to.</summary>
	[JsonProperty("defaultTargetPackageUId")]
	[JsonPropertyName("defaultTargetPackageUId")]
	public string DefaultTargetPackageUId { get; init; }

	/// <summary>
	/// Gets the name of the default target package, when <see cref="Required"/> is <c>true</c> and the name could be
	/// read; it names a default target that holds no layer of the page yet.
	/// </summary>
	[JsonProperty("defaultTargetPackageName", NullValueHandling = NullValueHandling.Ignore)]
	[JsonPropertyName("defaultTargetPackageName")]
	[System.Text.Json.Serialization.JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string DefaultTargetPackageName { get; init; }

	/// <summary>
	/// Gets the page's replacing layers, lowest first; the base schema of the page is not a layer. The highest
	/// layer wins at runtime for an element that several layers change.
	/// </summary>
	[JsonProperty("layers")]
	[JsonPropertyName("layers")]
	public IReadOnlyList<PagePackageLayer> Layers { get; init; } = [];

	/// <summary>Gets why the lock state of some layers is unknown, when it is.</summary>
	[JsonProperty("warning", NullValueHandling = NullValueHandling.Ignore)]
	[JsonPropertyName("warning")]
	[System.Text.Json.Serialization.JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string Warning { get; init; }
}

/// <summary>
/// One replacing layer of a Freedom UI page.
/// </summary>
public sealed class PagePackageLayer {
	/// <summary>Gets the package that holds the layer.</summary>
	[JsonProperty("packageName")]
	[JsonPropertyName("packageName")]
	public string PackageName { get; init; }

	/// <summary>Gets the package UId; pass it as <c>target-package-uid</c> to write to this layer.</summary>
	[JsonProperty("packageUId")]
	[JsonPropertyName("packageUId")]
	public string PackageUId { get; init; }

	/// <summary>Gets the replacing schema UId of the layer.</summary>
	[JsonProperty("schemaUId")]
	[JsonPropertyName("schemaUId")]
	public string SchemaUId { get; init; }

	/// <summary>Gets the position in the chain (root template = 0), as <c>get-page-hierarchy</c> reports it.</summary>
	[JsonProperty("hierarchyLevel")]
	[JsonPropertyName("hierarchyLevel")]
	public int HierarchyLevel { get; init; }

	/// <summary>Gets whether the package is locked; <c>null</c> when the lock state could not be read.</summary>
	[JsonProperty("packageLocked")]
	[JsonPropertyName("packageLocked")]
	public bool? PackageLocked { get; init; }

	/// <summary>Gets whether a write without <c>target-package-uid</c> goes to this layer.</summary>
	[JsonProperty("isDefaultTarget")]
	[JsonPropertyName("isDefaultTarget")]
	public bool IsDefaultTarget { get; init; }

	/// <summary>
	/// Gets what the layer itself changes: its <c>viewConfigDiff</c> operations as <c>"operation name"</c> and its
	/// handler requests, at most <see cref="PageLayerInspector.MaxChangesPerLayer"/> entries.
	/// </summary>
	[JsonProperty("changes")]
	[JsonPropertyName("changes")]
	public IReadOnlyList<string> Changes { get; init; } = [];

	/// <summary>Gets how many changes were left out of <see cref="Changes"/>.</summary>
	[JsonProperty("moreChanges")]
	[JsonPropertyName("moreChanges")]
	public int MoreChanges { get; init; }

	/// <summary>
	/// Gets whether clio could not parse the layer body, so <see cref="Changes"/> is empty because the changes are
	/// unknown, not because the layer has none.
	/// </summary>
	[JsonProperty("changesUnreadable", DefaultValueHandling = DefaultValueHandling.Ignore)]
	[JsonPropertyName("changesUnreadable")]
	[System.Text.Json.Serialization.JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
	public bool ChangesUnreadable { get; init; }
}

/// <inheritdoc />
public sealed class PageLayerInspector : IPageLayerInspector {
	/// <summary>The most changes listed per layer; the rest are counted in <see cref="PagePackageLayer.MoreChanges"/>.</summary>
	internal const int MaxChangesPerLayer = 10;

	private static readonly IReadOnlyList<SelectQueryHelper.SelectQueryColumnDefinition> PackageLockColumns = [
		new("UId", "UId"),
		new("Name", "Name"),
		new("InstallType", "InstallType")
	];

	private readonly IApplicationClient _applicationClient;
	private readonly IServiceUrlBuilder _serviceUrlBuilder;
	private readonly IPageSchemaBodyParser _bodyParser;

	/// <summary>
	/// Initializes a new instance of the <see cref="PageLayerInspector"/> class.
	/// </summary>
	/// <param name="applicationClient">Remote Creatio client.</param>
	/// <param name="serviceUrlBuilder">Service URL builder.</param>
	/// <param name="bodyParser">Parser for schema body sections.</param>
	public PageLayerInspector(
		IApplicationClient applicationClient,
		IServiceUrlBuilder serviceUrlBuilder,
		IPageSchemaBodyParser bodyParser) {
		_applicationClient = applicationClient;
		_serviceUrlBuilder = serviceUrlBuilder;
		_bodyParser = bodyParser;
	}

	/// <inheritdoc />
	public PagePackageLockRead ReadPackageLocks(IReadOnlyList<PageDesignerHierarchySchema> chain) =>
		ReadPackages(chain, null).LockRead;

	/// <summary>
	/// Reads the lock state of the chain's packages and the package names with one <c>SysPackage</c> query.
	/// </summary>
	/// <param name="chain">The page chain, in any order.</param>
	/// <param name="extraPackageUId">One more package to read the name of, outside the chain; <c>null</c> for none.
	/// Its lock state is never reported as unknown, since no layer of the chain is in it.</param>
	/// <returns>The lock read, as <see cref="ReadPackageLocks"/> returns it, and package names by UId.</returns>
	private (PagePackageLockRead LockRead, IReadOnlyDictionary<string, string> Names) ReadPackages(
		IReadOnlyList<PageDesignerHierarchySchema> chain, string extraPackageUId) {
		List<string> packageUIds = chain
			.Select(schema => schema.PackageUId)
			.Where(uId => !string.IsNullOrWhiteSpace(uId))
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.ToList();
		List<string> schemasWithoutPackage = chain
			.Where(schema => string.IsNullOrWhiteSpace(schema.PackageUId))
			.Select(schema => schema.Name)
			.ToList();
		string noPackageWarning = schemasWithoutPackage.Count == 0
			? null
			: "Package lock state is unknown (packageLocked is null) for schemas without a package UId: "
				+ $"{string.Join(", ", schemasWithoutPackage)}.";
		Dictionary<string, bool?> locks = new(StringComparer.OrdinalIgnoreCase);
		Dictionary<string, string> names = new(StringComparer.OrdinalIgnoreCase);
		List<string> queried = string.IsNullOrWhiteSpace(extraPackageUId)
			|| packageUIds.Contains(extraPackageUId, StringComparer.OrdinalIgnoreCase)
				? packageUIds
				: [.. packageUIds, extraPackageUId];
		if (queried.Count == 0) {
			return (new PagePackageLockRead(locks, noPackageWarning), names);
		}
		try {
			PackageLockSelectResponse answer = SelectQueryHelper.ExecuteSelectQuery<PackageLockSelectResponse>(
				_applicationClient, _serviceUrlBuilder,
				SelectQueryHelper.BuildSelectQueryWithInFilter(
					"SysPackage", PackageLockColumns, "UId", queried, SelectQueryHelper.GuidDataValueType));
			foreach (PackageLockRow row in answer.Rows.Where(row => !string.IsNullOrWhiteSpace(row.UId))) {
				locks[row.UId] = PackageTargetResolver.IsLockedInstallType(row.InstallType);
				names[row.UId] = row.Name;
			}
			List<string> unknown = packageUIds.Where(uId => locks.GetValueOrDefault(uId) is null).ToList();
			return (new PagePackageLockRead(locks, PageWarningText.Join(
				unknown.Count == 0
					? null
					: $"Package lock state is unknown (packageLocked is null) for packages: {string.Join(", ", unknown)}.",
				noPackageWarning)), names);
		}
		catch (Exception ex) {
			// A timeout (OperationCanceledException) is a failed read too: no cancellation token reaches it.
			return (new PagePackageLockRead(null,
				$"Package lock state could not be read, so packageLocked is null for every schema: {ex.Message}"), names);
		}
	}

	/// <inheritdoc />
	public PagePackageChoice Inspect(
		string schemaName,
		IReadOnlyList<PageDesignerHierarchySchema> effectiveFirstChain,
		string defaultTargetPackageUId) {
		List<PageDesignerHierarchySchema> rootFirst = effectiveFirstChain.Reverse().ToList();
		// The base schema of the page is the LOWEST entry that carries the page name — not the chain root, which
		// is a platform template (BaseTemplate) for every page built from one. Everything above the base with
		// the page name is a replacing layer.
		int baseIndex = rootFirst.FindIndex(schema =>
			string.Equals(schema.Name, schemaName, StringComparison.OrdinalIgnoreCase));
		if (baseIndex < 0) {
			return new PagePackageChoice { DefaultTargetPackageUId = defaultTargetPackageUId };
		}
		List<(PageDesignerHierarchySchema schema, int level)> replacing = rootFirst
			.Select((schema, level) => (schema, level))
			.Skip(baseIndex + 1)
			.Where(entry => string.Equals(entry.schema.Name, schemaName, StringComparison.OrdinalIgnoreCase))
			.ToList();
		List<PageDesignerHierarchySchema> outsideDefault = replacing
			.Select(entry => entry.schema)
			.Where(schema => !IsDefaultTarget(schema, defaultTargetPackageUId))
			.ToList();
		// The common case — the page is customized only in the default target — needs no lock read at all.
		if (outsideDefault.Count == 0) {
			return new PagePackageChoice { DefaultTargetPackageUId = defaultTargetPackageUId };
		}
		// One query for every layer, the default target's included, so each reported lock state is a read one. A
		// default target without a layer joins the same query only to have its name read.
		PageDesignerHierarchySchema defaultLayer = replacing
			.Select(entry => entry.schema)
			.FirstOrDefault(schema => IsDefaultTarget(schema, defaultTargetPackageUId));
		(PagePackageLockRead lockRead, IReadOnlyDictionary<string, string> names) = ReadPackages(
			replacing.Select(entry => entry.schema).ToList(), defaultLayer is null ? defaultTargetPackageUId : null);
		bool required = outsideDefault.Any(schema => lockRead.Get(schema.PackageUId) != true);
		if (!required) {
			// Only locked vendor layers sit outside the default target: nothing to choose, and listing them would
			// cost every get-page of a vendor page (~20 layers on Contacts_FormPage) tokens for no decision.
			return new PagePackageChoice { DefaultTargetPackageUId = defaultTargetPackageUId };
		}
		return new PagePackageChoice {
			Required = true,
			DefaultTargetPackageUId = defaultTargetPackageUId,
			DefaultTargetPackageName = defaultLayer?.PackageName
				?? (string.IsNullOrWhiteSpace(defaultTargetPackageUId) ? null : names.GetValueOrDefault(defaultTargetPackageUId)),
			Layers = replacing
				.Select(entry => BuildLayer(entry.schema, entry.level, lockRead.Get(entry.schema.PackageUId),
					defaultTargetPackageUId))
				.ToList(),
			Warning = lockRead.Warning
		};
	}

	// Both UIds must be known: a layer without a package UId must never match an unknown default target (null ==
	// null), or it would drop out of the layers outside the default and the write would go on without a choice.
	private static bool IsDefaultTarget(PageDesignerHierarchySchema schema, string defaultTargetPackageUId) =>
		!string.IsNullOrWhiteSpace(schema.PackageUId)
		&& !string.IsNullOrWhiteSpace(defaultTargetPackageUId)
		&& string.Equals(schema.PackageUId, defaultTargetPackageUId, StringComparison.OrdinalIgnoreCase);

	private PagePackageLayer BuildLayer(PageDesignerHierarchySchema schema, int level, bool? locked,
		string defaultTargetPackageUId) {
		List<string> changes = ReadChanges(schema.Body);
		return new PagePackageLayer {
			ChangesUnreadable = changes is null,
			PackageName = schema.PackageName,
			PackageUId = schema.PackageUId,
			SchemaUId = schema.UId,
			HierarchyLevel = level,
			PackageLocked = locked,
			IsDefaultTarget = IsDefaultTarget(schema, defaultTargetPackageUId),
			Changes = changes?.Take(MaxChangesPerLayer).ToList() ?? [],
			MoreChanges = Math.Max(0, (changes?.Count ?? 0) - MaxChangesPerLayer)
		};
	}

	/// <summary>Reads the layer's own changes; <c>null</c> when the body cannot be parsed.</summary>
	private List<string> ReadChanges(string body) {
		if (string.IsNullOrWhiteSpace(body)) {
			return [];
		}
		try {
			PageParsedSchemaBody parsed = _bodyParser.Parse(body);
			List<string> changes = PageGetCommand.ExtractViewConfigOps(parsed)
				.Select(op => string.IsNullOrWhiteSpace(op.Name) ? op.Operation : $"{op.Operation} {op.Name}")
				.Where(change => !string.IsNullOrWhiteSpace(change))
				.ToList();
			(_, List<string> handlerRequests) = PageGetCommand.ExtractHandlerInfo(parsed.Handlers?.Trim());
			changes.AddRange(handlerRequests.Select(request => $"handler {request}"));
			return changes;
		}
		catch (Exception ex) when (ex is InvalidOperationException or RegexMatchTimeoutException) {
			// The change list only helps the user choose; a body clio cannot parse must not hide the layer, and the
			// layer is marked unreadable so it is not reported as having no own changes.
			return null;
		}
	}

	private sealed class PackageLockSelectResponse : SelectQueryHelper.SelectQueryResponseBaseDto {
		[JsonPropertyName("rows")]
		public List<PackageLockRow> Rows { get; init; } = [];
	}

	private sealed class PackageLockRow {
		[JsonPropertyName("UId")]
		public string UId { get; init; }

		[JsonPropertyName("Name")]
		public string Name { get; init; }

		[JsonPropertyName("InstallType")]
		public int? InstallType { get; init; }
	}
}
