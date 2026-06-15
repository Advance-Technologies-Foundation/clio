namespace Clio.Command;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Clio.Common;
using Clio.Common.BrowserSession;
using Microsoft.AspNetCore.SignalR.Client;

internal interface IMessageChannelPublisher {
	string ClientConnectionClassName { get; }

	Task PublishAsync(MessageChannelPublishRequest request, CancellationToken cancellationToken = default);
}

internal interface IClientWebSocketConnection : IAsyncDisposable {
	void SetCookieHeader(string cookieHeader);

	Task ConnectAsync(Uri serviceUrl, CancellationToken cancellationToken);

	Task SendTextAsync(string payload, CancellationToken cancellationToken);

	Task CloseAsync(CancellationToken cancellationToken);
}

internal interface IClientWebSocketConnectionFactory {
	IClientWebSocketConnection Create();
}

internal interface IMessageChannelHubConnection : IAsyncDisposable {
	Task StartAsync(CancellationToken cancellationToken);

	Task SendMessageAsync(MessageChannelEnvelope message, CancellationToken cancellationToken);
}

internal interface IMessageChannelHubConnectionFactory {
	IMessageChannelHubConnection Create(Uri serviceUrl, IReadOnlyList<BrowserCookie> cookies);
}

internal sealed record MessageChannelPublishRequest(
	Uri ServiceUrl,
	IReadOnlyList<BrowserCookie> Cookies,
	MessageChannelEnvelope Envelope);

internal sealed class PageDesignerPresenceNotifier : IPageDesignerPresenceNotifier {
	private const string DesignerPresenceChannel = "DesignerPresence";
	private const string SaveMode = "save";
	private const string PageSchemaType = "page";
	// Direct broadcast (not "ServerMsg"): the front-end Designer Presence listener filters on
	// Header.Sender == "DesignerPresence_<type>_<name-lower>" and reacts to body.users[].mode == "save".
	// A "ServerMsg" client-publish only reaches the server-side presence handler, which re-broadcasts
	// just to already-registered designer sessions and is silently dropped for an external publisher.
	// A "BroadcastMsg" with the per-schema sender is fanned out verbatim to every connected channel,
	// matching the listener directly (verified live on a studio stand).
	private const string BroadcastMessageChannelType = "BroadcastMsg";
	private const string WebSocketChannelClassName = "Terrasoft.WebSocketChannel";
	private const string SignalRChannelClassName = "Terrasoft.SignalRChannel";
	private const char UriPathSeparator = '/';

	// Best-effort upper bound for the whole notification chain (cookie acquisition + WS/SignalR
	// handshake). This runs on the synchronous success path of update-page under McpToolExecutionLock,
	// so a hung handshake against an unreachable message-channel endpoint must never stall the
	// already-succeeded save or serialize sibling page tools — on timeout the toast is simply skipped.
	private static readonly TimeSpan PublishTimeout = TimeSpan.FromSeconds(5);

	private readonly IApplicationClient _applicationClient;
	private readonly IBrowserSessionService _browserSessionService;
	private readonly EnvironmentSettings _environmentSettings;
	private readonly System.IO.Abstractions.IFileSystem _fileSystem;
	private readonly ILogger _logger;
	private readonly IReadOnlyDictionary<string, IMessageChannelPublisher> _publishersByTransport;
	private readonly IServiceUrlBuilder _serviceUrlBuilder;

	public PageDesignerPresenceNotifier(
		IApplicationClient applicationClient,
		IBrowserSessionService browserSessionService,
		EnvironmentSettings environmentSettings,
		System.IO.Abstractions.IFileSystem fileSystem,
		ILogger logger,
		IEnumerable<IMessageChannelPublisher> publishers,
		IServiceUrlBuilder serviceUrlBuilder) {
		_applicationClient = applicationClient ?? throw new ArgumentNullException(nameof(applicationClient));
		_browserSessionService = browserSessionService ?? throw new ArgumentNullException(nameof(browserSessionService));
		_environmentSettings = environmentSettings ?? throw new ArgumentNullException(nameof(environmentSettings));
		_fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
		_logger = logger ?? throw new ArgumentNullException(nameof(logger));
		_serviceUrlBuilder = serviceUrlBuilder ?? throw new ArgumentNullException(nameof(serviceUrlBuilder));
		_publishersByTransport = publishers?.ToDictionary(
			publisher => publisher.ClientConnectionClassName,
			StringComparer.Ordinal)
			?? throw new ArgumentNullException(nameof(publishers));
	}

