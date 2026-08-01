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
				Uri = lookup.Article.Uri,
				MimeType = lookup.Article.MediaType,
				Text = lookup.Article.Text
			},
			KnowledgeArticleLookupStatus.Unavailable => throw UnavailableResource(uri),
			KnowledgeArticleLookupStatus.Ambiguous => throw AmbiguousResource(uri, lookup.Diagnostic),
			_ => throw new InvalidOperationException($"Unknown guidance resource '{uri}'.")
		};
	}

	private static McpProtocolException UnavailableResource(string uri) {
		KnowledgeGuidanceUnavailableException unavailable = new(uri);
		return new McpProtocolException(unavailable.Message, McpErrorCode.InternalError);
	}

	// An identifier that several installed libraries claim is a server-side collision, not a client
	// mistake, so it keeps the internal error code that the unavailable arm uses and carries the
	// resolver diagnostic naming the colliding libraries - exactly what get-guidance reports.
	private static McpProtocolException AmbiguousResource(string uri, string? diagnostic) {
		KnowledgeGuidanceAmbiguousException ambiguous = new(uri, diagnostic);
		return new McpProtocolException(ambiguous.Message, McpErrorCode.InternalError);
	}
}
