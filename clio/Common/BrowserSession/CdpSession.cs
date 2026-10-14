using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Clio.Common.BrowserSession;

/// <inheritdoc cref="ICdpSession" />
/// <remarks>
/// One receive loop reads every message: an answer completes the pending command with the same id, an event is raised
/// through <see cref="EventReceived"/>. Sends are serialized, because the channel carries one message at a time. When the
/// channel closes or fails, every pending command fails at once instead of waiting for its own timeout.
/// <para>
/// Over the browser's DevTools pipe the session starts at the browser target; once <see cref="RouteToSession"/> names the
/// flat session of an attached page, every command carries that session id, only that page's events are raised, and the
/// page being detached (closed, or crashed beyond recovery) closes the session.
/// </para>
/// </remarks>
public sealed class CdpSession : ICdpSession {

	private readonly ICdpTransport _transport;
	private readonly ConcurrentDictionary<int, PendingCommand> _pending = new();
	private readonly SemaphoreSlim _sendLock = new(1, 1);
	private readonly CancellationTokenSource _lifetime = new();
	private readonly Task _receiveLoop;
	private int _lastId;
	private readonly object _disposeGate = new();
	private Task _disposal;
	private volatile string _closedReason;
	private volatile string _sessionId;

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
			string sessionId = _sessionId;
			string message = sessionId is null
				? JsonSerializer.Serialize(new { id, method, @params = parameters ?? new { } })
				: JsonSerializer.Serialize(new { id, method, @params = parameters ?? new { }, sessionId });
			await _sendLock.WaitAsync(ct).ConfigureAwait(false);
			try {
				// The session may have closed while this sender waited for the lock.
				ThrowIfClosed(method);
				await _transport.SendAsync(message, ct).ConfigureAwait(false);
			}
			catch (Exception exception) when (IsTransportFailure(exception)) {
				// The channel dropped between ThrowIfClosed and the send: the same "the session is gone" every caller
				// already handles, not a raw socket error that slips past their filters.
				throw new CdpSessionClosedException(
					$"CDP command '{method}' could not be sent: the DevTools connection failed ({exception.Message}).",
					exception);
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
	public ValueTask DisposeAsync() {
		// Idempotent, and a second (or concurrent) call waits for the first one's cleanup instead of returning early.
		lock (_disposeGate) {
			_disposal ??= DisposeCoreAsync();
			return new ValueTask(_disposal);
		}
	}

	private async Task DisposeCoreAsync() {
		_closedReason ??= "the session was disposed";
		await _lifetime.CancelAsync().ConfigureAwait(false);
		try {
			await _transport.DisposeAsync().ConfigureAwait(false);
		}
		catch (Exception exception) when (exception is IOException or ObjectDisposedException
			or InvalidOperationException) {
			// Closing a dead connection is not worth reporting.
		}
		try {
			await _receiveLoop.ConfigureAwait(false);
		}
		catch (Exception exception) when (exception is not OutOfMemoryException) {
			// The loop records its own end in _closedReason. A fault here must neither replace the caller's real error
			// nor skip the cleanup below.
		}
		FailPending();
		// _sendLock is deliberately not disposed: a sender queued on it would never be woken (SemaphoreSlim.Dispose
		// does not release waiters). Without an AvailableWaitHandle it holds nothing to free; a woken sender sees the
		// closed session and fails at once.
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
		catch (Exception exception) when (IsTransportFailure(exception)) {
			_closedReason ??= "the DevTools connection failed: " + exception.Message;
		}
		catch (Exception exception) when (exception is not OutOfMemoryException) {
			// Anything else (a regex timeout in the oversized-reply sentinel, say) ends the loop just the same, so the
			// session must read as closed: otherwise every later command passes ThrowIfClosed and waits out its timeout.
			_closedReason ??= "the DevTools session stopped reading: " + exception.Message;
		}
		finally {
			FailPending();
		}
	}

	private static bool IsTransportFailure(Exception exception) =>
		exception is IOException or ObjectDisposedException
			or (InvalidOperationException and not CdpSessionClosedException);

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
			// A reply is matched by its id alone: ids are unique across the whole connection, and the oversized-reply
			// sentinel carries no session id.
			if (root.TryGetProperty(PipeCdpTransport.ReplyTooLargeMarker, out _)) {
				FailTooLarge(id);
				return;
			}
			Complete(id, root);
			return;
		}
		if (root.TryGetProperty("method", out JsonElement methodElement) && methodElement.ValueKind == JsonValueKind.String) {
			JsonElement parameters = root.TryGetProperty("params", out JsonElement value) ? value : EmptyObject;
			string method = methodElement.GetString();
			string eventSession = SessionIdOf(root);
			if (string.Equals(eventSession, _sessionId, StringComparison.Ordinal)) {
				Raise(new CdpEvent(method, parameters));
			}
			else if (eventSession is null && method == DetachedFromTarget && _sessionId is not null
				&& string.Equals(SessionIdOf(parameters), _sessionId, StringComparison.Ordinal)) {
				// The browser target reports that the page this session drives is gone: no later command can reach it.
				_closedReason ??= "the browser detached the page";
				FailPending();
			}
		}
	}

	/// <summary>Routes every later command to an attached page's flat session, and raises only that page's events.</summary>
	/// <param name="sessionId">The <c>sessionId</c> that <c>Target.attachToTarget</c> with <c>flatten</c> returned.</param>
	internal void RouteToSession(string sessionId) {
		ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
		_sessionId = sessionId;
	}

	private const string DetachedFromTarget = "Target.detachedFromTarget";

	private static string SessionIdOf(JsonElement element) =>
		element.ValueKind == JsonValueKind.Object && element.TryGetProperty("sessionId", out JsonElement session)
			&& session.ValueKind == JsonValueKind.String
				? session.GetString()
				: null;

	private void Complete(int id, JsonElement root) {
		if (!_pending.TryGetValue(id, out PendingCommand pending)) {
			return;
		}
		if (root.TryGetProperty("error", out JsonElement error)) {
			string text = error.ValueKind == JsonValueKind.Object && error.TryGetProperty("message", out JsonElement message)
				&& message.ValueKind == JsonValueKind.String
					? message.GetString() ?? "unknown CDP error"
					: error.ToString();
			pending.Completion.TrySetException(new CdpCommandException(pending.Method, text));
			return;
		}
		pending.Completion.TrySetResult(root.TryGetProperty("result", out JsonElement result) ? result : EmptyObject);
	}

	private void FailTooLarge(int id) {
		if (_pending.TryGetValue(id, out PendingCommand pending)) {
			pending.Completion.TrySetException(new CdpReplyTooLargeException(pending.Method,
				PipeCdpTransport.MaxMessageBytes));
		}
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
