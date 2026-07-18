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
			return new KnowledgeArticleLookup(KnowledgeArticleLookupStatus.NotFound, null, _runtime.ActiveSequence);
		}
		if (entry.IsExternal) {
			_activator.EnsureActivated();
			return _runtime.Find(entry.Name);
		}
		KnowledgeArticle article = new(entry.Name, entry.Article!.Uri, entry.Article.Text);
		return new KnowledgeArticleLookup(KnowledgeArticleLookupStatus.Active, article, _runtime.ActiveSequence);
	}

	public KnowledgeArticleLookup FindByUri(string uri) {
		ArgumentException.ThrowIfNullOrWhiteSpace(uri);
		GuidanceCatalogEntry? entry = GuidanceCatalog.GetEntries(_featureToggleService)
			.SingleOrDefault(candidate => string.Equals(candidate.Uri, uri, StringComparison.Ordinal));
		return entry is null
			? new KnowledgeArticleLookup(KnowledgeArticleLookupStatus.NotFound, null, _runtime.ActiveSequence)
			: FindByName(entry.Name);
	}

	public IReadOnlyList<string> GetNames() => GuidanceCatalog.GetNames(_featureToggleService);
}

internal sealed class KnowledgeGuidanceUnavailableException : InvalidOperationException {
	internal const string ErrorCode = "guidance-unavailable";

	public KnowledgeGuidanceUnavailableException(string identifier)
		: base($"[{ErrorCode}] Guidance '{identifier}' is unavailable because no compatible verified knowledge bundle is active.") {
	}
}

internal sealed class UnavailableKnowledgeGuidanceSource : IKnowledgeGuidanceSource {
	public KnowledgeArticleLookup FindByName(string name) =>
		new(KnowledgeArticleLookupStatus.Unavailable, null, null);

	public KnowledgeArticleLookup FindByUri(string uri) =>
		new(KnowledgeArticleLookupStatus.Unavailable, null, null);

	public IReadOnlyList<string> GetNames() => Array.Empty<string>();
}
