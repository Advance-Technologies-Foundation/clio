namespace Clio.Tests.Command;

using System.Linq;
using Clio.Command;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

[TestFixture]
[Property("Module", "Command")]
public class ListThemesCommandTestCase : BaseCommandTests<ListThemesOptions> {

	private IApplicationClient _applicationClient;
	private ILogger _logger;
	private ListThemesCommand _command;

	public override void Setup() {
		base.Setup();
		// Resolve the SUT from the container so it is wired exactly as production (real IServiceUrlBuilder,
		// the shared EnvironmentSettings singleton); only the I/O boundary (IApplicationClient) is faked.
		_command = Container.GetRequiredService<ListThemesCommand>();
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
	[Description("Posts GetAvailableThemes to the WebApp-prefixed ThemeService path when the environment runs under .NET Framework.")]
	public void ListThemes_FormsCorrectApplicationRequest_WhenApplicationRunsUnderNetFramework() {
		// Arrange
		EnvironmentSettings.IsNetCore = false;

		// Act
		_command.Execute(new ListThemesOptions());

		// Assert
		_applicationClient.Received(1).ExecutePostRequest(
			"http://localhost/0/ServiceModel/ThemeService.svc/GetAvailableThemes",
			"{}", 100_000, 3, 1);
	}

	[Test, Category("Unit")]
	[Description("Posts GetAvailableThemes to the ThemeService path without the WebApp prefix when the environment runs under .NET Core.")]
	public void ListThemes_FormsCorrectApplicationRequest_WhenApplicationRunsUnderNetCore() {
		// Arrange
		EnvironmentSettings.IsNetCore = true;

		// Act
		_command.Execute(new ListThemesOptions());

		// Assert
		_applicationClient.Received(1).ExecutePostRequest(
			"http://localhost/ServiceModel/ThemeService.svc/GetAvailableThemes",
			"{}", 100_000, 3, 1);
	}

	[Test, Category("Unit")]
	[Description("Parses the values array from a successful ListResponse and exposes the themes with all descriptor fields.")]
	public void ListThemes_ParsesThemesFromValues_WhenResponseReportsSuccess() {
		// Arrange
		_applicationClient.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(),
				Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("{\"success\":true,\"values\":[" +
				"{\"id\":\"dark\",\"caption\":\"Dark\",\"cssClassName\":\"theme-dark\",\"cssFilePath\":\"a/theme.css\"}," +
				"{\"id\":\"light\",\"caption\":\"Light\",\"cssClassName\":\"theme-light\",\"cssFilePath\":\"b/theme.css\"}]}");

		// Act
		int exitCode = _command.Execute(new ListThemesOptions());

		// Assert
		exitCode.Should().Be(0,
			because: "a ListResponse with success=true means the theme catalog was read");
		_command.Themes.Should().HaveCount(2,
			because: "both themes from the values array must be exposed");
		ThemeDescriptor dark = _command.Themes.Single(theme => theme.Id == "dark");
		dark.Caption.Should().Be("Dark", because: "the caption field must be mapped from the response");
		dark.CssClassName.Should().Be("theme-dark", because: "the cssClassName field must be mapped from the response");
		dark.CssFilePath.Should().Be("a/theme.css", because: "the cssFilePath field must be mapped from the response");
	}

	[Test, Category("Unit")]
	[Description("Returns success with an empty theme list when the response carries no values (e.g. unlicensed caller).")]
	public void ListThemes_ReturnsEmptyList_WhenResponseHasNoValues() {
		// Arrange
		_applicationClient.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(),
				Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("{\"success\":true,\"values\":[]}");

		// Act
		int exitCode = _command.Execute(new ListThemesOptions());

		// Assert
		exitCode.Should().Be(0,
			because: "an empty catalog (e.g. a caller without CanCustomizeBranding) is not an error");
		_command.Themes.Should().BeEmpty(because: "there are no themes to expose");
	}

	[Test, Category("Unit")]
	[Description("Returns failure exit code and surfaces the ThemeService error message when the response reports success=false with an errorInfo.")]
	public void ListThemes_ReturnsFailureAndLogsErrorInfoMessage_WhenResponseReportsFailure() {
		// Arrange
		_applicationClient.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(),
				Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("{\"success\":false,\"errorInfo\":{\"errorCode\":\"SecurityException\",\"message\":\"no permission\"}}");

		// Act
		int exitCode = _command.Execute(new ListThemesOptions());

		// Assert
		exitCode.Should().Be(1,
			because: "an explicit success=false in the ThemeService response must surface as a command failure");
		_command.Themes.Should().BeEmpty(because: "a failed read must not expose any themes");
		// Verifies the command surfaces the server-provided errorInfo.message so the user sees why the read failed.
		_logger.Received(1).WriteError(Arg.Is<string>(message => message.Contains("no permission")));
	}

	[Test, Category("Unit")]
	[Description("Treats a non-JSON or empty response body as an empty catalog to avoid false negatives if the contract evolves.")]
	public void ListThemes_ReturnsEmptyList_WhenResponseBodyIsNotParseableJson() {
		// Arrange
		_applicationClient.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(),
				Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("OK");

		// Act
		int exitCode = _command.Execute(new ListThemesOptions());

		// Assert
		exitCode.Should().Be(0,
			because: "a non-JSON body must not be misread as a failure");
		_command.Themes.Should().BeEmpty(because: "an unparseable body yields no themes");
	}
}
