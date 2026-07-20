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
}

internal sealed class KnowledgeGuidanceSource : IKnowledgeGuidanceSource {
	private readonly IKnowledgeBundleActivator _activator;
	private readonly IKnowledgeBundleRuntime _runtime;
	private readonly IFeatureToggleService _featureToggleService;

	public KnowledgeGuidanceSource(
		IKnowledgeBundleActivator activator,
		IKnowledgeBundleRuntime runtime,
		IFeatureToggleService featureToggleService) {
		_activator = activator ?? throw new ArgumentNullException(nameof(activator));
		_runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
		_featureToggleService = featureToggleService ?? throw new ArgumentNullException(nameof(featureToggleService));
	}

	public KnowledgeArticleLookup FindByName(string name) {
		ArgumentException.ThrowIfNullOrWhiteSpace(name);
		_activator.EnsureActivated();
		return RequireEnabledFeatures(_runtime.Find(name));
	}

	public KnowledgeArticleLookup FindByUri(string uri) {
		ArgumentException.ThrowIfNullOrWhiteSpace(uri);
		_activator.EnsureActivated();
		return RequireEnabledFeatures(_runtime.Find(uri));
	}

	public IReadOnlyList<string> GetNames() {
		_activator.EnsureActivated();
		return GetGuidanceArticles()
			.Select(article => article.ItemId)
			.Distinct(StringComparer.Ordinal)
			.OrderBy(name => name, StringComparer.Ordinal)
			.ToArray();
	}

	public IReadOnlyList<KnowledgeGuidanceDescriptor> GetCatalog() {
		_activator.EnsureActivated();
		return GetGuidanceArticles()
			.Concat(_runtime.GetArticlesByRole("reference").Select(result => result.Article))
			.Where(HasEnabledFeatures)
			.GroupBy(article => article.Uri, StringComparer.Ordinal)
			.Select(group => group.First())
			.Select(article => new KnowledgeGuidanceDescriptor(
				article.ItemId,
				article.Title,
				article.Description,
				article.Uri,
				article.MediaType))
			.OrderBy(article => article.Name, StringComparer.Ordinal)
			.ToArray();
	}

	private IEnumerable<KnowledgeArticle> GetGuidanceArticles() => _runtime.GetNames()
			.Select(_runtime.Find)
			.Where(lookup => lookup.Status == KnowledgeArticleLookupStatus.Active)
			.Select(lookup => lookup.Article!)
			.Where(HasEnabledFeatures)
			.GroupBy(article => article.Uri, StringComparer.Ordinal)
			.Select(group => group.First());

	private KnowledgeArticleLookup RequireEnabledFeatures(KnowledgeArticleLookup lookup) =>
		lookup.Status == KnowledgeArticleLookupStatus.Active && !HasEnabledFeatures(lookup.Article!)
			? new KnowledgeArticleLookup(
				KnowledgeArticleLookupStatus.NotFound,
				null,
				lookup.ActiveSequence)
			: lookup;

	private bool HasEnabledFeatures(KnowledgeArticle article) =>
		(article.RequiredFeatures ?? []).All(_featureToggleService.IsFeatureEnabled);
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

	public IReadOnlyList<KnowledgeGuidanceDescriptor> GetCatalog() => Array.Empty<KnowledgeGuidanceDescriptor>();
}
