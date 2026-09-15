using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Clio.Command.McpServer.Relay;
using FluentAssertions;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using NUnit.Framework;
using McpServerLib = ModelContextProtocol.Server;

namespace Clio.Tests.Command.McpServer;

/// <summary>
/// Unit coverage for the PRODUCTION parent leg of the worker relay (ENG-95262 Stage 4a).
/// </summary>
/// <remarks>
/// <para>
/// The relay's own fixture drives a recording fake, so the one gate that decides whether a worker may sample
/// at all — <see cref="McpServerParentSession.SupportsSampling"/> over the live client's advertised
/// capabilities — was covered by nothing. That gate fails SILENTLY in both directions: read as false when the
/// client can in fact sample, the relay never advertises sampling on the child handshake and refuses the
/// child's request with method-not-found, and <c>update-page</c> reports <c>Skipped=true</c> with no error
/// anywhere.
/// </para>
/// <para>
/// So these tests run against a REAL <see cref="McpServerLib.McpServer"/>, built by its public factory over a
/// scripted client transport and driven through a real <c>initialize</c>. Nothing is stubbed on the
/// production side: the capability the assertions read is the one the SDK stored from the client's own
/// handshake.
/// </para>
/// </remarks>
[TestFixture]
[Property("Module", "McpServer")]
public sealed class McpServerParentSessionTests {

	private static readonly TimeSpan AssertionTimeout = TimeSpan.FromSeconds(10);

	[Test]
	[Category("Unit")]
	[Description("TC-U-401: the production parent session reports sampling as supported when the real client advertised the capability during initialize.")]
	public async Task SupportsSampling_ShouldBeTrue_WhenTheRealClientAdvertisedTheCapability() {
		// Arrange
		await using LiveClientSession client = await LiveClientSession.ConnectAsync(advertiseSampling: true);

		// Act
		IParentMcpSession parentSession = new McpServerParentSession(client.Server);

		// Assert
		parentSession.SupportsSampling.Should().BeTrue(
			"because the relay mirrors this onto the child's initialize: read as false against a client that "
			+ "CAN sample, the worker is never told to ask and the page semantic review silently degrades to "
			+ "Skipped=true with no error on any surface");
	}

	[Test]
	[Category("Unit")]
	[Description("TC-U-401: the production parent session reports sampling as unsupported when the real client advertised no sampling capability, which is the arm the relay's recording fake defaults away from.")]
	public async Task SupportsSampling_ShouldBeFalse_WhenTheRealClientAdvertisedNoCapability() {
		// Arrange
		await using LiveClientSession client = await LiveClientSession.ConnectAsync(advertiseSampling: false);

		// Act
		IParentMcpSession parentSession = new McpServerParentSession(client.Server);

		// Assert
		parentSession.SupportsSampling.Should().BeFalse(
			"because a client that never advertised sampling cannot serve a child's request: the relay has to "
			+ "refuse it up front rather than after a wasted round trip, and every relay test until now ran "
			+ "against a fake whose capability defaulted to true");
	}

	[Test]
	[Category("Unit")]
	[Description("TC-U-401: the production parent session hands a notification to the real client's transport as the very object it was given, so _meta.clioStageEvent and a numeric progressToken are never rebuilt.")]
	public async Task SendMessageAsync_ShouldHandTheNotificationToTheClientVerbatim_WhenTheRelayForwardsOne() {
		// Arrange
		await using LiveClientSession client = await LiveClientSession.ConnectAsync(advertiseSampling: true);
		IParentMcpSession parentSession = new McpServerParentSession(client.Server);
		JsonNode payload = new JsonObject {
			["progressToken"] = 42,
			["progress"] = 3,
			["_meta"] = new JsonObject { ["clioStageEvent"] = new JsonObject { ["sequence"] = 3 } }
		};
		JsonRpcNotification notification =
			new() { Method = "notifications/progress", Params = payload };

		// Act
		await parentSession.SendMessageAsync(notification, CancellationToken.None);

		// Assert
		JsonRpcNotification delivered = client.SentToClient.OfType<JsonRpcNotification>()
			.Single(sent => sent.Method == "notifications/progress");
		delivered.Params.Should().BeSameAs(payload,
			"because ClioRing correlates on the exact progressToken ordinally and reads _meta.clioStageEvent, "
			+ "and a mismatch there is dropped on the consumer side with no error — so this adapter must pass "
			+ "the subtree through rather than deserialise and rebuild it");
		delivered.Params.ToJsonString().Should().Contain("\"progressToken\":42",
			"because a numeric token retyped as a string no longer matches the token the client issued");
	}

