using System;
using System.Collections.Generic;
using System.Linq;
using Clio.Command;

namespace Clio.Command.McpServer.Knowledge;

internal interface IKnowledgeGuidanceSource {
	KnowledgeArticleLookup FindByName(string name);

	KnowledgeArticleLookup FindByUri(string uri);

	IReadOnlyList<string> GetNames();

	IReadOnlyList<KnowledgeGuidanceDescriptor> GetCatalog();

	/// <summary>
	/// The catalog ordered and de-duplicated by URI, ready to be paged by resource discovery.
	/// </summary>
	/// <remarks>
	/// Discovery pages the same catalog repeatedly, so it is resolved once per active snapshot
	/// instead of being rebuilt and re-sorted per page.
	/// </remarks>
	/// <returns>The URI-ordered catalog.</returns>
	IReadOnlyList<KnowledgeGuidanceDescriptor> GetDiscoveryCatalog();
}

internal sealed class KnowledgeGuidanceSource : IKnowledgeGuidanceSource {
	private readonly IKnowledgeBundleActivator _activator;
	private readonly IKnowledgeRefreshTrigger _refreshTrigger;
	private readonly IKnowledgeBundleRuntime _runtime;
	private readonly IFeatureToggleService _featureToggleService;
	private readonly object _catalogLock = new();
	private const string ReferenceRole = "reference";
	private ResolvedCatalog? _catalog;

	public KnowledgeGuidanceSource(
		IKnowledgeBundleActivator activator,
		IKnowledgeRefreshTrigger refreshTrigger,
		IKnowledgeBundleRuntime runtime,
		IFeatureToggleService featureToggleService) {
		_activator = activator ?? throw new ArgumentNullException(nameof(activator));
		_refreshTrigger = refreshTrigger ?? throw new ArgumentNullException(nameof(refreshTrigger));
		_runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
		_featureToggleService = featureToggleService ?? throw new ArgumentNullException(nameof(featureToggleService));
	}

	/// <summary>
	/// Brings the served snapshot up to date before answering, and asks for a publisher refresh when the
	/// cached generation is due for one.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Activation first, deliberately: it is local and synchronous, so a generation another clio process
	/// already installed is picked up by THIS answer. The refresh that follows never blocks it — it is a
	/// stale-while-revalidate for the next reader, in the same shape the component registry behind
	/// <c>get-component-info</c> uses.
	/// </para>
	/// <para>
	/// The trigger is asked unconditionally; WHETHER it does anything is its own decision. This source
	/// serves the whole process — an ordinary CLI verb reaches it too (<c>clio config</c> reads the
	/// <c>knowledge-feedback</c> article) — and <see cref="IKnowledgeRefreshTrigger"/> is where that
	/// eligibility is decided and tested, so no run-mode policy leaks onto the read path.
	/// </para>
	/// </remarks>
	private void EnsureCurrent() {
		_activator.EnsureActivated();
		_refreshTrigger.TriggerIfDue();
	}

	// Eligibility goes into the resolver rather than around it: filtering afterwards would let a
	// gated higher-priority or pinned article win first and then collapse to not-found, hiding an
	// eligible lower-priority article, and would let a gated candidate create a false tie.
	public KnowledgeArticleLookup FindByName(string name) {
		ArgumentException.ThrowIfNullOrWhiteSpace(name);
		EnsureCurrent();
		return _runtime.Find(name, HasEnabledFeatures);
	}

	public KnowledgeArticleLookup FindByUri(string uri) {
		ArgumentException.ThrowIfNullOrWhiteSpace(uri);
		EnsureCurrent();
		return _runtime.Find(uri, HasEnabledFeatures);
	}

	public IReadOnlyList<string> GetNames() {
		EnsureCurrent();
		return ResolveCatalog().GuidanceNames;
	}

	public IReadOnlyList<KnowledgeGuidanceDescriptor> GetCatalog() {
		EnsureCurrent();
		return ResolveCatalog().ByName;
	}

	public IReadOnlyList<KnowledgeGuidanceDescriptor> GetDiscoveryCatalog() {
		EnsureCurrent();
		return ResolveCatalog().ByUri;
	}

	/// <summary>
	/// Returns the catalog for the current snapshot, rebuilding it only when the active snapshot or
	/// the state of the feature flags it depends on has changed.
	/// </summary>
	/// <remarks>
	/// Resolving the catalog costs one <see cref="IKnowledgeBundleRuntime.Find"/> per name, so
	/// rebuilding it per discovery page made a single listing quadratic in the number of articles.
	/// The snapshot token is the identity of the active set, which every mutation replaces, so no
	/// runtime write path has to remember to invalidate anything. The feature signature covers only
	/// the flags articles actually reference, so a bundle with no gated article never invalidates.
	/// </remarks>
	/// <returns>The resolved catalog.</returns>
	private ResolvedCatalog ResolveCatalog() {
		object token = _runtime.SnapshotToken;
		lock (_catalogLock) {
			if (_catalog is not null
					&& ReferenceEquals(_catalog.SnapshotToken, token)
					&& string.Equals(
						_catalog.FeatureSignature,
						CreateFeatureSignature(_catalog.GatedFeatures),
						StringComparison.Ordinal)) {
				return _catalog;
			}
			string[] gatedFeatures = CollectGatedFeatures();
			KnowledgeArticle[] guidance = ResolveGuidanceArticles();
			KnowledgeGuidanceDescriptor[] byName = guidance
				.Concat(_runtime.GetArticlesByRole(ReferenceRole).Select(result => result.Article)
					.Where(HasEnabledFeatures))
				.GroupBy(article => article.Uri, StringComparer.Ordinal)
				.Select(group => group.First())
				.Select(ToDescriptor)
				.OrderBy(article => article.Name, StringComparer.Ordinal)
				.ToArray();
			_catalog = new ResolvedCatalog(
				token,
				gatedFeatures,
				CreateFeatureSignature(gatedFeatures),
				guidance
					.Select(article => article.ItemId)
					.Distinct(StringComparer.Ordinal)
					.OrderBy(name => name, StringComparer.Ordinal)
					.ToArray(),
				byName,
				byName
					.DistinctBy(article => article.Uri, StringComparer.Ordinal)
					.OrderBy(article => article.Uri, StringComparer.Ordinal)
					.ThenBy(article => article.Name, StringComparer.Ordinal)
					.ToArray());
			return _catalog;
		}
	}

