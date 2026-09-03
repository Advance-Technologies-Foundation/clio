namespace Clio.Command;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Clio.Common;
using Clio.Package;
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

	/// <summary>
	/// Gets or sets a value indicating whether the raw bodies were auto-omitted because the selected
	/// window exceeded the response size budget (AC3). When <c>true</c>, re-request with
	/// <c>--metadata-only</c> or a smaller <c>--offset</c>/<c>--limit</c> window to page the bodies.
	/// </summary>
	[JsonPropertyName("bodiesOmittedForSize")]
	public bool BodiesOmittedForSize { get; set; }

	/// <summary>Gets or sets an advisory message (e.g. the size-budget omission hint), when applicable.</summary>
	[JsonPropertyName("warning")]
	public string Warning { get; set; }

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

	/// <summary>
	/// Default serialized-body budget (in characters) for one response. When the summed body length of
	/// the selected window exceeds this and bodies would otherwise be inlined, <see cref="BuildResponse"/>
	/// auto-omits the bodies and flags <see cref="GetPageHierarchyResponse.BodiesOmittedForSize"/> so a
	/// required-arg-only call on a deep (13–18-schema) chain cannot dump hundreds of KB–MB into the MCP
	/// transcript (ENG-93727 AC3). Metadata (incl. <c>bodyLength</c>) is always returned so the caller can
	/// page deliberately with <c>--offset</c>/<c>--limit</c> or fetch a single body via <c>get-page</c>.
	/// </summary>
	internal const int DefaultBodySizeBudgetChars = 200_000;

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
			(IReadOnlyList<PageDesignerHierarchySchema> effectiveFirst, string lookupError) =
				ResolveEffectiveFirstHierarchy(options.SchemaName);
			if (lookupError is not null) {
				response = new GetPageHierarchyResponse { Success = false, Error = lookupError };
				return false;
			}
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

		// AC3 size guard: when bodies would be inlined (not metadata-only), sum the raw body length of
		// the selected window up front. If it blows the budget, omit the bodies for the whole page and
		// flag it — keeping the one-round-trip metadata win while never dumping a multi-MB payload into
		// the transcript. Metadata (incl. bodyLength) is unaffected, so the caller can page deliberately.
		bool omitBodiesForSize = false;
		if (!options.MetadataOnly) {
			long windowBodyChars = 0;
			for (int i = 0; i < take; i++) {
				windowBodyChars += rootFirst[offset + i].Body?.Length ?? 0;
			}
			omitBodiesForSize = windowBodyChars > DefaultBodySizeBudgetChars;
		}
		bool includeBodies = !options.MetadataOnly && !omitBodiesForSize;

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
				Body = includeBodies && hasBody ? schema.Body : null
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
			BodiesOmittedForSize = omitBodiesForSize,
			Warning = omitBodiesForSize
				? $"Bodies omitted: the selected window exceeds the {DefaultBodySizeBudgetChars}-char response budget. "
					+ "Re-request with --metadata-only, or page with --offset/--limit, or fetch a single schema body via get-page."
				: null,
			Schemas = page
		};
	}

	// NOTE (ENG-93249): mirrors PageGetCommand's chain resolution (metadata -> design package -> full
	// hierarchy from the root). Kept as a focused copy rather than refactoring the working get-page
	// path; unifying both onto one resolver is tracked as ENG-93249.
	/// <summary>
	/// Resolves the replacing-schema chain for <paramref name="schemaName"/>, classifying the outcome into three
	/// states so that a call which never produced an answer is never reported as an answer about the page
	/// (ENG-95262 story 13, following the classification story 11 established in
	/// <see cref="PageSchemaMetadataHelper"/>):
	/// <list type="bullet">
	/// <item><description><c>(chain, null)</c> — the chain resolved.</description></item>
	/// <item><description><c>(empty, null)</c> — the environment answered, and the chain is genuinely
	/// empty; the caller reports the empty-hierarchy contract.</description></item>
	/// <item><description><c>(empty, message)</c> — the schema metadata lookup did not answer, or answered that the
	/// schema is absent. The message is the classified text the metadata helper produced (an HTML login page, a
	/// timeout, a transport failure, or "Schema 'X' not found") and is surfaced verbatim, so a broken call and an
	/// absent page stay two different outcomes.</description></item>
	/// </list>
	/// </summary>
	/// <param name="schemaName">Requested page schema name.</param>
	/// <returns>The designer-ordered chain (effective schema first), and the lookup failure when there is one.</returns>
	private (IReadOnlyList<PageDesignerHierarchySchema> schemas, string lookupError) ResolveEffectiveFirstHierarchy(
		string schemaName) {
		var (metadata, metadataError) = PageSchemaMetadataHelper.QuerySysSchemaRow(
			_applicationClient,
			_serviceUrlBuilder,
			schemaName,
			("UId", "UId"),
			("PackageUId", "SysPackage.UId"));
		string schemaUId = metadata?["UId"]?.ToString();
		string packageUId = metadata?["PackageUId"]?.ToString();
		if (string.IsNullOrWhiteSpace(schemaUId) || string.IsNullOrWhiteSpace(packageUId)) {
			// Collapsing this to an empty chain threw away the helper's classification: an expired session or an
			// unreachable environment then read as "this page has no hierarchy", sending the caller to look at
			// their schema name. A row that resolved but is missing one of the two columns is the only case here
			// with no message of its own.
			return (Array.Empty<PageDesignerHierarchySchema>(),
				metadataError ?? $"Schema '{schemaName}' metadata is missing the schema or package UId");
		}
		string designPackageUId = ResolveDesignPackageUId(schemaUId, packageUId, schemaName);
		IReadOnlyList<PageDesignerHierarchySchema> initialHierarchy =
			_hierarchyClient.GetParentSchemas(schemaUId, designPackageUId);
		if (initialHierarchy.Count == 0) {
			return (Array.Empty<PageDesignerHierarchySchema>(), null);
		}
		// Normalize to the root-most variant of the requested name and re-fetch from it, exactly as
		// get-page does: the name->UId metadata lookup can resolve to an arbitrary replacing variant
		// (a page replaced across packages has one SysSchema row per package), so anchoring on the
		// root variant yields the same complete, deterministic chain get-page merges.
		string rootSchemaUId = FindRootSchemaUId(initialHierarchy, schemaName) ?? schemaUId;
		if (string.Equals(rootSchemaUId, schemaUId, StringComparison.OrdinalIgnoreCase)) {
			return (initialHierarchy, null);
		}
		IReadOnlyList<PageDesignerHierarchySchema> fullHierarchy =
			_hierarchyClient.GetParentSchemas(rootSchemaUId, designPackageUId);
		return (fullHierarchy.Count > 0 ? fullHierarchy : initialHierarchy, null);
	}

	/// <summary>
	/// Resolves the design (editable) package UId the chain is anchored on, falling back to the schema's own
	/// package ONLY when the service answered and had no design package to give.
	/// </summary>
	/// <param name="schemaUId">Schema identifier to resolve the design package for.</param>
	/// <param name="ownPackageUId">The schema's own package, used as the fallback anchor.</param>
	/// <param name="schemaName">Requested schema name, for the diagnostic line.</param>
	/// <returns>The package UId to anchor the hierarchy read on.</returns>
	/// <remarks>
	/// The bare <c>catch</c> this replaces made every failure look like "no design package": a timeout, a 500 on
	/// that endpoint alone, or an HTML login page all silently re-anchored the read on the schema's RUNTIME package,
	/// and the designer service — a different endpoint, which may still be healthy — then answered with a chain that
	/// looks like the answer while it can be missing replacing layers. That is an exit-0 wrong answer, so only the
	/// answered-rejection family may license the fallback (ENG-95262 story 13); everything else propagates to
	/// <see cref="TryGetHierarchy"/>, which reports it as a failed read.
	/// </remarks>
	private string ResolveDesignPackageUId(string schemaUId, string ownPackageUId, string schemaName) {
		string designPackageUId = null;
		try {
			designPackageUId = _hierarchyClient.GetDesignPackageUId(schemaUId);
		}
		catch (Exception ex) when (IsAnsweredRejection(ex)) {
			// The service answered and rejected the lookup, so the schema's own package IS the correct anchor.
			// Logged at debug so the degradation stays diagnosable without adding noise to the common case, the
			// same way get-classic-page-sources records its own fallback.
			_logger.WriteDebug(
				$"GetDesignPackageUId was rejected for '{schemaName}' ({ex.Message}); anchoring on the schema's own package.");
		}
		return string.IsNullOrWhiteSpace(designPackageUId) ? ownPackageUId : designPackageUId;
	}

	/// <summary>
	/// Returns whether <paramref name="exception"/> means the design-package service ANSWERED and rejected the
	/// lookup — the one family that licenses the own-package fallback, because it is a statement about the schema.
	/// <see cref="NonJsonServiceResponseException"/> is excluded even though it derives from
	/// <see cref="InvalidOperationException"/>: an HTML login/error page carries no statement about the design
	/// package. Timeouts, transport failures and unparseable bodies are therefore not rejections and propagate.
	/// </summary>
	/// <param name="exception">Exception raised by the design-package lookup.</param>
	/// <returns><see langword="true"/> when the service answered and rejected the lookup.</returns>
	private static bool IsAnsweredRejection(Exception exception) =>
		exception is InvalidOperationException and not NonJsonServiceResponseException;

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
