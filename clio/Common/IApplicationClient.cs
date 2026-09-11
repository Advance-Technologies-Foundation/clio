using System;
using System.Net.WebSockets;
using System.Threading;
using Clio.Common.Responses;
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

	string ExecutePostRequest(string url, string requestData, int requestTimeout = Timeout.Infinite,
		int maxAttempts = 1, int delaySec = 1);

	T ExecutePostRequest<T>(string url, string requestData, int requestTimeout = Timeout.Infinite,
		int maxAttempts = 1, int delaySec = 1)
		where T : BaseResponse, new();

	string ExecutePatchRequest(string url, string requestData, int requestTimeout = Timeout.Infinite,
		int maxAttempts = 1, int delaySec = 1);

	/// <summary>
	/// Executes an authenticated HTTP PUT request against the Creatio application.
	/// </summary>
	/// <param name="url">The absolute request URL.</param>
	/// <param name="requestData">The request body.</param>
	/// <param name="requestTimeout">The request timeout in milliseconds.</param>
	/// <param name="maxAttempts">The maximum number of attempts.</param>
	/// <param name="delaySec">The delay between retry attempts in seconds.</param>
	/// <returns>The raw response body.</returns>
	/// <remarks>
	/// Defaulted rather than abstract. This is a stable public contract with implementations outside
	/// this repository; an abstract member here is a source-breaking change that fails every one of
	/// them with CS0535, exactly as it failed <c>ApplicationClientLease</c> in-tree. A transport that
	/// does not speak PUT keeps compiling and says so at the call site instead.
	/// </remarks>
	string ExecutePutRequest(string url, string requestData, int requestTimeout = Timeout.Infinite,
		int maxAttempts = 1, int delaySec = 1) =>
		throw new NotSupportedException(
			$"{GetType().Name} does not implement HTTP PUT. Use a client that overrides "
			+ $"{nameof(ExecutePutRequest)}, or call the service with POST or PATCH.");

	void Listen(CancellationToken cancellationToken);
	void Login();

	string UploadAlmFile(string url, string filePath);

	string UploadAlmFileByChunk(string url, string filePath);
	string UploadFile(string url, string filePath);

	#endregion
}
