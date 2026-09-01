using System;
using System.IO;
using System.Net.Http;
using System.Threading;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// Reads an HTTP response body into memory with a HARD byte ceiling enforced WHILE the bytes arrive.
/// </summary>
/// <remarks>
/// A ceiling checked after the body has been materialized is not a ceiling: by the time it can say "too
/// large", the process has already allocated the whole thing. Reading in chunks and stopping at the limit
/// bounds the memory a single call can cost, whatever the server sends - and it is the only form of the
/// bound that survives a server that lies in (or omits) Content-Length.
/// <para>
/// The bytes are kept as UTF-8, never decoded to a string: the destination file is UTF-8, and
/// <c>JsonDocument</c> parses UTF-8 directly, so a decode to UTF-16 and an encode back would double the
/// footprint of the very payload this exists to bound.
/// </para>
/// </remarks>
internal static class BoundedHttpResponseReader {

	private const int ChunkSize = 64 * 1024;

	/// <summary>Reads the response body, failing rather than allocating past <paramref name="maxBytes"/>.</summary>
	/// <param name="response">Response whose content is read; the caller owns disposal.</param>
	/// <param name="maxBytes">Hard ceiling on the body size.</param>
	/// <param name="cancellationToken">Caller token; an abandoned read stops here rather than running on.</param>
	/// <param name="payload">The body as UTF-8 bytes when the method returns <see langword="true"/>.</param>
	/// <param name="error">Caller-facing error when the method returns <see langword="false"/>.</param>
	internal static bool TryRead(
		HttpResponseMessage response,
		long maxBytes,
		CancellationToken cancellationToken,
		out byte[] payload,
		out string error) {
		payload = null;
		error = null;
		// A declared Content-Length past the ceiling is rejected before a single byte is read; a missing or
		// dishonest one is caught by the running total below.
		if (response.Content.Headers.ContentLength is { } declaredLength && declaredLength > maxBytes) {
			error = DescribeTooLarge(declaredLength, maxBytes);
			return false;
		}
		using Stream content = response.Content.ReadAsStream(cancellationToken);
		using MemoryStream buffer = new();
		byte[] chunk = new byte[ChunkSize];
		long total = 0;
		while (true) {
			cancellationToken.ThrowIfCancellationRequested();
			int read = content.Read(chunk, 0, chunk.Length);
			if (read == 0) {
				break;
			}
			total += read;
			if (total > maxBytes) {
				// Stop reading immediately: the point of the ceiling is that the rest of the body is never
				// pulled into memory at all.
				error = DescribeTooLarge(total, maxBytes);
				return false;
			}
			buffer.Write(chunk, 0, read);
		}
		payload = buffer.ToArray();
		return true;
	}

	private static string DescribeTooLarge(long observedBytes, long maxBytes) =>
		$"OData response is at least {observedBytes} bytes, which exceeds the {maxBytes}-byte limit for one "
		+ "call. Narrow the query with select, or page it with top and skip.";
}