	public string? TryNotifyPageSaved(string schemaName, string? schemaCaption = null) {
		try {
			return TryNotifyPageSavedAsync(schemaName, schemaCaption).GetAwaiter().GetResult();
		} catch (Exception ex) {
			return LogAndBuildFailureWarning("unexpected error while publishing designer presence", ex);
		}
	}

	private async Task<string?> TryNotifyPageSavedAsync(string schemaName, string? schemaCaption) {
		if (string.IsNullOrWhiteSpace(schemaName)) {
			return BuildSkipWarning("schema name is missing.");
		}
		if (string.IsNullOrWhiteSpace(_environmentSettings.Uri)) {
			return BuildSkipWarning("environment URI is missing.");
		}
		if (string.IsNullOrWhiteSpace(_environmentSettings.Login)
			|| string.IsNullOrWhiteSpace(_environmentSettings.Password)) {
			return BuildSkipWarning(
				"forms-auth cookies require login/password; OAuth-only or credential-less environments cannot publish the live notification.");
		}

		// Bound every network step with a single timeout so a slow/unreachable endpoint cannot stall
		// the synchronous save path (see PublishTimeout). The token is threaded through cookie
		// acquisition and the publish handshake instead of CancellationToken.None.
		using var timeoutSource = new CancellationTokenSource(PublishTimeout);
		CancellationToken cancellationToken = timeoutSource.Token;

		(IReadOnlyList<BrowserCookie>? cookies, string? cookieWarning) =
			await TryAcquireCookiesAsync(cancellationToken).ConfigureAwait(false);
		if (cookies is null) {
			return cookieWarning;
		}

		ApplicationInfoPayload? applicationInfo = TryReadApplicationInfo();
		if (applicationInfo is null) {
			return BuildSkipWarning("message-channel application info is unavailable.");
		}
		if (!_publishersByTransport.TryGetValue(applicationInfo.ClientConnectionClassName ?? string.Empty,
				out IMessageChannelPublisher? publisher)) {
			return BuildSkipWarning(
				$"unsupported message-channel transport '{applicationInfo.ClientConnectionClassName ?? "<empty>"}'.");
		}

		UserInfoPayload? userInfo = TryReadCurrentUserInfo();
		if (userInfo is null || string.IsNullOrWhiteSpace(userInfo.SessionId)) {
			return BuildSkipWarning("current user info is unavailable or missing sessionId.");
		}

		(Uri? serviceUrl, string? serviceUrlWarning) = TryResolveServiceUrl(applicationInfo);
		if (serviceUrl is null) {
			return serviceUrlWarning;
		}

		MessageChannelEnvelope envelope = BuildSaveEnvelope(schemaName, schemaCaption, userInfo);

		try {
			await publisher.PublishAsync(new MessageChannelPublishRequest(serviceUrl, cookies, envelope), cancellationToken)
				.ConfigureAwait(false);
			return null;
		} catch (OperationCanceledException) {
			return BuildTimeoutWarning("publishing the live notification");
		} catch (Exception ex) {
			return LogAndBuildFailureWarning("live notification publish failed", ex);
		}
	}

	/// <summary>
	/// Acquires the forms-auth browser-session cookies under the bounded <paramref name="cancellationToken"/>.
	/// Returns the cookies on success, or a non-null fail-open warning (skip/timeout/failure) otherwise.
	/// </summary>
	private async Task<(IReadOnlyList<BrowserCookie>? Cookies, string? Warning)> TryAcquireCookiesAsync(
		CancellationToken cancellationToken) {
		string sessionPath;
		try {
			sessionPath = await _browserSessionService
				.GetSessionPathAsync(_environmentSettings, forceRefresh: false, ct: cancellationToken)
				.ConfigureAwait(false);
		} catch (OperationCanceledException) {
			return (null, BuildTimeoutWarning("acquiring browser-session cookies"));
		} catch (Exception ex) {
			return (null, LogAndBuildFailureWarning("could not obtain browser-session cookies", ex));
		}
		if (string.IsNullOrWhiteSpace(sessionPath) || !_fileSystem.File.Exists(sessionPath)) {
			return (null, BuildSkipWarning("browser-session storageState was not available."));
		}
		string storageStateJson = await _fileSystem.File.ReadAllTextAsync(sessionPath).ConfigureAwait(false);
		IReadOnlyList<BrowserCookie> cookies = StorageStateJson.ParseCookies(storageStateJson);
		if (cookies.Count == 0) {
			return (null, BuildSkipWarning("browser-session storageState did not contain usable cookies."));
		}
		return (cookies, null);
	}

