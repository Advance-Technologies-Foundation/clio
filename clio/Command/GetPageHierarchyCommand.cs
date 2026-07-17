namespace Clio.Command;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Clio.Common;
using CommandLine;

/// <summary>
/// Options for the <c>get-page-hierarchy</c> command.
/// </summary>
[Verb("get-page-hierarchy", Aliases = ["page-hierarchy-get"],
	HelpText = "Get the full Freedom UI page replacing-schema chain (root first) with each schema's raw body in one round-trip")]
public class GetPageHierarchyOptions : EnvironmentOptions {

	/// <summary>
	/// Gets or sets the page schema name (any variant of the replacing chain).
	/// </summary>
	[Option("schema-name", Required = true,
		HelpText = "Freedom UI page schema name (any variant in the replacing chain)")]
	public string SchemaName { get; set; }

	/// <summary>
	/// Gets or sets the zero-based index of the first chain entry to return (ordered by hierarchy
	/// level, root first). Use with <c>--limit</c> to page a large chain.
	/// </summary>
	[Option("offset", Required = false, Default = 0,
		HelpText = "Zero-based index of the first chain entry to return (ordered by hierarchy level, root first)")]
	public int Offset { get; set; }

	/// <summary>
	/// Gets or sets the maximum number of chain entries to return. <c>0</c> (default) returns the whole
	/// chain from <c>--offset</c> onward.
	/// </summary>
	[Option("limit", Required = false, Default = 0,
		HelpText = "Maximum number of chain entries to return; 0 (default) returns the whole chain from --offset onward")]
	public int Limit { get; set; }

	/// <summary>
	/// Gets or sets a value indicating whether to omit each schema's raw body and return metadata only.
	/// </summary>
	[Option("metadata-only", Required = false, Default = false,
		HelpText = "Return chain metadata only (schema/package names, UIds, versions) without the raw bodies")]
	public bool MetadataOnly { get; set; }
}

/// <summary>
/// One entry in the ordered page replacing-schema chain.
/// </summary>
public sealed class PageHierarchySchemaEntry {

	/// <summary>Gets the zero-based hierarchy level (0 = root/base schema; highest = effective leaf).</summary>
	[JsonPropertyName("hierarchyLevel")]
	public int HierarchyLevel { get; init; }

	/// <summary>Gets the schema name.</summary>
	[JsonPropertyName("schemaName")]
	public string SchemaName { get; init; }

	/// <summary>Gets the schema identifier.</summary>
	[JsonPropertyName("schemaUId")]
	public string SchemaUId { get; init; }

	/// <summary>Gets the package name that owns this schema in the chain.</summary>
	[JsonPropertyName("packageName")]
	public string PackageName { get; init; }

	/// <summary>Gets the package identifier that owns this schema in the chain.</summary>
	[JsonPropertyName("packageUId")]
	public string PackageUId { get; init; }

	/// <summary>Gets the schema version.</summary>
	[JsonPropertyName("schemaVersion")]
	public int SchemaVersion { get; init; }

	/// <summary>Gets the schema type label (<c>web</c> / <c>mobile</c>).</summary>
	[JsonPropertyName("schemaType")]
	public string SchemaType { get; init; }

	/// <summary>Gets a value indicating whether this schema has a readable body.</summary>
	[JsonPropertyName("hasBody")]
	public bool HasBody { get; init; }

	/// <summary>Gets the length of the raw body (in characters), regardless of whether it is included.</summary>
	[JsonPropertyName("bodyLength")]
	public int BodyLength { get; init; }

	/// <summary>
	/// Gets the raw schema body. Omitted when <c>--metadata-only</c> is set or the schema has no body.
	/// </summary>
	[JsonPropertyName("body")]
	public string Body { get; init; }
}

/// <summary>
/// Response envelope for <c>get-page-hierarchy</c>.
/// </summary>
public sealed class GetPageHierarchyResponse {

	/// <summary>Gets or sets a value indicating whether the chain was resolved successfully.</summary>
	[JsonPropertyName("success")]
	public bool Success { get; set; }

	/// <summary>Gets or sets the requested schema name.</summary>
	[JsonPropertyName("schemaName")]
	public string SchemaName { get; set; }

	/// <summary>Gets or sets the root (base) schema name of the resolved chain.</summary>
	[JsonPropertyName("rootSchemaName")]
	public string RootSchemaName { get; set; }

	/// <summary>Gets or sets the total number of schemas in the full chain (before paging).</summary>
	[JsonPropertyName("totalCount")]
	public int TotalCount { get; set; }

	/// <summary>Gets or sets the effective offset applied to the chain.</summary>
	[JsonPropertyName("offset")]
	public int Offset { get; set; }

