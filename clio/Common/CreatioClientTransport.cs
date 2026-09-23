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
/// Production <see cref="ICreatioClientTransport"/>: forwards every member to the lazily created
/// <see cref="CreatioClient"/> and adds nothing else.
/// </summary>
/// <remarks>
/// The authoritative documentation lives on <see cref="ICreatioClientTransport"/>. The client is kept
/// lazy because several construction sites build an adapter for an environment that is never actually
/// contacted; resolving the client eagerly would open a connection (and fail on a bad URL) at
/// construction time. Nothing here forwards through the reauth executor or the login diagnostics -
/// that wrapping belongs to <see cref="CreatioClientAdapter"/>, and this type adds nothing to the
/// call it forwards.
/// </remarks>
internal sealed class CreatioClientTransport : ICreatioClientTransport {

	#region Fields: Private

	private readonly Func<CreatioClient> _clientFactory;
	private readonly List<CreatioClient> _retiredClients = [];
	private readonly object _renewSync = new();
	private Lazy<CreatioClient> _lazyClient;

	#endregion

	#region Constructors: Public

	/// <summary>Creates a transport over the supplied lazy client.</summary>
	/// <param name="lazyClient">The lazily created Creatio client. Required.</param>
	public CreatioClientTransport(Lazy<CreatioClient> lazyClient) {
		_lazyClient = lazyClient ?? throw new ArgumentNullException(nameof(lazyClient));
	}

	/// <summary>Creates a renewable transport whose client is built by <paramref name="clientFactory"/>.</summary>
	/// <param name="clientFactory">Builds the client on first use and again after every <see cref="Renew"/>. Required.</param>
	public CreatioClientTransport(Func<CreatioClient> clientFactory) {
		_clientFactory = clientFactory ?? throw new ArgumentNullException(nameof(clientFactory));
		_lazyClient = new Lazy<CreatioClient>(clientFactory);
	}

	#endregion

	#region Properties: Private

	private CreatioClient Client => Volatile.Read(ref _lazyClient).Value;

	#endregion

	#region Properties: Public

	/// <inheritdoc />
	public bool IsCreated {
		get {
			lock (_renewSync) {
				return _lazyClient.IsValueCreated || _retiredClients.Count > 0;
			}
		}
	}

	#endregion

	#region Events: Public

	/// <inheritdoc />
	public event EventHandler<WebSocketState> ConnectionStateChanged {
		add => Client.ConnectionStateChanged += value;
		remove => Client.ConnectionStateChanged -= value;
	}

	/// <inheritdoc />
	public event EventHandler<WsMessage> MessageReceived {
		add => Client.MessageReceived += value;
		remove => Client.MessageReceived -= value;
	}

	#endregion

	#region Methods: Public

	/// <inheritdoc />
	public string CallConfigurationService(string serviceName, string serviceMethod, string requestData,
		int requestTimeout) =>
		Client.CallConfigurationService(serviceName, serviceMethod, requestData, requestTimeout);

	/// <inheritdoc />
	public void Dispose() {
		lock (_renewSync) {
			// Only a created client owns unmanaged transport state; resolving the Lazy here just to
			// dispose it would open the very connection the caller decided never to use.
			if (_lazyClient.IsValueCreated) {
				_lazyClient.Value?.Dispose();
			}
			foreach (CreatioClient retired in _retiredClients) {
				retired?.Dispose();
			}
			_retiredClients.Clear();
		}
	}

	/// <summary>
	/// Makes the next call build a new client through the factory this transport was created with. Used
	/// when the credential the current client carries (an OAuth access token) has been replaced.
	/// </summary>
	/// <remarks>
	/// The replaced client is kept, not disposed, until the transport itself is disposed: a request that
	/// started on it may still be running on another thread, and disposing its HTTP transport would fail
	/// that request. A SignalR listener started on it keeps running on it for the same reason.
	/// </remarks>
	/// <exception cref="InvalidOperationException">The transport was created over a fixed lazy client.</exception>
	public void Renew() {
		if (_clientFactory is null) {
			throw new InvalidOperationException("This transport was created over a fixed client and cannot be renewed.");
		}
		lock (_renewSync) {
			Lazy<CreatioClient> replaced = _lazyClient;
			if (!replaced.IsValueCreated) {
				return;
			}
			_retiredClients.Add(replaced.Value);
			Volatile.Write(ref _lazyClient, new Lazy<CreatioClient>(_clientFactory));
		}
	}

