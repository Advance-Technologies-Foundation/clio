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

	/// <summary>
	///     Executes DELETE Request with retry
	/// </summary>
	/// <param name="url">Request URL</param>
	/// <param name="requestData">Request body (optional)</param>
	/// <param name="requestTimeout">Request Timeout</param>
	/// <param name="retryCount">retry count</param>
	/// <param name="delaySec">delay between retries in seconds</param>
	/// <returns>Response</returns>
	/// <exception cref="Exception">Throws when request fails after attempts made exceed <paramref name="retryCount" /> count</exception>
	string ExecuteDeleteRequest(string url, string requestData, int requestTimeout = Timeout.Infinite,
		int retryCount = 1, int delaySec = 1);

	/// <summary>
	///     Executes GET Request with retry
	/// </summary>
	/// <param name="url">Request URL</param>
	/// <param name="requestTimeout">Request Timeout</param>
	/// <param name="retryCount">retry count</param>
	/// <param name="delaySec">delay between retries in seconds</param>
	/// <returns>Response</returns>
	/// <exception cref="Exception">Throws when request fails after attempts made exceed <paramref name="retryCount" /> count</exception>
	string ExecuteGetRequest(string url, int requestTimeout = Timeout.Infinite, int retryCount = 1, int delaySec = 1);

	/// <summary>
	///     Executes POST Request with retry
	/// </summary>
	/// <param name="url">Request URL</param>
	/// <param name="requestData">Request Data</param>
	/// <param name="requestTimeout">Request Timeout</param>
	/// <param name="retryCount">retry count</param>
	/// <param name="delaySec">delay between retries in seconds</param>
	/// <returns>Response</returns>
	/// <exception cref="Exception">Throws when request fails after attempts made exceed <paramref name="retryCount" /> count</exception>
	string ExecutePostRequest(string url, string requestData, int requestTimeout = Timeout.Infinite, int retryCount = 1,
		int delaySec = 1);


	T ExecutePostRequest<T>(string url, string requestData, int requestTimeout = Timeout.Infinite)
		where T : BaseResponse, new();

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

	private CreatioClient Client => _lazyClient.Value;

	#endregion

	#region Constructors: Private

	private CreatioClientAdapter(Lazy<CreatioClient> lazyClient, IServiceUrlBuilder serviceUrlBuilder = null) {
		_lazyClient = lazyClient;
		_serviceUrlBuilder = serviceUrlBuilder;
	}

	#endregion

	#region Constructors: Public

	public CreatioClientAdapter(string appUrl, string userName, string userPassword, bool isNetCore = false,
		IServiceUrlBuilder serviceUrlBuilder = null)
		: this(new Lazy<CreatioClient>(() => new CreatioClient(appUrl, userName, userPassword, true, isNetCore)),
			serviceUrlBuilder) { }

	public CreatioClientAdapter(string appUrl, string clientId, string clientSecret, string authAppUrl,
		bool isNetCore = false, IServiceUrlBuilder serviceUrlBuilder = null)
		: this(new Lazy<CreatioClient>(() =>
			CreatioClient.CreateOAuth20Client(appUrl, authAppUrl, clientId, clientSecret, isNetCore)),
			serviceUrlBuilder) { }

	public CreatioClientAdapter(CreatioClient creatioClient)
		: this(new Lazy<CreatioClient>(() => creatioClient), null) { }

	public CreatioClientAdapter(Lazy<CreatioClient> lazyClient)
		: this(lazyClient, null) { }

	#endregion

	public event EventHandler<WebSocketState> ConnectionStateChanged;

	public event EventHandler<WsMessage> MessageReceived;

	#region Methods: Protected

	// internal T As<T>() {
	// 	throw new NotImplementedException();
	// }

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
		return Client.ExecuteDeleteRequest(url, requestData, requestTimeout, retryCount, delaySec);
	}

	public string ExecuteGetRequest(string url, int requestTimeout = Timeout.Infinite, int retryCount = 1,
		int delaySec = 1) {
		return Client.ExecuteGetRequest(url, requestTimeout, retryCount, delaySec);
	}

	public string ExecutePostRequest(string url, string requestData, int requestTimeout = Timeout.Infinite,
		int retryCount = 1, int delaySec = 1) {
		return Client.ExecutePostRequest(url, requestData, requestTimeout, retryCount, delaySec);
	}

	/// <summary>
	///     Performs post request and returns deserialized response.
	/// </summary>
	/// <param name="url">Request url.</param>
	/// <param name="requestData">Request data.</param>
	/// <param name="requestTimeout">Request timeout. Default: infinity period.</param>
	/// <typeparam name="T">Return value type.</typeparam>
	/// <returns>Response.<see cref="T" /></returns>
	public T ExecutePostRequest<T>(string url, string requestData, int requestTimeout = Timeout.Infinite)
		where T : BaseResponse, new() {
		JsonConverter converter = new();
		string response = Client.ExecutePostRequest(url, requestData, requestTimeout);
		return converter.DeserializeObject<T>(response);
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