	/// <summary>
	/// Resolves and validates the message-channel service URL. Returns the URI on success, or a
	/// non-null skip warning when the URL is empty/foreign-host/downgraded/unsupported.
	/// </summary>
	private (Uri? ServiceUrl, string? Warning) TryResolveServiceUrl(ApplicationInfoPayload applicationInfo) {
		try {
			return (ResolveServiceUrl(applicationInfo.ServiceUrl, applicationInfo.ClientConnectionClassName), null);
		} catch (InvalidOperationException ex) {
			// ResolveServiceUrl throws only controlled, non-sensitive messages (empty/foreign-host/
			// downgrade/unsupported-scheme), so surfacing the message here leaks nothing.
			return (null, BuildSkipWarning($"message-channel serviceUrl is invalid: {ex.Message}"));
		} catch (Exception ex) {
			return (null, BuildSkipWarning($"message-channel serviceUrl is invalid ({ex.GetType().Name})."));
		}
	}

	/// <summary>
	/// Builds the Designer Presence save broadcast envelope (per-schema sender + server-event
	/// payload whose single <c>users[]</c> element carries the saving user with <c>mode="save"</c>).
	/// </summary>
	private static MessageChannelEnvelope BuildSaveEnvelope(string schemaName, string? schemaCaption, UserInfoPayload userInfo) {
		var payload = new DesignerPresenceServerPayload {
			SchemaType = PageSchemaType,
			SchemaName = schemaName,
			SchemaCaption = string.IsNullOrWhiteSpace(schemaCaption) ? schemaName : schemaCaption,
			Users = [
				new DesignerPresenceUserPayload {
					SessionId = userInfo.SessionId,
					Id = userInfo.Id,
					Name = string.IsNullOrWhiteSpace(userInfo.ContactName) ? userInfo.Id : userInfo.ContactName,
					ContactId = userInfo.ContactId,
					ContactName = userInfo.ContactName,
					PhotoId = userInfo.PhotoId,
					Email = userInfo.Email,
					Mode = SaveMode
				}
			]
		};
		return MessageChannelEnvelope.Create(
			BuildPerSchemaSender(PageSchemaType, schemaName),
			BroadcastMessageChannelType,
			JsonSerializer.Serialize(payload));
	}

	private ApplicationInfoPayload? TryReadApplicationInfo() {
		try {
			string url = _serviceUrlBuilder.Build(CreatioServicePaths.GetApplicationInfo);
			string json = _applicationClient.ExecutePostRequest(url, "{}");
			return JsonSerializer.Deserialize<ApplicationInfoResponsePayload>(json)?.ApplicationInfo;
		} catch (JsonException) {
			return null;
		} catch (InvalidOperationException) {
			return null;
		}
	}

	private UserInfoPayload? TryReadCurrentUserInfo() {
		try {
			string url = _serviceUrlBuilder.Build(CreatioServicePaths.GetCurrentUserInfo);
			string json = _applicationClient.ExecutePostRequest(url, "{}");
			return JsonSerializer.Deserialize<UserInfoResponsePayload>(json)?.UserInfo;
		} catch (JsonException) {
			return null;
		} catch (InvalidOperationException) {
			return null;
		}
	}

