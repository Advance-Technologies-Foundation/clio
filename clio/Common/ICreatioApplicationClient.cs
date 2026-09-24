using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Creatio.Client;

namespace Clio.Common;

/// <summary>
/// Extends the stable application-client contract with CreatioClient transport capabilities.
/// </summary>
public interface ICreatioApplicationClient : IApplicationClient {
	/// <summary>Executes a cancellation-aware GET and transfers response ownership to the caller.</summary>
	Task<HttpResponseMessage> ExecuteGetRequestAsync(string url, int requestTimeout = 100_000,
		int maxAttempts = 1, int delaySec = 1, CancellationToken cancellationToken = default);

	/// <summary>
	/// Executes a GET whose response body is STREAMED, with a hard ceiling enforced as the bytes arrive.
	/// </summary>
	/// <param name="url">The absolute request URL.</param>
	/// <param name="maxBytes">Hard ceiling on the response body.</param>
	/// <param name="requestTimeout">The request timeout in milliseconds.</param>
	/// <param name="cancellationToken">Token that abandons the transfer.</param>
	/// <returns>The response body as UTF-8 bytes.</returns>
	/// <exception cref="ResponseTooLargeException">The body reached the ceiling; the transfer is abandoned.</exception>
	/// <exception cref="TimeoutException">
	/// <paramref name="requestTimeout"/> elapsed before the body finished arriving. This is distinct from
	/// <paramref name="cancellationToken"/> firing, which surfaces as <see cref="OperationCanceledException"/>:
	/// the deadline means the server did not deliver in time, cancellation means the caller withdrew.
	/// </exception>
	/// <remarks>
	/// <see cref="ExecuteGetRequestAsync"/> completes only once the WHOLE body has been buffered, so a
	/// ceiling applied to its result cannot prevent the allocation it exists to prevent - a large response
	/// is already in memory by the time it can be rejected. This overload reads response headers first and
	/// then pulls the body incrementally, so the transfer is abandoned near the limit instead of after it.
	/// <para>
	/// <paramref name="requestTimeout"/> bounds the WHOLE transfer, not just the headers. Once headers are
	/// read the transport's own timeout no longer applies, so the deadline is carried by a linked token across
	/// the send, the stream acquisition and every body read - otherwise a server that answers the headers and
	/// then withholds the body leaves the call bounded only by caller cancellation, which an MCP host is not
	/// guaranteed to deliver.
	/// </para>
	/// <para>
	/// Defaulted rather than abstract, for the same reason as <see cref="IApplicationClient.ExecutePutRequest"/>:
	/// this contract has implementations outside this repository, and an abstract member breaks every one of
	/// them at compile time. A transport that cannot stream says so at the call site, and the caller FAILS -
	/// there is no buffered fallback, because reading an unbounded body into memory is the outcome the
	/// ceiling exists to prevent.
	/// </para>
	/// </remarks>
	Task<byte[]> ExecuteGetRequestBoundedAsync(string url, long maxBytes, int requestTimeout = 100_000,
		CancellationToken cancellationToken = default) =>
		throw new NotSupportedException(
			$"{GetType().Name} does not implement a streamed GET. Use a client that overrides "
			+ $"{nameof(ExecuteGetRequestBoundedAsync)}.");

	/// <summary>Executes a cancellation-aware POST and transfers response ownership to the caller.</summary>
	Task<HttpResponseMessage> ExecutePostRequestAsync(string url, string requestData,
		int requestTimeout = 100_000, int maxAttempts = 1, int delaySec = 1,
		CancellationToken cancellationToken = default);

	/// <summary>Logs in with cancellation support and transfers response ownership to the caller.</summary>
	Task<HttpResponseMessage> LoginAsync(int requestTimeout = 100_000,
		CancellationToken cancellationToken = default);

	/// <summary>Returns detached copies of the current Creatio session cookies.</summary>
	IReadOnlyList<CreatioSessionCookie> ExportSessionCookies();

	/// <summary>Imports cookies for the configured Creatio application.</summary>
	void ImportSessionCookies(IEnumerable<CreatioSessionCookie> cookies);

	/// <summary>Uploads an image and transfers response ownership to the caller.</summary>
	Task<HttpResponseMessage> UploadImageAsync(string url, byte[] data, string fileName, string mimeType,
		int requestTimeout = 100_000, CancellationToken cancellationToken = default);
}

/// <summary>An application client whose creator transfers ownership to the caller.</summary>
/// <remarks>Dispose the client after the operation completes.</remarks>
public interface IOwnedApplicationClient : ICreatioApplicationClient, IDisposable { }
