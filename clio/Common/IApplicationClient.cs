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

	/// <summary>
	/// Executes an authenticated HTTP POST that the caller has declared to be a write, and that
	/// automatic re-authentication must therefore never re-issue.
	/// </summary>
	/// <param name="url">The absolute request URL.</param>
	/// <param name="requestData">The request body.</param>
	/// <param name="requestTimeout">The request timeout in milliseconds.</param>
	/// <param name="maxAttempts">The maximum number of attempts.</param>
	/// <param name="delaySec">The delay between retry attempts in seconds.</param>
	/// <returns>The raw response body.</returns>
	/// <remarks>
	/// POST is the only verb clio uses for both reads (DataService <c>SelectQuery</c>, long-running
	/// configuration-service calls) and writes (record creation, arbitrary <c>call-service</c>
	/// payloads), so the read/write decision cannot be made from the verb and has to be made by the
	/// caller. A client whose expired-session recovery replayed the call would otherwise commit a
	/// write twice whenever its legitimate response happened to contain a login-page marker
	/// (GitHub #1313).
	/// Defaulted rather than abstract for the same reason as <see cref="ExecutePutRequest"/>: this is
	/// a stable public contract with implementations outside this repository, and an abstract member
	/// here fails every one of them with CS0535. A transport with no replay behaviour of its own is
	/// already correct with the default body.
	/// </remarks>
	string ExecuteNonReplayablePostRequest(string url, string requestData,
		int requestTimeout = Timeout.Infinite, int maxAttempts = 1, int delaySec = 1) =>
		ExecutePostRequest(url, requestData, requestTimeout, maxAttempts, delaySec);

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
