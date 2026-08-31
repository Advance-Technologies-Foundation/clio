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

public interface IApplicationClient {
	public event EventHandler<WebSocketState> ConnectionStateChanged;

	public event EventHandler<WsMessage> MessageReceived;

	#region Methods: Public

	string CallConfigurationService(string serviceName, string serviceMethod, string requestData,
		int requestTimeout = 10000);

	void DownloadFile(string url, string filePath, string requestData);

	string ExecuteDeleteRequest(string url, string requestData, int requestTimeout = Timeout.Infinite,
		int maxAttempts = 1, int delaySec = 1);

	string ExecuteGetRequest(string url, int requestTimeout = Timeout.Infinite, int maxAttempts = 1, int delaySec = 1);

	/// <summary>
	/// Executes a cancellation-aware GET and transfers ownership of the complete response to the caller.
	/// </summary>
	Task<HttpResponseMessage> ExecuteGetRequestAsync(string url, int requestTimeout = 100_000,
		int maxAttempts = 1, int delaySec = 1, CancellationToken cancellationToken = default);

	string ExecutePostRequest(string url, string requestData, int requestTimeout = Timeout.Infinite,
		int maxAttempts = 1, int delaySec = 1);

	/// <summary>
	/// Executes a cancellation-aware POST and transfers ownership of the complete response to the caller.
	/// </summary>
	Task<HttpResponseMessage> ExecutePostRequestAsync(string url, string requestData,
		int requestTimeout = 100_000, int maxAttempts = 1, int delaySec = 1,
		CancellationToken cancellationToken = default);

	T ExecutePostRequest<T>(string url, string requestData, int requestTimeout = Timeout.Infinite,
		int maxAttempts = 1, int delaySec = 1)
		where T : BaseResponse, new();

	string ExecutePatchRequest(string url, string requestData, int requestTimeout = Timeout.Infinite,
		int maxAttempts = 1, int delaySec = 1);

	void Listen(CancellationToken cancellationToken);
	void Login();

	/// <summary>
	/// Logs in with cancellation support and transfers ownership of the authentication response to the caller.
	/// </summary>
	Task<HttpResponseMessage> LoginAsync(int requestTimeout = 100_000,
		CancellationToken cancellationToken = default);

	/// <summary>Returns detached copies of the current Creatio session cookies.</summary>
	IReadOnlyList<CreatioSessionCookie> ExportSessionCookies();

	/// <summary>Imports cookies for the configured Creatio application.</summary>
	void ImportSessionCookies(IEnumerable<CreatioSessionCookie> cookies);
	string UploadAlmFile(string url, string filePath);

	string UploadAlmFileByChunk(string url, string filePath);
	string UploadFile(string url, string filePath);

	/// <summary>
	/// Uploads one image payload through CreatioClient and transfers ownership of the Image API response
	/// to the caller.
	/// </summary>
	Task<HttpResponseMessage> UploadImageAsync(string url, byte[] data, string fileName, string mimeType,
		int requestTimeout = 100_000, CancellationToken cancellationToken = default);

	#endregion
}

/// <summary>An application client whose creator transfers ownership to the caller.</summary>
/// <remarks>Dispose the client after the operation completes.</remarks>
public interface IOwnedApplicationClient : IApplicationClient, IDisposable { }