	/// <summary>Gets or sets the number of entries returned in this response.</summary>
	[JsonPropertyName("returnedCount")]
	public int ReturnedCount { get; set; }

	/// <summary>Gets or sets a value indicating whether more entries remain beyond this page.</summary>
	[JsonPropertyName("hasMore")]
	public bool HasMore { get; set; }

	/// <summary>Gets or sets the ordered chain entries (root first).</summary>
	[JsonPropertyName("schemas")]
	public List<PageHierarchySchemaEntry> Schemas { get; set; }

	/// <summary>Gets or sets the error message when <see cref="Success"/> is <c>false</c>.</summary>
	[JsonPropertyName("error")]
	public string Error { get; set; }
}

/// <summary>
/// Reads the full Freedom UI page replacing-schema chain (root first) with each schema's raw body in
/// one round-trip. This collapses the per-schema fan-out (one <c>get-page</c> / <c>get-client-unit-schema</c>
/// per chain member) that migration discovery otherwise performs into a single call: the platform
/// designer service already returns every body in the chain in one response
/// (<see cref="IPageDesignerHierarchyClient.GetParentSchemas"/>), so this command just surfaces it ordered.
/// </summary>
public class GetPageHierarchyCommand : Command<GetPageHierarchyOptions> {

	private readonly IApplicationClient _applicationClient;
	private readonly IServiceUrlBuilder _serviceUrlBuilder;
	private readonly IPageDesignerHierarchyClient _hierarchyClient;
	private readonly ILogger _logger;

	/// <summary>
	/// Initializes a new instance of the <see cref="GetPageHierarchyCommand"/> class.
	/// </summary>
	/// <param name="applicationClient">Remote Creatio client.</param>
	/// <param name="serviceUrlBuilder">Service URL builder.</param>
	/// <param name="hierarchyClient">Designer hierarchy client that returns the full chain with bodies.</param>
	/// <param name="logger">Logger used for CLI output.</param>
	public GetPageHierarchyCommand(
		IApplicationClient applicationClient,
		IServiceUrlBuilder serviceUrlBuilder,
		IPageDesignerHierarchyClient hierarchyClient,
		ILogger logger) {
		_applicationClient = applicationClient;
		_serviceUrlBuilder = serviceUrlBuilder;
		_hierarchyClient = hierarchyClient;
		_logger = logger;
	}

	/// <summary>
	/// Attempts to resolve the page replacing-schema chain for the requested schema.
	/// </summary>
	/// <param name="options">Command options.</param>
	/// <param name="response">Response envelope.</param>
	/// <returns><c>true</c> when the chain was resolved successfully; otherwise <c>false</c>.</returns>
	public virtual bool TryGetHierarchy(GetPageHierarchyOptions options, out GetPageHierarchyResponse response) {
		if (string.IsNullOrWhiteSpace(options.SchemaName)) {
			response = new GetPageHierarchyResponse { Success = false, Error = "schema-name is required" };
			return false;
		}
		if (options.Offset < 0) {
			response = new GetPageHierarchyResponse { Success = false, Error = "offset must be zero or greater" };
			return false;
		}
		if (options.Limit < 0) {
			response = new GetPageHierarchyResponse { Success = false, Error = "limit must be zero or greater" };
			return false;
		}
		try {
			IReadOnlyList<PageDesignerHierarchySchema> effectiveFirst = ResolveEffectiveFirstHierarchy(options.SchemaName);
			if (effectiveFirst.Count == 0) {
				response = new GetPageHierarchyResponse {
					Success = false,
					Error = $"Schema '{options.SchemaName}' hierarchy is empty or could not be resolved"
				};
				return false;
			}
			response = BuildResponse(options, effectiveFirst);
			return true;
		}
		catch (Exception ex) {
			response = new GetPageHierarchyResponse { Success = false, Error = ex.Message };
			return false;
		}
	}

