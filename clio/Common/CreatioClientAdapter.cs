using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using Clio.Common.Responses;
using Creatio.Client;
using Creatio.Client.Dto;

namespace Clio.Common;

public class CreatioClientAdapter : IOwnedApplicationClient {
	#region Fields: Private

	private readonly Lazy<CreatioClient> _lazyClient;
	private readonly IServiceUrlBuilder _serviceUrlBuilder;
	private readonly JsonConverter _jsonConverter;
	private readonly IReauthExecutor _reauthExecutor;
	private readonly ILoginDiagnostics _loginDiagnostics;
	private readonly bool _ownsClient;
	private readonly object _lifetimeSync = new();
	private bool _disposed;
	private bool _listenerStarted;

	private CreatioClient Client {
		get {
			lock (_lifetimeSync) {
				ObjectDisposedException.ThrowIf(_disposed, this);
				return _lazyClient.Value;
			}
		}
	}

	#endregion

	#region Constructors: Private

	// The reauthExecutor parameter is null in production: the executor captures a closure
	// over this adapter's own _lazyClient.Value.Login() and is therefore created here
	// rather than resolved from DI. Tests pass a non-null executor through the internal
	// constructor below to exercise the adapter in isolation from CreatioClient.
	private CreatioClientAdapter(Lazy<CreatioClient> lazyClient, IServiceUrlBuilder serviceUrlBuilder,
		JsonConverter jsonConverter, IReauthExecutor reauthExecutor,
		ILoginDiagnostics loginDiagnostics = null, bool ownsClient = false) {
		_lazyClient = lazyClient;
		_ownsClient = ownsClient;
		_serviceUrlBuilder = serviceUrlBuilder;
		_jsonConverter = jsonConverter ?? new JsonConverter();
		// Recover transparently from server-side session expiration: a singleton CreatioClient
		// shared across a long-lived MCP process can otherwise keep sending a stale cookie
		// after long-running operations and start receiving the HTML login page instead of
		// JSON for every subsequent request.
		// Like the reauth executor below, the diagnostics recorder is per-adapter state (its own client
		// correlation token and attempt counter) rather than a DI service, so it defaults to a new
		// instance here; the parameter exists so tests can substitute it and assert that every request
		// method and both login paths really route through it.
		_loginDiagnostics = loginDiagnostics ?? new LoginDiagnostics();
		_reauthExecutor = reauthExecutor ?? new ReauthExecutor(
			() => _loginDiagnostics.Track(() => _lazyClient.Value.Login(), LoginAttemptKind.Reauthentication));
	}

	#endregion

	#region Constructors: Public

	public CreatioClientAdapter(string appUrl, string userName, string userPassword, bool isNetCore = false,
		IServiceUrlBuilder serviceUrlBuilder = null)
		: this(appUrl, userName, userPassword, true, isNetCore, serviceUrlBuilder) { }

	/// <summary>Creates a forms-auth adapter with explicit server-certificate validation behavior.</summary>
	/// <param name="appUrl">Creatio application URL.</param>
	/// <param name="userName">Forms-auth user name.</param>
	/// <param name="userPassword">Forms-auth password.</param>
	/// <param name="useUntrustedSsl">Whether invalid server certificates are accepted.</param>
	/// <param name="isNetCore">Whether the target Creatio application uses the .NET runtime.</param>
	/// <param name="serviceUrlBuilder">Optional environment-relative URL builder.</param>
	internal CreatioClientAdapter(string appUrl, string userName, string userPassword,
		bool useUntrustedSsl, bool isNetCore, IServiceUrlBuilder serviceUrlBuilder = null)
		: this(new Lazy<CreatioClient>(() => new CreatioClient(appUrl, userName, userPassword,
			useUntrustedSsl, isNetCore)),
			serviceUrlBuilder, null, null, ownsClient: true) { }

	public CreatioClientAdapter(string appUrl, string clientId, string clientSecret, string authAppUrl,
		bool isNetCore = false, IServiceUrlBuilder serviceUrlBuilder = null)
		: this(new Lazy<CreatioClient>(() =>
			CreatioClient.CreateOAuth20Client(appUrl, authAppUrl, clientId, clientSecret, isNetCore)),
			serviceUrlBuilder, null, null, ownsClient: true) { }

	public CreatioClientAdapter(CreatioClient creatioClient)
		: this(new Lazy<CreatioClient>(() => creatioClient), null, null, null) { }

