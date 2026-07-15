using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Clio.Common.DbHub;
using Clio.Tests.Command;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Common.DbHub;

[TestFixture]
[Property("Module", "Common")]
public sealed class DbHubHttpClientTests : BaseClioModuleTests {
	private IHttpClientFactory _httpClientFactory;
	private RecordingHandler _handler;
	private IDbHubHttpClient _sut;

	protected override void AdditionalRegistrations(IServiceCollection containerBuilder) {
		base.AdditionalRegistrations(containerBuilder);
		_httpClientFactory = Substitute.For<IHttpClientFactory>();
		containerBuilder.AddSingleton(_httpClientFactory);
	}

	public override void Setup() {
		base.Setup();
		_handler = new RecordingHandler();
		_httpClientFactory.CreateClient(Arg.Any<string>()).Returns(_ => new HttpClient(_handler, disposeHandler: false));
		_sut = Container.GetRequiredService<IDbHubHttpClient>();
	}

	public override void TearDown() {
		_handler.Dispose();
		_httpClientFactory.ClearReceivedCalls();
		base.TearDown();
	}

	[Test]
	[Description("Server verification checks health and performs an MCP initialize handshake.")]
	public void VerifyServer_ShouldCheckHealthAndInitializeMcp() {
		// Arrange
		_handler.Responses.Enqueue(new HttpResponseMessage(HttpStatusCode.OK));
		_handler.Responses.Enqueue(JsonResponse("{\"jsonrpc\":\"2.0\",\"id\":1,\"result\":{}}"));

		// Act
		DbHubVerificationResult result = _sut.VerifyServer(Settings());

		// Assert
		result.Verified.Should().BeTrue(because: "both dbHub health and MCP initialization succeeded");
		_handler.Requests.Should().HaveCount(2, because: "verification requires both protocol surfaces");
		_handler.Requests[0].Method.Should().Be(HttpMethod.Get, because: "healthz is an HTTP GET endpoint");
		_handler.Requests[0].Uri.AbsolutePath.Should().Be("/healthz", because: "the official health path is healthz");
		_handler.Requests[1].Body.Should().Contain("\"method\":\"initialize\"",
			because: "an HTTP 200 alone does not prove the MCP endpoint works");
		_handler.Requests[1].ProtocolVersion.Should().Be("2025-06-18",
			because: "dbHub expects the negotiated MCP version on HTTP requests");
	}

	[Test]
	[Description("Server verification rejects an HTTP-successful MCP JSON-RPC error response.")]
	public void VerifyServer_ShouldRejectMcpErrorEnvelope() {
		// Arrange
		_handler.Responses.Enqueue(new HttpResponseMessage(HttpStatusCode.OK));
		_handler.Responses.Enqueue(JsonResponse(
			"{\"jsonrpc\":\"2.0\",\"id\":1,\"error\":{\"code\":-32601,\"message\":\"method missing\"}}"));

		// Act
		DbHubVerificationResult result = _sut.VerifyServer(Settings());

		// Assert
		result.Verified.Should().BeFalse(
			because: "HTTP 200 does not prove the dbHub endpoint completed MCP initialization");
	}

	[Test]
	[Description("Source verification recognizes source-scoped tools returned by dbHub hot reload.")]
	public void VerifySource_ShouldRecognizeSourceScopedTool() {
		// Arrange
		_handler.Responses.Enqueue(JsonResponse("[{\"id\":\"local_dev\",\"type\":\"postgres\"}]"));
		_handler.Responses.Enqueue(JsonResponse("{\"jsonrpc\":\"2.0\",\"id\":2,\"result\":{\"tools\":[{\"name\":\"execute_sql_local_dev\"}]}}"));

		// Act
		DbHubVerificationResult result = _sut.VerifySource(Settings(), "local_dev", expectedPresent: true,
			waitForReload: false);

		// Assert
		result.Verified.Should().BeTrue(because: "the inventory contains the exact source and MCP exposes tools");
	}

	[Test]
	[Description("Offline verification returns a safe warning without exception or endpoint details.")]
	public void VerifyServer_ShouldReturnSafeWarning_WhenOffline() {
		// Arrange
		_handler.Exception = new HttpRequestException("secret-host failure");

		// Act
		DbHubVerificationResult result = _sut.VerifyServer(Settings());

		// Assert
		result.Verified.Should().BeFalse(because: "the HTTP request failed");
		$"{result.Warning.Message} {result.Warning.Detail}".Should().NotContain("secret-host",
			because: "transport exceptions may contain sensitive endpoint details");
	}

	[Test]
	[Description("Refuses live verification when settings point outside the loopback trust boundary.")]
	public void VerifyServer_ShouldRefuseNonLoopbackEndpoint() {
		// Arrange
		DbHubSettings settings = Settings();
		settings.Host = "db.internal.example";

		// Act
		DbHubVerificationResult result = _sut.VerifyServer(settings);

		// Assert
		result.Verified.Should().BeFalse(because: "dbHub's unauthenticated HTTP transport is local-only");
		_httpClientFactory.DidNotReceive().CreateClient(Arg.Any<string>());
	}

	private static DbHubSettings Settings() => new() { Host = "127.0.0.1", Port = 17998 };

	private static HttpResponseMessage JsonResponse(string json) => new(HttpStatusCode.OK) {
		Content = new StringContent(json, Encoding.UTF8, "application/json")
	};

	private sealed class RecordingHandler : HttpMessageHandler {
		public Queue<HttpResponseMessage> Responses { get; } = new();
		public List<RecordedRequest> Requests { get; } = [];
		public HttpRequestException Exception { get; set; }

		protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
			CancellationToken cancellationToken) {
			Requests.Add(new RecordedRequest(request.Method, request.RequestUri,
				request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken),
				request.Headers.TryGetValues("MCP-Protocol-Version", out IEnumerable<string> values)
					? string.Join(",", values)
					: null));
			if (Exception is not null) {
				throw Exception;
			}
			return Responses.Dequeue();
		}
	}

	private sealed record RecordedRequest(HttpMethod Method, Uri Uri, string Body, string ProtocolVersion);
}
