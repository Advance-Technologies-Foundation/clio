using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// Provides the curated Freedom UI request catalog (<c>crt.*Request</c> types wired
/// through <c>RequestBindingConfig</c> outputs) used by the <c>get-request-info</c>
/// MCP tool. Backed by the same CDN → file cache fallback chain as the component
/// catalogs, via the requests-flavored <see cref="IRequestRegistryClient"/>.
/// OOTB button-action requests initiative (ENG-93187).
/// </summary>
public interface IRequestInfoCatalog {
	/// <summary>
	/// Returns the parsed catalog state for the requested version, including the source
	/// tier that produced the bytes (CDN, cache, or local override). Symmetric with
	/// <see cref="IComponentInfoCatalog.LoadAsync"/>.
	/// </summary>
	Task<RequestCatalogState> LoadAsync(string requestedVersion, CancellationToken cancellationToken = default);
}

/// <summary>
/// Loads the curated Freedom UI request catalog through <see cref="IRequestRegistryClient"/>.
/// Re-parses on every request so background CDN refreshes become visible to AI without a
/// process restart — the byte-level cache lives in the registry client; the catalog only
/// turns bytes into POCOs (parse cost is sub-millisecond on the small request payload).
/// </summary>
public sealed class RequestInfoCatalog : IRequestInfoCatalog {
	private readonly IRequestRegistryClient _registryClient;

	public RequestInfoCatalog(IRequestRegistryClient registryClient) {
		_registryClient = registryClient ?? throw new ArgumentNullException(nameof(registryClient));
	}

	/// <inheritdoc />
	public async Task<RequestCatalogState> LoadAsync(string requestedVersion, CancellationToken cancellationToken = default) {
		string key = string.IsNullOrWhiteSpace(requestedVersion)
			? ComponentRegistryClient.LatestVersion
			: requestedVersion.Trim();
		ComponentRegistryFetchResult fetch = await _registryClient.GetAsync(key, cancellationToken).ConfigureAwait(false);
		using (fetch.Content) {
			return LoadFromStream(fetch.Content, fetch.ResolvedVersion, fetch.Source);
		}
	}

	/// <summary>
	/// Parses a request-registry stream into the in-memory catalog state. Exposed for
	/// hermetic tests and for callers that want to bypass the CDN/cache chain entirely.
	/// Unlike the component registry there is no legacy top-level-array shape: the request
	/// registry has always been the wrapped envelope, so only
	/// <c>{ "requests": [...], "references": {...} }</c> is accepted.
	/// </summary>
	public static RequestCatalogState LoadFromStream(
		Stream stream,
		string resolvedVersion = ComponentRegistryClient.LatestVersion,
		ComponentRegistrySource source = ComponentRegistrySource.Cdn) {
		if (stream is null) {
			throw new ArgumentNullException(nameof(stream));
		}

		(RequestRegistryEntry[] entries, RequestGlobalReferences? globalReferences) =
			DeserializeEnvelope(stream, "Request registry stream");
		return BuildState(entries, globalReferences, "Request registry stream", resolvedVersion, source);
	}

	/// <summary>
	/// Deserialises a request-registry payload into entries + the global
	/// <c>references</c> block (when present). The envelope requires a <c>requests</c>
	/// array; anything else is a malformed registry and fails loudly so a producer-side
	/// shape mistake never degrades into an empty catalog.
	/// </summary>
	internal static (RequestRegistryEntry[] Entries, RequestGlobalReferences? GlobalReferences) DeserializeEnvelope(
		Stream stream, string sourceDescription) {
		using JsonDocument document = JsonDocument.Parse(stream);
		if (document.RootElement.ValueKind != JsonValueKind.Object
			|| !document.RootElement.TryGetProperty("requests", out JsonElement requestsArray)
			|| requestsArray.ValueKind != JsonValueKind.Array) {
			throw new InvalidOperationException(
				$"{sourceDescription} must be an object with a 'requests' array.");
		}
		RequestRegistryEnvelope envelope = document.RootElement
				.Deserialize<RequestRegistryEnvelope>(ComponentInfoCatalog.DeserializerOptions)
			?? throw new InvalidOperationException($"{sourceDescription} envelope was empty or invalid.");
		if (envelope.Requests is null || envelope.Requests.Length == 0) {
			throw new InvalidOperationException($"{sourceDescription} is empty or invalid.");
		}
		return (envelope.Requests, envelope.References);
	}

