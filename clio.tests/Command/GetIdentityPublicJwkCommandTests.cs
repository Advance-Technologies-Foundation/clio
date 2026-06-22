using Clio.Command;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
public class GetIdentityPublicJwkCommandTests : BaseCommandTests<GetIdentityPublicJwkOptions>
{

	private readonly IServiceUrlBuilder _serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
	private readonly IApplicationClient _applicationClient = Substitute.For<IApplicationClient>();
	private readonly ILogger _logger = Substitute.For<ILogger>();

	private const string Endpoint = "http://test/0/identityAssertion/publicJwk";
	private const string JwkResponse = "{\"kty\":\"EC\",\"crv\":\"P-256\",\"x\":\"abc\",\"y\":\"def\",\"alg\":\"ES256\",\"use\":\"sig\"}";

	protected override void AdditionalRegistrations(IServiceCollection containerBuilder) {
		base.AdditionalRegistrations(containerBuilder);
		containerBuilder.AddSingleton(_serviceUrlBuilder);
		containerBuilder.AddSingleton(_applicationClient);
	}

	[SetUp]
	public override void Setup() {
		base.Setup();
		_serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.IdentityAssertionPublicJwk).Returns(Endpoint);
		_applicationClient
			.ExecuteGetRequest(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(JwkResponse);
	}

	[TearDown]
	public override void TearDown() {
		_serviceUrlBuilder.ClearReceivedCalls();
		_applicationClient.ClearReceivedCalls();
		_logger.ClearReceivedCalls();
		base.TearDown();
	}

	private GetIdentityPublicJwkCommand CreateSut() {
		GetIdentityPublicJwkCommand command = Container.GetRequiredService<GetIdentityPublicJwkCommand>();
		command.Logger = _logger;
		return command;
	}

	[Test]
	[Description("Verifies the command GETs the public JWK endpoint and succeeds")]
	public void Execute_ShouldGetPublicJwkEndpoint_WhenInvoked() {
		// Arrange
		GetIdentityPublicJwkCommand command = CreateSut();
		GetIdentityPublicJwkOptions options = new();

		// Act
		int result = command.Execute(options);

		// Assert
		result.Should().Be(0, "because the server returned a valid JWK");
		_serviceUrlBuilder.Received().Build(ServiceUrlBuilder.KnownRoute.IdentityAssertionPublicJwk);
		_applicationClient.Received().ExecuteGetRequest(Endpoint, Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
	}

	[Test]
	[Description("Verifies that text format prints a compact single-line JWK")]
	public void Execute_ShouldPrintCompactJwk_WhenFormatIsText() {
		// Arrange
		GetIdentityPublicJwkCommand command = CreateSut();
		GetIdentityPublicJwkOptions options = new() { Format = IdentityOutputFormat.Text };

		// Act
		int result = command.Execute(options);

		// Assert
		result.Should().Be(0, "because the JWK was retrieved successfully");
		_logger.Received().WriteLine(Arg.Is<string>(s => s.Contains("\"kty\":\"EC\"") && !s.Contains("\n")));
	}

	[Test]
	[Description("Verifies that json format prints an indented JWK payload")]
	public void Execute_ShouldPrintIndentedJwk_WhenFormatIsJson() {
		// Arrange
		GetIdentityPublicJwkCommand command = CreateSut();
		GetIdentityPublicJwkOptions options = new() { Format = IdentityOutputFormat.Json };

		// Act
		int result = command.Execute(options);

		// Assert
		result.Should().Be(0, "because the JWK was retrieved successfully");
		_logger.Received().WriteLine(Arg.Is<string>(s => s.Contains("kty") && s.Contains("\n")));
	}

	[Test]
	[Description("Verifies that an ErrorInfo response (e.g. AccessDenied) is treated as a failure")]
	public void Execute_ShouldReturnOne_WhenResponseIsErrorInfo() {
		// Arrange
		_applicationClient
			.ExecuteGetRequest(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("{\"error\":\"AccessDenied\",\"error_description\":\"No permission.\"}");
		GetIdentityPublicJwkCommand command = CreateSut();
		GetIdentityPublicJwkOptions options = new();

		// Act
		int result = command.Execute(options);

		// Assert
		result.Should().Be(1, "because an ErrorInfo payload means no public key was returned");
	}

	[Test]
	[Description("Verifies that an empty response is treated as a failure")]
	public void Execute_ShouldReturnOne_WhenResponseIsEmpty() {
		// Arrange
		_applicationClient
			.ExecuteGetRequest(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(string.Empty);
		GetIdentityPublicJwkCommand command = CreateSut();
		GetIdentityPublicJwkOptions options = new();

		// Act
		int result = command.Execute(options);

		// Assert
		result.Should().Be(1, "because an empty body means no public key was returned");
	}

}
