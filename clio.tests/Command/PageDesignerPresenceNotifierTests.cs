namespace Clio.Tests.Command;

using System;
using System.Threading;
using System.Threading.Tasks;
using System.IO.Abstractions.TestingHelpers;
using System.Text.Json;
using Clio.Command;
using Clio.Common;
using Clio.Common.BrowserSession;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
public sealed class PageDesignerPresenceNotifierTests {
	private const string ApplicationInfoUrl = "http://test/0/ServiceModel/ApplicationInfoService.svc/GetApplicationInfo";
	private const string UserInfoUrl = "http://test/0/ServiceModel/UserInfoService.svc/GetCurrentUserInfo";
	private const string StorageStatePath = "/tmp/storage-state.json";

	private IApplicationClient _applicationClient = null!;
	private IBrowserSessionService _browserSessionService = null!;
	private EnvironmentSettings _environmentSettings = null!;
	private MockFileSystem _fileSystem = null!;
	private ILogger _logger = null!;
	private IMessageChannelPublisher _webSocketPublisher = null!;
	private IMessageChannelPublisher _signalRPublisher = null!;
	private IServiceUrlBuilder _serviceUrlBuilder = null!;

	[SetUp]
	public void SetUp() {
		_applicationClient = Substitute.For<IApplicationClient>();
		_browserSessionService = Substitute.For<IBrowserSessionService>();
		_environmentSettings = new EnvironmentSettings {
			Uri = "http://test",
			Login = "Supervisor",
			Password = "Supervisor",
			IsNetCore = false
		};
		_fileSystem = new MockFileSystem();
		_logger = Substitute.For<ILogger>();
		_webSocketPublisher = Substitute.For<IMessageChannelPublisher>();
		_signalRPublisher = Substitute.For<IMessageChannelPublisher>();
		_webSocketPublisher.ClientConnectionClassName.Returns("Terrasoft.WebSocketChannel");
		_signalRPublisher.ClientConnectionClassName.Returns("Terrasoft.SignalRChannel");
		_serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		_serviceUrlBuilder.Build(CreatioServicePaths.GetApplicationInfo).Returns(ApplicationInfoUrl);
		_serviceUrlBuilder.Build(CreatioServicePaths.GetCurrentUserInfo).Returns(UserInfoUrl);
		_browserSessionService.GetSessionPathAsync(_environmentSettings, null, false, Arg.Any<CancellationToken>())
			.Returns(StorageStatePath);
		_fileSystem.AddFile(StorageStatePath, new MockFileData(StorageStateJson.Serialize(
			new StorageStateResult([
				new BrowserCookie("BPMCSRF", "token", "test", "/", false, false, "Lax", -1)
			]))));
		_applicationClient.ExecutePostRequest(ApplicationInfoUrl, "{}").Returns("""{"applicationInfo":{"serviceUrl":"ws://test/0/Nui/ViewModule.aspx.ashx","clientConnectionClassName":"Terrasoft.WebSocketChannel"}}""");
		_applicationClient.ExecutePostRequest(UserInfoUrl, "{}").Returns("""{"id":"root-id","userInfo":{"id":"11111111-2222-3333-4444-555555555555","contactId":"contact-guid","contactName":"Test User","photoId":"photo-guid","email":"user@test","sessionId":"session-123"}}""");
	}

	private PageDesignerPresenceNotifier CreateSut() =>
		new(
			_applicationClient,
			_browserSessionService,
			_environmentSettings,
			_fileSystem,
			_logger,
			[_webSocketPublisher, _signalRPublisher],
			_serviceUrlBuilder);