	private Uri ResolveServiceUrl(string? rawServiceUrl, string? clientConnectionClassName) {
		if (string.IsNullOrWhiteSpace(rawServiceUrl)) {
			throw new InvalidOperationException("serviceUrl is empty.");
		}
		if (!Uri.TryCreate(_environmentSettings.Uri, UriKind.Absolute, out Uri? environmentUri)) {
			throw new InvalidOperationException("environment URI is not an absolute URI.");
		}
		Uri resolved = Uri.TryCreate(rawServiceUrl, UriKind.Absolute, out Uri? absolute)
			? absolute
			: new Uri(new Uri(_environmentSettings.Uri.TrimEnd(UriPathSeparator) + UriPathSeparator, UriKind.Absolute), rawServiceUrl);
		// Security: the full forms-auth cookie set (including the auth/session cookie) is attached to
		// this connection downstream. A server-supplied absolute serviceUrl pointing at a foreign host
		// would harvest the live session, so only the environment's own host may receive the cookies.
		if (!string.Equals(resolved.Host, environmentUri.Host, StringComparison.OrdinalIgnoreCase)) {
			throw new InvalidOperationException("serviceUrl host does not match the environment host.");
		}
		if (!IsSupportedTransportScheme(resolved.Scheme)) {
			throw new InvalidOperationException($"serviceUrl scheme '{resolved.Scheme}' is not a supported message-channel transport.");
		}
		// Never downgrade to a plaintext transport when the environment itself is TLS-secured.
		if (string.Equals(environmentUri.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal)
			&& IsPlaintextTransportScheme(resolved.Scheme)) {
			throw new InvalidOperationException("serviceUrl would downgrade a secured environment to a plaintext transport.");
		}
		if (string.Equals(clientConnectionClassName, WebSocketChannelClassName, StringComparison.Ordinal)
			&& (resolved.Scheme == Uri.UriSchemeHttp || resolved.Scheme == Uri.UriSchemeHttps)) {
			var builder = new UriBuilder(resolved) {
				Scheme = resolved.Scheme == Uri.UriSchemeHttps ? "wss" : "ws"
			};
			if (resolved.IsDefaultPort) {
				builder.Port = -1;
			}
			return builder.Uri;
		}
		return resolved;
	}

	private static bool IsSupportedTransportScheme(string scheme) =>
		string.Equals(scheme, Uri.UriSchemeHttp, StringComparison.Ordinal)
		|| string.Equals(scheme, Uri.UriSchemeHttps, StringComparison.Ordinal)
		|| string.Equals(scheme, Uri.UriSchemeWs, StringComparison.Ordinal)
		|| string.Equals(scheme, Uri.UriSchemeWss, StringComparison.Ordinal);

	private static bool IsPlaintextTransportScheme(string scheme) =>
		string.Equals(scheme, Uri.UriSchemeHttp, StringComparison.Ordinal)
		|| string.Equals(scheme, Uri.UriSchemeWs, StringComparison.Ordinal);

	/// <summary>
	/// Builds the per-schema message sender the front-end Designer Presence listener filters on:
	/// <c>DesignerPresence_&lt;schemaType&gt;_&lt;schemaName-lowercased&gt;</c>. Mirrors
	/// <c>DesignerPresenceService._getSenderName</c> in creatio-ui.
	/// </summary>
	private static string BuildPerSchemaSender(string schemaType, string schemaName) =>
		$"{DesignerPresenceChannel}_{schemaType.ToLowerInvariant()}_{schemaName.ToLowerInvariant()}";

	private static string BuildSkipWarning(string reason) =>
		$"Designer presence notification skipped: {reason} The page save already succeeded.";

	private static string BuildFailureWarning(string reason) =>
		$"Designer presence notification failed: {reason} The page save already succeeded.";

	/// <summary>
	/// Builds a sanitized failure warning and logs it. Security: transport exceptions in this area can
	/// echo the target URI, request headers, or cookie/session fragments, so the raw <c>ex.Message</c>
	/// is never interpolated into agent-visible output or logs — only the exception type is surfaced.
	/// </summary>
	private string LogAndBuildFailureWarning(string context, Exception ex) {
		string warning = BuildFailureWarning($"{context} ({ex.GetType().Name}).");
		_logger.WriteDebug(warning);
		return warning;
	}

	private string BuildTimeoutWarning(string context) {
		string warning = BuildFailureWarning($"{context} timed out after {PublishTimeout.TotalSeconds:0}s.");
		_logger.WriteDebug(warning);
		return warning;
	}

	private sealed class ApplicationInfoResponsePayload {
		[JsonPropertyName("applicationInfo")]
		public ApplicationInfoPayload? ApplicationInfo { get; set; }
	}

	private sealed class ApplicationInfoPayload {
		[JsonPropertyName("serviceUrl")]
		public string? ServiceUrl { get; set; }

		[JsonPropertyName("clientConnectionClassName")]
		public string? ClientConnectionClassName { get; set; }
	}

