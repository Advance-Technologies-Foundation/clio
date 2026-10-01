using System.Net;
using System.Text;

namespace Clio.Mcp.E2E.Support.Creatio;

/// <summary>
/// Loopback Creatio stub that answers the forms-auth login and <c>DataService SelectQuery</c>, counting the
/// SelectQuery requests and the bytes of their responses.
/// </summary>
/// <remarks>
/// The SelectQuery response is either a fixed body sent with a Content-Length, or a chunked body produced by a
/// caller-supplied writer, for responses too large to hold in memory. Every other path answers 404.
/// </remarks>
internal sealed class SelectQueryStubServer : IAsyncDisposable {

	/// <summary>Writes one chunk of the SelectQuery response and counts its bytes.</summary>
	/// <param name="chunk">Bytes to write.</param>
	internal delegate Task ChunkWriter(ReadOnlyMemory<byte> chunk);

	private readonly CancellationTokenSource _cancellationTokenSource = new();
	private readonly HttpListener _listener;
	private readonly Task _listenerLoop;
	private readonly Func<ChunkWriter, Task> _writeSelectQueryBody;
	private readonly byte[]? _fixedSelectQueryBody;
	private int _selectQueryRequests;
	private long _selectQueryResponseBytes;

	private SelectQueryStubServer(
		HttpListener listener,
		string applicationUri,
		byte[]? fixedSelectQueryBody,
		Func<ChunkWriter, Task> writeSelectQueryBody) {
		_listener = listener;
		ApplicationUri = applicationUri;
		_fixedSelectQueryBody = fixedSelectQueryBody;
		_writeSelectQueryBody = writeSelectQueryBody;
		_listenerLoop = Task.Run(ListenAsync);
	}

	/// <summary>Base URI to register as the environment's Uri.</summary>
	public string ApplicationUri { get; }

	/// <summary>Number of SelectQuery requests received.</summary>
	public int SelectQueryRequests => Volatile.Read(ref _selectQueryRequests);

	/// <summary>Number of SelectQuery response body bytes written.</summary>
	public long SelectQueryResponseBytes => Interlocked.Read(ref _selectQueryResponseBytes);

	/// <summary>Starts a stub that answers every SelectQuery with <paramref name="selectQueryBody"/>.</summary>
	/// <param name="selectQueryBody">JSON body of the SelectQuery response.</param>
	public static SelectQueryStubServer Start(string selectQueryBody) {
		byte[] body = Encoding.UTF8.GetBytes(selectQueryBody);
		return Start(body, writer => writer(body));
	}

	/// <summary>
	/// Starts a stub that answers every SelectQuery with a chunked body produced by
	/// <paramref name="writeSelectQueryBody"/> through the writer it is given.
	/// </summary>
	/// <param name="writeSelectQueryBody">Writes the response body chunk by chunk.</param>
	public static SelectQueryStubServer StartChunked(Func<ChunkWriter, Task> writeSelectQueryBody) =>
		Start(null, writeSelectQueryBody);

	private static SelectQueryStubServer Start(byte[]? fixedBody, Func<ChunkWriter, Task> writeBody) {
		for (int attempt = 0; attempt < 5; attempt++) {
			int port = Random.Shared.Next(20_000, 60_000);
			HttpListener listener = new();
			listener.Prefixes.Add($"http://127.0.0.1:{port}/");
			try {
				listener.Start();
				return new SelectQueryStubServer(listener, $"http://127.0.0.1:{port}", fixedBody, writeBody);
			}
			catch (HttpListenerException) {
				listener.Close();
			}
		}
		throw new InvalidOperationException("Unable to start the SelectQuery loopback stub.");
	}

	/// <inheritdoc />
	public async ValueTask DisposeAsync() {
		_cancellationTokenSource.Cancel();
		_listener.Stop();
		try {
			await _listenerLoop.ConfigureAwait(false);
		}
		catch (OperationCanceledException) {
			// Expected when the fixture stops the listener.
		}
		finally {
			_listener.Close();
			_cancellationTokenSource.Dispose();
		}
	}

	private async Task ListenAsync() {
		while (!_cancellationTokenSource.IsCancellationRequested) {
			HttpListenerContext context;
			try {
				context = await _listener.GetContextAsync()
					.WaitAsync(_cancellationTokenSource.Token)
					.ConfigureAwait(false);
			}
			catch (OperationCanceledException) {
				return;
			}
			catch (HttpListenerException) when (_cancellationTokenSource.IsCancellationRequested) {
				return;
			}
			catch (ObjectDisposedException) when (_cancellationTokenSource.IsCancellationRequested) {
				return;
			}
			await RespondAsync(context).ConfigureAwait(false);
		}
	}

	private async Task RespondAsync(HttpListenerContext context) {
		string path = context.Request.Url?.AbsolutePath ?? string.Empty;
		if (path.EndsWith("/ServiceModel/AuthService.svc/Login", StringComparison.Ordinal)) {
			context.Response.Headers.Add("Set-Cookie", ".ASPXAUTH=stub-session; path=/");
			context.Response.Headers.Add("Set-Cookie", "BPMCSRF=stub-csrf; path=/");
			await WriteFixedAsync(context.Response, Encoding.UTF8.GetBytes("{\"Code\":0}")).ConfigureAwait(false);
		} else if (path.EndsWith("/DataService/json/SyncReply/SelectQuery", StringComparison.Ordinal)) {
			Interlocked.Increment(ref _selectQueryRequests);
			await WriteSelectQueryResponseAsync(context.Response).ConfigureAwait(false);
		} else {
			context.Response.StatusCode = (int)HttpStatusCode.NotFound;
			await WriteFixedAsync(context.Response, Encoding.UTF8.GetBytes("Not Found")).ConfigureAwait(false);
		}
	}

	private async Task WriteSelectQueryResponseAsync(HttpListenerResponse response) {
		response.ContentType = "application/json";
		if (_fixedSelectQueryBody is not null) {
			response.ContentLength64 = _fixedSelectQueryBody.Length;
		} else {
			response.SendChunked = true;
		}
		await _writeSelectQueryBody(async chunk => {
			await response.OutputStream.WriteAsync(chunk).ConfigureAwait(false);
			Interlocked.Add(ref _selectQueryResponseBytes, chunk.Length);
		}).ConfigureAwait(false);
		response.Close();
	}

	private static async Task WriteFixedAsync(HttpListenerResponse response, byte[] bytes) {
		response.ContentType = "application/json";
		response.ContentLength64 = bytes.Length;
		await response.OutputStream.WriteAsync(bytes).ConfigureAwait(false);
		response.Close();
	}
}