	[Test]
	[Description("The notifier selects the WebSocket publisher from GetApplicationInfo and builds the expected Designer Presence payload from GetCurrentUserInfo.")]
	public void TryNotifyPageSaved_ShouldUseWebSocketPublisher_WhenApplicationInfoSelectsWebSocketTransport() {
		// Arrange
		PageDesignerPresenceNotifier sut = CreateSut();
		MessageChannelPublishRequest? capturedRequest = null;
		_webSocketPublisher
			.PublishAsync(Arg.Do<MessageChannelPublishRequest>(request => capturedRequest = request), Arg.Any<CancellationToken>())
			.Returns(Task.CompletedTask);

		// Act
		string? warning = sut.TryNotifyPageSaved("UsrPage_FormPage");

		// Assert
		warning.Should().BeNull(because: "the supported WebSocket transport should publish successfully");
		_webSocketPublisher.Received(1).PublishAsync(Arg.Any<MessageChannelPublishRequest>(), Arg.Any<CancellationToken>());
		_signalRPublisher.DidNotReceive().PublishAsync(Arg.Any<MessageChannelPublishRequest>(), Arg.Any<CancellationToken>());
		capturedRequest.Should().NotBeNull(because: "the publisher should receive the fully built envelope");
		capturedRequest!.Envelope.Header.Sender.Should().Be("DesignerPresence_page_usrpage_formpage",
			because: "the front-end listener filters on the per-schema sender DesignerPresence_<type>_<name-lower>, not the bare channel name");
		capturedRequest.Envelope.Header.BodyTypeName.Should().Be("System.String",
			because: "the message body is a JSON string");
		capturedRequest.Envelope.Header.ChannelType.Should().Be("BroadcastMsg",
			because: "a direct broadcast is fanned out verbatim to every channel and matches the listener; a ServerMsg client-publish is dropped for an external publisher");
		using JsonDocument payload = JsonDocument.Parse(capturedRequest.Envelope.Body);
		payload.RootElement.GetProperty("schemaName").GetString().Should().Be("UsrPage_FormPage",
			because: "the server-event payload carries the schema name");
		payload.RootElement.GetProperty("schemaType").GetString().Should().Be("page",
			because: "this feature is scoped to page designer presence");
		payload.RootElement.GetProperty("schemaCaption").GetString().Should().Be("UsrPage_FormPage",
			because: "v1 defaults schemaCaption to the schema name");
		JsonElement users = payload.RootElement.GetProperty("users");
		users.GetArrayLength().Should().Be(1, because: "the broadcast carries the saving user as a single-element users array");
		JsonElement user = users[0];
		user.GetProperty("mode").GetString().Should().Be("save",
			because: "the listener triggers the reload toast only for a remote user whose mode is 'save'");
		user.GetProperty("id").GetString().Should().Be("11111111-2222-3333-4444-555555555555",
			because: "the payload must preserve the current user's id");
		user.GetProperty("contactId").GetString().Should().Be("contact-guid",
			because: "the payload must preserve the current user's contact id");
		user.GetProperty("contactName").GetString().Should().Be("Test User",
			because: "the payload must preserve the current user's display name");
		user.GetProperty("sessionId").GetString().Should().Be("session-123",
			because: "the listener excludes the receiving session by session id, so the publisher must stamp its own");
	}

	[Test]
	[Description("The notifier selects the SignalR publisher when GetApplicationInfo reports the SignalR transport.")]
	public void TryNotifyPageSaved_ShouldUseSignalRPublisher_WhenApplicationInfoSelectsSignalRTransport() {
		// Arrange
		_applicationClient.ExecutePostRequest(ApplicationInfoUrl, "{}")
			.Returns("""{"applicationInfo":{"serviceUrl":"https://test/signalr-hubs/messages","clientConnectionClassName":"Terrasoft.SignalRChannel"}}""");
		PageDesignerPresenceNotifier sut = CreateSut();

		// Act
		string? warning = sut.TryNotifyPageSaved("UsrPage_FormPage");

		// Assert
		warning.Should().BeNull(because: "the supported SignalR transport should publish successfully");
		_signalRPublisher.Received(1).PublishAsync(Arg.Any<MessageChannelPublishRequest>(), Arg.Any<CancellationToken>());
		_webSocketPublisher.DidNotReceive().PublishAsync(Arg.Any<MessageChannelPublishRequest>(), Arg.Any<CancellationToken>());
	}

	[Test]
	[Description("The notifier skips the live push with a warning when forms-auth credentials are unavailable.")]
	public void TryNotifyPageSaved_ShouldReturnWarning_WhenFormsCredentialsMissing() {
		// Arrange
		_environmentSettings.Login = null;
		PageDesignerPresenceNotifier sut = CreateSut();

		// Act
		string? warning = sut.TryNotifyPageSaved("UsrPage_FormPage");

		// Assert
		warning.Should().Contain("forms-auth cookies require login/password",
			because: "OAuth-only or credential-less environments cannot produce the browser-session cookies needed for the live channel");
		_webSocketPublisher.DidNotReceive().PublishAsync(Arg.Any<MessageChannelPublishRequest>(), Arg.Any<CancellationToken>());
		_signalRPublisher.DidNotReceive().PublishAsync(Arg.Any<MessageChannelPublishRequest>(), Arg.Any<CancellationToken>());
	}

