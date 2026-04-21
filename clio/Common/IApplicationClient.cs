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

	private CreatioClient Client => _lazyClient.Value;

	#endregion

	#region Constructors: Private

	private CreatioClientAdapter(Lazy<CreatioClient> lazyClient, IServiceUrlBuilder serviceUrlBuilder = null, JsonConverter jsonConverter = null) {
		_lazyClient = lazyClient;
		_serviceUrlBuilder = serviceUrlBuilder;
		_jsonConverter = jsonConverter ?? new JsonConverter();
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

	public T ExecutePostRequest<T>(string url, string requestData, int requestTimeout = Timeout.Infinite,
		int retryCount = 1, int delaySec = 1)
		where T : BaseResponse, new() {
		string response = Client.ExecutePostRequest(url, requestData, requestTimeout, retryCount, delaySec);
		return _jsonConverter.DeserializeObject<T>(response);
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
