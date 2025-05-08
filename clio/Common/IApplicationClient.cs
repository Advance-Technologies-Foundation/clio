using System;
using System.Net.WebSockets;
using System.Threading;
using Clio.Common.Responses;
using Creatio.Client;
using Creatio.Client.Dto;

namespace Clio.Common;

public interface IApplicationClient
{

    #region Events: Public

    public event EventHandler<WsMessage> MessageReceived;

    public event EventHandler<WebSocketState> ConnectionStateChanged;

    #endregion

    #region Methods: Public

    string CallConfigurationService(string serviceName, string serviceMethod, string requestData,
        int requestTimeout = 10000);

    void DownloadFile(string url, string filePath, string requestData);

    /// <summary>
    ///     Executes GET Request with retry
    /// </summary>
    /// <param name="url">Request URL</param>
    /// <param name="requestTimeout">Request Timeout</param>
    /// <param name="retryCount">retry count</param>
    /// <param name="delaySec">delay between retries in seconds</param>
    /// <returns>Response</returns>
    /// <exception cref="Exception">Throws when request fails after attempts exceed <paramref name="retryCount" /> count</exception>
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
    /// <exception cref="Exception">Throws when request fails after attempts exceed <paramref name="retryCount" /> count</exception>
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

public class CreatioClientAdapter : IApplicationClient
{

    #region Fields: Private

    private readonly CreatioClient _creatioClient;
    private readonly IServiceUrlBuilder _serviceUrlBuilder;

    #endregion

    #region Constructors: Public

    public CreatioClientAdapter(string appUrl, string userName, string userPassword, bool isNetCore = false,
        ServiceUrlBuilder serviceUrlBuilder = null)
    {
        _creatioClient = new CreatioClient(appUrl, userName, userPassword, true, isNetCore);
        _serviceUrlBuilder = serviceUrlBuilder;
    }

    public CreatioClientAdapter(string appUrl, string clientId, string clientSecret, string AuthAppUrl,
        bool isNetCore = false, ServiceUrlBuilder serviceUrlBuilder = null)
    {
        _creatioClient = CreatioClient.CreateOAuth20Client(appUrl, AuthAppUrl, clientId, clientSecret, isNetCore);
        _serviceUrlBuilder = serviceUrlBuilder;
    }

    public CreatioClientAdapter(CreatioClient creatioClient, ServiceUrlBuilder serviceUrlBuilder = null)
    {
        _creatioClient = creatioClient;
        _serviceUrlBuilder = serviceUrlBuilder;
    }

    #endregion

    #region Events: Public

    public event EventHandler<WsMessage> MessageReceived;

    public event EventHandler<WebSocketState> ConnectionStateChanged;

    #endregion

    #region Methods: Internal

    internal T As<T>()
    {
        throw new NotImplementedException();
    }

    #endregion

    #region Methods: Public

    public string CallConfigurationService(string serviceName, string serviceMethod, string requestData,
        int requestTimeout = Timeout.Infinite)
    {
        return _creatioClient.CallConfigurationService(serviceName, serviceMethod, requestData, requestTimeout);
    }

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
        int delaySec = 1)
    {
        return _creatioClient.ExecuteGetRequest(url, requestTimeout, retryCount, delaySec);
    }

    public string ExecutePostRequest(string url, string requestData, int requestTimeout = Timeout.Infinite,
        int retryCount = 1, int delaySec = 1)
    {
        return _creatioClient.ExecutePostRequest(url, requestData, requestTimeout, retryCount, delaySec);
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
        where T : BaseResponse, new()
    {
        JsonConverter converter = new();
        string response = _creatioClient.ExecutePostRequest(url, requestData, requestTimeout);
        return converter.DeserializeObject<T>(response);
    }

    public void Listen(CancellationToken cancellationToken)
    {
        _creatioClient.ConnectionStateChanged += (sender, state) => { ConnectionStateChanged?.Invoke(sender, state); };

        _creatioClient.MessageReceived += (sender, message) => { MessageReceived?.Invoke(sender, message); };

        _creatioClient.StartListening(cancellationToken);
    }

    public void Login()
    {
        _creatioClient.Login();
    }

    public string UploadAlmFile(string url, string filePath)
    {
        return _creatioClient.UploadAlmFile(url, filePath);
    }

    public string UploadAlmFileByChunk(string url, string filePath)
    {
        return _creatioClient.UploadAlmFileByChunk(url, filePath);
    }

    public string UploadFile(string url, string filePath)
    {
        return _creatioClient.UploadFile(url, filePath);
    }

    #endregion

}
