using System;
using System.Net.WebSockets;
using System.Threading;
using Clio.Common.Responses;
using Creatio.Client;
using Creatio.Client.Dto;

namespace Clio.Common;

public interface IApplicationClient
{
    public event EventHandler<WsMessage> MessageReceived;

    public event EventHandler<WebSocketState> ConnectionStateChanged;

    string CallConfigurationService(string serviceName, string serviceMethod, string requestData,
        int requestTimeout = 10000);

    void DownloadFile(string url, string filePath, string requestData);

    /// <summary>
    ///     Executes GET Request with retry.
    /// </summary>
    /// <param name="url">Request URL.</param>
    /// <param name="requestTimeout">Request Timeout.</param>
    /// <param name="retryCount">retry count.</param>
    /// <param name="delaySec">delay between retries in seconds.</param>
    /// <returns>Response.</returns>
    /// <exception cref="Exception">Throws when request fails after attempts exceed <paramref name="retryCount" /> count.</exception>
    string ExecuteGetRequest(string url, int requestTimeout = Timeout.Infinite, int retryCount = 1, int delaySec = 1);

    /// <summary>
    ///     Executes POST Request with retry.
    /// </summary>
    /// <param name="url">Request URL.</param>
    /// <param name="requestData">Request Data.</param>
    /// <param name="requestTimeout">Request Timeout.</param>
    /// <param name="retryCount">retry count.</param>
    /// <param name="delaySec">delay between retries in seconds.</param>
    /// <returns>Response.</returns>
    /// <exception cref="Exception">Throws when request fails after attempts exceed <paramref name="retryCount" /> count.</exception>
    string ExecutePostRequest(string url, string requestData, int requestTimeout = Timeout.Infinite, int retryCount = 1,
        int delaySec = 1);

    void Login();

    string UploadFile(string url, string filePath);

    string UploadAlmFile(string url, string filePath);

    string UploadAlmFileByChunk(string url, string filePath);

    void Listen(CancellationToken cancellationToken);

    T ExecutePostRequest<T>(string url, string requestData, int requestTimeout = Timeout.Infinite)
        where T : BaseResponse, new();
}

public class CreatioClientAdapter : IApplicationClient
{
    private readonly CreatioClient _creatioClient;
    private readonly IServiceUrlBuilder _serviceUrlBuilder;

    public CreatioClientAdapter(string appUrl, string userName, string userPassword, bool isNetCore = false,
        ServiceUrlBuilder serviceUrlBuilder = null)
    {
        _creatioClient = new CreatioClient(appUrl, userName, userPassword, true, isNetCore);
        _serviceUrlBuilder = serviceUrlBuilder;
    }

    public CreatioClientAdapter(string appUrl, string clientId, string clientSecret, string authAppUrl,
        bool isNetCore = false, ServiceUrlBuilder serviceUrlBuilder = null)
    {
        _creatioClient = CreatioClient.CreateOAuth20Client(appUrl, authAppUrl, clientId, clientSecret, isNetCore);
        _serviceUrlBuilder = serviceUrlBuilder;
    }

    public CreatioClientAdapter(CreatioClient creatioClient, ServiceUrlBuilder serviceUrlBuilder = null)
    {
        _creatioClient = creatioClient;
        _serviceUrlBuilder = serviceUrlBuilder;
    }

    public string CallConfigurationService(string serviceName, string serviceMethod, string requestData,
        int requestTimeout = Timeout.Infinite) =>
        _creatioClient.CallConfigurationService(serviceName, serviceMethod, requestData, requestTimeout);

    public void DownloadFile(string url, string filePath, string requestData)
    {
        string absoluteUrl = url;
        if (_serviceUrlBuilder != null)
        {
            absoluteUrl = _serviceUrlBuilder.Build(url);
        }

        _creatioClient.DownloadFile(absoluteUrl, filePath, requestData);
    }

    public string ExecuteGetRequest(string url, int requestTimeout = Timeout.Infinite, int retryCount = 1,
        int delaySec = 1) => _creatioClient.ExecuteGetRequest(url, requestTimeout, retryCount, delaySec);

    public string ExecutePostRequest(string url, string requestData, int requestTimeout = Timeout.Infinite,
        int retryCount = 1, int delaySec = 1) =>
        _creatioClient.ExecutePostRequest(url, requestData, requestTimeout, retryCount, delaySec);

    public void Login() => _creatioClient.Login();

    public string UploadFile(string url, string filePath) => _creatioClient.UploadFile(url, filePath);

    public string UploadAlmFile(string url, string filePath) => _creatioClient.UploadAlmFile(url, filePath);

    public void Listen(CancellationToken cancellationToken)
    {
        _creatioClient.ConnectionStateChanged += (sender, state) => { ConnectionStateChanged?.Invoke(sender, state); };

        _creatioClient.MessageReceived += (sender, message) => { MessageReceived?.Invoke(sender, message); };

        _creatioClient.StartListening(cancellationToken);
    }

    public event EventHandler<WsMessage> MessageReceived;

    public event EventHandler<WebSocketState> ConnectionStateChanged;

    /// <summary>
    ///     Performs post request and returns deserialized response.
    /// </summary>
    /// <param name="url">Request url.</param>
    /// <param name="requestData">Request data.</param>
    /// <param name="requestTimeout">Request timeout. Default: infinity period.</param>
    /// <typeparam name="T">Return value type.</typeparam>
    /// <returns>Response.<see cref="T" />.</returns>
    public T ExecutePostRequest<T>(string url, string requestData, int requestTimeout = Timeout.Infinite)
        where T : BaseResponse, new()
    {
        JsonConverter converter = new();
        string response = _creatioClient.ExecutePostRequest(url, requestData, requestTimeout);
        return converter.DeserializeObject<T>(response);
    }

    public string UploadAlmFileByChunk(string url, string filePath) =>
        _creatioClient.UploadAlmFileByChunk(url, filePath);

    internal T As<T>() => throw new NotImplementedException();
}
