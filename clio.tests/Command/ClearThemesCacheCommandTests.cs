namespace Clio.Tests.Command;

using Clio.Command;
using Clio.Command.Theming;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

[TestFixture]
[Property("Module", "Command")]
public sealed class ClearThemesCacheCommandTests : BaseCommandTests<ClearThemesCacheOptions> {

	private IApplicationClient _applicationClient;
	private ILogger _logger;
	private ClearThemesCacheCommand _command;

	public override void Setup() {
		base.Setup();
		// Resolve the SUT from the container so it is wired exactly as production (real IServiceUrlBuilder,
		// the shared EnvironmentSettings singleton); only the I/O boundary (IApplicationClient) is faked.
		_command = Container.GetRequiredService<ClearThemesCacheCommand>();
		// Logger is a settable property (not constructor-injected), so swap in a substitute to assert on
		// the failure-path diagnostics the command surfaces.
		_logger = Substitute.For<ILogger>();
		_command.Logger = _logger;
	}

	public override void TearDown() {
		_applicationClient.ClearReceivedCalls();
		base.TearDown();
	}

	protected override void AdditionalRegistrations(IServiceCollection containerBuilder) {
		base.AdditionalRegistrations(containerBuilder);
		_applicationClient = Substitute.For<IApplicationClient>();
		containerBuilder.AddTransient<IApplicationClient>(_ => _applicationClient);
	}

	[Test, Category("Unit")]
	[Description("Posts ClearThemesCache to the WebApp-prefixed ThemeService path when the environment runs under .NET Framework.")]
	public void ClearThemesCache_ShouldFormCorrectApplicationRequest_WhenApplicationRunsUnderNetFramework() {
		// Arrange
		EnvironmentSettings.IsNetCore = false;

		// Act
		_command.Execute(new ClearThemesCacheOptions());

		// Assert
		_applicationClient.Received(1).ExecutePostRequest(
			"http://localhost/0/ServiceModel/ThemeService.svc/ClearThemesCache",
			"{}", 100_000, 3, 1);
	}

	[Test, Category("Unit")]
	[Description("Posts ClearThemesCache to the ThemeService path without the WebApp prefix when the environment runs under .NET Core.")]
	public void ClearThemesCache_ShouldFormCorrectApplicationRequest_WhenApplicationRunsUnderNetCore() {
		// Arrange
		EnvironmentSettings.IsNetCore = true;

		// Act
		_command.Execute(new ClearThemesCacheOptions());

		// Assert
		_applicationClient.Received(1).ExecutePostRequest(
			"http://localhost/ServiceModel/ThemeService.svc/ClearThemesCache",
			"{}", 100_000, 3, 1);
	}

	[Test, Category("Unit")]
	[Description("Returns success exit code when the ThemeService responds with success=true.")]
	public void ClearThemesCache_ShouldReturnSuccess_WhenResponseReportsSuccess() {
		// Arrange
		_applicationClient.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(),
				Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("{\"success\":true}");

		// Act
		int exitCode = _command.Execute(new ClearThemesCacheOptions());

		// Assert
		exitCode.Should().Be(0,
			because: "a ThemeService BaseResponse with success=true means the theme cache was refreshed");
	}

	[Test, Category("Unit")]
	[Description("Returns failure exit code and surfaces the ThemeService error message when the response reports success=false with an errorInfo.")]
	public void ClearThemesCache_ShouldReturnFailureAndLogErrorInfoMessage_WhenResponseReportsFailure() {
		// Arrange
		_applicationClient.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(),
				Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("{\"success\":false,\"errorInfo\":{\"errorCode\":\"SecurityException\",\"message\":\"no permission\"}}");

		// Act
		int exitCode = _command.Execute(new ClearThemesCacheOptions());

		// Assert
		exitCode.Should().Be(1,
			because: "an explicit success=false in the ThemeService response must surface as a command failure");
		// Verifies the command surfaces the server-provided errorInfo.message so the user sees why the clear failed.
		_logger.Received(1).WriteError(Arg.Is<string>(message => message.Contains("no permission")));
	}

	[Test, Category("Unit")]
	[Description("Returns failure exit code and logs a generic diagnostic when the response reports success=false without an errorInfo block.")]
	public void ClearThemesCache_ShouldReturnFailureAndLogGenericMessage_WhenResponseReportsFailureWithoutErrorInfo() {
		// Arrange
		_applicationClient.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(),
				Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("{\"success\":false}");

		// Act
		int exitCode = _command.Execute(new ClearThemesCacheOptions());

		// Assert
		exitCode.Should().Be(1,
			because: "success=false must fail the command even when the server omits an errorInfo block");
		// With no errorInfo.message the command must still tell the user the clear failed and where to look.
		_logger.Received(1).WriteError(Arg.Is<string>(message => message.Contains("success=false")));
	}

	[Test, Category("Unit")]
	[Description("Fails the command when the response body is a non-empty non-JSON payload (e.g. an auth redirect): ThemeService always answers with JSON, so a non-JSON body means the clear never reached the service.")]
	public void ClearThemesCache_ShouldReturnFailureAndLogError_WhenResponseBodyIsNotParseableJson() {
		// Arrange
		_applicationClient.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(),
				Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("OK");

		// Act
		int exitCode = _command.Execute(new ClearThemesCacheOptions());

		// Assert
		exitCode.Should().Be(1,
			because: "a non-JSON body signals the clear did not reach ThemeService and must surface as a failure");
		_logger.Received(1).WriteError(Arg.Is<string>(message => message.Contains("Unexpected response from server")));
	}

	[Test, Category("Unit")]
	[Description("Treats an empty response body as success (the contract default), so a minimal success response is not misread as a failure.")]
	public void ClearThemesCache_ShouldReturnSuccess_WhenResponseBodyIsEmpty() {
		// Arrange
		_applicationClient.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(),
				Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(string.Empty);

		// Act
		int exitCode = _command.Execute(new ClearThemesCacheOptions());

		// Assert
		exitCode.Should().Be(0,
			because: "an empty body is the contract default for a successful clear");
	}
}