	/// <summary>
	/// Orders the resolved hierarchy root-first, applies <c>offset</c>/<c>limit</c> paging, and projects
	/// each schema into a response entry. Pure (no I/O) so the ordering/paging/body-inclusion contract is
	/// unit-testable without a live environment.
	/// </summary>
	/// <param name="options">Command options (schema name, paging, metadata-only).</param>
	/// <param name="effectiveFirst">
	/// The resolved chain as returned by the designer service: element [0] is the effective (leaf)
	/// schema, the rest ascend to the root.
	/// </param>
	/// <returns>The ordered, paged response.</returns>
	internal static GetPageHierarchyResponse BuildResponse(
		GetPageHierarchyOptions options,
		IReadOnlyList<PageDesignerHierarchySchema> effectiveFirst) {
		// Element [0] is the effective (leaf) schema; the rest ascend to the root. The deterministic
		// bundle merge consumes the REVERSED order (root first), which is also "ordered by hierarchy
		// level" — surface that order so callers see base-to-derived, matching the merge and get-page.
		List<PageDesignerHierarchySchema> rootFirst = effectiveFirst.Reverse().ToList();

		int total = rootFirst.Count;
		int offset = Math.Min(options.Offset, total);
		int take = options.Limit == 0 ? total - offset : Math.Min(options.Limit, total - offset);
		var page = new List<PageHierarchySchemaEntry>(take);
		for (int i = 0; i < take; i++) {
			int level = offset + i;
			PageDesignerHierarchySchema schema = rootFirst[level];
			bool hasBody = !string.IsNullOrEmpty(schema.Body);
			page.Add(new PageHierarchySchemaEntry {
				HierarchyLevel = level,
				SchemaName = schema.Name,
				SchemaUId = schema.UId,
				PackageName = schema.PackageName,
				PackageUId = schema.PackageUId,
				SchemaVersion = schema.SchemaVersion,
				SchemaType = PageSchemaTypeExtensions.FromNumericValue(schema.SchemaType).ToLabel(),
				HasBody = hasBody,
				BodyLength = schema.Body?.Length ?? 0,
				Body = options.MetadataOnly || !hasBody ? null : schema.Body
			});
		}
		return new GetPageHierarchyResponse {
			Success = true,
			SchemaName = options.SchemaName,
			RootSchemaName = rootFirst[0].Name,
			TotalCount = total,
			Offset = offset,
			ReturnedCount = page.Count,
			HasMore = offset + page.Count < total,
			Schemas = page
		};
	}

	// ponytail: mirrors PageGetCommand's chain resolution (metadata -> design package -> full
	// hierarchy from the root). Kept as a focused copy rather than refactoring the working get-page
	// path; unifying both onto one resolver is tracked as ENG-93249.
	private IReadOnlyList<PageDesignerHierarchySchema> ResolveEffectiveFirstHierarchy(string schemaName) {
		var (metadata, _) = PageSchemaMetadataHelper.QuerySysSchemaRow(
			_applicationClient,
			_serviceUrlBuilder,
			schemaName,
			("UId", "UId"),
			("PackageUId", "SysPackage.UId"));
		string schemaUId = metadata?["UId"]?.ToString();
		string packageUId = metadata?["PackageUId"]?.ToString();
		if (string.IsNullOrWhiteSpace(schemaUId) || string.IsNullOrWhiteSpace(packageUId)) {
			return Array.Empty<PageDesignerHierarchySchema>();
		}
		string designPackageUId = null;
		try {
			designPackageUId = _hierarchyClient.GetDesignPackageUId(schemaUId);
		} catch {
			designPackageUId = null;
		}
		if (string.IsNullOrWhiteSpace(designPackageUId)) {
			designPackageUId = packageUId;
		}
		IReadOnlyList<PageDesignerHierarchySchema> initialHierarchy =
			_hierarchyClient.GetParentSchemas(schemaUId, designPackageUId);
		if (initialHierarchy.Count == 0) {
			return Array.Empty<PageDesignerHierarchySchema>();
		}
		// Normalize to the root-most variant of the requested name and re-fetch from it, exactly as
		// get-page does: the name->UId metadata lookup can resolve to an arbitrary replacing variant
		// (a page replaced across packages has one SysSchema row per package), so anchoring on the
		// root variant yields the same complete, deterministic chain get-page merges.
		string rootSchemaUId = FindRootSchemaUId(initialHierarchy, schemaName) ?? schemaUId;
		if (string.Equals(rootSchemaUId, schemaUId, StringComparison.OrdinalIgnoreCase)) {
			return initialHierarchy;
		}
		IReadOnlyList<PageDesignerHierarchySchema> fullHierarchy =
			_hierarchyClient.GetParentSchemas(rootSchemaUId, designPackageUId);
		return fullHierarchy.Count > 0 ? fullHierarchy : initialHierarchy;
	}

	private static string FindRootSchemaUId(IReadOnlyList<PageDesignerHierarchySchema> hierarchy, string schemaName) {
		for (int i = hierarchy.Count - 1; i >= 0; i--) {
			if (string.Equals(hierarchy[i].Name, schemaName, StringComparison.OrdinalIgnoreCase)) {
				return hierarchy[i].UId;
			}
		}
		return null;
	}

	/// <inheritdoc />
	public override int Execute(GetPageHierarchyOptions options) {
		bool success = TryGetHierarchy(options, out GetPageHierarchyResponse response);
		_logger.WriteInfo(JsonSerializer.Serialize(response));
		return success ? 0 : 1;
	}
}
