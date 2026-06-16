namespace Clio.Tests.Command;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Clio.Command;
using Clio.Common.BrowserSession;
using FluentAssertions;
using NUnit.Framework;

[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
public sealed class PageDesignerPresencePublishersTests {
	[Test]
	[Description("The WebSocket publisher sends the exact Designer Presence envelope through its socket transport and closes the connection afterwards.")]
	public async Task PublishAsync_ShouldSendExpectedEnvelope_WhenUsingWebSocketPublisher() {
		// Arrange
		var connection = new FakeClientWebSocketConnection();
		var factory = new FakeClientWebSocketConnectionFactory(connection);
		var sut = new WebSocketMessageChannelPublisher(factory);
		MessageChannelEnvelope envelope = MessageChannelEnvelope.Create(
			"DesignerPresence_page_usrpage_formpage",
			"BroadcastMsg",
			"""{"schemaName":"UsrPage_FormPage","schemaType":"page","schemaCaption":"UsrPage_FormPage","users":[{"sessionId":"session-123","id":"u","name":"N","mode":"save"}]}""");

		// Act
		await sut.PublishAsync(new MessageChannelPublishRequest(
			new Uri("ws://test/0/Nui/ViewModule.aspx.ashx"),
			[
				new BrowserCookie("BPMCSRF", "csrf-token", "test", "/", false, false, "Lax", -1),
				new BrowserCookie(".ASPXAUTH", "auth-token", "test", "/", true, false, "Lax", -1)
			],
			envelope));

		// Assert
		connection.CookieHeader.Should().Be("BPMCSRF=csrf-token; .ASPXAUTH=auth-token",
			because: "the websocket upgrade request must carry the harvested forms-auth cookies");
		connection.ConnectedUri.Should().Be(new Uri("ws://test/0/Nui/ViewModule.aspx.ashx"),
			because: "the publisher should connect to the resolved message-channel URL");
		connection.SentPayload.Should().Be(envelope.Serialize(),
			because: "the websocket publisher must send the exact server envelope payload");
		connection.CloseCallCount.Should().Be(1,
			because: "the publisher should close the short-lived notification socket after sending");
	}

	[Test]
	[Description("The SignalR publisher starts the hub connection and sends the exact Designer Presence envelope through SendMessage.")]
	public async Task PublishAsync_ShouldSendExpectedEnvelope_WhenUsingSignalRPublisher() {
		// Arrange
		var connection = new FakeMessageChannelHubConnection();
		var factory = new FakeMessageChannelHubConnectionFactory(connection);
		var sut = new SignalRMessageChannelPublisher(factory);
		MessageChannelEnvelope envelope = MessageChannelEnvelope.Create(
			"DesignerPresence_page_usrpage_formpage",
			"BroadcastMsg",
			"""{"schemaName":"UsrPage_FormPage","schemaType":"page","schemaCaption":"UsrPage_FormPage","users":[{"sessionId":"session-123","id":"u","name":"N","mode":"save"}]}""");
		var serviceUrl = new Uri("https://test/signalr-hubs/messages");
		BrowserCookie[] cookies = [
			new("BPMCSRF", "csrf-token", "test", "/", false, false, "Lax", -1)
		];

		// Act
		await sut.PublishAsync(new MessageChannelPublishRequest(serviceUrl, cookies, envelope));

		// Assert
		factory.CapturedServiceUrl.Should().Be(serviceUrl,
			because: "the SignalR publisher should build the hub connection for the resolved service URL");
		factory.CapturedCookies.Should().BeEquivalentTo(cookies,
			because: "the SignalR publisher should pass the harvested cookies into the hub connection factory");
		connection.StartCallCount.Should().Be(1,
			because: "the hub connection must be started before sending the message");
		connection.SentEnvelope.Should().BeSameAs(envelope,
			because: "the SignalR publisher must send the exact server envelope object");
	}

	private sealed class FakeClientWebSocketConnectionFactory(FakeClientWebSocketConnection connection)
		: IClientWebSocketConnectionFactory {
		public IClientWebSocketConnection Create() => connection;
	}

	private sealed class FakeClientWebSocketConnection : IClientWebSocketConnection {
		public string? CookieHeader { get; private set; }

		public Uri? ConnectedUri { get; private set; }

		public string? SentPayload { get; private set; }

		public int CloseCallCount { get; private set; }

		public void SetCookieHeader(string cookieHeader) => CookieHeader = cookieHeader;

		public Task ConnectAsync(Uri serviceUrl, CancellationToken cancellationToken) {
			ConnectedUri = serviceUrl;
			return Task.CompletedTask;
		}

		public Task SendTextAsync(string payload, CancellationToken cancellationToken) {
			SentPayload = payload;
			return Task.CompletedTask;
		}

		public Task CloseAsync(CancellationToken cancellationToken) {
			CloseCallCount++;
			return Task.CompletedTask;
		}

		public ValueTask DisposeAsync() => ValueTask.CompletedTask;
	}

	private sealed class FakeMessageChannelHubConnectionFactory(FakeMessageChannelHubConnection connection)
		: IMessageChannelHubConnectionFactory {
		public Uri? CapturedServiceUrl { get; private set; }

		public IReadOnlyList<BrowserCookie>? CapturedCookies { get; private set; }

		public IMessageChannelHubConnection Create(Uri serviceUrl, IReadOnlyList<BrowserCookie> cookies) {
			CapturedServiceUrl = serviceUrl;
			CapturedCookies = cookies;
			return connection;
		}
	}

	private sealed class FakeMessageChannelHubConnection : IMessageChannelHubConnection {
		public int StartCallCount { get; private set; }

		public MessageChannelEnvelope? SentEnvelope { get; private set; }

		public Task StartAsync(CancellationToken cancellationToken) {
			StartCallCount++;
			return Task.CompletedTask;
		}

		public Task SendMessageAsync(MessageChannelEnvelope message, CancellationToken cancellationToken) {
			SentEnvelope = message;
			return Task.CompletedTask;
		}

		public ValueTask DisposeAsync() => ValueTask.CompletedTask;
	}
}
