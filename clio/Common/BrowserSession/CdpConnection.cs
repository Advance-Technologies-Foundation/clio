using System;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Clio.Common.BrowserSession;

/// <summary>
/// Minimal duplex text-frame transport over a WebSocket. This is the seam that lets <see cref="CdpSession"/>
/// be unit-tested with a fake connection instead of a live browser.
/// </summary>
public interface ICdpConnection : IAsyncDisposable {
	/// <summary>Whether the underlying socket is open.</summary>
	bool IsOpen { get; }

	/// <summary>Opens the connection to the given (loopback) WebSocket URL.</summary>
	Task ConnectAsync(Uri url, CancellationToken ct);

	/// <summary>Sends one UTF-8 text frame.</summary>
	Task SendTextAsync(string text, CancellationToken ct);

	/// <summary>Receives one full UTF-8 text frame (re-assembling continuation frames).</summary>
	Task<string> ReceiveTextAsync(CancellationToken ct);
}

/// <summary>
/// <see cref="ClientWebSocket"/>-backed <see cref="ICdpConnection"/>. The URL it connects to always comes
/// from the local DevTools <c>/json</c> endpoint, so it is loopback-only by construction.
/// </summary>
internal sealed class ClientWebSocketCdpConnection : ICdpConnection {
	// ClientWebSocket is a framework I/O transport (like Process/HttpClient), not a DI-managed behavior
	// service, so it is constructed locally per connection.
	private readonly ClientWebSocket _ws = new();

	/// <inheritdoc />
	public bool IsOpen => _ws.State == WebSocketState.Open;

	/// <inheritdoc />
	public Task ConnectAsync(Uri url, CancellationToken ct) => _ws.ConnectAsync(url, ct);

	/// <inheritdoc />
	public Task SendTextAsync(string text, CancellationToken ct) =>
		_ws.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(text)), WebSocketMessageType.Text, endOfMessage: true, ct);

	/// <inheritdoc />
	public async Task<string> ReceiveTextAsync(CancellationToken ct) {
		byte[] buffer = new byte[8192];
		using var stream = new MemoryStream();
		WebSocketReceiveResult result;
		do {
			result = await _ws.ReceiveAsync(buffer, ct).ConfigureAwait(false);
			await stream.WriteAsync(buffer.AsMemory(0, result.Count), ct).ConfigureAwait(false);
		} while (!result.EndOfMessage);
		return Encoding.UTF8.GetString(stream.GetBuffer(), 0, (int)stream.Length);
	}

	/// <inheritdoc />
	public async ValueTask DisposeAsync() {
		try {
			if (_ws.State == WebSocketState.Open) {
				await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None).ConfigureAwait(false);
			}
		} catch (WebSocketException) {
			// Best effort — the browser may already have gone away.
		} catch (OperationCanceledException) {
			// Best effort.
		}
		_ws.Dispose();
	}
}
