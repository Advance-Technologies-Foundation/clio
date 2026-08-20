using System;
using System.Net.WebSockets;
using System.Threading;
using Clio.Common.Responses;
using Creatio.Client;
using Creatio.Client.Dto;

namespace Clio.Common;

public class CreatioClientAdapter : IApplicationClient{
	#region Fields: Private

	private readonly Lazy<CreatioClient> _lazyClient;
	private readonly IServiceUrlBuilder _serviceUrlBuilder;
	private readonly JsonConverter _jsonConverter;
	private readonly IReauthExecutor _reauthExecutor;
	private readonly ILoginDiagnostics _loginDiagnostics;

	private CreatioClient Client => _lazyClient.Value;

	#endregion

	#region Constructors: Private

	// The reauthExecutor parameter is null in production: the executor captures a closure
	// over this adapter's own _lazyClient.Value.Login() and is therefore created here
	// rather than resolved from DI. Tests pass a non-null executor through the internal
	// constructor below to exercise the adapter in isolation from CreatioClient.
	private CreatioClientAdapter(Lazy<CreatioClient> lazyClient, IServiceUrlBuilder serviceUrlBuilder,
		JsonConverter jsonConverter, IReauthExecutor reauthExecutor,
		ILoginDiagnostics loginDiagnostics = null) {
		_lazyClient = lazyClient;
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
		: this(new Lazy<CreatioClient>(() => new CreatioClient(appUrl, userName, userPassword, true, isNetCore)),
			serviceUrlBuilder, null, null) { }

	public CreatioClientAdapter(string appUrl, string clientId, string clientSecret, string authAppUrl,
		bool isNetCore = false, IServiceUrlBuilder serviceUrlBuilder = null)
		: this(new Lazy<CreatioClient>(() =>
			CreatioClient.CreateOAuth20Client(appUrl, authAppUrl, clientId, clientSecret, isNetCore)),
			serviceUrlBuilder, null, null) { }

	public CreatioClientAdapter(CreatioClient creatioClient)
		: this(new Lazy<CreatioClient>(() => creatioClient), null, null, null) { }

	public CreatioClientAdapter(Lazy<CreatioClient> lazyClient)
		: this(lazyClient, null, null, null) { }

	#endregion

	#region Constructors: Internal

	// Test-only constructor. Allows substituting the reauth executor without instantiating
	// a real CreatioClient (the NuGet type is not mockable). The lazyClient may resolve to
	// null in tests because the substituted executor never invokes the wrapped callback.
	// reauthExecutor is required so tests cannot silently fall back to the default executor.
	internal CreatioClientAdapter(Lazy<CreatioClient> lazyClient, IReauthExecutor reauthExecutor)
		: this(lazyClient, null, null,
			reauthExecutor ?? throw new ArgumentNullException(nameof(reauthExecutor))) { }

	// Test-only constructor for the diagnostics seam. Unlike the two constructors around it, the
	// reauth executor MAY be null here: leaving it to the default is the only way to reach the
	// Reauthentication login closure the default executor owns, which is precisely one of the
	// wirings that has to be pinned. The diagnostics recorder is required so a test cannot silently
	// assert against a private instance.
	internal CreatioClientAdapter(Lazy<CreatioClient> lazyClient, IReauthExecutor reauthExecutor,
		ILoginDiagnostics loginDiagnostics)
		: this(lazyClient, null, null, reauthExecutor,
			loginDiagnostics ?? throw new ArgumentNullException(nameof(loginDiagnostics))) { }

	// Credential-passthrough constructor: lets ApplicationClientFactory.CreateEnvironmentClient
	// wire BOTH a service-url builder (for environment-relative routes) and an explicit
	// reauth executor (the NoReauthExecutor, because bearer material cannot re-login).
	internal CreatioClientAdapter(Lazy<CreatioClient> lazyClient, IServiceUrlBuilder serviceUrlBuilder,
		IReauthExecutor reauthExecutor)
		: this(lazyClient, serviceUrlBuilder, null,
			reauthExecutor ?? throw new ArgumentNullException(nameof(reauthExecutor))) { }

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

	public string ExecutePatchRequest(string url, string requestData, int requestTimeout = Timeout.Infinite,
		int maxAttempts = 1, int delaySec = 1) {
		return ExecuteRequest(
			() => Client.ExecutePatchRequest(url, requestData, requestTimeout, maxAttempts, delaySec));
	}

	public void Listen(CancellationToken cancellationToken) {
		Client.ConnectionStateChanged += (sender, state) => { ConnectionStateChanged?.Invoke(sender, state); };

		Client.MessageReceived += (sender, message) => { MessageReceived?.Invoke(sender, message); };

		Client.StartListening(cancellationToken);
	}

	public void Login() {
		// Recorded the same way as the automatic re-login above so a rejected login is diagnosable
		// from CI output alone, and so the two attempt kinds are distinguishable (GitHub #1106).
		_loginDiagnostics.Track(() => Client.Login(), LoginAttemptKind.Initial);
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

	#endregion
}
