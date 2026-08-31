using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using Clio.Common.Responses;
using Creatio.Client;
using Creatio.Client.Dto;

namespace Clio.Common;

/// <summary>
/// Adapts clients returned by legacy factory implementations to the ownership-aware internal contract.
/// </summary>
internal sealed class ApplicationClientLease(IApplicationClient client) : IOwnedApplicationClient {
	private readonly IApplicationClient _client = client ?? throw new ArgumentNullException(nameof(client));
	private ICreatioApplicationClient Extended => _client as ICreatioApplicationClient
		?? throw new NotSupportedException("The configured client does not expose extended Creatio transport operations.");

	public event EventHandler<WebSocketState> ConnectionStateChanged {
		add => _client.ConnectionStateChanged += value;
		remove => _client.ConnectionStateChanged -= value;
	}

	public event EventHandler<WsMessage> MessageReceived {
		add => _client.MessageReceived += value;
		remove => _client.MessageReceived -= value;
	}

	public string CallConfigurationService(string serviceName, string serviceMethod, string requestData,
		int requestTimeout = 10000) =>
		_client.CallConfigurationService(serviceName, serviceMethod, requestData, requestTimeout);

	public void DownloadFile(string url, string filePath, string requestData) =>
		_client.DownloadFile(url, filePath, requestData);

	public string ExecuteDeleteRequest(string url, string requestData, int requestTimeout = Timeout.Infinite,
		int maxAttempts = 1, int delaySec = 1) =>
		_client.ExecuteDeleteRequest(url, requestData, requestTimeout, maxAttempts, delaySec);

	public string ExecuteGetRequest(string url, int requestTimeout = Timeout.Infinite, int maxAttempts = 1,
		int delaySec = 1) => _client.ExecuteGetRequest(url, requestTimeout, maxAttempts, delaySec);

	public Task<HttpResponseMessage> ExecuteGetRequestAsync(string url, int requestTimeout = 100_000,
		int maxAttempts = 1, int delaySec = 1, CancellationToken cancellationToken = default) =>
		Extended.ExecuteGetRequestAsync(url, requestTimeout, maxAttempts, delaySec, cancellationToken);

	public string ExecutePostRequest(string url, string requestData, int requestTimeout = Timeout.Infinite,
		int maxAttempts = 1, int delaySec = 1) =>
		_client.ExecutePostRequest(url, requestData, requestTimeout, maxAttempts, delaySec);

	public Task<HttpResponseMessage> ExecutePostRequestAsync(string url, string requestData,
		int requestTimeout = 100_000, int maxAttempts = 1, int delaySec = 1,
		CancellationToken cancellationToken = default) =>
		Extended.ExecutePostRequestAsync(url, requestData, requestTimeout, maxAttempts, delaySec, cancellationToken);

	public T ExecutePostRequest<T>(string url, string requestData, int requestTimeout = Timeout.Infinite,
		int maxAttempts = 1, int delaySec = 1) where T : BaseResponse, new() =>
		_client.ExecutePostRequest<T>(url, requestData, requestTimeout, maxAttempts, delaySec);

	public string ExecutePatchRequest(string url, string requestData, int requestTimeout = Timeout.Infinite,
		int maxAttempts = 1, int delaySec = 1) =>
		_client.ExecutePatchRequest(url, requestData, requestTimeout, maxAttempts, delaySec);

	public void Listen(CancellationToken cancellationToken) => _client.Listen(cancellationToken);
	public void Login() => _client.Login();
	public Task<HttpResponseMessage> LoginAsync(int requestTimeout = 100_000,
		CancellationToken cancellationToken = default) => Extended.LoginAsync(requestTimeout, cancellationToken);
	public IReadOnlyList<CreatioSessionCookie> ExportSessionCookies() => Extended.ExportSessionCookies();
	public void ImportSessionCookies(IEnumerable<CreatioSessionCookie> cookies) => Extended.ImportSessionCookies(cookies);
	public string UploadAlmFile(string url, string filePath) => _client.UploadAlmFile(url, filePath);
	public string UploadAlmFileByChunk(string url, string filePath) => _client.UploadAlmFileByChunk(url, filePath);
	public string UploadFile(string url, string filePath) => _client.UploadFile(url, filePath);
	public Task<HttpResponseMessage> UploadImageAsync(string url, byte[] data, string fileName, string mimeType,
		int requestTimeout = 100_000, CancellationToken cancellationToken = default) =>
		Extended.UploadImageAsync(url, data, fileName, mimeType, requestTimeout, cancellationToken);

	public void Dispose() => (_client as IDisposable)?.Dispose();
}
