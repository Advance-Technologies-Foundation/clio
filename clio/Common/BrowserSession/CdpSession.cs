using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Clio.Common.BrowserSession;

/// <inheritdoc cref="ICdpSession" />
/// <remarks>
/// One receive loop reads every message: an answer completes the pending command with the same id, an event is raised
/// through <see cref="EventReceived"/>. Sends are serialized, because a WebSocket allows one send at a time. When the
/// channel closes or fails, every pending command fails at once instead of waiting for its own timeout.
/// </remarks>
public sealed class CdpSession : ICdpSession {

	private readonly ICdpTransport _transport;
	private readonly ConcurrentDictionary<int, PendingCommand> _pending = new();
	private readonly SemaphoreSlim _sendLock = new(1, 1);
	private readonly CancellationTokenSource _lifetime = new();
	private readonly Task _receiveLoop;
	private int _lastId;
	private volatile string _closedReason;

	private sealed record PendingCommand(string Method, TaskCompletionSource<JsonElement> Completion);

	/// <summary>Starts a session over an open transport; the session owns and disposes it.</summary>
	/// <param name="transport">An open text-message channel to a page target.</param>
	public CdpSession(ICdpTransport transport) {
		_transport = transport ?? throw new ArgumentNullException(nameof(transport));
		_receiveLoop = Task.Run(ReceiveLoopAsync, CancellationToken.None);
	}

	/// <inheritdoc />
	public event EventHandler<CdpEvent> EventReceived;

	/// <inheritdoc />
	public async Task<JsonElement> SendAsync(string method, object parameters, CancellationToken ct) {
		ArgumentException.ThrowIfNullOrWhiteSpace(method);
		ThrowIfClosed(method);
		int id = Interlocked.Increment(ref _lastId);
		PendingCommand pending = new(method,
			new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously));
		_pending[id] = pending;
		try {
			string message = JsonSerializer.Serialize(new { id, method, @params = parameters ?? new { } });
			await _sendLock.WaitAsync(ct).ConfigureAwait(false);
			try {
				await _transport.SendAsync(message, ct).ConfigureAwait(false);
			}
			finally {
				_sendLock.Release();
			}
			// The loop may have closed between the check above and the registration; fail now rather than wait.
			if (_closedReason is not null) {
				pending.Completion.TrySetException(Closed(method));
			}
			return await pending.Completion.Task.WaitAsync(ct).ConfigureAwait(false);
		}
		finally {
			_pending.TryRemove(id, out _);
		}
	}

	/// <inheritdoc />
	public async ValueTask DisposeAsync() {
		_closedReason ??= "the session was disposed";
		await _lifetime.CancelAsync().ConfigureAwait(false);
		try {
			await _transport.DisposeAsync().ConfigureAwait(false);
		}
		catch (Exception exception) when (exception is WebSocketException or IOException or ObjectDisposedException
			or InvalidOperationException) {
			// Closing a dead connection is not worth reporting.
		}
		try {
			await _receiveLoop.ConfigureAwait(false);
		}
		catch (OperationCanceledException) {
			// The loop ends by cancellation on dispose.
		}
		FailPending();
		_sendLock.Dispose();
		_lifetime.Dispose();
	}

	private async Task ReceiveLoopAsync() {
		try {
			while (!_lifetime.IsCancellationRequested) {
				string message = await _transport.ReceiveAsync(_lifetime.Token).ConfigureAwait(false);
				if (message is null) {
					_closedReason ??= "the browser closed the DevTools connection";
					break;
				}
				Dispatch(message);
			}
		}
		catch (OperationCanceledException) {
			_closedReason ??= "the session was disposed";
		}
		catch (Exception exception) when (exception is WebSocketException or IOException or ObjectDisposedException
			or InvalidOperationException) {
			_closedReason ??= "the DevTools connection failed: " + exception.Message;
		}
		finally {
			FailPending();
		}
	}

	// A message that is not JSON, or has neither an id nor a method, is ignored: the browser never sends one, and one
	// bad frame must not stop every later answer.
	internal void Dispatch(string message) {
		JsonElement root;
		try {
			using JsonDocument document = JsonDocument.Parse(message);
			root = document.RootElement.Clone();
		}
		catch (JsonException) {
			return;
		}
		if (root.ValueKind != JsonValueKind.Object) {
			return;
		}
		if (root.TryGetProperty("id", out JsonElement idElement) && idElement.TryGetInt32(out int id)) {
			Complete(id, root);
			return;
		}
		if (root.TryGetProperty("method", out JsonElement methodElement) && methodElement.ValueKind == JsonValueKind.String) {
			JsonElement parameters = root.TryGetProperty("params", out JsonElement value) ? value : EmptyObject;
			Raise(new CdpEvent(methodElement.GetString(), parameters));
		}
	}

	private void Complete(int id, JsonElement root) {
		if (!_pending.TryGetValue(id, out PendingCommand pending)) {
			return;
		}
		if (root.TryGetProperty("error", out JsonElement error)) {
			string text = error.ValueKind == JsonValueKind.Object && error.TryGetProperty("message", out JsonElement message)
				? message.GetString() ?? "unknown CDP error"
				: error.ToString();
			pending.Completion.TrySetException(new CdpCommandException(pending.Method, text));
			return;
		}
		pending.Completion.TrySetResult(root.TryGetProperty("result", out JsonElement result) ? result : EmptyObject);
	}

	private void Raise(CdpEvent cdpEvent) {
		EventHandler<CdpEvent> handlers = EventReceived;
		if (handlers is null) {
			return;
		}
		foreach (EventHandler<CdpEvent> handler in handlers.GetInvocationList().Cast<EventHandler<CdpEvent>>()) {
			try {
				handler(this, cdpEvent);
			}
			catch (Exception exception) when (exception is not OutOfMemoryException) {
				// A failing subscriber must not stop the loop that answers every command.
			}
		}
	}

	private void FailPending() {
		foreach (PendingCommand pending in _pending.Values) {
			pending.Completion.TrySetException(Closed(pending.Method));
		}
	}

	private void ThrowIfClosed(string method) {
		if (_closedReason is not null) {
			throw Closed(method);
		}
	}

	private CdpSessionClosedException Closed(string method) =>
		new($"CDP command '{method}' could not complete: {_closedReason ?? "the session closed"}.");

	private static readonly JsonElement EmptyObject = JsonDocument.Parse("{}").RootElement.Clone();
}

