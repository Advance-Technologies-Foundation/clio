using System;
using System.Net.WebSockets;
using System.Threading;
using Clio.Common.Responses;
using Creatio.Client;
using Creatio.Client.Dto;

namespace Clio.Common;

public interface IApplicationClient{
	public event EventHandler<WebSocketState> ConnectionStateChanged;

	public event EventHandler<WsMessage> MessageReceived;

	#region Methods: Public

	string CallConfigurationService(string serviceName, string serviceMethod, string requestData,
		int requestTimeout = 10000);

	void DownloadFile(string url, string filePath, string requestData);

	string ExecuteDeleteRequest(string url, string requestData, int requestTimeout = Timeout.Infinite,
		int retryCount = 1, int delaySec = 1);

	string ExecuteGetRequest(string url, int requestTimeout = Timeout.Infinite, int retryCount = 1, int delaySec = 1);

	string ExecutePostRequest(string url, string requestData, int requestTimeout = Timeout.Infinite,
		int retryCount = 1, int delaySec = 1);

	T ExecutePostRequest<T>(string url, string requestData, int requestTimeout = Timeout.Infinite,
		int retryCount = 1, int delaySec = 1)
		where T : BaseResponse, new();

	string ExecutePatchRequest(string url, string requestData, int requestTimeout = Timeout.Infinite,
		int retryCount = 1, int delaySec = 1);

	void Listen(CancellationToken cancellationToken);
	void Login();
	string UploadAlmFile(string url, string filePath);

	string UploadAlmFileByChunk(string url, string filePath);
	string UploadFile(string url, string filePath);

	#endregion
}

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
		JsonConverter jsonConverter, ILogger logger, IReauthExecutor reauthExecutor) {
		_lazyClient = lazyClient;
		_serviceUrlBuilder = serviceUrlBuilder;
		_jsonConverter = jsonConverter ?? new JsonConverter();
		// Recover transparently from server-side session expiration: a singleton CreatioClient
		// shared across a long-lived MCP process can otherwise keep sending a stale cookie
		// after long-running operations and start receiving the HTML login page instead of
		// JSON for every subsequent request.
		_reauthExecutor = reauthExecutor ?? new ReauthExecutor(() => _lazyClient.Value.Login(), logger);
	}

	#endregion

	#region Constructors: Public

	public CreatioClientAdapter(string appUrl, string userName, string userPassword, bool isNetCore = false,
		IServiceUrlBuilder serviceUrlBuilder = null)
		: this(new Lazy<CreatioClient>(() => new CreatioClient(appUrl, userName, userPassword, true, isNetCore)),
			serviceUrlBuilder, null, null, null) { }

	public CreatioClientAdapter(string appUrl, string clientId, string clientSecret, string authAppUrl,
		bool isNetCore = false, IServiceUrlBuilder serviceUrlBuilder = null)
		: this(new Lazy<CreatioClient>(() =>
			CreatioClient.CreateOAuth20Client(appUrl, authAppUrl, clientId, clientSecret, isNetCore)),
			serviceUrlBuilder, null, null, null) { }

	public CreatioClientAdapter(CreatioClient creatioClient)
		: this(new Lazy<CreatioClient>(() => creatioClient), null, null, null, null) { }

	public CreatioClientAdapter(Lazy<CreatioClient> lazyClient)
		: this(lazyClient, null, null, null, null) { }

	public CreatioClientAdapter(Lazy<CreatioClient> lazyClient, ILogger logger)
		: this(lazyClient, null, null, logger, null) { }

	#endregion

	#region Constructors: Internal

	// Test-only constructor. Allows substituting the reauth executor without instantiating
	// a real CreatioClient (the NuGet type is not mockable). The lazyClient may resolve to
	// null in tests because the substituted executor never invokes the wrapped callback.
	// reauthExecutor is required so tests cannot silently fall back to the default executor.
	internal CreatioClientAdapter(Lazy<CreatioClient> lazyClient, IReauthExecutor reauthExecutor)
		: this(lazyClient, null, null, null,
			reauthExecutor ?? throw new ArgumentNullException(nameof(reauthExecutor))) { }

	#endregion

	public event EventHandler<WebSocketState> ConnectionStateChanged;

	public event EventHandler<WsMessage> MessageReceived;

	#region Methods: Protected

	#endregion

	#region Methods: Public

	public string CallConfigurationService(string serviceName, string serviceMethod, string requestData,
		int requestTimeout = Timeout.Infinite) {
		return Client.CallConfigurationService(serviceName, serviceMethod, requestData, requestTimeout);
	}

	public void DownloadFile(string url, string filePath, string requestData) {
		string absoluteUrl = url;
		if (_serviceUrlBuilder != null) {
			absoluteUrl = _serviceUrlBuilder.Build(url);
		}

		Client.DownloadFile(absoluteUrl, filePath, requestData);
	}

	public string ExecuteDeleteRequest(string url, string requestData, int requestTimeout = Timeout.Infinite,
		int retryCount = 1, int delaySec = 1) {
		return _reauthExecutor.Execute(
			() => Client.ExecuteDeleteRequest(url, requestData, requestTimeout, retryCount, delaySec),
			ReauthExecutor.IsSessionExpiredResponse);
	}

	public string ExecuteGetRequest(string url, int requestTimeout = Timeout.Infinite, int retryCount = 1,
		int delaySec = 1) {
		return _reauthExecutor.Execute(
			() => Client.ExecuteGetRequest(url, requestTimeout, retryCount, delaySec),
			ReauthExecutor.IsSessionExpiredResponse);
	}

	public string ExecutePostRequest(string url, string requestData, int requestTimeout = Timeout.Infinite,
		int retryCount = 1, int delaySec = 1) {
		return _reauthExecutor.Execute(
			() => Client.ExecutePostRequest(url, requestData, requestTimeout, retryCount, delaySec),
			ReauthExecutor.IsSessionExpiredResponse);
	}

	public T ExecutePostRequest<T>(string url, string requestData, int requestTimeout = Timeout.Infinite,
		int retryCount = 1, int delaySec = 1)
		where T : BaseResponse, new() {
		// Re-auth detection runs against the raw body so an expired session cannot reach
		// the JSON deserializer (which would throw on the HTML login page).
		string response = _reauthExecutor.Execute(
			() => Client.ExecutePostRequest(url, requestData, requestTimeout, retryCount, delaySec),
			ReauthExecutor.IsSessionExpiredResponse);
		return _jsonConverter.DeserializeObject<T>(response);
	}

	public string ExecutePatchRequest(string url, string requestData, int requestTimeout = Timeout.Infinite,
		int retryCount = 1, int delaySec = 1) {
		return _reauthExecutor.Execute(
			() => Client.ExecutePatchRequest(url, requestData, requestTimeout, retryCount, delaySec),
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
		return Client.UploadAlmFile(url, filePath);
	}

	public string UploadAlmFileByChunk(string url, string filePath) {
		return Client.UploadAlmFileByChunk(url, filePath);
	}

	public string UploadFile(string url, string filePath) {
		return Client.UploadFile(url, filePath);
	}

	#endregion
}