	public CreatioClientAdapter(Lazy<CreatioClient> lazyClient)
		: this(lazyClient, null, null, null, ownsClient: false) { }

	#endregion

	#region Constructors: Internal

	// Test-only constructor. Allows substituting the reauth executor without instantiating
	// a real CreatioClient (the NuGet type is not mockable). The lazyClient may resolve to
	// null in tests because the substituted executor never invokes the wrapped callback.
	// reauthExecutor is required so tests cannot silently fall back to the default executor.
	internal CreatioClientAdapter(Lazy<CreatioClient> lazyClient, IReauthExecutor reauthExecutor)
		: this(lazyClient, null, null,
			reauthExecutor ?? throw new ArgumentNullException(nameof(reauthExecutor)), ownsClient: true) { }

	// DI composition constructor: unlike the public Lazy overload (which preserves borrowed-client
	// compatibility), this adapter is the sole owner of the lazily-created environment client.
	internal CreatioClientAdapter(Lazy<CreatioClient> lazyClient, bool ownsClient)
		: this(lazyClient, null, null, null, ownsClient: ownsClient) { }

	// Test-only constructor for the diagnostics seam. Unlike the two constructors around it, the
	// reauth executor MAY be null here: leaving it to the default is the only way to reach the
	// Reauthentication login closure the default executor owns, which is precisely one of the
	// wirings that has to be pinned. The diagnostics recorder is required so a test cannot silently
	// assert against a private instance.
	internal CreatioClientAdapter(Lazy<CreatioClient> lazyClient, IReauthExecutor reauthExecutor,
		ILoginDiagnostics loginDiagnostics)
		: this(lazyClient, null, null, reauthExecutor,
			loginDiagnostics ?? throw new ArgumentNullException(nameof(loginDiagnostics)), ownsClient: true) { }

	// Credential-passthrough constructor: lets ApplicationClientFactory.CreateEnvironmentClient
	// wire BOTH a service-url builder (for environment-relative routes) and an explicit
	// reauth executor (the NoReauthExecutor, because bearer material cannot re-login).
	internal CreatioClientAdapter(Lazy<CreatioClient> lazyClient, IServiceUrlBuilder serviceUrlBuilder,
		IReauthExecutor reauthExecutor)
		: this(lazyClient, serviceUrlBuilder, null,
			reauthExecutor ?? throw new ArgumentNullException(nameof(reauthExecutor)), ownsClient: true) { }

	// Factory-created bearer clients are short-lived and owned by the returned adapter. The DI path
	// intentionally uses the three-argument overload above because its client can back a SignalR
	// listener whose cancellation completes asynchronously after the service provider is disposed.
	internal CreatioClientAdapter(Lazy<CreatioClient> lazyClient, IServiceUrlBuilder serviceUrlBuilder,
		IReauthExecutor reauthExecutor, bool ownsClient)
		: this(lazyClient, serviceUrlBuilder, null,
			reauthExecutor ?? throw new ArgumentNullException(nameof(reauthExecutor)), ownsClient: ownsClient) { }

	#endregion

	public event EventHandler<WebSocketState> ConnectionStateChanged;

	public event EventHandler<WsMessage> MessageReceived;

	#region Methods: Private

	// One composition point for every request method: reauth on a session-expired response, with the
	// login diagnostics recorded inside it. Keeping it in one place means a request method added later
	// cannot silently lose either wrapper.
	// Not generic: the session-expired predicate inspects the raw response body, so it only applies to
	// the string-returning request methods — which is all of them that go through the reauth executor.
	private string ExecuteRequest(Func<string> call) =>
		_reauthExecutor.Execute(() => _loginDiagnostics.TrackRequest(call),
			ReauthExecutor.IsSessionExpiredResponse);

	#endregion

	#region Methods: Public

	// Sonar S1006: the implementation deliberately defaults to Timeout.Infinite even though
	// the interface defaults to 10_000 ms. Configuration-service calls can legitimately run
	// for minutes (package install, long compile triggers); the runtime behavior pre-dates
	// this PR and is preserved to avoid surprising direct callers of CreatioClientAdapter
	// with a tighter timeout. Interface callers (the common path) keep the 10-second default.
#pragma warning disable S1006
	public string CallConfigurationService(string serviceName, string serviceMethod, string requestData,
		int requestTimeout = Timeout.Infinite) {
#pragma warning restore S1006
		// The minutes-long profile of this call (package install, long compile triggers) is
		// exactly the scenario that expires the session, so the call MUST route through the
		// reauth executor — otherwise a stale-cookie response surfaces directly as raw HTML
		// to the caller.
		return ExecuteRequest(
			() => Client.CallConfigurationService(serviceName, serviceMethod, requestData, requestTimeout));
	}