/// <summary>A <see cref="ICdpTransport"/> over a <see cref="ClientWebSocket"/>.</summary>
public sealed class WebSocketCdpTransport : ICdpTransport {

	// A page answer is small; this bounds memory if the browser ever pushes something huge (the rest is discarded).
	internal const int MaxMessageBytes = 4 * 1024 * 1024;

	private readonly ClientWebSocket _socket;

	/// <summary>Wraps an open socket; the transport owns and disposes it.</summary>
	/// <param name="socket">A connected WebSocket.</param>
	public WebSocketCdpTransport(ClientWebSocket socket) {
		_socket = socket ?? throw new ArgumentNullException(nameof(socket));
	}

	/// <inheritdoc />
	public Task SendAsync(string message, CancellationToken ct) =>
		_socket.SendAsync(Encoding.UTF8.GetBytes(message), WebSocketMessageType.Text, endOfMessage: true, ct);

	/// <inheritdoc />
	public async Task<string> ReceiveAsync(CancellationToken ct) {
		byte[] buffer = new byte[16 * 1024];
		using MemoryStream stream = new();
		bool overflow = false;
		while (true) {
			WebSocketReceiveResult result = await _socket.ReceiveAsync(buffer, ct).ConfigureAwait(false);
			if (result.MessageType == WebSocketMessageType.Close) {
				return null;
			}
			if (stream.Length + result.Count <= MaxMessageBytes) {
				await stream.WriteAsync(buffer.AsMemory(0, result.Count), ct).ConfigureAwait(false);
			}
			else {
				overflow = true;
			}
			if (result.EndOfMessage) {
				// An oversized message is replaced with an empty object, which the session ignores.
				return overflow ? "{}" : Encoding.UTF8.GetString(stream.GetBuffer(), 0, (int)stream.Length);
			}
		}
	}

	/// <inheritdoc />
	public async ValueTask DisposeAsync() {
		try {
			if (_socket.State == WebSocketState.Open) {
				using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(2));
				await _socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "done", timeout.Token)
					.ConfigureAwait(false);
			}
		}
		catch (Exception exception) when (exception is WebSocketException or OperationCanceledException
			or ObjectDisposedException) {
			// The browser may already be gone.
		}
		finally {
			_socket.Dispose();
		}
	}
}

/// <inheritdoc cref="ICdpSessionFactory" />
public sealed class CdpSessionFactory : ICdpSessionFactory {

	/// <inheritdoc />
	public async Task<ICdpSession> ConnectAsync(Uri webSocketUrl, CancellationToken ct) {
		ArgumentNullException.ThrowIfNull(webSocketUrl);
		if (!webSocketUrl.IsLoopback) {
			// The DevTools endpoint is unauthenticated, so clio only ever talks to the browser it started itself.
			throw new InvalidOperationException("A DevTools session is opened only on a loopback address.");
		}
		// ClientWebSocket is a framework I/O transport (like HttpClient), constructed per connection.
		ClientWebSocket socket = new();
		try {
			await socket.ConnectAsync(webSocketUrl, ct).ConfigureAwait(false);
		}
		catch {
			socket.Dispose();
			throw;
		}
		return new CdpSession(new WebSocketCdpTransport(socket));
	}
}