	[Test]
	[Description("The notifier skips the live push with a warning when the message-channel transport is unknown.")]
	public void TryNotifyPageSaved_ShouldReturnWarning_WhenTransportUnsupported() {
		// Arrange
		_applicationClient.ExecutePostRequest(ApplicationInfoUrl, "{}")
			.Returns("""{"applicationInfo":{"serviceUrl":"https://test/unknown","clientConnectionClassName":"Terrasoft.UnknownChannel"}}""");
		PageDesignerPresenceNotifier sut = CreateSut();

		// Act
		string? warning = sut.TryNotifyPageSaved("UsrPage_FormPage");

		// Assert
		warning.Should().Contain("unsupported message-channel transport",
			because: "the notifier must fail open when the environment advertises an unhandled transport");
		_webSocketPublisher.DidNotReceive().PublishAsync(Arg.Any<MessageChannelPublishRequest>(), Arg.Any<CancellationToken>());
		_signalRPublisher.DidNotReceive().PublishAsync(Arg.Any<MessageChannelPublishRequest>(), Arg.Any<CancellationToken>());
	}

	[Test]
	[Description("The notifier skips the live push with a warning when the browser-session storageState carries no usable cookies.")]
	public void TryNotifyPageSaved_ShouldReturnWarning_WhenStorageStateHasNoCookies() {
		// Arrange
		_fileSystem.File.WriteAllText(StorageStatePath, StorageStateJson.Serialize(new StorageStateResult([])));
		PageDesignerPresenceNotifier sut = CreateSut();

		// Act
		string? warning = sut.TryNotifyPageSaved("UsrPage_FormPage");

		// Assert
		warning.Should().Contain("did not contain usable cookies",
			because: "a cookie-less storageState cannot authenticate the live channel and must fail open");
		_webSocketPublisher.DidNotReceive().PublishAsync(Arg.Any<MessageChannelPublishRequest>(), Arg.Any<CancellationToken>());
	}

	[Test]
	[Description("The notifier skips the live push with a warning when GetApplicationInfo returns no usable application info.")]
	public void TryNotifyPageSaved_ShouldReturnWarning_WhenApplicationInfoUnavailable() {
		// Arrange
		_applicationClient.ExecutePostRequest(ApplicationInfoUrl, "{}").Returns("{}");
		PageDesignerPresenceNotifier sut = CreateSut();

		// Act
		string? warning = sut.TryNotifyPageSaved("UsrPage_FormPage");

		// Assert
		warning.Should().Contain("application info is unavailable",
			because: "without the message-channel application info the notifier cannot pick a transport and must fail open");
		_webSocketPublisher.DidNotReceive().PublishAsync(Arg.Any<MessageChannelPublishRequest>(), Arg.Any<CancellationToken>());
	}

	[Test]
	[Description("The notifier skips the live push with a warning when the current user info has no sessionId.")]
	public void TryNotifyPageSaved_ShouldReturnWarning_WhenUserSessionIdMissing() {
		// Arrange
		_applicationClient.ExecutePostRequest(UserInfoUrl, "{}")
			.Returns("""{"userInfo":{"id":"11111111-2222-3333-4444-555555555555","sessionId":""}}""");
		PageDesignerPresenceNotifier sut = CreateSut();

		// Act
		string? warning = sut.TryNotifyPageSaved("UsrPage_FormPage");

		// Assert
		warning.Should().Contain("missing sessionId",
			because: "the listener excludes the receiving session by sessionId, so a blank sessionId makes the broadcast unusable");
		_webSocketPublisher.DidNotReceive().PublishAsync(Arg.Any<MessageChannelPublishRequest>(), Arg.Any<CancellationToken>());
	}

	[Test]
	[Description("The notifier refuses to attach session cookies when the server-supplied absolute serviceUrl points at a host other than the environment host.")]
	public void TryNotifyPageSaved_ShouldReturnWarningAndNotPublish_WhenServiceUrlHostDiffersFromEnvironment() {
		// Arrange
		_applicationClient.ExecutePostRequest(ApplicationInfoUrl, "{}")
			.Returns("""{"applicationInfo":{"serviceUrl":"wss://evil.example.com/messages","clientConnectionClassName":"Terrasoft.WebSocketChannel"}}""");
		PageDesignerPresenceNotifier sut = CreateSut();

		// Act
		string? warning = sut.TryNotifyPageSaved("UsrPage_FormPage");

		// Assert
		warning.Should().Contain("host does not match the environment host",
			because: "attaching the live session cookies to a foreign host would harvest the authenticated session");
		_webSocketPublisher.DidNotReceive().PublishAsync(Arg.Any<MessageChannelPublishRequest>(), Arg.Any<CancellationToken>());
		_signalRPublisher.DidNotReceive().PublishAsync(Arg.Any<MessageChannelPublishRequest>(), Arg.Any<CancellationToken>());
	}

