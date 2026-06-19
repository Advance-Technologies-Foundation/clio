using Clio.Command;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
public class GetIdentityAssertionCommandTests : BaseCommandTests<GetIdentityAssertionOptions>
{

	private readonly IServiceUrlBuilder _serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
	private readonly IApplicationClient _applicationClient = Substitute.For<IApplicationClient>();
	private readonly ILogger _logger = Substitute.For<ILogger>();

	private const string Endpoint = "http://test/0/identityAssertion/currentUser";

	private const string SuccessResponse =
		"{\"assertion\":\"eyJ.header.signature\",\"assertionType\":\"urn:jwt\"," +
		"\"expiresIn\":300,\"expiresAt\":\"2026-06-18T12:00:00Z\",\"issuer\":\"crt\",\"audience\":\"idv3\"}";

	protected override void AdditionalRegistrations(IServiceCollection containerBuilder) {
		base.AdditionalRegistrations(containerBuilder);
		containerBuilder.AddSingleton(_serviceUrlBuilder);
		containerBuilder.AddSingleton(_applicationClient);
	}

	[SetUp]
	public override void Setup() {
		base.Setup();
		_serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.IdentityAssertionCurrentUser).Returns(Endpoint);
		_applicationClient
			.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(SuccessResponse);
	}

	[TearDown]
	public override void TearDown() {
		_serviceUrlBuilder.ClearReceivedCalls();
		_applicationClient.ClearReceivedCalls();
		_logger.ClearReceivedCalls();
		base.TearDown();
	}

	private GetIdentityAssertionCommand CreateSut() {
		GetIdentityAssertionCommand command = Container.GetRequiredService<GetIdentityAssertionCommand>();
		command.Logger = _logger;
		return command;
	}

	[Test]
	[Description("Verifies the command POSTs to the current-user assertion endpoint and succeeds")]
	public void Execute_ShouldPostToCurrentUserAssertionEndpoint_WhenInvoked() {
		// Arrange
		GetIdentityAssertionCommand command = CreateSut();
		GetIdentityAssertionOptions options = new();

		// Act
		int result = command.Execute(options);

		// Assert
		result.Should().Be(0, "because the server returned a valid assertion payload");
		_serviceUrlBuilder.Received().Build(ServiceUrlBuilder.KnownRoute.IdentityAssertionCurrentUser);
		_applicationClient.Received().ExecutePostRequest(Endpoint, string.Empty,
			Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
	}

	[Test]
	[Description("Verifies that text format prints only the assertion token")]
	public void Execute_ShouldPrintAssertionToken_WhenFormatIsText() {
		// Arrange
		GetIdentityAssertionCommand command = CreateSut();
		GetIdentityAssertionOptions options = new() { Format = IdentityOutputFormat.Text };

		// Act
		int result = command.Execute(options);

		// Assert
		result.Should().Be(0, "because the assertion was issued successfully");
		_logger.Received().WriteLine("eyJ.header.signature");
	}

	[Test]
	[Description("Verifies that json format prints the full structured payload")]
	public void Execute_ShouldPrintFullPayload_WhenFormatIsJson() {
		// Arrange
		GetIdentityAssertionCommand command = CreateSut();
		GetIdentityAssertionOptions options = new() { Format = IdentityOutputFormat.Json };

		// Act
		int result = command.Execute(options);

		// Assert
		result.Should().Be(0, "because the assertion was issued successfully");
		_logger.Received().WriteLine(Arg.Is<string>(s => s.Contains("assertion") && s.Contains("expiresIn")));
	}

	[Test]
	[Description("Verifies that a PascalCase assertion payload (.NET Framework serializer) is parsed in text mode")]
	public void Execute_ShouldPrintAssertionToken_WhenResponseIsPascalCase() {
		// Arrange
		const string pascalResponse =
			"{\"Assertion\":\"eyJ.header.signature\",\"AssertionType\":\"jwt\",\"ExpiresIn\":300," +
			"\"ExpiresAt\":\"2026-06-18T12:00:00Z\",\"Issuer\":\"crt\",\"Audience\":\"idv3\"}";
		_applicationClient
			.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(pascalResponse);
		GetIdentityAssertionCommand command = CreateSut();
		GetIdentityAssertionOptions options = new() { Format = IdentityOutputFormat.Text };

		// Act
		int result = command.Execute(options);

		// Assert
		result.Should().Be(0, "because case-insensitive parsing must accept the .NET Framework PascalCase payload");
		_logger.Received().WriteLine("eyJ.header.signature");
	}

	[Test]
	[Description("Verifies that an ErrorInfo response (e.g. FeatureDisabled) is treated as a failure")]
	public void Execute_ShouldReturnOne_WhenResponseIsErrorInfo() {
		// Arrange
		_applicationClient
			.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("{\"error\":\"FeatureDisabled\",\"error_description\":\"The endpoint is disabled.\"}");
		GetIdentityAssertionCommand command = CreateSut();
		GetIdentityAssertionOptions options = new();

		// Act
		int result = command.Execute(options);

		// Assert
		result.Should().Be(1, "because an ErrorInfo payload means the assertion was not issued");
	}

	[Test]
	[Description("Verifies that an empty response is treated as a failure")]
	public void Execute_ShouldReturnOne_WhenResponseIsEmpty() {
		// Arrange
		_applicationClient
			.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(string.Empty);
		GetIdentityAssertionCommand command = CreateSut();
		GetIdentityAssertionOptions options = new();

		// Act
		int result = command.Execute(options);

		// Assert
		result.Should().Be(1, "because an empty body means no assertion was issued");
	}

}
