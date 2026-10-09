using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Clio.Command;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using FluentAssertions;
using ModelContextProtocol.Server;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
[Property("Module", "McpServer")]
public sealed class CallServiceGetToolTests {

	[TestCase("https://other.example/rest/Test")]
	[TestCase("//other.example/rest/Test")]
	[TestCase("/rest/Test")]
	[TestCase("../rest/Test")]
	[TestCase("rest/%2e%2e/Test")]
	[TestCase("rest%2fTest")]
	[TestCase("rest\\Test")]
	[TestCase("rest/Test#fragment")]
	[Category("Unit")]
	[Description("Refuses every route shape that could select another origin or escape the selected application path before resolving credentials.")]
	public async Task Execute_ShouldRejectUnsafeRoute_WhenPathCanEscapeSelectedApplication(string servicePath) {
		// Arrange
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		CallServiceGetTool tool = new(resolver, Substitute.For<IHttpClientFactory>());

		// Act
		CallServiceGetResponse response = await tool.Execute(new CallServiceGetArgs("main", servicePath));

		// Assert
		response.Success.Should().BeFalse(because: "an authenticated request must stay on its bound Creatio origin");
		response.ErrorClass.Should().Be("invalid-service-path");
		resolver.ReceivedCalls().Should().BeEmpty(because: "path validation must run before environment credentials are resolved");
	}

	[Test]
	[Category("Unit")]
	[Description("Uses the environment-scoped authenticated client for a relative GET and exposes only status, media type and the bounded JSON body.")]
	public async Task Execute_ShouldReturnStatusAndJsonBody_WhenBoundGetCompletes() {
		// Arrange
		const string responseJson = "{\"result\":12.5}";
		(IToolCommandResolver resolver, ICreatioApplicationClient client, IServiceUrlBuilder urlBuilder) =
			Build(HttpStatusCode.OK, responseJson);
		CallServiceGetTool tool = new(resolver, Substitute.For<IHttpClientFactory>());

		// Act
		CallServiceGetResponse response = await tool.Execute(new CallServiceGetArgs(
			"main", "rest/UsrBuilderDesignArithmeticService/Add?a=5&b=7.5", Timeout: 4_000));

		// Assert
		response.Success.Should().BeTrue(because: "a received HTTP response completes the read even when its status is later non-success");
		response.Status.Should().Be(200);
		response.ContentType.Should().Be("application/json");
		response.Body.Should().Be(responseJson);
		response.Error.Should().BeNull();
		urlBuilder.Received(1).Build("rest/UsrBuilderDesignArithmeticService/Add?a=5&b=7.5");
		await client.Received(1).ExecuteGetResponseBoundedAsync(
			"http://creatio/rest/UsrBuilderDesignArithmeticService/Add?a=5&b=7.5",
			CallServiceGetTool.MaxBodyBytes, 4_000, Arg.Any<CancellationToken>());
		resolver.Received(1).ResolvePair<IApplicationClient, IServiceUrlBuilder>(
			Arg.Is<EnvironmentOptions>(o => o.Environment == "main"));
		resolver.DidNotReceive().Resolve<IApplicationClient>(Arg.Any<EnvironmentOptions>());
		resolver.DidNotReceive().Resolve<IServiceUrlBuilder>(Arg.Any<EnvironmentOptions>());
	}

	[Test]
	[Category("Unit")]
	[Description("Returns a JSON error body with its real HTTP status so endpoint validation can distinguish a deliberate 400 from transport failure.")]
	public async Task Execute_ShouldReturnErrorStatusAndBody_WhenServiceRejectsInput() {
		// Arrange
		const string body = "{\"error\":\"b must not be zero\"}";
		(IToolCommandResolver resolver, _, _) = Build(HttpStatusCode.BadRequest, body);

		// Act
		CallServiceGetResponse response = await new CallServiceGetTool(resolver, Substitute.For<IHttpClientFactory>()).Execute(
			new CallServiceGetArgs("main", "rest/Arithmetic/Divide?a=1&b=0"));

		// Assert
		response.Success.Should().BeTrue(because: "HTTP 400 is endpoint evidence, not a failed or missing transport");
		response.Status.Should().Be(400);
		response.Body.Should().Be(body);
	}