	/// <summary>
	/// Builds the catalog state: rejects duplicate request types (a duplicate would
	/// silently shadow one entry through the case-insensitive lookup), drops entries
	/// with a blank <c>requestType</c> only if any valid entry remains, and orders the
	/// result alphabetically for stable list-mode output.
	/// </summary>
	internal static RequestCatalogState BuildState(
		RequestRegistryEntry[] rawEntries,
		RequestGlobalReferences? globalReferences,
		string sourceDescription,
		string resolvedVersion,
		ComponentRegistrySource source) {
		string[] duplicateTypes = rawEntries
			.Where(entry => !string.IsNullOrWhiteSpace(entry.RequestType))
			.GroupBy(entry => entry.RequestType, StringComparer.OrdinalIgnoreCase)
			.Where(group => group.Count() > 1)
			.Select(group => group.Key)
			.OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
			.ToArray();
		if (duplicateTypes.Length > 0) {
			throw new InvalidOperationException(
				$"{sourceDescription} contains duplicate request types: {string.Join(", ", duplicateTypes)}.");
		}

		RequestRegistryEntry[] orderedEntries = rawEntries
			.Where(entry => !string.IsNullOrWhiteSpace(entry.RequestType))
			.OrderBy(entry => entry.RequestType, StringComparer.OrdinalIgnoreCase)
			.ToArray();
		if (orderedEntries.Length == 0) {
			throw new InvalidOperationException($"{sourceDescription} does not contain valid request types.");
		}

		Dictionary<string, RequestRegistryEntry> lookup = orderedEntries
			.ToDictionary(entry => entry.RequestType, StringComparer.OrdinalIgnoreCase);
		return new RequestCatalogState(orderedEntries, lookup, resolvedVersion, source, globalReferences);
	}
}

/// <summary>
/// Mobile Freedom UI request catalog (<c>MobileRequestRegistry.json</c>). Backed by the same
/// wrapped-envelope contract and the same CDN → file cache fallback chain as the web request
/// catalog, differing only in the <see cref="RegistryFlavor.MobileRequests"/> config carried by
/// the underlying <see cref="IMobileRequestRegistryClient"/> (separate CDN file, cache
/// subdirectory, and local-override env var). Mirrors how <see cref="IMobileComponentInfoCatalog"/>
/// relates to <see cref="IComponentInfoCatalog"/> for components. The catalog is scoped to only
/// the <c>crt.*Request</c> types actually available on Freedom UI mobile.
/// </summary>
public interface IMobileRequestInfoCatalog {
	/// <summary>
	/// Returns the parsed mobile request catalog state for the requested version, including the
	/// source tier that produced the bytes. Symmetric with <see cref="IRequestInfoCatalog.LoadAsync"/>.
	/// </summary>
	Task<RequestCatalogState> LoadAsync(string requestedVersion, CancellationToken cancellationToken = default);
}

/// <summary>
/// Default mobile request catalog implementation. Reuses
/// <see cref="RequestInfoCatalog.LoadFromStream"/> over the same wrapped envelope, so AI consumers
/// see the identical response shape on both flavors. The underlying
/// <see cref="IMobileRequestRegistryClient"/> handles tier resolution (local override → cache → CDN
/// → <c>latest</c> fallback). Mirrors <see cref="MobileComponentInfoCatalog"/>.
/// </summary>
public sealed class MobileRequestInfoCatalog : IMobileRequestInfoCatalog {
	private readonly IMobileRequestRegistryClient _registryClient;