	/// <inheritdoc />
	public void EnsureCreated() {
		_ = Client;
	}

	/// <inheritdoc />
	public void DownloadFile(string url, string filePath, string requestData) =>
		Client.DownloadFile(url, filePath, requestData);

	/// <inheritdoc />
	public string ExecuteDeleteRequest(string url, string requestData, int requestTimeout, int maxAttempts,
		int delaySec) =>
		Client.ExecuteDeleteRequest(url, requestData, requestTimeout, maxAttempts, delaySec);

	/// <inheritdoc />
	public string ExecuteGetRequest(string url, int requestTimeout, int maxAttempts, int delaySec) =>
		Client.ExecuteGetRequest(url, requestTimeout, maxAttempts, delaySec);

	/// <inheritdoc />
	public Task<HttpResponseMessage> ExecuteGetRequestAsync(string url, int requestTimeout, int maxAttempts,
		int delaySec, CancellationToken cancellationToken) =>
		Client.ExecuteGetRequestAsync(url, requestTimeout, maxAttempts, delaySec, cancellationToken);

	/// <inheritdoc />
	public string ExecutePatchRequest(string url, string requestData, int requestTimeout, int maxAttempts,
		int delaySec) =>
		Client.ExecutePatchRequest(url, requestData, requestTimeout, maxAttempts, delaySec);

	/// <inheritdoc />
	public string ExecutePostRequest(string url, string requestData, int requestTimeout, int maxAttempts,
		int delaySec) =>
		Client.ExecutePostRequest(url, requestData, requestTimeout, maxAttempts, delaySec);

	/// <inheritdoc />
	public Task<HttpResponseMessage> ExecutePostRequestAsync(string url, string requestData, int requestTimeout,
		int maxAttempts, int delaySec, CancellationToken cancellationToken) =>
		Client.ExecutePostRequestAsync(url, requestData, requestTimeout, maxAttempts, delaySec, cancellationToken);

	/// <inheritdoc />
	public string ExecutePutRequest(string url, string requestData, int requestTimeout, int maxAttempts,
		int delaySec) =>
		Client.ExecutePutRequest(url, requestData, requestTimeout, maxAttempts, delaySec);

	/// <inheritdoc />
	public IReadOnlyList<CreatioSessionCookie> ExportSessionCookies() => Client.ExportSessionCookies();

	/// <inheritdoc />
	public void ImportSessionCookies(IEnumerable<CreatioSessionCookie> cookies) =>
		Client.ImportSessionCookies(cookies);

	/// <inheritdoc />
	public void Login() => Client.Login();

	/// <inheritdoc />
	public Task<HttpResponseMessage> LoginAsync(int requestTimeout, CancellationToken cancellationToken) =>
		Client.LoginAsync(requestTimeout, cancellationToken);

	/// <inheritdoc />
	public void StartListening(CancellationToken cancellationToken) => Client.StartListening(cancellationToken);

	/// <inheritdoc />
	public string UploadAlmFile(string url, string filePath) => Client.UploadAlmFile(url, filePath);

	/// <inheritdoc />
	public string UploadAlmFileByChunk(string url, string filePath) => Client.UploadAlmFileByChunk(url, filePath);

	/// <inheritdoc />
	public string UploadFile(string url, string filePath) => Client.UploadFile(url, filePath);

	/// <inheritdoc />
	public Task<HttpResponseMessage> UploadImageAsync(string url, byte[] data, string fileName, string mimeType,
		int requestTimeout, CancellationToken cancellationToken) =>
		Client.UploadImageAsync(url, data, fileName, mimeType, requestTimeout, cancellationToken);

	#endregion
}
