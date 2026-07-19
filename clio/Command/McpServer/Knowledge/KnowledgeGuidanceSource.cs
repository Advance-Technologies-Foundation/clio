using System;
using System.Collections.Generic;
using System.Linq;
using Clio.Command.McpServer.Resources;

namespace Clio.Command.McpServer.Knowledge;

internal interface IKnowledgeGuidanceSource {
	KnowledgeArticleLookup FindByName(string name);

	KnowledgeArticleLookup FindByUri(string uri);

	IReadOnlyList<string> GetNames();
}

internal sealed class KnowledgeGuidanceSource : IKnowledgeGuidanceSource {
	private readonly IFeatureToggleService _featureToggleService;
	private readonly IKnowledgeBundleActivator _activator;
	private readonly IKnowledgeBundleRuntime _runtime;

	public KnowledgeGuidanceSource(
		IFeatureToggleService featureToggleService,
		IKnowledgeBundleActivator activator,
		IKnowledgeBundleRuntime runtime) {
		_featureToggleService = featureToggleService
			?? throw new ArgumentNullException(nameof(featureToggleService));
		_activator = activator ?? throw new ArgumentNullException(nameof(activator));
		_runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
	}

	public KnowledgeArticleLookup FindByName(string name) {
		ArgumentException.ThrowIfNullOrWhiteSpace(name);
		if (!GuidanceCatalog.TryGet(name, _featureToggleService, out GuidanceCatalogEntry entry)) {
			_activator.EnsureActivated();
			return _runtime.Find(name);
		}
		if (entry.IsExternal) {
			_activator.EnsureActivated();
			string canonicalIdentifier = $"{KnowledgeResolver.NamespacedUriPrefix}com.creatio.clio/"
				+ Uri.EscapeDataString(entry.Name);
			KnowledgeArticleLookup lookup = _runtime.Find(canonicalIdentifier);
			return lookup.Status == KnowledgeArticleLookupStatus.NotFound
				? new KnowledgeArticleLookup(KnowledgeArticleLookupStatus.Unavailable, null, lookup.ActiveSequence)
				: lookup;
		}
		KnowledgeArticle article = new(entry.Name, entry.Article!.Uri, entry.Article.Text);
		return new KnowledgeArticleLookup(KnowledgeArticleLookupStatus.Active, article, _runtime.ActiveSequence);
	}

	public KnowledgeArticleLookup FindByUri(string uri) {
		ArgumentException.ThrowIfNullOrWhiteSpace(uri);
		GuidanceCatalogEntry? entry = GuidanceCatalog.GetEntries(_featureToggleService)
			.SingleOrDefault(candidate => string.Equals(candidate.Uri, uri, StringComparison.Ordinal));
		if (entry is not null) {
			return FindByName(entry.Name);
		}
		_activator.EnsureActivated();
		return _runtime.Find(uri);
	}

	public IReadOnlyList<string> GetNames() {
		_activator.EnsureActivated();
		return GuidanceCatalog.GetNames(_featureToggleService)
			.Concat(_runtime.GetNames())
			.Distinct(StringComparer.Ordinal)
			.OrderBy(name => name, StringComparer.Ordinal)
			.ToArray();
	}
}

internal sealed class KnowledgeGuidanceUnavailableException : InvalidOperationException {
	internal const string ErrorCode = "guidance-unavailable";

	public KnowledgeGuidanceUnavailableException(string identifier)
		: base($"[{ErrorCode}] Guidance '{identifier}' is unavailable because no compatible verified knowledge bundle is active.") {
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
}