	private sealed class UserInfoResponsePayload {
		[JsonPropertyName("userInfo")]
		public UserInfoPayload? UserInfo { get; set; }
	}

	private sealed class UserInfoPayload {
		[JsonPropertyName("id")]
		public string? Id { get; set; }

		[JsonPropertyName("contactId")]
		public string? ContactId { get; set; }

		[JsonPropertyName("contactName")]
		public string? ContactName { get; set; }

		[JsonPropertyName("photoId")]
		public string? PhotoId { get; set; }

		[JsonPropertyName("email")]
		public string? Email { get; set; }

		[JsonPropertyName("sessionId")]
		public string? SessionId { get; set; }
	}

	/// <summary>
	/// Server-event payload shape the front-end Designer Presence listener consumes: a
	/// <c>users</c> array whose elements each carry their own <c>mode</c>. A remote element with
	/// <c>mode == "save"</c> from a different session triggers the "… just updated …, reload" toast.
	/// </summary>
	private sealed class DesignerPresenceServerPayload {
		[JsonPropertyName("schemaName")]
		public string SchemaName { get; set; } = string.Empty;

		[JsonPropertyName("schemaType")]
		public string SchemaType { get; set; } = string.Empty;

		[JsonPropertyName("schemaCaption")]
		public string SchemaCaption { get; set; } = string.Empty;

		[JsonPropertyName("users")]
		public IReadOnlyList<DesignerPresenceUserPayload> Users { get; set; } = [];
	}

	private sealed class DesignerPresenceUserPayload {
		[JsonPropertyName("sessionId")]
		public string? SessionId { get; set; }

		[JsonPropertyName("id")]
		public string? Id { get; set; }

		[JsonPropertyName("name")]
		public string? Name { get; set; }

		[JsonPropertyName("contactId")]
		public string? ContactId { get; set; }

		[JsonPropertyName("contactName")]
		public string? ContactName { get; set; }

		[JsonPropertyName("photoId")]
		public string? PhotoId { get; set; }

		[JsonPropertyName("email")]
		public string? Email { get; set; }

		[JsonPropertyName("mode")]
		public string? Mode { get; set; }
	}
}

internal sealed class WebSocketMessageChannelPublisher(
	IClientWebSocketConnectionFactory connectionFactory) : IMessageChannelPublisher {
	internal const string TransportClassName = "Terrasoft.WebSocketChannel";

	public string ClientConnectionClassName => TransportClassName;

	public async Task PublishAsync(MessageChannelPublishRequest request, CancellationToken cancellationToken = default) {
		await using IClientWebSocketConnection connection = connectionFactory.Create();
		string cookieHeader = string.Join("; ", request.Cookies.Select(cookie => $"{cookie.Name}={cookie.Value}"));
		connection.SetCookieHeader(cookieHeader);
		await connection.ConnectAsync(request.ServiceUrl, cancellationToken).ConfigureAwait(false);
		await connection.SendTextAsync(request.Envelope.Serialize(), cancellationToken).ConfigureAwait(false);
		await connection.CloseAsync(cancellationToken).ConfigureAwait(false);
	}
}

internal sealed class SignalRMessageChannelPublisher(
	IMessageChannelHubConnectionFactory connectionFactory) : IMessageChannelPublisher {
	internal const string TransportClassName = "Terrasoft.SignalRChannel";

	public string ClientConnectionClassName => TransportClassName;

	public async Task PublishAsync(MessageChannelPublishRequest request, CancellationToken cancellationToken = default) {
		await using IMessageChannelHubConnection connection = connectionFactory.Create(request.ServiceUrl, request.Cookies);
		await connection.StartAsync(cancellationToken).ConfigureAwait(false);
		await connection.SendMessageAsync(request.Envelope, cancellationToken).ConfigureAwait(false);
	}
}

internal sealed class ClientWebSocketConnectionFactory : IClientWebSocketConnectionFactory {
	public IClientWebSocketConnection Create() => new ClientWebSocketConnection();
}

internal sealed class ClientWebSocketConnection : IClientWebSocketConnection {
	private readonly System.Net.WebSockets.ClientWebSocket _clientWebSocket = new();

