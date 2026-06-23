using Clio.Command;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
public class RegenerateIdentitySigningKeyCommandTests : BaseCommandTests<RegenerateIdentitySigningKeyOptions>
{

	private readonly IServiceUrlBuilder _serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
	private readonly IApplicationClient _applicationClient = Substitute.For<IApplicationClient>();
	private readonly ILogger _logger = Substitute.For<ILogger>();

	private const string Endpoint = "http://test/0/identityAssertion/regenerateSigningKey";

	protected override void AdditionalRegistrations(IServiceCollection containerBuilder) {
		base.AdditionalRegistrations(containerBuilder);
		containerBuilder.AddSingleton(_serviceUrlBuilder);
		containerBuilder.AddSingleton(_applicationClient);
	}

	[SetUp]
	public override void Setup() {
		base.Setup();
		_serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.IdentityAssertionRegenerateSigningKey).Returns(Endpoint);
		// The endpoint returns 204 No Content, i.e. an empty body, on success.
		_applicationClient
			.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(string.Empty);
	}

	[TearDown]
	public override void TearDown() {
		_serviceUrlBuilder.ClearReceivedCalls();
		_applicationClient.ClearReceivedCalls();
		_logger.ClearReceivedCalls();
		base.TearDown();
	}

	private RegenerateIdentitySigningKeyCommand CreateSut() {
		RegenerateIdentitySigningKeyCommand command = Container.GetRequiredService<RegenerateIdentitySigningKeyCommand>();
		command.Logger = _logger;
		return command;
	}

	[Test]
	[Description("Verifies the command POSTs to the regenerate signing key endpoint and succeeds on empty body")]
	public void Execute_ShouldPostToRegenerateEndpoint_WhenInvoked() {
		// Arrange
		RegenerateIdentitySigningKeyCommand command = CreateSut();
		RegenerateIdentitySigningKeyOptions options = new();

		// Act
		int result = command.Execute(options);

		// Assert
		result.Should().Be(0, "because a 204 No Content body indicates success");
		_serviceUrlBuilder.Received().Build(ServiceUrlBuilder.KnownRoute.IdentityAssertionRegenerateSigningKey);
		_applicationClient.Received().ExecutePostRequest(Endpoint, string.Empty,
			Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
	}

	[Test]
	[Description("Verifies that text format prints a plain OK acknowledgement")]
	public void Execute_ShouldPrintOk_WhenFormatIsText() {
		// Arrange
		RegenerateIdentitySigningKeyCommand command = CreateSut();
		RegenerateIdentitySigningKeyOptions options = new() { Format = IdentityOutputFormat.Text };

		// Act
		int result = command.Execute(options);

		// Assert
		result.Should().Be(0, "because the key was regenerated successfully");
		_logger.Received().WriteLine("OK");
	}

	[Test]
	[Description("Verifies that an ErrorInfo response fails instead of reporting a false 'regenerated' success")]
	public void Execute_ShouldReturnOne_WhenResponseIsErrorInfo() {
		// Arrange
		_applicationClient
			.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("{\"error\":\"FeatureDisabled\",\"error_description\":\"The issuer is disabled.\"}");
		RegenerateIdentitySigningKeyCommand command = CreateSut();
		RegenerateIdentitySigningKeyOptions options = new();

		// Act
		int result = command.Execute(options);

		// Assert
		result.Should().Be(1, "because a FeatureDisabled response means the key was not regenerated");
		_logger.DidNotReceive().WriteLine("OK");
	}

	[Test]
	[Description("Verifies that json format prints a regenerated status object")]
	public void Execute_ShouldPrintStatusObject_WhenFormatIsJson() {
		// Arrange
		RegenerateIdentitySigningKeyCommand command = CreateSut();
		RegenerateIdentitySigningKeyOptions options = new() { Format = IdentityOutputFormat.Json };

		// Act
		int result = command.Execute(options);

		// Assert
		result.Should().Be(0, "because the key was regenerated successfully");
		_logger.Received().WriteLine(Arg.Is<string>(s => s.Contains("regenerated")));
	}

}
