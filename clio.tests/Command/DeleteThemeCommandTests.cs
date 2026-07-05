namespace Clio.Tests.Command;

using Clio.Command;
using Clio.Command.Theming;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

[TestFixture]
[Property("Module", "Command")]
public sealed class DeleteThemeCommandTests : BaseCommandTests<DeleteThemeOptions>
{
	private IApplicationClient _applicationClient;
	private ILogger _logger;
	private DeleteThemeCommand _command;

	public override void Setup() {
		base.Setup();
		_command = Container.GetRequiredService<DeleteThemeCommand>();
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

	private void StubDeleteThemeSuccess() {
		_applicationClient.ExecutePostRequest(
				Arg.Is<string>(u => u.Contains("DeleteTheme")), Arg.Any<string>(),
				Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("{\"success\":true}");
	}

	[Test, Category("Unit")]
	[Description("Posts DeleteTheme to the WebApp-prefixed ThemeService path when the environment runs under .NET Framework.")]
	public void DeleteTheme_ShouldFormCorrectApplicationRequest_WhenApplicationRunsUnderNetFramework() {
		// Arrange
		EnvironmentSettings.IsNetCore = false;
		StubDeleteThemeSuccess();

		// Act
		_command.Execute(new DeleteThemeOptions { Id = "ocean-theme" });

		// Assert
		_applicationClient.Received(1).ExecutePostRequest(
			Arg.Is<string>(u => u == "http://localhost/0/ServiceModel/ThemeService.svc/DeleteTheme"),
			Arg.Is<string>(b => b.Contains("\"id\":\"ocean-theme\"")), 100_000, 3, 1);
	}

	[Test, Category("Unit")]
	[Description("Posts DeleteTheme without the WebApp prefix when the environment runs under .NET Core, sending only the id.")]
	public void DeleteTheme_ShouldFormCorrectApplicationRequest_WhenApplicationRunsUnderNetCore() {
		// Arrange
		EnvironmentSettings.IsNetCore = true;
		StubDeleteThemeSuccess();

		// Act
		int exitCode = _command.Execute(new DeleteThemeOptions { Id = "ocean-theme" });

		// Assert
		exitCode.Should().Be(0, because: "a success=true response means the theme was deleted");
		_applicationClient.Received(1).ExecutePostRequest(
			Arg.Is<string>(u => u == "http://localhost/ServiceModel/ThemeService.svc/DeleteTheme"),
			Arg.Is<string>(b => b.Contains("\"id\":\"ocean-theme\"")), 100_000, 3, 1);
	}

	[Test, Category("Unit")]
	[Description("Fails fast without any HTTP call when the id violates the format rule.")]
	public void DeleteTheme_ShouldFailFastWithoutHttp_WhenIdInvalid() {
		// Act
		int exitCode = _command.Execute(new DeleteThemeOptions { Id = "bad id" });

		// Assert
		exitCode.Should().Be(1, because: "an invalid id is rejected before any service call");
		_applicationClient.DidNotReceive().ExecutePostRequest(
			Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
		_logger.Received(1).WriteError(Arg.Is<string>(m => m.Contains("id")));
	}

	[Test, Category("Unit")]
	[Description("Returns failure and surfaces the error message when the response reports success=false (deleting an unknown id is not idempotent).")]
	public void DeleteTheme_ShouldReturnFailureAndLogErrorInfoMessage_WhenResponseReportsFailure() {
		// Arrange
		_applicationClient.ExecutePostRequest(
				Arg.Is<string>(u => u.Contains("DeleteTheme")), Arg.Any<string>(),
				Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("{\"success\":false,\"errorInfo\":{\"errorCode\":\"InvalidOperationException\",\"message\":\"theme not found\"}}");

		// Act
		int exitCode = _command.Execute(new DeleteThemeOptions { Id = "ghost-theme" });

		// Assert
		exitCode.Should().Be(1, because: "delete is not idempotent — an unknown id surfaces as a failure");
		_logger.Received(1).WriteError(Arg.Is<string>(m => m.Contains("theme not found")));
	}

	[Test, Category("Unit")]
	[Description("Fails the command when the response body is a non-empty non-JSON payload (e.g. an auth redirect): ThemeService always answers with JSON, so a non-JSON body means the delete never reached the service.")]
	public void DeleteTheme_ShouldReturnFailureAndLogError_WhenResponseBodyIsNotParseableJson() {
		// Arrange
		_applicationClient.ExecutePostRequest(
				Arg.Is<string>(u => u.Contains("DeleteTheme")), Arg.Any<string>(),
				Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("OK");

		// Act
		int exitCode = _command.Execute(new DeleteThemeOptions { Id = "ocean-theme" });

		// Assert
		exitCode.Should().Be(1, because: "a non-JSON body signals the delete did not reach ThemeService and must surface as a failure");
		_logger.Received(1).WriteError(Arg.Is<string>(m => m.Contains("Unexpected response from server")));
	}

	[Test, Category("Unit")]
	[Description("Returns success when the response body is empty — an empty body is the ThemeService contract default for a successful delete.")]
	public void DeleteTheme_ShouldReturnSuccess_WhenResponseBodyIsEmpty() {
		// Arrange
		_applicationClient.ExecutePostRequest(
				Arg.Is<string>(u => u.Contains("DeleteTheme")), Arg.Any<string>(),
				Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(string.Empty);

		// Act
		int exitCode = _command.Execute(new DeleteThemeOptions { Id = "ocean-theme" });

		// Assert
		exitCode.Should().Be(0, because: "an empty body is the contract default for a successful delete");
	}
}