	public void SetCookieHeader(string cookieHeader) {
		if (!string.IsNullOrWhiteSpace(cookieHeader)) {
			_clientWebSocket.Options.SetRequestHeader("Cookie", cookieHeader);
		}
	}

	public Task ConnectAsync(Uri serviceUrl, CancellationToken cancellationToken) =>
		_clientWebSocket.ConnectAsync(serviceUrl, cancellationToken);

	public Task SendTextAsync(string payload, CancellationToken cancellationToken) {
		byte[] bytes = Encoding.UTF8.GetBytes(payload);
		return _clientWebSocket.SendAsync(
			bytes,
			System.Net.WebSockets.WebSocketMessageType.Text,
			endOfMessage: true,
			cancellationToken);
	}

	public Task CloseAsync(CancellationToken cancellationToken) {
		if (_clientWebSocket.State != System.Net.WebSockets.WebSocketState.Open) {
			return Task.CompletedTask;
		}
		return _clientWebSocket.CloseAsync(
			System.Net.WebSockets.WebSocketCloseStatus.NormalClosure,
			"done",
			cancellationToken);
	}

	public ValueTask DisposeAsync() {
		_clientWebSocket.Dispose();
		return ValueTask.CompletedTask;
	}
}

internal sealed class MessageChannelHubConnectionFactory : IMessageChannelHubConnectionFactory {
	public IMessageChannelHubConnection Create(Uri serviceUrl, IReadOnlyList<BrowserCookie> cookies) {
		var cookieContainer = new System.Net.CookieContainer();
		foreach (BrowserCookie cookie in cookies) {
			var concreteCookie = new System.Net.Cookie(
				cookie.Name,
				cookie.Value,
				string.IsNullOrWhiteSpace(cookie.Path) ? "/" : cookie.Path,
				string.IsNullOrWhiteSpace(cookie.Domain) ? serviceUrl.Host : cookie.Domain) {
				HttpOnly = cookie.HttpOnly,
				Secure = cookie.Secure
			};
			if (cookie.Expires > 0) {
				concreteCookie.Expires = DateTimeOffset.FromUnixTimeSeconds((long)cookie.Expires).UtcDateTime;
			}
			cookieContainer.Add(serviceUrl, concreteCookie);
		}

		HubConnection connection = new HubConnectionBuilder()
			.WithUrl(serviceUrl.ToString(), options => {
				options.Cookies = cookieContainer;
				options.Transports = Microsoft.AspNetCore.Http.Connections.HttpTransportType.WebSockets
					| Microsoft.AspNetCore.Http.Connections.HttpTransportType.ServerSentEvents
					| Microsoft.AspNetCore.Http.Connections.HttpTransportType.LongPolling;
			})
			.WithAutomaticReconnect()
			.Build();
		return new MessageChannelHubConnection(connection);
	}
}

internal sealed class MessageChannelHubConnection(HubConnection connection) : IMessageChannelHubConnection {
	public Task StartAsync(CancellationToken cancellationToken) =>
		connection.StartAsync(cancellationToken);

	public Task SendMessageAsync(MessageChannelEnvelope message, CancellationToken cancellationToken) =>
		connection.SendAsync("SendMessage", message, cancellationToken);

	public ValueTask DisposeAsync() =>
		connection.DisposeAsync();
}

internal sealed class MessageChannelEnvelope {
	private static readonly JsonSerializerOptions SerializerOptions = new() {
		PropertyNamingPolicy = null
	};

	[JsonPropertyName("Id")]
	public required string Id { get; init; }

	[JsonPropertyName("Header")]
	public required MessageChannelHeader Header { get; init; }

	[JsonPropertyName("Body")]
	public required string Body { get; init; }

	public static MessageChannelEnvelope Create(string sender, string channelType, string body) =>
		new() {
			Id = Guid.NewGuid().ToString(),
			Header = new MessageChannelHeader {
				Sender = sender,
				BodyTypeName = "System.String",
				ChannelType = channelType
			},
			Body = body
		};

	public string Serialize() => JsonSerializer.Serialize(this, SerializerOptions);
}

internal sealed class MessageChannelHeader {
	[JsonPropertyName("Sender")]
	public required string Sender { get; init; }

	[JsonPropertyName("BodyTypeName")]
	public required string BodyTypeName { get; init; }

	[JsonPropertyName("ChannelType")]
	public required string ChannelType { get; init; }
}
