using System;
using System.IO;
using System.Net.Http;
using System.Threading;

namespace Clio.Command.McpServer.Knowledge;

/// <summary>
/// HTTP reading primitives shared by the knowledge artifact transports.
/// </summary>
/// <remarks>
/// Every transport that pulls bytes over HTTP has to bound them the same way, and a bound that
/// drifts between transports is a bound one of them is missing. Keeping the reader in one place
/// makes that impossible rather than merely unlikely.
/// </remarks>
internal static class KnowledgeTransportHttp {

	// RFC 3986 path separator: fixed by the URI grammar, never the platform file-system separator.
	private const char UriPathSeparator = '/';

	/// <summary>
	/// Reads a response body, refusing anything larger than the caller's limit.
	/// </summary>
	/// <remarks>
	/// The advertised <c>Content-Length</c> is checked first so an oversized body is refused before a
	/// single byte is buffered, and the running total is checked again while streaming because that
	/// header is a claim, not a guarantee.
	/// </remarks>
	/// <param name="content">The response content.</param>
	/// <param name="maximumBytes">The inclusive byte ceiling.</param>
	/// <param name="cancellationToken">Stops the read when the operation deadline elapses.</param>
	/// <returns>The complete body.</returns>
	/// <exception cref="InvalidDataException">The body exceeds <paramref name="maximumBytes"/>.</exception>
	internal static byte[] ReadBounded(
		HttpContent content,
		int maximumBytes,
		CancellationToken cancellationToken) {
		ArgumentNullException.ThrowIfNull(content);
		if (content.Headers.ContentLength is long length && (length < 0 || length > maximumBytes)) {
			throw new InvalidDataException($"HTTP content exceeds the {maximumBytes}-byte limit.");
		}
		int initialCapacity = content.Headers.ContentLength is long contentLength
			? checked((int)contentLength)
			: 0;
		using Stream stream = content.ReadAsStream(cancellationToken);
		using MemoryStream output = new(initialCapacity);
		byte[] buffer = new byte[81920];
		int read;
		while ((read = stream.ReadAsync(buffer, cancellationToken).AsTask().GetAwaiter().GetResult()) > 0) {
			if (output.Length + read > maximumBytes) {
				throw new InvalidDataException($"Content exceeds the {maximumBytes}-byte limit.");
			}
			output.Write(buffer, 0, read);
		}
		return output.Length == output.Capacity ? output.GetBuffer() : output.ToArray();
	}

	/// <summary>
	/// Returns <paramref name="uri"/> with a trailing slash, so relative resolution appends rather
	/// than replaces its last path segment.
	/// </summary>
	/// <param name="uri">The base URI.</param>
	/// <returns>The URI, guaranteed to end in a path separator.</returns>
	internal static Uri EnsureTrailingSlash(Uri uri) {
		ArgumentNullException.ThrowIfNull(uri);
		return uri.AbsoluteUri.EndsWith(UriPathSeparator)
			? uri
			: new Uri(uri.AbsoluteUri + UriPathSeparator, UriKind.Absolute);
	}
}
