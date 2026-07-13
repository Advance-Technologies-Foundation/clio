using Clio.Command;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
public class RegisterSsoProviderCommandTests : BaseCommandTests<RegisterSsoProviderOptions>
{

	private readonly IApplicationClient _applicationClient = Substitute.For<IApplicationClient>();
	private readonly ILogger _logger = Substitute.For<ILogger>();

	private const string SuccessResponse = "{\"id\":\"11111111-1111-1111-1111-111111111111\",\"code\":\"okta\",\"name\":\"Okta\"}";

	protected override void AdditionalRegistrations(IServiceCollection containerBuilder) {
		base.AdditionalRegistrations(containerBuilder);
		containerBuilder.AddSingleton(_applicationClient);
	}

	[SetUp]
	public override void Setup() {
		base.Setup();
		_applicationClient
			.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(SuccessResponse);
	}

	[TearDown]
	public override void TearDown() {
		_applicationClient.ClearReceivedCalls();
		_logger.ClearReceivedCalls();
		base.TearDown();
	}

	private RegisterSsoProviderCommand CreateSut() {
		RegisterSsoProviderCommand command = Container.GetRequiredService<RegisterSsoProviderCommand>();
		command.Logger = _logger;
		return command;
	}

	private static RegisterSsoProviderOptions RequiredOptions() =>
		new() {
			Code = "okta",
			Name = "Okta",
			Url = "https://my-org.okta.com",
			OidcClientId = "my-client-id",
			OidcClientSecret = "s3cr3t"
		};

