using System;
using Clio.Command.McpServer.Knowledge;
using ModelContextProtocol.Protocol;

namespace Clio.Command.McpServer.Resources;

internal sealed class KnowledgeGuidanceResourceAdapter {
	private readonly IKnowledgeGuidanceSource _source;

	public KnowledgeGuidanceResourceAdapter(IKnowledgeGuidanceSource source) {
		_source = source ?? throw new ArgumentNullException(nameof(source));
	}

	internal static KnowledgeGuidanceResourceAdapter CreateUnavailable() =>
		new(new UnavailableKnowledgeGuidanceSource());

	public ResourceContents Get(string uri) {
		KnowledgeArticleLookup lookup = _source.FindByUri(uri);
		return lookup.Status switch {
			KnowledgeArticleLookupStatus.Active => new TextResourceContents {
				Uri = lookup.Article!.Uri,
				MimeType = "text/plain",
				Text = lookup.Article.Text
			},
			KnowledgeArticleLookupStatus.Unavailable => throw new KnowledgeGuidanceUnavailableException(uri),
			_ => throw new InvalidOperationException($"Unknown guidance resource '{uri}'.")
		};
	}
}
