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

	private CreatioClient Client => _lazyClient.Value;

	#endregion

	#region Constructors: Private

	// The reauthExecutor parameter is null in production: the executor captures a closure
	// over this adapter's own _lazyClient.Value.Login() and is therefore created here
	// rather than resolved from DI. Tests pass a non-null executor through the internal
	// constructor below to exercise the adapter in isolation from CreatioClient.
	private CreatioClientAdapter(Lazy<CreatioClient> lazyClient, IServiceUrlBuilder serviceUrlBuilder,
		JsonConverter jsonConverter, IReauthExecutor reauthExecutor) {
		_lazyClient = lazyClient;
		_serviceUrlBuilder = serviceUrlBuilder;
		_jsonConverter = jsonConverter ?? new JsonConverter();
		// Recover transparently from server-side session expiration: a singleton CreatioClient
		// shared across a long-lived MCP process can otherwise keep sending a stale cookie
		// after long-running operations and start receiving the HTML login page instead of
		// JSON for every subsequent request.
		_reauthExecutor = reauthExecutor ?? new ReauthExecutor(() => _lazyClient.Value.Login());
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
		return _reauthExecutor.Execute(
			() => Client.CallConfigurationService(serviceName, serviceMethod, requestData, requestTimeout),
			ReauthExecutor.IsSessionExpiredResponse);
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
		Client.DownloadFile(absoluteUrl, filePath, requestData);
	}

	public string ExecuteDeleteRequest(string url, string requestData, int requestTimeout = Timeout.Infinite,
		int maxAttempts = 1, int delaySec = 1) {
		return _reauthExecutor.Execute(
			() => Client.ExecuteDeleteRequest(url, requestData, requestTimeout, maxAttempts, delaySec),
			ReauthExecutor.IsSessionExpiredResponse);
	}

	public string ExecuteGetRequest(string url, int requestTimeout = Timeout.Infinite, int maxAttempts = 1,
		int delaySec = 1) {
		return _reauthExecutor.Execute(
			() => Client.ExecuteGetRequest(url, requestTimeout, maxAttempts, delaySec),
			ReauthExecutor.IsSessionExpiredResponse);
	}

	public string ExecutePostRequest(string url, string requestData, int requestTimeout = Timeout.Infinite,
		int maxAttempts = 1, int delaySec = 1) {
		return _reauthExecutor.Execute(
			() => Client.ExecutePostRequest(url, requestData, requestTimeout, maxAttempts, delaySec),
			ReauthExecutor.IsSessionExpiredResponse);
	}

	public T ExecutePostRequest<T>(string url, string requestData, int requestTimeout = Timeout.Infinite,
		int maxAttempts = 1, int delaySec = 1)
		where T : BaseResponse, new() {
		// Re-auth detection runs against the raw body so an expired session cannot reach
		// the JSON deserializer (which would throw on the HTML login page).
		string response = _reauthExecutor.Execute(
			() => Client.ExecutePostRequest(url, requestData, requestTimeout, maxAttempts, delaySec),
			ReauthExecutor.IsSessionExpiredResponse);
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
		return _reauthExecutor.Execute(
			() => Client.ExecutePatchRequest(url, requestData, requestTimeout, maxAttempts, delaySec),
			ReauthExecutor.IsSessionExpiredResponse);
	}

	public void Listen(CancellationToken cancellationToken) {
		Client.ConnectionStateChanged += (sender, state) => { ConnectionStateChanged?.Invoke(sender, state); };

		Client.MessageReceived += (sender, message) => { MessageReceived?.Invoke(sender, message); };

		Client.StartListening(cancellationToken);
	}

	public void Login() {
		Client.Login();
	}

	public string UploadAlmFile(string url, string filePath) {
		return _reauthExecutor.Execute(
			() => Client.UploadAlmFile(url, filePath),
			ReauthExecutor.IsSessionExpiredResponse);
	}

	public string UploadAlmFileByChunk(string url, string filePath) {
		return _reauthExecutor.Execute(
			() => Client.UploadAlmFileByChunk(url, filePath),
			ReauthExecutor.IsSessionExpiredResponse);
	}

	public string UploadFile(string url, string filePath) {
		return _reauthExecutor.Execute(
			() => Client.UploadFile(url, filePath),
			ReauthExecutor.IsSessionExpiredResponse);
	}

	#endregion
}
