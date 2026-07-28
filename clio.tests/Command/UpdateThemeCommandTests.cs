namespace Clio.Tests.Command;

using Clio.Command;
using Clio.Command.Theming;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

[TestFixture]
[Property("Module", "Command")]
public sealed class UpdateThemeCommandTests : BaseCommandTests<UpdateThemeOptions>
{
	private IApplicationClient _applicationClient;
	private ILogger _logger;
	private UpdateThemeCommand _command;

	public override void Setup() {
		base.Setup();
		_command = Container.GetRequiredService<UpdateThemeCommand>();
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

	private static UpdateThemeOptions ValidOptions() => new() {
		Id = "ocean-theme",
		Caption = "Ocean",
		CssClassName = "ocean-theme",
		CssContent = ".ocean-theme{--crt-x:2}"
	};

	private void StubUpdateThemeSuccess() {
		_applicationClient.ExecutePostRequest(
				Arg.Is<string>(u => u.Contains("UpdateTheme")), Arg.Any<string>(),
				Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("{\"success\":true}");
	}

	[Test, Category("Unit")]
	[Description("Posts UpdateTheme to the WebApp-prefixed ThemeService path when the environment runs under .NET Framework.")]
	public void UpdateTheme_ShouldFormCorrectApplicationRequest_WhenApplicationRunsUnderNetFramework() {
		// Arrange
		EnvironmentSettings.IsNetCore = false;
		StubUpdateThemeSuccess();

		// Act
		_command.Execute(ValidOptions());

		// Assert
		_applicationClient.Received(1).ExecutePostRequest(
			Arg.Is<string>(u => u == "http://localhost/0/ServiceModel/ThemeService.svc/UpdateTheme"),
			Arg.Any<string>(), 100_000, 3, 1);
	}

	[Test, Category("Unit")]
	[Description("Posts UpdateTheme without the WebApp prefix when the environment runs under .NET Core.")]
	public void UpdateTheme_ShouldFormCorrectApplicationRequest_WhenApplicationRunsUnderNetCore() {
		// Arrange
		EnvironmentSettings.IsNetCore = true;
		StubUpdateThemeSuccess();

		// Act
		_command.Execute(ValidOptions());

		// Assert
		_applicationClient.Received(1).ExecutePostRequest(
			Arg.Is<string>(u => u == "http://localhost/ServiceModel/ThemeService.svc/UpdateTheme"),
			Arg.Any<string>(), 100_000, 3, 1);
	}

	[Test, Category("Unit")]
	[Description("Sends a camelCase full-overwrite body and omits the packageUId key (UpdateTheme cannot re-home a theme).")]
	public void UpdateTheme_ShouldSendBodyWithoutPackageUId_WhenExecuted() {
		// Arrange
		string capturedBody = null;
		_applicationClient.ExecutePostRequest(
				Arg.Is<string>(u => u.Contains("UpdateTheme")), Arg.Any<string>(),
				Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(ci => { capturedBody = ci.ArgAt<string>(1); return "{\"success\":true}"; });

		// Act
		int exitCode = _command.Execute(ValidOptions());

		// Assert
		exitCode.Should().Be(0, because: "a success=true response means the theme was overwritten");
		capturedBody.Should().Contain("\"id\":\"ocean-theme\"", because: "the target id must be sent");
		capturedBody.Should().Contain("\"cssClassName\":\"ocean-theme\"", because: "the css class name must be sent in camelCase");
		capturedBody.Should().NotContain("packageUId", because: "UpdateTheme has no package parameter — a theme cannot be re-homed");
	}

	[Test, Category("Unit")]
	[Description("Fails fast without any HTTP call when css-class-name violates the format rule.")]
	public void UpdateTheme_ShouldFailFastWithoutHttp_WhenCssClassNameInvalid() {
		// Arrange
		UpdateThemeOptions options = ValidOptions();
		options.CssClassName = "1-bad";

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(1, because: "an invalid css-class-name is rejected before any service call");
		_applicationClient.DidNotReceive().ExecutePostRequest(
			Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
		_logger.Received(1).WriteError(Arg.Is<string>(m => m.Contains("css-class-name")));
	}

	[Test, Category("Unit")]
	[Description("Returns failure and surfaces the ThemeService error message when the response reports success=false.")]
	public void UpdateTheme_ShouldReturnFailureAndLogErrorInfoMessage_WhenResponseReportsFailure() {
		// Arrange
		_applicationClient.ExecutePostRequest(
				Arg.Is<string>(u => u.Contains("UpdateTheme")), Arg.Any<string>(),
				Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("{\"success\":false,\"errorInfo\":{\"errorCode\":\"InvalidOperationException\",\"message\":\"theme not found\"}}");

		// Act
		int exitCode = _command.Execute(ValidOptions());

		// Assert
		exitCode.Should().Be(1, because: "an explicit success=false must surface as a command failure");
		_logger.Received(1).WriteError(Arg.Is<string>(m => m.Contains("theme not found")));
	}

	[Test, Category("Unit")]
	[Description("Fails the command when the response body is a non-empty non-JSON payload (e.g. an auth redirect): ThemeService always answers with JSON, so a non-JSON body means the update never reached the service.")]
	public void UpdateTheme_ShouldReturnFailureAndLogError_WhenResponseBodyIsNotParseableJson() {
		// Arrange
		_applicationClient.ExecutePostRequest(
				Arg.Is<string>(u => u.Contains("UpdateTheme")), Arg.Any<string>(),
				Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("OK");

		// Act
		int exitCode = _command.Execute(ValidOptions());

		// Assert
		exitCode.Should().Be(1, because: "a non-JSON body signals the update did not reach ThemeService and must surface as a failure");
		_logger.Received(1).WriteError(Arg.Is<string>(m => m.Contains("Unexpected response from server")));
	}

	[Test, Category("Unit")]
	[Description("Returns success when the response body is empty — an empty body is the ThemeService contract default for a successful update.")]
	public void UpdateTheme_ShouldReturnSuccess_WhenResponseBodyIsEmpty() {
		// Arrange
		_applicationClient.ExecutePostRequest(
				Arg.Is<string>(u => u.Contains("UpdateTheme")), Arg.Any<string>(),
				Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(string.Empty);

		// Act
		int exitCode = _command.Execute(ValidOptions());

		// Assert
		exitCode.Should().Be(0, because: "an empty body is the contract default for a successful update");
	}
}
