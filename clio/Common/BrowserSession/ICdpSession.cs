using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Clio.Common.BrowserSession;

/// <summary>
/// A Chrome DevTools Protocol (CDP) session over a single page-target WebSocket. Extracted from
/// <see cref="AuthenticatedBrowserLauncher"/> so both the launcher and the Process Designer driver can
/// share one frame-pump implementation instead of duplicating WebSocket plumbing.
/// </summary>
/// <remarks>
/// The DevTools endpoint is loopback-only (<c>127.0.0.1</c>); cookie values are never logged (names only).
/// </remarks>
public interface ICdpSession : IAsyncDisposable {
	/// <summary>
	/// Resolves the browser's page target on the given loopback DevTools port and opens its WebSocket.
	/// </summary>
	/// <param name="devToolsPort">The loopback remote-debugging port the browser is listening on.</param>
	/// <param name="ct">Cancellation token.</param>
	/// <exception cref="System.InvalidOperationException">No CDP page target could be obtained.</exception>
	Task ConnectAsync(int devToolsPort, CancellationToken ct = default);

	/// <summary>
	/// Sends one CDP command and returns its <c>result</c> frame. Drains interleaved event frames until
	/// the response with the matching id arrives; throws on a CDP error frame.
	/// </summary>
	/// <param name="method">The CDP method (e.g. <c>Network.setCookie</c>, <c>Page.navigate</c>).</param>
	/// <param name="params">The method parameters (serialized as the <c>params</c> object).</param>
	/// <param name="ct">Cancellation token.</param>
	/// <returns>The cloned <c>result</c> element of the matching response (or <see langword="default"/> when absent).</returns>
	/// <exception cref="System.InvalidOperationException">The command returned a CDP error frame or the socket closed.</exception>
	Task<JsonElement> SendAsync(string method, object @params, CancellationToken ct = default);

	/// <summary>
	/// Evaluates a JavaScript expression via <c>Runtime.evaluate</c> and returns the awaited JSON result value.
	/// </summary>
	/// <param name="expression">The JavaScript expression to evaluate in the page.</param>
	/// <param name="awaitPromise">When <see langword="true"/>, awaits a returned promise before resolving.</param>
	/// <param name="ct">Cancellation token.</param>
	/// <returns>The cloned <c>result.value</c> element (or <see langword="default"/> when there is no value).</returns>
	/// <exception cref="System.InvalidOperationException">The evaluation raised a runtime exception.</exception>
	Task<JsonElement> EvaluateAsync(string expression, bool awaitPromise = true, CancellationToken ct = default);
}