	public void DownloadFile(string url, string filePath, string requestData) {
		string absoluteUrl = url;
		if (_serviceUrlBuilder != null) {
			absoluteUrl = _serviceUrlBuilder.Build(url);
		}

		// DownloadFile is intentionally NOT wrapped through ReauthExecutor: the underlying
		// NuGet method returns void and writes the response body directly to disk, so the
		// session-expired detector — which works on the in-memory response string — has no
		// hook to inspect. If the session is stale, the file on disk will contain the HTML
		// login page; the caller (the download initiator) is responsible for verifying the
		// payload before consuming it. Fortunately downloads in clio go through cookie-bound
		// short-lived flows (cliogate file fetch) where session expiry mid-download is
		// uncommon; wrapping it would require either a pre-flight probe or a post-download
		// file-content sniff, both of which add I/O for very little practical gain.
		_loginDiagnostics.TrackRequest(() => Client.DownloadFile(absoluteUrl, filePath, requestData));
	}

	public string ExecuteDeleteRequest(string url, string requestData, int requestTimeout = Timeout.Infinite,
		int maxAttempts = 1, int delaySec = 1) {
		return ExecuteRequest(
			() => Client.ExecuteDeleteRequest(url, requestData, requestTimeout, maxAttempts, delaySec));
	}

	public string ExecuteGetRequest(string url, int requestTimeout = Timeout.Infinite, int maxAttempts = 1,
		int delaySec = 1) {
		return ExecuteRequest(() => Client.ExecuteGetRequest(url, requestTimeout, maxAttempts, delaySec));
	}

	/// <inheritdoc />
	public Task<HttpResponseMessage> ExecuteGetRequestAsync(string url, int requestTimeout = 100_000,
		int maxAttempts = 1, int delaySec = 1, CancellationToken cancellationToken = default) =>
		_loginDiagnostics.TrackRequestAsync(() =>
			Client.ExecuteGetRequestAsync(url, requestTimeout, maxAttempts, delaySec, cancellationToken));