	[Test]
	[Description("Verifies the command posts to the 0/-prefixed route on a .NET Framework environment")]
	public void Execute_ShouldPostToPrefixedRoute_WhenEnvironmentIsNetFramework() {
		// Arrange (base EnvironmentSettings has IsNetCore = false, i.e. .NET Framework)
		RegisterSsoProviderCommand command = CreateSut();

		// Act
		int result = command.Execute(RequiredOptions());

		// Assert
		result.Should().Be(0, "because the server returned a valid provider payload");
		_applicationClient.Received().ExecutePostRequest(
			"http://localhost/0/api/SsoProvider/Register", Arg.Any<string>(),
			Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
	}

	[Test]
	[Description("Verifies the command posts to the unprefixed route on a .NET Core environment")]
	public void Execute_ShouldPostToUnprefixedRoute_WhenEnvironmentIsNetCore() {
		// Arrange
		EnvironmentSettings netCore = new() { Uri = "http://test", IsNetCore = true, Login = "", Password = "" };
		RegisterSsoProviderCommand command = new(_applicationClient, netCore) { Logger = _logger };

		// Act
		int result = command.Execute(RequiredOptions());

		// Assert
		result.Should().Be(0, "because the server returned a valid provider payload");
		_applicationClient.Received().ExecutePostRequest(
			"http://test/api/SsoProvider/Register", Arg.Any<string>(),
			Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
	}

	[Test]
	[Description("Verifies the request body carries the required fields as camelCase properties")]
	public void Execute_ShouldSendCamelCasePayload_WhenRequiredOptionsProvided() {
		// Arrange
		string body = null;
		_applicationClient
			.ExecutePostRequest(Arg.Any<string>(), Arg.Do<string>(b => body = b),
				Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(SuccessResponse);
		RegisterSsoProviderCommand command = CreateSut();

		// Act
		int result = command.Execute(RequiredOptions());

		// Assert
		result.Should().Be(0, "because the payload is valid");
		body.Should().Contain("\"code\":\"okta\"", "because the provider code is sent");
		body.Should().Contain("\"name\":\"Okta\"", "because the display name is sent");
		body.Should().Contain("\"url\":\"https://my-org.okta.com\"", "because the issuer url is sent");
		body.Should().Contain("\"clientId\":\"my-client-id\"", "because the OIDC client id is sent");
		body.Should().Contain("\"clientSecret\":\"s3cr3t\"", "because the resolved secret is sent");
	}

	[Test]
	[Description("Verifies optional fields are omitted from the payload when not provided")]
	public void Execute_ShouldOmitOptionalFields_WhenNotProvided() {
		// Arrange
		string body = null;
		_applicationClient
			.ExecutePostRequest(Arg.Any<string>(), Arg.Do<string>(b => body = b),
				Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(SuccessResponse);
		RegisterSsoProviderCommand command = CreateSut();

		// Act
		command.Execute(RequiredOptions());

		// Assert
		body.Should().NotContain("discoveryUrl", "because --discovery-url was not provided");
		body.Should().NotContain("logoutUrl", "because --logout-url was not provided");
	}

	[Test]
	[Description("Verifies providing both secret sources fails without calling the server")]
	public void Execute_ShouldReturnOne_WhenBothSecretSourcesProvided() {
		// Arrange
		RegisterSsoProviderOptions options = RequiredOptions();
		options.OidcClientSecretFile = "./some.secret";
		RegisterSsoProviderCommand command = CreateSut();

		// Act
		int result = command.Execute(options);

		// Assert
		result.Should().Be(1, "because the secret sources are mutually exclusive");
		_applicationClient.DidNotReceive().ExecutePostRequest(
			Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
	}

	[Test]
	[Description("Verifies a missing secret file fails without calling the server")]
	public void Execute_ShouldReturnOne_WhenSecretFileMissing() {
		// Arrange
		RegisterSsoProviderOptions options = RequiredOptions();
		options.OidcClientSecret = null;
		options.OidcClientSecretFile = "./does-not-exist.secret";
		RegisterSsoProviderCommand command = CreateSut();

		// Act
		int result = command.Execute(options);

		// Assert
		result.Should().Be(1, "because the secret file does not exist");
		_applicationClient.DidNotReceive().ExecutePostRequest(
			Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
	}

	[Test]
	[Description("Verifies an ErrorInfo response is treated as a failure")]
	public void Execute_ShouldReturnOne_WhenResponseIsErrorInfo() {
		// Arrange
		_applicationClient
			.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("{\"error\":\"Conflict\",\"error_description\":\"provider already exists\"}");
		RegisterSsoProviderCommand command = CreateSut();

		// Act
		int result = command.Execute(RequiredOptions());

		// Assert
		result.Should().Be(1, "because an ErrorInfo payload is not a successful registration");
	}

	[Test]
	[Description("Verifies a non-JSON response (e.g. login page or 404) is treated as a failure")]
	public void Execute_ShouldReturnOne_WhenResponseIsNotJsonObject() {
		// Arrange
		_applicationClient
			.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("<!DOCTYPE html><html><body>Sign in</body></html>");
		RegisterSsoProviderCommand command = CreateSut();

		// Act
		int result = command.Execute(RequiredOptions());

		// Assert
		result.Should().Be(1, "because an HTML/non-JSON body means the request never reached the controller");
	}

	[Test]
	[Description("Verifies a bare JSON-string server message (e.g. a conflict) is surfaced as a failure")]
	public void Execute_ShouldReturnOne_WhenResponseIsJsonStringMessage() {
		// Arrange
		_applicationClient
			.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("\"SsoProvider with Code 'Test1code' already exists.\"");
		RegisterSsoProviderCommand command = CreateSut();

		// Act
		int result = command.Execute(RequiredOptions());

		// Assert
		result.Should().Be(1, "because the provider already exists (create-only conflict)");
		_logger.Received().WriteError("SsoProvider with Code 'Test1code' already exists.");
	}

	[Test]
	[Description("Verifies json format pretty-prints the full server payload")]
	public void Execute_ShouldPrintPayload_WhenFormatIsJson() {
		// Arrange
		RegisterSsoProviderOptions options = RequiredOptions();
		options.Format = IdentityOutputFormat.Json;
		RegisterSsoProviderCommand command = CreateSut();

		// Act
		int result = command.Execute(options);

		// Assert
		result.Should().Be(0, "because registration succeeded");
		_logger.Received().WriteLine(Arg.Is<string>(s => s.Contains("11111111-1111-1111-1111-111111111111")));
	}

}