	[Test]
	[Category("Unit")]
	[Description("TC-U-401: the production parent session carries a child's sampling request out to the REAL client and returns the client's own answer, which is the member ADR rule 1 depends on and the one the relay's fixture cannot exercise.")]
	public async Task SampleAsync_ShouldCarryTheRequestToTheRealClientAndReturnItsAnswer_WhenTheChildAsksForAReview() {
		// Arrange
		await using LiveClientSession client = await LiveClientSession.ConnectAsync(advertiseSampling: true);
		IParentMcpSession parentSession = new McpServerParentSession(client.Server);
		// MCP9005: the sampling payload types are deprecated in SDK 2.2.0 (SEP-2577). Suppressed with this
		// justification rather than silently, matching the production adapter: the feature still works, ADR
		// rule 1 depends on it, and OQ-6 tracks the migration to InputRequest / ResolveInputRequestsAsync.
#pragma warning disable MCP9005
		CreateMessageRequestParams requestParams = new() {
			MaxTokens = 500,
			Messages = [
				new SamplingMessage {
					Role = Role.User,
					Content = [new TextContentBlock { Text = "review this page" }]
				}
			]
		};

		// Act
		Task<CreateMessageResult> sampled = parentSession.SampleAsync(requestParams, CancellationToken.None)
			.AsTask();
		JsonRpcRequest askedOfTheClient = await client.WaitForRequestAsync("sampling/createMessage");
		// The scripted client ANSWERS, echoing the server's own request id: without the answer this test would
		// only prove that a method was called, which is precisely the coverage the previous pass called
		// plausible-but-unproven.
		client.FromClient(new JsonRpcResponse {
			Id = askedOfTheClient.Id,
			Result = JsonSerializer.SerializeToNode(new CreateMessageResult {
				Model = "scripted-client-model",
				Role = Role.Assistant,
				Content = [new TextContentBlock { Text = "{\"verdict\":\"ok\"}" }]
			}, McpJsonUtilities.DefaultOptions)
		});
		CreateMessageResult answer = await sampled;
#pragma warning restore MCP9005

		// Assert
		askedOfTheClient.Params["messages"][0]["content"]["text"].GetValue<string>()
			.Should().Be("review this page",
				"because the child's own prompt has to reach the real client — a relay that rebuilt or dropped it "
				+ "would make update-page's semantic review answer about the wrong page");
		answer.Model.Should().Be("scripted-client-model",
			"because the answer the caller gets must be the CLIENT's, not one this adapter invented: it is "
			+ "returned into the child's pending sampling request, and a fabricated one degrades the page review "
			+ "to Skipped=true with no error on any surface");
		answer.Content.Should().ContainSingle(
			"because the client answered with exactly one content block")
			.Which.Should().BeOfType<TextContentBlock>(
				"because the client's text block is what PageBodySamplingService parses its verdict out of")
			.Which.Text.Should().Contain("verdict",
				"because the round trip has to carry the client's payload, not just its shape");
	}

	[Test]
	[Category("Unit")]
	[Description("TC-U-401: a default-constructed parent session refuses to be used, because a struct can be default-constructed and a silently server-less session would look like a client that cannot sample.")]
	public async Task DefaultConstructedSession_ShouldRefuseToBeUsed_WhenItCarriesNoServer() {
		// Arrange
		IParentMcpSession parentSession = default(McpServerParentSession);

		// Act
		Func<Task> send = async () => await parentSession.SendMessageAsync(
			new JsonRpcNotification { Method = "notifications/progress" }, CancellationToken.None);

		// Assert
		parentSession.SupportsSampling.Should().BeFalse(
			"because there is no client to have advertised anything");
		await send.Should().ThrowAsync<InvalidOperationException>(
			"because this adapter is a struct — chosen so the DI assembly scan cannot pick it up — and the cost "
			+ "of that choice is that it can be default-constructed, so being given no live session has to fail "
			+ "loudly rather than drop the worker's traffic on the floor");
	}

	/// <summary>
	/// A real MCP server session with a scripted client on the other end: the client's <c>initialize</c> is
	/// written straight into the transport, so the capabilities the production adapter reads are the ones the
	/// SDK itself negotiated.
	/// </summary>
	private sealed class LiveClientSession : IAsyncDisposable {

		private readonly ScriptedClientTransport _transport;
		private readonly Task _serverRun;

		private LiveClientSession(ScriptedClientTransport transport, McpServerLib.McpServer server,
			Task serverRun) {
			_transport = transport;
			Server = server;
			_serverRun = serverRun;
		}

