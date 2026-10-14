using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Clio.Common.BrowserSession;

/// <summary>
/// One Chrome DevTools Protocol session over a page target: sends commands, correlates each answer by id, and raises
/// every event the browser pushes.
/// </summary>
public interface ICdpSession : IAsyncDisposable {

	/// <summary>
	/// Raised for every CDP event (a message with a <c>method</c> and no <c>id</c>), on the session's receive loop.
	/// A handler must be quick and thread-safe; an exception it throws is swallowed so it cannot stop the loop.
	/// </summary>
	event EventHandler<CdpEvent> EventReceived;

	/// <summary>Sends one command and waits for its answer.</summary>
	/// <param name="method">CDP method, for example <c>Runtime.evaluate</c>.</param>
	/// <param name="parameters">Command parameters, serialized as the <c>params</c> object; null sends <c>{}</c>.</param>
	/// <param name="ct">Cancels the wait; the browser may still execute the command.</param>
	/// <returns>The <c>result</c> object of the answer.</returns>
	/// <exception cref="CdpCommandException">The browser answered with an <c>error</c>.</exception>
	/// <exception cref="CdpSessionClosedException">The connection closed before the answer arrived.</exception>
	Task<JsonElement> SendAsync(string method, object parameters, CancellationToken ct);
}

/// <summary>One CDP event.</summary>
/// <param name="Method">Event name, for example <c>Runtime.exceptionThrown</c>.</param>
/// <param name="Params">The event's <c>params</c> object (an empty object when absent).</param>
public sealed record CdpEvent(string Method, JsonElement Params);

/// <summary>A CDP command the browser answered with an error.</summary>
public sealed class CdpCommandException : Exception {

	/// <summary>Creates the exception for <paramref name="method"/>.</summary>
	/// <param name="method">The command that failed.</param>
	/// <param name="message">The browser's error message.</param>
	public CdpCommandException(string method, string message)
		: base($"CDP command '{method}' failed: {message}") {
		Method = method;
	}

	/// <summary>The command that failed.</summary>
	public string Method { get; }
}

/// <summary>The browser's reply to a command was larger than clio reads, so it was discarded.</summary>
public sealed class CdpReplyTooLargeException : InvalidOperationException {

	/// <summary>Creates the exception for <paramref name="method"/>.</summary>
	/// <param name="method">The command whose reply was discarded.</param>
	/// <param name="maxBytes">The largest reply clio reads.</param>
	public CdpReplyTooLargeException(string method, int maxBytes)
		: base($"CDP command '{method}' failed: its reply exceeded {maxBytes / (1024 * 1024)} MB and was discarded.") {
		Method = method;
	}

	/// <summary>The command whose reply was discarded.</summary>
	public string Method { get; }
}

/// <summary>The DevTools connection is gone (the browser exited or the socket dropped); no later command can succeed.</summary>
public sealed class CdpSessionClosedException : InvalidOperationException {

	/// <summary>Creates the exception.</summary>
	/// <param name="message">What could not complete, and why the session closed.</param>
	public CdpSessionClosedException(string message) : base(message) {
	}

	/// <summary>Creates the exception around the transport failure that closed the session.</summary>
	/// <param name="message">What could not complete, and why the session closed.</param>
	/// <param name="innerException">The transport's own failure.</param>
	public CdpSessionClosedException(string message, Exception innerException) : base(message, innerException) {
	}
}

/// <summary>Opens <see cref="ICdpSession"/> instances.</summary>
public interface ICdpSessionFactory {

	/// <summary>Connects to a page target's DevTools WebSocket.</summary>
	/// <param name="webSocketUrl">The target's <c>webSocketDebuggerUrl</c>, a loopback address.</param>
	/// <param name="ct">Cancels the connect.</param>
	/// <returns>An open session; dispose it to close the connection.</returns>
	Task<ICdpSession> ConnectAsync(Uri webSocketUrl, CancellationToken ct);
}

/// <summary>The text-message channel a <see cref="CdpSession"/> runs over; a WebSocket in production.</summary>
public interface ICdpTransport : IAsyncDisposable {

	/// <summary>Sends one text message. Never called concurrently by the session.</summary>
	Task SendAsync(string message, CancellationToken ct);

	/// <summary>Receives the next whole text message; null once the channel is closed.</summary>
	Task<string> ReceiveAsync(CancellationToken ct);
}