	[Test]
	[Description("The notifier upgrades an http message-channel URL to ws for the WebSocket transport.")]
	public void TryNotifyPageSaved_ShouldUpgradeHttpToWs_ForWebSocketTransport() {
		// Arrange
		_applicationClient.ExecutePostRequest(ApplicationInfoUrl, "{}")
			.Returns("""{"applicationInfo":{"serviceUrl":"http://test/0/Nui/ViewModule.aspx.ashx","clientConnectionClassName":"Terrasoft.WebSocketChannel"}}""");
		MessageChannelPublishRequest? captured = null;
		_webSocketPublisher.PublishAsync(Arg.Do<MessageChannelPublishRequest>(r => captured = r), Arg.Any<CancellationToken>());

		// Act
		string? warning = CreateSut().TryNotifyPageSaved("UsrPage_FormPage");

		// Assert
		warning.Should().BeNull(because: "the http URL is a valid same-host WebSocket target");
		captured!.ServiceUrl.Scheme.Should().Be("ws",
			because: "an http message-channel URL must be upgraded to ws for the WebSocket transport");
	}

	[Test]
	[Description("The notifier upgrades an https message-channel URL to wss for the WebSocket transport.")]
	public void TryNotifyPageSaved_ShouldUpgradeHttpsToWss_ForWebSocketTransport() {
		// Arrange
		_applicationClient.ExecutePostRequest(ApplicationInfoUrl, "{}")
			.Returns("""{"applicationInfo":{"serviceUrl":"https://test/0/Nui/ViewModule.aspx.ashx","clientConnectionClassName":"Terrasoft.WebSocketChannel"}}""");
		MessageChannelPublishRequest? captured = null;
		_webSocketPublisher.PublishAsync(Arg.Do<MessageChannelPublishRequest>(r => captured = r), Arg.Any<CancellationToken>());

		// Act
		string? warning = CreateSut().TryNotifyPageSaved("UsrPage_FormPage");

		// Assert
		warning.Should().BeNull(because: "the https URL is a valid same-host WebSocket target");
		captured!.ServiceUrl.Scheme.Should().Be("wss",
			because: "an https message-channel URL must be upgraded to wss for the WebSocket transport");
	}

	[Test]
	[Description("The notifier returns a failure warning that exposes only the exception type and never the raw exception message when the publish throws.")]
	public void TryNotifyPageSaved_ShouldReturnSanitizedWarning_WhenPublishThrows() {
		// Arrange
		const string secret = "SECRET-LEAK-session-123";
		_webSocketPublisher.PublishAsync(Arg.Any<MessageChannelPublishRequest>(), Arg.Any<CancellationToken>())
			.Returns(Task.FromException(new InvalidOperationException(secret)));
		PageDesignerPresenceNotifier sut = CreateSut();

		// Act
		string? warning = sut.TryNotifyPageSaved("UsrPage_FormPage");

		// Assert
		warning.Should().Contain("live notification publish failed",
			because: "a publish failure must still be surfaced as a best-effort warning");
		warning.Should().Contain("InvalidOperationException",
			because: "the exception type is safe to log and helps diagnosis");
		warning.Should().NotContain(secret,
			because: "raw transport exception messages can carry the target URI, cookie fragments, or sessionId and must never reach agent-visible output");
	}

	[Test]
	[Description("The notifier returns a timeout warning when the publish is canceled by the bounded timeout.")]
	public void TryNotifyPageSaved_ShouldReturnTimeoutWarning_WhenPublishCanceled() {
		// Arrange
		_webSocketPublisher.PublishAsync(Arg.Any<MessageChannelPublishRequest>(), Arg.Any<CancellationToken>())
			.Returns(Task.FromException(new OperationCanceledException()));
		PageDesignerPresenceNotifier sut = CreateSut();

		// Act
		string? warning = sut.TryNotifyPageSaved("UsrPage_FormPage");

		// Assert
		warning.Should().Contain("timed out",
			because: "a stalled handshake must surface as a bounded timeout instead of blocking the save indefinitely");
	}

	[Test]
	[Description("The notifier hands the publisher a cancelable token (the bounded timeout), never CancellationToken.None.")]
	public void TryNotifyPageSaved_ShouldPassCancelableToken_ToPublisher() {
		// Arrange
		CancellationToken capturedToken = default;
		_webSocketPublisher.PublishAsync(Arg.Any<MessageChannelPublishRequest>(), Arg.Do<CancellationToken>(t => capturedToken = t));
		PageDesignerPresenceNotifier sut = CreateSut();

		// Act
		string? warning = sut.TryNotifyPageSaved("UsrPage_FormPage");

		// Assert
		warning.Should().BeNull(because: "the default arrange publishes successfully");
		capturedToken.CanBeCanceled.Should().BeTrue(
			because: "the publish must run under a bounded timeout token so a hung handshake cannot stall the save");
	}
}