	private KnowledgeArticle[] ResolveGuidanceArticles() => _runtime.GetNames(HasEnabledFeatures)
		.Select(name => _runtime.Find(name, HasEnabledFeatures))
		.Where(lookup => lookup.Status == KnowledgeArticleLookupStatus.Active)
		.Select(lookup => lookup.Article)
		.GroupBy(article => article.Uri, StringComparer.Ordinal)
		.Select(group => group.First())
		.ToArray();

	private string[] CollectGatedFeatures() => _runtime.GetArticlesByRole(KnowledgeArticle.DefaultRole)
		.Concat(_runtime.GetArticlesByRole(ReferenceRole))
		.SelectMany(result => result.Article.RequiredFeatures ?? [])
		.Distinct(StringComparer.Ordinal)
		.OrderBy(feature => feature, StringComparer.Ordinal)
		.ToArray();

	private string CreateFeatureSignature(IReadOnlyList<string> gatedFeatures) => gatedFeatures.Count == 0
		? string.Empty
		: string.Join(
			'\u001f',
			gatedFeatures.Select(feature => $"{feature}={_featureToggleService.IsFeatureEnabled(feature)}"));

	private static KnowledgeGuidanceDescriptor ToDescriptor(KnowledgeArticle article) =>
		new(article.ItemId, article.Title, article.Description, article.Uri, article.MediaType);

	private bool HasEnabledFeatures(KnowledgeArticle article) =>
		(article.RequiredFeatures ?? []).All(_featureToggleService.IsFeatureEnabled);

	private sealed record ResolvedCatalog(
		object SnapshotToken,
		string[] GatedFeatures,
		string FeatureSignature,
		string[] GuidanceNames,
		KnowledgeGuidanceDescriptor[] ByName,
		KnowledgeGuidanceDescriptor[] ByUri);
}

internal sealed class KnowledgeGuidanceUnavailableException : InvalidOperationException {
	internal const string ErrorCode = "guidance-unavailable";

	public KnowledgeGuidanceUnavailableException(string identifier)
		: base($"[{ErrorCode}] Guidance '{identifier}' is unavailable because no compatible verified knowledge bundle is active.") {
	}
}

/// <summary>
/// Reports an identifier no active library resolves, whatever the reason it does not resolve.
/// </summary>
/// <remarks>
/// A topic whose <c>requiredFeatures</c> are not all enabled resolves to
/// <see cref="KnowledgeArticleLookupStatus.NotFound"/> exactly like an identifier nobody publishes, and
/// this message must keep the two indistinguishable. Naming the gate — or even admitting one exists —
/// would turn every refusal into an existence oracle for hidden content, which is what the gate is for.
/// </remarks>
internal sealed class KnowledgeGuidanceNotFoundException : InvalidOperationException {
	internal const string ErrorCode = "guidance-not-found";

	public KnowledgeGuidanceNotFoundException(string identifier)
		: base($"[{ErrorCode}] Unknown guidance resource '{identifier}'. Use one of the URIs returned by resources/list.") {
	}
}

internal sealed class KnowledgeGuidanceAmbiguousException : InvalidOperationException {
	internal const string ErrorCode = "guidance-ambiguous";

	public KnowledgeGuidanceAmbiguousException(string identifier, string? diagnostic)
		: base($"[{ErrorCode}] Guidance '{identifier}' cannot be resolved deterministically. {diagnostic}") {
	}
}

internal sealed class UnavailableKnowledgeGuidanceSource : IKnowledgeGuidanceSource {
	public KnowledgeArticleLookup FindByName(string name) =>
		new(KnowledgeArticleLookupStatus.Unavailable, null, null);

	public KnowledgeArticleLookup FindByUri(string uri) =>
		new(KnowledgeArticleLookupStatus.Unavailable, null, null);

	public IReadOnlyList<string> GetNames() => Array.Empty<string>();

	public IReadOnlyList<KnowledgeGuidanceDescriptor> GetCatalog() => Array.Empty<KnowledgeGuidanceDescriptor>();

	public IReadOnlyList<KnowledgeGuidanceDescriptor> GetDiscoveryCatalog() =>
		Array.Empty<KnowledgeGuidanceDescriptor>();
}
