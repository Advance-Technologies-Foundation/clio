using System;
using Clio.Command.McpServer.Knowledge;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;

namespace Clio.Command.McpServer.Resources;

internal interface IKnowledgeGuidanceResourceAdapter {
	ResourceContents Get(string uri);
}

internal sealed class KnowledgeGuidanceResourceAdapter : IKnowledgeGuidanceResourceAdapter {
	private readonly IKnowledgeGuidanceSource _source;

	public KnowledgeGuidanceResourceAdapter(IKnowledgeGuidanceSource source) {
		_source = source ?? throw new ArgumentNullException(nameof(source));
	}

	internal static IKnowledgeGuidanceResourceAdapter CreateUnavailable() =>
		new KnowledgeGuidanceResourceAdapter(new UnavailableKnowledgeGuidanceSource());

	public ResourceContents Get(string uri) {
		KnowledgeArticleLookup lookup = _source.FindByUri(uri);
		return lookup.Status switch {
			KnowledgeArticleLookupStatus.Active => new TextResourceContents {
				Uri = lookup.Article!.Uri,
				MimeType = "text/plain",
				Text = lookup.Article.Text
			},
			KnowledgeArticleLookupStatus.Unavailable => throw UnavailableResource(uri),
			_ => throw new InvalidOperationException($"Unknown guidance resource '{uri}'.")
		};
	}

	private static McpProtocolException UnavailableResource(string uri) {
		KnowledgeGuidanceUnavailableException unavailable = new(uri);
		return new McpProtocolException(unavailable.Message, McpErrorCode.InternalError);
	}
}
