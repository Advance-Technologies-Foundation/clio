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
	private readonly object _reauthSyncRoot = new();

	private CreatioClient Client => _lazyClient.Value;

	#endregion

	#region Constructors: Private

	private CreatioClientAdapter(Lazy<CreatioClient> lazyClient, IServiceUrlBuilder serviceUrlBuilder, JsonConverter jsonConverter) {
		_lazyClient = lazyClient;
		_serviceUrlBuilder = serviceUrlBuilder;
		_jsonConverter = jsonConverter ?? new JsonConverter();
	}

	#endregion

	#region Constructors: Public

	public CreatioClientAdapter(string appUrl, string userName, string userPassword, bool isNetCore = false,
		IServiceUrlBuilder serviceUrlBuilder = null)
		: this(new Lazy<CreatioClient>(() => new CreatioClient(appUrl, userName, userPassword, true, isNetCore)),
			serviceUrlBuilder, null) { }

	public CreatioClientAdapter(string appUrl, string clientId, string clientSecret, string authAppUrl,
		bool isNetCore = false, IServiceUrlBuilder serviceUrlBuilder = null)
		: this(new Lazy<CreatioClient>(() =>
			CreatioClient.CreateOAuth20Client(appUrl, authAppUrl, clientId, clientSecret, isNetCore)),
			serviceUrlBuilder, null) { }

	public CreatioClientAdapter(CreatioClient creatioClient)
		: this(new Lazy<CreatioClient>(() => creatioClient), null, null) { }

	public CreatioClientAdapter(Lazy<CreatioClient> lazyClient)
		: this(lazyClient, null, null) { }

	#endregion

	public event EventHandler<WebSocketState> ConnectionStateChanged;

	public event EventHandler<WsMessage> MessageReceived;

	#region Methods: Protected

	#endregion

	#region Methods: Public

	/// <summary>
	/// Executes a Creatio service call and, when the response is a login redirect caused by an
	/// expired Forms-auth session, re-authenticates once via <see cref="CreatioClient.Login()"/>
	/// and retries the call exactly once. The re-login is serialized through
	/// <see cref="_reauthSyncRoot"/> because the adapter is a DI singleton shared across calls.
	/// </summary>
	/// <remarks>
	/// Scope: this recovers Forms-auth (cookie) sessions only. OAuth bearer expiry returns a 401
	/// (not a login-page redirect) and has no public token-refresh entry point on the client, so it
	/// is not covered here. Sessions held by <c>RemoteDataProvider</c> (ATF.Repository) are separate
	/// and are likewise not covered by this adapter.
	/// </remarks>
	private string ExecuteWithReauthRetry(Func<string> serviceCall) =>
		ExecuteWithReauthRetry(serviceCall, () => {
			lock (_reauthSyncRoot) {
				Client.Login();
			}
		});

	/// <summary>
	/// Core re-auth-and-retry logic, separated from the live client so it can be unit tested.
	/// Runs <paramref name="serviceCall"/>; if the response is a Creatio login redirect, invokes
	/// <paramref name="reauthenticate"/> once and runs the call again exactly once. The second
	/// result is returned as-is (no further detection) to guarantee termination. A login redirect
	/// proves the request never reached the service (Forms auth bounces it before the handler), so
	/// the retry is side-effect-safe even for non-idempotent writes; an
	/// <see cref="UnauthorizedAccessException"/> raised by <paramref name="reauthenticate"/> (wrong
	/// credentials, not expiry) propagates and is never retried.
	/// </summary>
	internal static string ExecuteWithReauthRetry(Func<string> serviceCall, Action reauthenticate) {
		string response = serviceCall();
		if (!CreatioAuthResponseGuard.IsLikelyAuthRedirect(response)) {
			return response;
		}
		reauthenticate();
		return serviceCall();
	}

	public string CallConfigurationService(string serviceName, string serviceMethod, string requestData,
		int requestTimeout = Timeout.Infinite) {
		return ExecuteWithReauthRetry(() =>
			Client.CallConfigurationService(serviceName, serviceMethod, requestData, requestTimeout));
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
		return ExecuteWithReauthRetry(() =>
			Client.ExecuteDeleteRequest(url, requestData, requestTimeout, retryCount, delaySec));
	}

	public string ExecuteGetRequest(string url, int requestTimeout = Timeout.Infinite, int retryCount = 1,
		int delaySec = 1) {
		return ExecuteWithReauthRetry(() =>
			Client.ExecuteGetRequest(url, requestTimeout, retryCount, delaySec));
	}

	public string ExecutePostRequest(string url, string requestData, int requestTimeout = Timeout.Infinite,
		int retryCount = 1, int delaySec = 1) {
		return ExecuteWithReauthRetry(() =>
			Client.ExecutePostRequest(url, requestData, requestTimeout, retryCount, delaySec));
	}

	public T ExecutePostRequest<T>(string url, string requestData, int requestTimeout = Timeout.Infinite,
		int retryCount = 1, int delaySec = 1)
		where T : BaseResponse, new() {
		// Re-auth on the raw string boundary so an expired session is recovered BEFORE
		// deserialization runs on the (now valid) response rather than throwing on login HTML.
		string response = ExecuteWithReauthRetry(() =>
			Client.ExecutePostRequest(url, requestData, requestTimeout, retryCount, delaySec));
		return _jsonConverter.DeserializeObject<T>(response);
	}

	public string ExecutePatchRequest(string url, string requestData, int requestTimeout = Timeout.Infinite,
		int retryCount = 1, int delaySec = 1) {
		return ExecuteWithReauthRetry(() =>
			Client.ExecutePatchRequest(url, requestData, requestTimeout, retryCount, delaySec));
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
