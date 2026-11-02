namespace Clio.Tests.Command;

using System.Collections.Generic;
using System.Linq;
using Clio.Command;
using Clio.Command.Theming;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

[TestFixture]
[Property("Module", "Command")]
public sealed class ListThemesCommandTests : BaseCommandTests<ListThemesOptions> {

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
	public void ListThemes_ShouldFormCorrectApplicationRequest_WhenApplicationRunsUnderNetFramework() {
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
	public void ListThemes_ShouldFormCorrectApplicationRequest_WhenApplicationRunsUnderNetCore() {
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
	public void ListThemes_ShouldParseThemesFromValues_WhenResponseReportsSuccess() {
		// Arrange
		_applicationClient.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(),
				Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("{\"success\":true,\"values\":[" +
				"{\"id\":\"dark\",\"caption\":\"Dark\",\"cssClassName\":\"theme-dark\",\"cssFilePath\":\"a/theme.css\"}," +
				"{\"id\":\"light\",\"caption\":\"Light\",\"cssClassName\":\"theme-light\",\"cssFilePath\":\"b/theme.css\"}]}");

		// Act
		bool succeeded = _command.TryGetAvailableThemes(new ListThemesOptions(),
			out IReadOnlyList<ThemeDescriptor> themes, out _);

		// Assert
		succeeded.Should().BeTrue(
			because: "a ListResponse with success=true means the theme catalog was read");
		themes.Should().HaveCount(2,
			because: "both themes from the values array must be exposed");
		ThemeDescriptor dark = themes.Single(theme => theme.Id == "dark");
		dark.Caption.Should().Be("Dark", because: "the caption field must be mapped from the response");
		dark.CssClassName.Should().Be("theme-dark", because: "the cssClassName field must be mapped from the response");
		dark.CssFilePath.Should().Be("a/theme.css", because: "the cssFilePath field must be mapped from the response");
	}

	[Test, Category("Unit")]
	[Description("Returns success with an empty theme list when the response carries no values (e.g. unlicensed caller).")]
	public void ListThemes_ShouldReturnEmptyList_WhenResponseHasNoValues() {
		// Arrange
		_applicationClient.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(),
				Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("{\"success\":true,\"values\":[]}");

		// Act
		bool succeeded = _command.TryGetAvailableThemes(new ListThemesOptions(),
			out IReadOnlyList<ThemeDescriptor> themes, out _);

		// Assert
		succeeded.Should().BeTrue(
			because: "an empty catalog (e.g. a caller without CanCustomizeBranding) is not an error");
		themes.Should().BeEmpty(because: "there are no themes to expose");
	}

	[Test, Category("Unit")]
	[Description("Returns failure exit code and surfaces the ThemeService error message when the response reports success=false with an errorInfo.")]
	public void ListThemes_ShouldReturnFailureAndLogErrorInfoMessage_WhenResponseReportsFailure() {
		// Arrange
		_applicationClient.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(),
				Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("{\"success\":false,\"errorInfo\":{\"errorCode\":\"SecurityException\",\"message\":\"no permission\"}}");

		// Act
		int exitCode = _command.Execute(new ListThemesOptions());

		// Assert
		exitCode.Should().Be(1,
			because: "an explicit success=false in the ThemeService response must surface as a command failure");
		// Verifies the command surfaces the server-provided errorInfo.message so the user sees why the read failed.
		_logger.Received(1).WriteError(Arg.Is<string>(message => message.Contains("no permission")));
	}

	[Test, Category("Unit")]
	[Description("Fails the read when the response body is a non-empty non-JSON payload (e.g. an auth redirect): ThemeService always answers with JSON, so a non-JSON body means the request never reached the service.")]
	public void ListThemes_ShouldReturnFailureAndLogError_WhenResponseBodyIsNotParseableJson() {
		// Arrange
		_applicationClient.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(),
				Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("OK");

		// Act
		int exitCode = _command.Execute(new ListThemesOptions());

		// Assert
		exitCode.Should().Be(1,
			because: "a non-JSON body signals the read did not reach ThemeService and must surface as a failure");
		_logger.Received(1).WriteError(Arg.Is<string>(message => message.Contains("Unexpected response from server")));
	}

	[Test, Category("Unit")]
	[Description("Prints the catalog with exit code 0 when a theme omits descriptor fields (e.g. no cssFilePath yet), instead of crashing on a null table cell.")]
	public void ListThemes_ShouldPrintCatalog_WhenThemeFieldsAreMissing() {
		// Arrange
		_applicationClient.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(),
				Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("{\"success\":true,\"values\":[{\"id\":\"bare\"}]}");

		// Act
		int exitCode = _command.Execute(new ListThemesOptions());

		// Assert
		exitCode.Should().Be(0,
			because: "a theme with missing optional descriptor fields is still a readable catalog entry");
		// Verifies the table was rendered (with empty cells), i.e. the print path survived the null fields.
		_logger.Received(1).WriteInfo(Arg.Is<string>(message => message.Contains("bare")));
	}

	[Test, Category("Unit")]
	[Description("Replaces control characters (carriage return, line feed, ANSI escape) in server-provided descriptor fields with spaces before printing the catalog so a hostile theme caption cannot forge extra output lines or inject terminal escape sequences.")]
	public void ListThemes_ShouldReplaceControlCharactersWithSpaces_WhenPrintingCatalog() {
		// Arrange
		_applicationClient.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(),
				Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("{\"success\":true,\"values\":[" +
				"{\"id\":\"evil\",\"caption\":\"Dark\\r\\nInjected line\\u001b[31m\",\"cssClassName\":\"theme\",\"cssFilePath\":\"a/theme.css\"}]}");

		// Act
		int exitCode = _command.Execute(new ListThemesOptions());

		// Assert
		exitCode.Should().Be(0,
			because: "a well-formed catalog is still readable regardless of the descriptor field contents");
		_logger.Received(1).WriteInfo(Arg.Is<string>(message =>
			message.Contains("Dark  Injected line [31m")
			&& !message.Contains("Dark\nInjected")
			&& !message.Contains("Dark\rInjected")));
	}

	[Test, Category("Unit")]
	[Description("Treats an empty response body as an empty catalog (the contract default), so a minimal response is not misread as a failure.")]
	public void ListThemes_ShouldReturnEmptyList_WhenResponseBodyIsEmpty() {
		// Arrange
		_applicationClient.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(),
				Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(string.Empty);

		// Act
		int exitCode = _command.Execute(new ListThemesOptions());

		// Assert
		exitCode.Should().Be(0,
			because: "an empty body is the contract default and yields an empty catalog");
	}
}