	[Test]
	[Category("Unit")]
	[Description("Never exposes an HTML login or proxy page in the MCP transcript.")]
	public async Task Execute_ShouldOmitBody_WhenResponseIsNotJson() {
		// Arrange
		(IToolCommandResolver resolver, _, _) = Build(HttpStatusCode.OK, "<html>private login page</html>", "text/html");

		// Act
		CallServiceGetResponse response = await new CallServiceGetTool(resolver, Substitute.For<IHttpClientFactory>()).Execute(
			new CallServiceGetArgs("main", "rest/Arithmetic/Add?a=1&b=2"));

		// Assert
		response.Success.Should().BeFalse();
		response.ErrorClass.Should().Be("unexpected-content-type");
		response.Status.Should().Be(200);
		response.Body.Should().BeNull(because: "non-JSON bodies can contain login forms, proxy details or injected content");
		response.Error.Should().NotContain("private login page");
	}

	[Test]
	[Category("Unit")]
	[Description("Rejects a final response from another origin without returning its body.")]
	public async Task Execute_ShouldOmitBody_WhenClientFinishesOnAnotherOrigin() {
		// Arrange
		(IToolCommandResolver resolver, ICreatioApplicationClient client, _) = Build(HttpStatusCode.OK, "{\"secret\":true}");
		client.ExecuteGetResponseBoundedAsync(Arg.Any<string>(), Arg.Any<long>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
			.Returns(Bounded(HttpStatusCode.OK, "{\"secret\":true}", "application/json", "https://other.example/rest/Test"));

		// Act
		CallServiceGetResponse response = await new CallServiceGetTool(resolver, Substitute.For<IHttpClientFactory>()).Execute(
			new CallServiceGetArgs("main", "rest/Test"));

		// Assert
		response.Success.Should().BeFalse();
		response.ErrorClass.Should().Be("target-redirected");
		response.Body.Should().BeNull();
	}

	[Test]
	[Category("Unit")]
	[Description("Fails closed when the authenticated transport cannot prove the final request URI.")]
	public async Task Execute_ShouldOmitBody_WhenFinalUriIsMissing() {
		// Arrange
		(IToolCommandResolver resolver, ICreatioApplicationClient client, _) = Build(HttpStatusCode.OK, "{\"secret\":true}");
		client.ExecuteGetResponseBoundedAsync(Arg.Any<string>(), Arg.Any<long>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
			.Returns(new BoundedGetResponse(200, "application/json", null, Encoding.UTF8.GetBytes("{\"secret\":true}")));

		// Act
		CallServiceGetResponse response = await new CallServiceGetTool(
			resolver,
			Substitute.For<IHttpClientFactory>()).Execute(new CallServiceGetArgs("main", "rest/Test"));

		// Assert
		response.Success.Should().BeFalse();
		response.ErrorClass.Should().Be("target-redirected");
		response.Body.Should().BeNull();
	}

	[Test]
	[Category("Unit")]
	[Description("Rejects an oversized response without copying any portion of it into the result.")]
	public async Task Execute_ShouldOmitBody_WhenJsonResponseExceedsLimit() {
		// Arrange
		(IToolCommandResolver resolver, ICreatioApplicationClient client, _) = Build(HttpStatusCode.OK, "{}");
		client.ExecuteGetResponseBoundedAsync(Arg.Any<string>(), Arg.Any<long>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
			.Returns<Task<BoundedGetResponse>>(_ => throw new ResponseTooLargeException(
				CallServiceGetTool.MaxBodyBytes + 1,
				CallServiceGetTool.MaxBodyBytes));

		// Act
		CallServiceGetResponse response = await new CallServiceGetTool(resolver, Substitute.For<IHttpClientFactory>()).Execute(
			new CallServiceGetArgs("main", "rest/Test"));

		// Assert
		response.Success.Should().BeFalse();
		response.ErrorClass.Should().Be("response-too-large");
		response.Body.Should().BeNull(because: "even a prefix of an oversized remote body is unnecessary transcript data");
	}

	[Test]
	[Category("Unit")]
	[Description("Uses the existing cookie-free, no-redirect client for an anonymous probe and returns its rejection status without resolving environment credentials.")]
	public async Task Execute_ShouldReturnAnonymousRejection_WithoutCredentialsOrRedirectFollowing() {
		// Arrange
		RecordingHandler handler = new(_ => Response(
			HttpStatusCode.Found,
			string.Empty,
			"text/plain",
			"http://creatio/rest/Test"));
		IHttpClientFactory factory = Substitute.For<IHttpClientFactory>();
		factory.CreateClient(EnvironmentAvailabilityProbe.HttpClientName)
			.Returns(_ => new HttpClient(handler, disposeHandler: false));
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		IServiceUrlBuilder urlBuilder = Substitute.For<IServiceUrlBuilder>();
		resolver.Resolve<IServiceUrlBuilder>(Arg.Any<EnvironmentOptions>()).Returns(urlBuilder);
		urlBuilder.Build("rest/Test").Returns("http://creatio/rest/Test");
		CallServiceGetTool tool = new(resolver, factory);

		// Act
		CallServiceGetResponse response = await tool.Execute(new CallServiceGetArgs(
			"main", "rest/Test", Authenticated: false, Timeout: 4_000));

		// Assert
		response.Success.Should().BeTrue(because: "an HTTP rejection is the expected anonymous endpoint evidence");
		response.Status.Should().Be(302);
		response.Body.Should().BeEmpty();
		handler.Requests.Should().ContainSingle();
		HttpRequestMessage request = handler.Requests.Single();
		request.Method.Should().Be(HttpMethod.Get);
		request.RequestUri.Should().Be("http://creatio/rest/Test");
		request.Headers.Authorization.Should().BeNull();
		request.Headers.Contains("Cookie").Should().BeFalse();
		resolver.DidNotReceive().Resolve<IApplicationClient>(Arg.Any<EnvironmentOptions>());
		factory.Received(1).CreateClient(EnvironmentAvailabilityProbe.HttpClientName);
	}

	[Test]
	[Category("Unit")]
	[Description("Returns a bounded plain-text endpoint response without widening the body contract to HTML.")]
	public async Task Execute_ShouldReturnBody_WhenResponseIsPlainText() {
		// Arrange
		(IToolCommandResolver resolver, _, _) = Build(HttpStatusCode.OK, "ready", "text/plain");

		// Act
		CallServiceGetResponse response = await new CallServiceGetTool(
			resolver,
			Substitute.For<IHttpClientFactory>()).Execute(new CallServiceGetArgs("main", "rest/Health"));

		// Assert
		response.Success.Should().BeTrue();
		response.Status.Should().Be(200);
		response.Body.Should().Be("ready");
	}

	[Test]
	[Category("Unit")]
	[Description("Advertises one stable read-only MCP contract with no generic write or open-world capability.")]
	public void Execute_ShouldAdvertiseReadOnlyClosedWorldContract() {
		// Act
		McpServerToolAttribute attribute = (McpServerToolAttribute)typeof(CallServiceGetTool)
			.GetMethod(nameof(CallServiceGetTool.Execute))!
			.GetCustomAttributes(typeof(McpServerToolAttribute), false)
			.Single();

		// Assert
		attribute.Name.Should().Be(CallServiceGetTool.ToolName);
		attribute.ReadOnly.Should().BeTrue();
		attribute.Destructive.Should().BeFalse();
		attribute.OpenWorld.Should().BeFalse();
	}

	private static (IToolCommandResolver resolver, ICreatioApplicationClient client, IServiceUrlBuilder urlBuilder)
		Build(HttpStatusCode status, string body, string mediaType = "application/json") {
		ICreatioApplicationClient client = Substitute.For<ICreatioApplicationClient>();
		IServiceUrlBuilder urlBuilder = Substitute.For<IServiceUrlBuilder>();
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		resolver.ResolvePair<IApplicationClient, IServiceUrlBuilder>(Arg.Any<EnvironmentOptions>())
			.Returns((client, urlBuilder));
		urlBuilder.Build(Arg.Any<string>()).Returns(call => "http://creatio/" + call.Arg<string>());
		client.ExecuteGetResponseBoundedAsync(Arg.Any<string>(), Arg.Any<long>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
			.Returns(call => Bounded(status, body, mediaType, call.Arg<string>()));
		return (resolver, client, urlBuilder);
	}

	private static HttpResponseMessage Response(
		HttpStatusCode status,
		string body,
		string mediaType,
		string requestUrl) {
		HttpResponseMessage response = new(status) {
			Content = new StringContent(body, Encoding.UTF8, mediaType),
			RequestMessage = new HttpRequestMessage(HttpMethod.Get, requestUrl)
		};
		response.Headers.TryAddWithoutValidation("Set-Cookie", "BPMCSRF=must-not-surface");
		return response;
	}

	private static BoundedGetResponse Bounded(
		HttpStatusCode status,
		string body,
		string mediaType,
		string requestUrl) =>
		new((int)status, mediaType, new Uri(requestUrl), Encoding.UTF8.GetBytes(body));

	private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
		: HttpMessageHandler {
		public List<HttpRequestMessage> Requests { get; } = [];

		protected override Task<HttpResponseMessage> SendAsync(
			HttpRequestMessage request,
			CancellationToken cancellationToken) {
			Requests.Add(request);
			return Task.FromResult(responseFactory(request));
		}
	}
}
