using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Clio.Command.OAuthAppConfiguration;
using Clio.Common;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.OAuthAppConfiguration;

[TestFixture]
[Property("Module", "Command")]
internal sealed class IdentityServerProbeTests : BaseClioModuleTests {
	private IApplicationClientFactory _applicationClientFactory;
	private IOwnedApplicationClient _applicationClient;
	private IIdentityServerProbe _sut;
	private IHttpClientFactory _httpClientFactory;

	protected override void AdditionalRegistrations(IServiceCollection containerBuilder) {
		base.AdditionalRegistrations(containerBuilder);
		_applicationClientFactory = Substitute.For<IApplicationClientFactory>();
		_applicationClient = Substitute.For<IOwnedApplicationClient>();
		_httpClientFactory = Substitute.For<IHttpClientFactory>();
		containerBuilder.AddSingleton(_applicationClientFactory);
		containerBuilder.AddSingleton(_httpClientFactory);
	}

	public override void Setup() {
		base.Setup();
		_sut = Container.GetRequiredService<IIdentityServerProbe>();
		_applicationClientFactory.CreateBearerEnvironmentClient(Arg.Any<EnvironmentSettings>(),
			Arg.Any<string>())
			.Returns(_applicationClient);
	}

	public override void TearDown() {
		_applicationClient?.Dispose();
		_applicationClient.ClearReceivedCalls();
		_applicationClientFactory.ClearReceivedCalls();
		_httpClientFactory.ClearReceivedCalls();
		base.TearDown();
	}

	[TestCase("{}")]
	[TestCase("[]")]
	[TestCase("not json")]
	[TestCase("{\"access_token\":\"secret-token\"}")]
	[TestCase("{\"access_token\":\"\",\"token_type\":\"Bearer\"}")]
	[TestCase("{\"access_token\":\"secret token\",\"token_type\":\"Bearer\"}")]
	[TestCase("{\"access_token\":\"secret-token\",\"token_type\":\"Basic\"}")]
	[Description("HTTP 200 cannot make an empty, malformed, or non-bearer token response pass verification.")]
	public void AcquireClientCredentialsToken_ShouldReturnEmpty_WhenResponseIsInvalid(string body) {
		// Arrange
		_httpClientFactory.CreateClient(Arg.Any<string>()).Returns(_ => new HttpClient(new ResponseHandler(body)));

		// Act
		string token = _sut.AcquireClientCredentialsToken("https://identity.example", "client", "secret");

		// Assert
		token.Should().BeEmpty(because: "only a usable bearer token can be tested against CRM");
	}

	[Test]
	[Description("Token validation accepts a nonempty bearer token without decoding or reimplementing its permissions.")]
	public void AcquireClientCredentialsToken_ShouldReturnToken_WhenBearerResponseIsValid() {
		// Arrange
		_httpClientFactory.CreateClient(Arg.Any<string>()).Returns(_ => new HttpClient(
			new ResponseHandler("{\"access_token\":\"opaque-token\",\"token_type\":\"bearer\"}")));

		// Act
		string token = _sut.AcquireClientCredentialsToken("https://identity.example", "client", "secret");

		// Assert
		token.Should().Be("opaque-token", because: "CRM owns validation of the issued token");
	}

	[TestCase("{}", false)]
	[TestCase("[]", false)]
	[TestCase("invalid", false)]
	[TestCase("{\"issuer\":\"https://identity.example\"}", false)]
	[TestCase("{\"issuer\":\"https://identity.example\",\"token_endpoint\":\"https://identity.example/connect/token\",\"jwks_uri\":\"https://identity.example/keys\",\"authorization_endpoint\":\"https://identity.example/connect/authorize\"}", true)]
	[TestCase("{\"issuer\":\"creatio.com\",\"token_endpoint\":\"https://identity.example/connect/token\",\"jwks_uri\":\"https://identity.example/keys\",\"authorization_endpoint\":\"https://identity.example/connect/authorize\"}", true)]
	[Description("Discovery validates the issuer and required endpoint metadata instead of trusting HTTP 200.")]
	public void IsDiscoveryReachable_ShouldValidateMetadata_WhenHttpStatusIsSuccessful(string body, bool expected) {
		// Arrange
		_httpClientFactory.CreateClient(Arg.Any<string>()).Returns(_ => new HttpClient(new ResponseHandler(body)));

		// Act
		bool result = _sut.IsDiscoveryReachable("https://identity.example");

		// Assert
		result.Should().Be(expected, because: "a discovery response must contain usable issuer and endpoint URLs");
	}

	[TestCase("<html>login</html>")]
	[TestCase("{}")]
	[TestCase("{\"success\":false}")]
	[Description("A login page or failed DataService response must not count as CRM accepting the token.")]
	public void RunBearerDataServiceSmokeTest_ShouldFail_WhenHttp200BodyIsNotSuccessful(string body) {
		// Arrange
		_applicationClient.ExecutePostRequestAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(),
			Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
			.Returns(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) }));

		// Act
		Action act = () => _sut.RunBearerDataServiceSmokeTest(new EnvironmentSettings { Uri = "https://crm.example" },
			"https://crm.example/select", "opaque-token");

		// Assert
		act.Should().Throw<InvalidOperationException>(because: "HTTP success alone does not prove CRM authentication")
			.WithMessage("CRM OAuth smoke request*", because: "the sanitized error identifies the failing check");
	}

	private sealed class ResponseHandler(string body) : HttpMessageHandler {
		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
			Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });
	}

	[Test]
	[Description("The bearer DataService smoke test uses an ephemeral CreatioClient instead of raw HttpClient.")]
	public void RunBearerDataServiceSmokeTest_ShouldUseApplicationClientFactory_WhenTokenIsPresent() {
		// Arrange
		EnvironmentSettings environment = new() { Uri = "https://dev.creatio.com", IsNetCore = true };
		_applicationClient.ExecutePostRequestAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(),
			Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
			.Returns(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent)));

		// Act
		int status = _sut.RunBearerDataServiceSmokeTest(environment,
			"https://dev.creatio.com/DataService/json/SyncReply/SelectQuery", "opaque-token");

		// Assert
		status.Should().Be(204, because: "the probe reports the exact Creatio response status");
		_applicationClientFactory.Received(1).CreateBearerEnvironmentClient(environment, "opaque-token");
		_ = _applicationClient.Received(1).ExecutePostRequestAsync(
			"https://dev.creatio.com/DataService/json/SyncReply/SelectQuery",
			Arg.Is<string>(body => body.Contains("\"rootSchemaName\":\"Contact\"")),
			100_000, 1, 1, Arg.Any<CancellationToken>());
		((IDisposable)_applicationClient).Received(1).Dispose();
	}

	[Test]
	[Description("The bearer DataService smoke test skips client creation when the token is empty.")]
	public void RunBearerDataServiceSmokeTest_ShouldReturnZero_WhenTokenIsMissing() {
		// Act
		int status = _sut.RunBearerDataServiceSmokeTest(new EnvironmentSettings(), "https://dev", string.Empty);

		// Assert
		status.Should().Be(0, because: "no authenticated request can be issued without a token");
		_applicationClientFactory.DidNotReceive().CreateBearerEnvironmentClient(
			Arg.Any<EnvironmentSettings>(), Arg.Any<string>());
	}
}
