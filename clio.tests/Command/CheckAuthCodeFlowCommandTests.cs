using Clio.Command;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
public class CheckAuthCodeFlowCommandTests : BaseCommandTests<CheckAuthCodeFlowOptions>
{

	private readonly IServiceUrlBuilder _serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
	private readonly IApplicationClient _applicationClient = Substitute.For<IApplicationClient>();
	private readonly ILogger _logger = Substitute.For<ILogger>();

	private const string Endpoint = "http://test/0/identityServiceInfo/canUseAuthorizationCodeFlow";

	protected override void AdditionalRegistrations(IServiceCollection containerBuilder) {
		base.AdditionalRegistrations(containerBuilder);
		containerBuilder.AddSingleton(_serviceUrlBuilder);
		containerBuilder.AddSingleton(_applicationClient);
	}

	[SetUp]
	public override void Setup() {
		base.Setup();
		_serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.IdentityServiceInfoCanUseAuthorizationCodeFlow)
			.Returns(Endpoint);
		_applicationClient
			.ExecuteGetRequest(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("true");
	}

	[TearDown]
	public override void TearDown() {
		_serviceUrlBuilder.ClearReceivedCalls();
		_applicationClient.ClearReceivedCalls();
		_logger.ClearReceivedCalls();
		base.TearDown();
	}

	private CheckAuthCodeFlowCommand CreateSut() {
		CheckAuthCodeFlowCommand command = Container.GetRequiredService<CheckAuthCodeFlowCommand>();
		command.Logger = _logger;
		return command;
	}

	[Test]
	[Description("Verifies the command GETs the canUseAuthorizationCodeFlow endpoint and succeeds")]
	public void Execute_ShouldGetCanUseAuthorizationCodeFlowEndpoint_WhenInvoked() {
		// Arrange
		CheckAuthCodeFlowCommand command = CreateSut();
		CheckAuthCodeFlowOptions options = new();

		// Act
		int result = command.Execute(options);

		// Assert
		result.Should().Be(0, "because the server returned a valid boolean");
		_serviceUrlBuilder.Received().Build(ServiceUrlBuilder.KnownRoute.IdentityServiceInfoCanUseAuthorizationCodeFlow);
		_applicationClient.Received().ExecuteGetRequest(Endpoint, Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
	}

	[Test]
	[Description("Verifies that text format prints the plain boolean value")]
	public void Execute_ShouldPrintPlainBoolean_WhenFormatIsText() {
		// Arrange
		CheckAuthCodeFlowCommand command = CreateSut();
		CheckAuthCodeFlowOptions options = new() { Format = IdentityOutputFormat.Text };

		// Act
		int result = command.Execute(options);

		// Assert
		result.Should().Be(0, "because the flag was read successfully");
		_logger.Received().WriteLine("true");
	}

	[Test]
	[Description("Verifies that an ErrorInfo response is treated as a failure")]
	public void Execute_ShouldReturnOne_WhenResponseIsErrorInfo() {
		// Arrange
		_applicationClient
			.ExecuteGetRequest(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("{\"error\":\"InternalServerError\",\"error_description\":\"boom\"}");
		CheckAuthCodeFlowCommand command = CreateSut();
		CheckAuthCodeFlowOptions options = new();

		// Act
		int result = command.Execute(options);

		// Assert
		result.Should().Be(1, "because an ErrorInfo payload is not a valid flag response");
	}

	[Test]
	[Description("Verifies that json format prints a canUseAuthorizationCodeFlow object")]
	public void Execute_ShouldPrintFlagObject_WhenFormatIsJson() {
		// Arrange
		_applicationClient
			.ExecuteGetRequest(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("false");
		CheckAuthCodeFlowCommand command = CreateSut();
		CheckAuthCodeFlowOptions options = new() { Format = IdentityOutputFormat.Json };

		// Act
		int result = command.Execute(options);

		// Assert
		result.Should().Be(0, "because the flag was read successfully");
		_logger.Received().WriteLine("{\"canUseAuthorizationCodeFlow\":false}");
	}

}
