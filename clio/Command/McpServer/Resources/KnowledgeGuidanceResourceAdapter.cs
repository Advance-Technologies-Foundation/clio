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
			KnowledgeArticleLookupStatus.NotFound => throw NotFoundResource(uri),
			_ => throw new InvalidOperationException($"Unknown guidance resource '{uri}'.")
		};
	}

	// An identifier no active library resolves is the client naming something that is not there, so it
	// answers with the protocol's own resource-not-found code instead of the generic internal error a
	// plain exception collapses into - a caller could not tell that apart from a server fault. The URI
	// of a feature-gated topic lands here too, and deliberately produces the same answer as an
	// identifier nobody publishes: see KnowledgeGuidanceNotFoundException.
	private static McpProtocolException NotFoundResource(string uri) {
		KnowledgeGuidanceNotFoundException notFound = new(uri);
		return new McpProtocolException(notFound.Message, McpErrorCode.ResourceNotFound);
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