	public MobileRequestInfoCatalog(IMobileRequestRegistryClient registryClient) {
		_registryClient = registryClient ?? throw new ArgumentNullException(nameof(registryClient));
	}

	/// <inheritdoc />
	public async Task<RequestCatalogState> LoadAsync(string requestedVersion, CancellationToken cancellationToken = default) {
		string key = string.IsNullOrWhiteSpace(requestedVersion)
			? ComponentRegistryClient.LatestVersion
			: requestedVersion.Trim();
		ComponentRegistryFetchResult fetch = await _registryClient.GetAsync(key, cancellationToken).ConfigureAwait(false);
		using (fetch.Content) {
			return RequestInfoCatalog.LoadFromStream(fetch.Content, fetch.ResolvedVersion, fetch.Source);
		}
	}
}

/// <summary>
/// Parsed snapshot of a request registry ready for catalog operations.
/// </summary>
/// <param name="Entries">Ordered list of catalog entries.</param>
/// <param name="Lookup">Case-insensitive map of requestType → entry.</param>
/// <param name="ResolvedVersion">The version actually loaded; may differ from the requested version on fallback.</param>
/// <param name="Source">Which tier of the fallback chain produced the bytes.</param>
/// <param name="GlobalReferences">
/// Optional global <c>references</c> block from the envelope; carries the shared
/// <c>baseParameters</c> (fields every request inherits from <c>BaseRequest</c> —
/// platform-injected, never authored via <c>params</c>) and the shared
/// <c>typeDefinitions</c> (e.g. <c>RequestBindingConfig</c>).
/// </param>
public sealed record RequestCatalogState(
	IReadOnlyList<RequestRegistryEntry> Entries,
	IReadOnlyDictionary<string, RequestRegistryEntry> Lookup,
	string ResolvedVersion,
	ComponentRegistrySource Source,
	RequestGlobalReferences? GlobalReferences = null);

/// <summary>
/// Root-level envelope of <c>RequestRegistry.json</c>. Mirrors the wrapped component
/// registry contract (<c>{ "requests": [...], "references": {...} }</c>); there is no
/// legacy top-level-array generation for requests. The deserialiser is strict over this
/// envelope (see the request-registry snapshot guard test): any new producer field
/// forces a coordinated decision — map it, or explicitly allowlist it via
/// <see cref="UnmappedExtensions"/>.
/// </summary>
public sealed class RequestRegistryEnvelope {
	/// <summary>Per-request entries (the only required field).</summary>
	[JsonPropertyName("requests")]
	public RequestRegistryEntry[] Requests { get; init; } = [];

	/// <summary>Global references block shared across every request.</summary>
	[JsonPropertyName("references")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public RequestGlobalReferences? References { get; init; }

	/// <summary>
	/// Captures any top-level producer field clio has not mapped yet. Always expected
	/// to be empty against the pinned snapshot — the request-registry snapshot guard
	/// test fails when this dictionary is non-empty, so silent data loss becomes a CI
	/// failure rather than a runtime mystery.
	/// </summary>
	[JsonExtensionData]
	public IDictionary<string, JsonElement>? UnmappedExtensions { get; init; }
}

/// <summary>
/// Curated Freedom UI request definition from <c>RequestRegistry.json</c> — one
/// platform request (e.g. <c>crt.ClosePageRequest</c>) with its authorable parameter
/// contract and optional long-form documentation references.
/// </summary>
public sealed class RequestRegistryEntry {
	/// <summary>
	/// Gets or sets the Freedom UI request type — the value a page schema passes as the
	/// <c>request</c> field of a request binding (e.g. <c>crt.ClosePageRequest</c>).
	/// </summary>
	[JsonPropertyName("requestType")]
	public string RequestType { get; init; } = string.Empty;

	/// <summary>
	/// Gets or sets the one-line request description extracted from the source JSDoc.
	/// </summary>
	[JsonPropertyName("description")]
	public string Description { get; init; } = string.Empty;

