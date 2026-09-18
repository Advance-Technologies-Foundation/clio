using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using Creatio.Client;
using Creatio.Client.Dto;

namespace Clio.Common;

/// <summary>
/// The transport surface <see cref="CreatioClientAdapter"/> consumes from the
/// <see cref="Creatio.Client.CreatioClient"/> NuGet type, expressed as an interface.
/// </summary>
/// <remarks>
/// <para>
/// Exists purely as a test seam. <c>CreatioClient</c> is a concrete NuGet class with no interface and
/// no virtual members, so the adapter's own unit tests could not substitute it: they resolved the
/// adapter's <see cref="Lazy{T}"/> to <see langword="null"/> and therefore could never invoke the
/// callback handed to <see cref="IReauthExecutor"/>. That left the delegation itself unverified — a
/// PUT routed to the underlying DELETE, or a dropped url/body/timeout argument, passed every test
/// (GitHub #1313).
/// </para>
/// <para>
/// The members mirror the underlying client one-for-one; this interface deliberately adds no
/// behaviour of its own. <see cref="IsCreated"/> and <see cref="IDisposable"/> expose the lazy
/// client's lifetime so the adapter can keep its ownership rules unchanged.
/// </para>
/// </remarks>
internal interface ICreatioClientTransport : IDisposable {

	/// <summary>Raised when the SignalR listener connection state changes.</summary>
	event EventHandler<WebSocketState> ConnectionStateChanged;

	/// <summary>Raised when the SignalR listener receives a message.</summary>
	event EventHandler<WsMessage> MessageReceived;

	/// <summary>Whether the underlying client has been created already.</summary>
	bool IsCreated { get; }

	/// <summary>Creates the underlying client now, if it does not exist yet.</summary>
	/// <remarks>
	/// Lets <see cref="CreatioClientAdapter"/> keep creation and disposal mutually exclusive under its
	/// own lifetime lock, as they were when it held the <see cref="Lazy{T}"/> itself. Without it a
	/// caller that had already passed the disposed check could create a client after <c>Dispose</c>
	/// observed <see cref="IsCreated"/> as <see langword="false"/>, and that client - with its pooled
	/// HTTP transport - would never be released.
	/// </remarks>
	void EnsureCreated();

	/// <inheritdoc cref="IApplicationClient.CallConfigurationService"/>
	string CallConfigurationService(string serviceName, string serviceMethod, string requestData,
		int requestTimeout);

	/// <inheritdoc cref="IApplicationClient.DownloadFile"/>
	void DownloadFile(string url, string filePath, string requestData);

	/// <inheritdoc cref="IApplicationClient.ExecuteDeleteRequest"/>
	string ExecuteDeleteRequest(string url, string requestData, int requestTimeout, int maxAttempts, int delaySec);

	/// <inheritdoc cref="IApplicationClient.ExecuteGetRequest"/>
	string ExecuteGetRequest(string url, int requestTimeout, int maxAttempts, int delaySec);

	/// <inheritdoc cref="ICreatioApplicationClient.ExecuteGetRequestAsync"/>
	Task<HttpResponseMessage> ExecuteGetRequestAsync(string url, int requestTimeout, int maxAttempts,
		int delaySec, CancellationToken cancellationToken);

	/// <inheritdoc cref="IApplicationClient.ExecutePatchRequest"/>
	string ExecutePatchRequest(string url, string requestData, int requestTimeout, int maxAttempts, int delaySec);

	/// <inheritdoc cref="IApplicationClient.ExecutePostRequest(string,string,int,int,int)"/>
	string ExecutePostRequest(string url, string requestData, int requestTimeout, int maxAttempts, int delaySec);

	/// <inheritdoc cref="ICreatioApplicationClient.ExecutePostRequestAsync"/>
	Task<HttpResponseMessage> ExecutePostRequestAsync(string url, string requestData, int requestTimeout,
		int maxAttempts, int delaySec, CancellationToken cancellationToken);

	/// <inheritdoc cref="IApplicationClient.ExecutePutRequest"/>
	string ExecutePutRequest(string url, string requestData, int requestTimeout, int maxAttempts, int delaySec);

	/// <inheritdoc cref="ICreatioApplicationClient.ExportSessionCookies"/>
	IReadOnlyList<CreatioSessionCookie> ExportSessionCookies();

	/// <inheritdoc cref="ICreatioApplicationClient.ImportSessionCookies"/>
	void ImportSessionCookies(IEnumerable<CreatioSessionCookie> cookies);

	/// <inheritdoc cref="IApplicationClient.Login"/>
	void Login();

	/// <inheritdoc cref="ICreatioApplicationClient.LoginAsync"/>
	Task<HttpResponseMessage> LoginAsync(int requestTimeout, CancellationToken cancellationToken);

	/// <summary>Starts the SignalR listener for the configured application.</summary>
	/// <param name="cancellationToken">Token that stops the listener.</param>
	void StartListening(CancellationToken cancellationToken);

	/// <inheritdoc cref="IApplicationClient.UploadAlmFile"/>
	string UploadAlmFile(string url, string filePath);

	/// <inheritdoc cref="IApplicationClient.UploadAlmFileByChunk"/>
	string UploadAlmFileByChunk(string url, string filePath);

	/// <inheritdoc cref="IApplicationClient.UploadFile"/>
	string UploadFile(string url, string filePath);

	/// <inheritdoc cref="ICreatioApplicationClient.UploadImageAsync"/>
	Task<HttpResponseMessage> UploadImageAsync(string url, byte[] data, string fileName, string mimeType,
		int requestTimeout, CancellationToken cancellationToken);
}