	/// <inheritdoc />
	public async Task<byte[]> ExecuteGetRequestBoundedAsync(string url, long maxBytes,
		int requestTimeout = 100_000, CancellationToken cancellationToken = default) {
		// The transfer runs through the ONE configured, authenticated client. DownloadFileByGetBoundedAsync
		// issues its request with HttpCompletionOption.ResponseHeadersRead and copies the body incrementally
		// to disk, so it streams exactly like a hand-built transport would - while keeping everything a
		// parallel stack loses: the OAuth/bearer token, the configured certificate-validation policy
		// (useUntrustedSsl is held by the client, never by this adapter) and the session-recovery retry.
		// The ceiling is enforced INSIDE that copy loop, before each write. The previous version could only
		// watch the growing scratch file from another task, which is a TIME bound rather than a byte bound:
		// the producer is not scheduled in step with the observer, so an arbitrary amount got through between
		// two observations - measured at over 134 MB against a 64 MiB limit. Nothing outside the client could
		// fix that, because 2.0.2 exposed no per-chunk hook, no Stream-returning download and no
		// HttpMessageHandler seam to wrap.
		// The raw OData response is staged on disk before it is handed back, and the download opens that path
		// with an ordinary FileMode.Create - which under the usual umask 022 leaves an ambient 0644 file that
		// any other local account can read while the transfer runs. The staging file therefore lives inside a
		// directory created owner-only IN THE SAME CALL that creates it, so there is no window in which the
		// business data underneath is reachable by anyone else, whatever mode the file itself ends up with.
		string scratchDirectory = CreateOwnerOnlyScratchDirectory();
		string scratch = Path.Combine(scratchDirectory, "response.tmp");
		// One deadline across send, stream acquisition and EVERY body read. With ResponseHeadersRead a server
		// can answer the headers in milliseconds and then withhold the body forever: the reads would then be
		// governed by the caller token alone, and MCP host cancellation is not guaranteed to arrive, so the
		// invocation would hang with no bound at all.
		using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		if (requestTimeout > 0) {
			deadline.CancelAfter(requestTimeout);
		}
		try {
			// EVERY status streams to the scratch file through that one counted loop, so a non-2xx body is
			// bounded as well and is readable here. 2.0.2 answered a final non-2xx by draining the whole body
			// into memory and writing no file, so this read failed with FileNotFoundException and the real
			// server error was lost - the status was all that survived.
			using HttpResponseMessage response = await Client
				.DownloadFileByGetBoundedAsync(url, scratch, maxBytes, requestTimeout, deadline.Token)
				.ConfigureAwait(false);
			return await File.ReadAllBytesAsync(scratch, cancellationToken).ConfigureAwait(false);
		}
		// Translated at the boundary: callers of IApplicationClient must not have to reference the transport
		// package to catch its exception type, and ResponseTooLargeException is what the OData tools already
		// report to the agent.
		catch (CreatioResponseTooLargeException exception) {
			throw new ResponseTooLargeException(exception.ObservedBytes, exception.MaxBytes);
		}
		// Deadline expiry and caller cancellation arrive as the SAME exception type from the linked source, and
		// they mean different things to the caller: one is the server failing to deliver in time (retryable,
		// and the message has to say so), the other is the caller withdrawing the request (nothing to report).
		// Distinguishing them is only possible here, where both tokens are still in scope.
		catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested
			&& deadline.IsCancellationRequested) {
			throw new TimeoutException(
				$"the request to '{url}' did not complete within {requestTimeout} ms. The response headers may "
				+ "have arrived while the body stalled; narrow the query with 'select' or 'top', or raise the "
				+ "request timeout.");
		}
		finally {
			DeleteScratchQuietly(scratch);
			DeleteScratchDirectoryQuietly(scratchDirectory);
		}
	}

	// Owner-only AT CREATION rather than tightened afterwards: File/Directory.SetUnixFileMode runs after the
	// directory already exists, and everything staged during that gap is world-readable. The mode argument is
	// applied by the mkdir syscall itself, so the directory is never briefly open.
	private static string CreateOwnerOnlyScratchDirectory() {
		string path = Path.Combine(Path.GetTempPath(), $"clio-bounded-{Guid.NewGuid():N}");
		if (OperatingSystem.IsWindows()) {
			// %TEMP% on Windows is per-user (under the profile) and inherits its owner-only ACL, so the
			// directory is not shared the way the Unix temp root is. Explicit DACL tightening is the same
			// tracked follow-up FileSecurityHardening records rather than shipping unverified ACL code.
			Directory.CreateDirectory(path);
			return path;
		}
		Directory.CreateDirectory(path, OwnerOnlyDirectory);
		return path;
	}

	private const UnixFileMode OwnerOnlyDirectory =
		UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;

	private static void DeleteScratchDirectoryQuietly(string path) {
		try {
			if (Directory.Exists(path)) {
				Directory.Delete(path, true);
			}
		}
		catch (IOException) {
			// A leftover staging directory is not worth replacing the real failure with a second exception.
		}
		catch (UnauthorizedAccessException) {
			// Same reasoning as above.
		}
	}

	private static void DeleteScratchQuietly(string path) {
		try {
			if (File.Exists(path)) {
				File.Delete(path);
			}
		}
		catch (IOException) {
			// A leftover scratch file is not worth replacing the real failure with a second exception.
		}
		catch (UnauthorizedAccessException) {
			// Same reasoning as above.
		}
	}


	public string ExecutePostRequest(string url, string requestData, int requestTimeout = Timeout.Infinite,
		int maxAttempts = 1, int delaySec = 1) {
		return ExecuteRequest(
			() => Client.ExecutePostRequest(url, requestData, requestTimeout, maxAttempts, delaySec));
	}

	public T ExecutePostRequest<T>(string url, string requestData, int requestTimeout = Timeout.Infinite,
		int maxAttempts = 1, int delaySec = 1)
		where T : BaseResponse, new() {
		// Re-auth detection runs against the raw body so an expired session cannot reach
		// the JSON deserializer (which would throw on the HTML login page).
		string response = ExecuteRequest(
			() => Client.ExecutePostRequest(url, requestData, requestTimeout, maxAttempts, delaySec));
		// If the retry also returned the session-expired HTML page, the JSON deserializer
		// below would surface the same opaque "Invalid response format" symptom that
		// triggered ENG-90393. Throw a clearer message so the caller (and the user) can
		// distinguish an unrecoverable auth failure from a real bad payload.
		if (ReauthExecutor.IsSessionExpiredResponse(response)) {
			throw new InvalidOperationException(
				"Creatio session expired and the automatic re-authentication did not restore it. " +
				"Verify the environment credentials (e.g. via 'clio reg-web-app --check-login') and retry.");
		}
		return _jsonConverter.DeserializeObject<T>(response);
	}

	/// <inheritdoc />
	public Task<HttpResponseMessage> ExecutePostRequestAsync(string url, string requestData,
		int requestTimeout = 100_000, int maxAttempts = 1, int delaySec = 1,
		CancellationToken cancellationToken = default) =>
		_loginDiagnostics.TrackRequestAsync(() =>
			Client.ExecutePostRequestAsync(url, requestData, requestTimeout, maxAttempts, delaySec,
				cancellationToken));

	public string ExecutePatchRequest(string url, string requestData, int requestTimeout = Timeout.Infinite,
		int maxAttempts = 1, int delaySec = 1) {
		return ExecuteRequest(
			() => Client.ExecutePatchRequest(url, requestData, requestTimeout, maxAttempts, delaySec));
	}

	public string ExecutePutRequest(string url, string requestData, int requestTimeout = Timeout.Infinite,
		int maxAttempts = 1, int delaySec = 1) {
		return ExecuteRequest(
			() => Client.ExecutePutRequest(url, requestData, requestTimeout, maxAttempts, delaySec));
	}

	public void Listen(CancellationToken cancellationToken) {
		CreatioClient client;
		lock (_lifetimeSync) {
			ObjectDisposedException.ThrowIf(_disposed, this);
			client = _lazyClient.Value;
			_listenerStarted = true;
		}
		client.ConnectionStateChanged += (sender, state) => { ConnectionStateChanged?.Invoke(sender, state); };

		client.MessageReceived += (sender, message) => { MessageReceived?.Invoke(sender, message); };

		client.StartListening(cancellationToken);
	}

	public void Login() {
		// Recorded the same way as the automatic re-login above so a rejected login is diagnosable
		// from CI output alone, and so the two attempt kinds are distinguishable (GitHub #1106).
		_loginDiagnostics.Track(() => Client.Login(), LoginAttemptKind.Initial);
	}

	/// <inheritdoc />
	public Task<HttpResponseMessage> LoginAsync(int requestTimeout = 100_000,
		CancellationToken cancellationToken = default) =>
		_loginDiagnostics.TrackAsync(() => Client.LoginAsync(requestTimeout, cancellationToken),
			LoginAttemptKind.Initial);

	/// <inheritdoc />
	public IReadOnlyList<CreatioSessionCookie> ExportSessionCookies() => Client.ExportSessionCookies();

	/// <inheritdoc />
	public void ImportSessionCookies(IEnumerable<CreatioSessionCookie> cookies) => Client.ImportSessionCookies(cookies);

	/// <inheritdoc />
	public void Dispose() {
		Dispose(true);
		GC.SuppressFinalize(this);
	}

	/// <summary>Releases the owned Creatio transport when disposal was requested.</summary>
	/// <param name="disposing">Whether managed resources should be released.</param>
	protected virtual void Dispose(bool disposing) {
		if (!disposing) {
			return;
		}
		lock (_lifetimeSync) {
			if (_disposed) {
				return;
			}
			_disposed = true;
			// CreatioClient's SignalR listener can still re-enter Login while its cancellation is
			// draining. Disposing the pooled HTTP transport here races that reconnect and crashes the
			// process; listener clients therefore live until process/GC teardown after cancellation.
			if (_ownsClient && !_listenerStarted && _lazyClient.IsValueCreated) {
				_lazyClient.Value?.Dispose();
			}
		}
	}

	public string UploadAlmFile(string url, string filePath) {
		return ExecuteRequest(() => Client.UploadAlmFile(url, filePath));
	}

	public string UploadAlmFileByChunk(string url, string filePath) {
		return ExecuteRequest(() => Client.UploadAlmFileByChunk(url, filePath));
	}

	public string UploadFile(string url, string filePath) {
		return ExecuteRequest(() => Client.UploadFile(url, filePath));
	}

	/// <inheritdoc />
	public Task<HttpResponseMessage> UploadImageAsync(string url, byte[] data, string fileName,
		string mimeType, int requestTimeout = 100_000,
		CancellationToken cancellationToken = default) =>
		_loginDiagnostics.TrackRequestAsync(() =>
			Client.UploadImageAsync(url, data, fileName, mimeType, requestTimeout, cancellationToken));

	#endregion
}