	/// <summary>
	/// Gets or sets the request's authorable parameters — the keys a page schema may pass
	/// through the binding's <c>params</c> block. Values are kept as
	/// <see cref="JsonElement"/> so the producer can evolve the inner schema (e.g. add
	/// <c>required</c>, <c>values</c>, <c>deprecated</c>) without a coordinated clio
	/// release. An empty map is meaningful: the request accepts no parameters.
	/// </summary>
	[JsonPropertyName("parameters")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public IReadOnlyDictionary<string, JsonElement>? Parameters { get; init; }

	/// <summary>
	/// Gets or sets the per-request <c>references</c> block — long-form documentation
	/// paths and request-scoped named type definitions.
	/// </summary>
	[JsonPropertyName("references")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public RequestReferences? References { get; init; }

	/// <summary>
	/// Captures any per-request producer field clio has not mapped yet. The
	/// request-registry snapshot guard test fails when this is non-empty.
	/// </summary>
	[JsonExtensionData]
	public IDictionary<string, JsonElement>? UnmappedExtensions { get; init; }
}

/// <summary>
/// The optional <c>references</c> block inside a <see cref="RequestRegistryEntry"/>.
/// </summary>
public sealed class RequestReferences {
	/// <summary>
	/// Gets or sets the list of long-form documentation files for the request. Each entry
	/// is a path relative to <c>/api/mcp/{version}/</c> (e.g.
	/// <c>"request-docs/close-page.request.md"</c>); clio fetches the bytes lazily on
	/// detail requests through the shared docs pipeline and concatenates them into the
	/// response <c>documentation</c> field.
	/// </summary>
	[JsonPropertyName("docs")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public IReadOnlyList<string>? Docs { get; init; }

	/// <summary>
	/// Gets or sets the named type schemas referenced by the request's
	/// <c>parameters</c> values. Same forward-compatible <see cref="JsonElement"/>
	/// treatment as the component registry's type definitions.
	/// </summary>
	[JsonPropertyName("typeDefinitions")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public IReadOnlyDictionary<string, JsonElement>? TypeDefinitions { get; init; }

	/// <summary>
	/// Strict-mode catch-all for any new per-request <c>references.*</c> producer key.
	/// The request-registry snapshot guard test fails when this is non-empty.
	/// </summary>
	[JsonExtensionData]
	public IDictionary<string, JsonElement>? UnmappedExtensions { get; init; }
}

/// <summary>
/// Global metadata block under <c>RequestRegistry.json</c>'s root <c>references</c> key.
/// </summary>
public sealed class RequestGlobalReferences {
	/// <summary>
	/// Gets or sets the fields every request inherits from <c>BaseRequest</c>
	/// (<c>type</c>, <c>$context</c>, <c>scopes</c>, …). These are platform-injected at
	/// dispatch time — a page schema never sets them through the binding's <c>params</c>
	/// block, so unlike the component registry's <c>baseInputs</c> they are surfaced as a
	/// SEPARATE <c>baseParameters</c> field on the detail response instead of being
	/// merged into <c>parameters</c>. Merging would teach an AI consumer to author
	/// platform-owned fields.
	/// </summary>
	[JsonPropertyName("baseParameters")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public IReadOnlyDictionary<string, JsonElement>? BaseParameters { get; init; }

	/// <summary>
	/// Gets or sets the global named-type schemas shared across requests (e.g.
	/// <c>RequestBindingConfig</c> — the wiring contract of every request binding).
	/// </summary>
	[JsonPropertyName("typeDefinitions")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public IReadOnlyDictionary<string, JsonElement>? TypeDefinitions { get; init; }

	/// <summary>
	/// Strict-mode catch-all for any new producer key under root <c>references.*</c>.
	/// The request-registry snapshot guard test fails when this is non-empty.
	/// </summary>
	[JsonExtensionData]
	public IDictionary<string, JsonElement>? UnmappedExtensions { get; init; }
}