		internal McpServerLib.McpServer Server { get; }

		internal IReadOnlyList<JsonRpcMessage> SentToClient => _transport.SentToClient;

		/// <summary>Writes one message into the session as if the real client had sent it.</summary>
		internal void FromClient(JsonRpcMessage message) => _transport.FromClient(message);

		/// <summary>
		/// Waits for the server to ask the client <paramref name="method"/>, and returns that request so a
		/// test can answer it under the id the SDK itself issued.
		/// </summary>
		internal async Task<JsonRpcRequest> WaitForRequestAsync(string method) {
			Stopwatch elapsed = Stopwatch.StartNew();
			while (elapsed.Elapsed < AssertionTimeout) {
				JsonRpcRequest asked = _transport.SentToClient.OfType<JsonRpcRequest>()
					.FirstOrDefault(request => request.Method == method);
				if (asked is not null) {
					return asked;
				}
				await Task.Delay(10, CancellationToken.None);
			}
			throw new AssertionException(
				$"The server never asked the client '{method}', so nothing was relayed upward at all.");
		}

		internal static async Task<LiveClientSession> ConnectAsync(bool advertiseSampling) {
			ScriptedClientTransport transport = new();
			McpServerLib.McpServerOptions options = new() {
				ServerInfo = new Implementation { Name = "clio-parent-under-test", Version = "1" }
			};
			McpServerLib.McpServer server =
				McpServerLib.McpServer.Create(transport, options, loggerFactory: null, serviceProvider: null);
			Task serverRun = server.RunAsync(CancellationToken.None);
			JsonObject capabilities = new();
			if (advertiseSampling) {
				// Raw JSON rather than the typed SamplingCapability, which is [Obsolete] in SDK 2.2.0
				// (MCP9005, SEP-2577): a client's initialize is bytes on a wire, so the test has no reason to
				// touch the deprecated type at all. OQ-6 tracks the migration to InputRequest /
				// ResolveInputRequestsAsync.
				capabilities["sampling"] = new JsonObject();
			}
			transport.FromClient(new JsonRpcRequest {
				Id = new RequestId(1L),
				Method = "initialize",
				Params = new JsonObject {
					["protocolVersion"] = WorkerRelayOptions.MeasuredProtocolVersion,
					["capabilities"] = capabilities,
					["clientInfo"] = new JsonObject {
						["name"] = "scripted-client",
						["version"] = "1"
					}
				}
			});
			// The initialize RESPONSE reaching the wire is the observable proof that the SDK finished storing
			// the client's capabilities; the test never polls the deprecated capability property itself.
			Stopwatch elapsed = Stopwatch.StartNew();
			while (!transport.SentToClient.OfType<JsonRpcResponse>().Any()
				&& elapsed.Elapsed < AssertionTimeout) {
				await Task.Delay(10, CancellationToken.None);
			}
			transport.FromClient(new JsonRpcNotification { Method = "notifications/initialized" });
			return new LiveClientSession(transport, server, serverRun);
		}

		public async ValueTask DisposeAsync() {
			await Server.DisposeAsync();
			await _transport.DisposeAsync();
			try {
				await _serverRun;
			}
			catch (OperationCanceledException) {
				// Shutting the session down is how the run loop ends; that is not a failure.
			}
		}
	}

	/// <summary>
	/// The client end of a real server session: whatever the server sends is recorded, and whatever the test
	/// writes arrives as if the client had sent it.
	/// </summary>
	private sealed class ScriptedClientTransport : ITransport {

		private readonly Channel<JsonRpcMessage> _fromClient =
			Channel.CreateUnbounded<JsonRpcMessage>(new UnboundedChannelOptions { SingleReader = true });
		private readonly List<JsonRpcMessage> _sentToClient = [];
		private readonly object _sentLock = new();

		public string SessionId => "scripted-client";

		public ChannelReader<JsonRpcMessage> MessageReader => _fromClient.Reader;

		internal IReadOnlyList<JsonRpcMessage> SentToClient {
			get {
				lock (_sentLock) {
					return [.. _sentToClient];
				}
			}
		}

		public Task SendMessageAsync(JsonRpcMessage message, CancellationToken cancellationToken) {
			lock (_sentLock) {
				_sentToClient.Add(message);
			}
			return Task.CompletedTask;
		}

		internal void FromClient(JsonRpcMessage message) => _fromClient.Writer.TryWrite(message);

		public ValueTask DisposeAsync() {
			_fromClient.Writer.TryComplete();
			return default;
		}
	}
}
