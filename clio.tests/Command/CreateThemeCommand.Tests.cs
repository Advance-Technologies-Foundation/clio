namespace Clio.Tests.Command;

using System.Text.RegularExpressions;
using Clio.Command;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

[TestFixture]
[Property("Module", "Command")]
public class CreateThemeCommandTestCase : BaseCommandTests<CreateThemeOptions>
{
	private IApplicationClient _applicationClient;
	private ILogger _logger;
	private CreateThemeCommand _command;

	public override void Setup() {
		base.Setup();
		_command = Container.GetRequiredService<CreateThemeCommand>();
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

	private static CreateThemeOptions ValidOptions() => new() {
		Caption = "Ocean",
		CssClassName = "ocean-theme",
		CssContent = ".ocean-theme{--crt-x:1}"
	};

	private void StubCreateThemeSuccess() {
		_applicationClient.ExecutePostRequest(
				Arg.Is<string>(u => u.Contains("CreateTheme")), Arg.Any<string>(),
				Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("{\"success\":true}");
	}

	[Test, Category("Unit")]
	[Description("Posts CreateTheme to the WebApp-prefixed ThemeService path when the environment runs under .NET Framework.")]
	public void CreateTheme_FormsCorrectApplicationRequest_WhenApplicationRunsUnderNetFramework() {
		// Arrange
		EnvironmentSettings.IsNetCore = false;
		StubCreateThemeSuccess();

		// Act
		_command.Execute(ValidOptions());

		// Assert
		_applicationClient.Received(1).ExecutePostRequest(
			Arg.Is<string>(u => u == "http://localhost/0/ServiceModel/ThemeService.svc/CreateTheme"),
			Arg.Any<string>(), 100_000, 3, 1);
	}

	[Test, Category("Unit")]
	[Description("Posts CreateTheme to the ThemeService path without the WebApp prefix when the environment runs under .NET Core.")]
	public void CreateTheme_FormsCorrectApplicationRequest_WhenApplicationRunsUnderNetCore() {
		// Arrange
		EnvironmentSettings.IsNetCore = true;
		StubCreateThemeSuccess();

		// Act
		_command.Execute(ValidOptions());

		// Assert
		_applicationClient.Received(1).ExecutePostRequest(
			Arg.Is<string>(u => u == "http://localhost/ServiceModel/ThemeService.svc/CreateTheme"),
			Arg.Any<string>(), 100_000, 3, 1);
	}

	[Test, Category("Unit")]
	[Description("Sends a camelCase body that omits packageUId when --package-name is omitted, and an auto-generated UUID id when --id is omitted.")]
	public void CreateTheme_SendsCamelCaseBodyWithoutPackageUIdAndAutoId_WhenIdAndPackageOmitted() {
		// Arrange
		string capturedBody = null;
		_applicationClient.ExecutePostRequest(
				Arg.Is<string>(u => u.Contains("CreateTheme")), Arg.Any<string>(),
				Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(ci => { capturedBody = ci.ArgAt<string>(1); return "{\"success\":true}"; });

		// Act
		int exitCode = _command.Execute(ValidOptions());

		// Assert
		exitCode.Should().Be(0, because: "a success=true response means the theme was created");
		capturedBody.Should().Contain("\"caption\":\"Ocean\"", because: "the caption must be serialized in camelCase");
		capturedBody.Should().Contain("\"cssClassName\":\"ocean-theme\"", because: "the css class name key must be camelCase 'cssClassName'");
		capturedBody.Should().NotContain("packageUId",
			because: "an omitted --package-name must omit packageUId so the server falls back to CurrentPackageId");
		_command.CreatedId.Should().MatchRegex("^[A-Za-z0-9-]+$",
			because: "an omitted --id must yield an auto-generated UUID that satisfies the id contract");
		_command.CreatedId.Should().NotBeNullOrWhiteSpace(because: "the generated id must be reported back");
	}

	[Test, Category("Unit")]
	[Description("Preserves an explicitly supplied --id in the request body and the reported CreatedId.")]
	public void CreateTheme_PreservesExplicitId_WhenIdSupplied() {
		// Arrange
		string capturedBody = null;
		_applicationClient.ExecutePostRequest(
				Arg.Is<string>(u => u.Contains("CreateTheme")), Arg.Any<string>(),
				Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(ci => { capturedBody = ci.ArgAt<string>(1); return "{\"success\":true}"; });
		CreateThemeOptions options = ValidOptions();
		options.Id = "my-explicit-theme";

		// Act
		_command.Execute(options);

		// Assert
		capturedBody.Should().Contain("\"id\":\"my-explicit-theme\"", because: "an explicit id must be sent verbatim");
		_command.CreatedId.Should().Be("my-explicit-theme", because: "the effective id is the supplied one");
	}

	[Test, Category("Unit")]
	[Description("Resolves --package-name to its UId via SysPackage and sends that packageUId in the CreateTheme body.")]
	public void CreateTheme_ResolvesPackageNameToUId_WhenPackageNameSupplied() {
		// Arrange
		_applicationClient.ExecutePostRequest(Arg.Is<string>(u => u.Contains("SelectQuery")), Arg.Any<string>())
			.Returns("{\"success\":true,\"rows\":[{\"UId\":\"11111111-1111-1111-1111-111111111111\"}]}");
		string capturedBody = null;
		_applicationClient.ExecutePostRequest(
				Arg.Is<string>(u => u.Contains("CreateTheme")), Arg.Any<string>(),
				Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(ci => { capturedBody = ci.ArgAt<string>(1); return "{\"success\":true}"; });
		CreateThemeOptions options = ValidOptions();
		options.PackageName = "UsrBranding";

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(0, because: "the package resolved and the theme was created");
		capturedBody.Should().Contain("\"packageUId\":\"11111111-1111-1111-1111-111111111111\"",
			because: "the resolved SysPackage UId must be sent as packageUId");
	}

	[Test, Category("Unit")]
	[Description("Fails fast without any HTTP call when both --css-content and --css-content-file are supplied.")]
	public void CreateTheme_FailsFastWithoutHttp_WhenBothCssInputsSupplied() {
		// Arrange
		CreateThemeOptions options = ValidOptions();
		options.CssContentFile = "theme.css";

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(1, because: "the mutually-exclusive CSS inputs are an invalid request");
		_applicationClient.DidNotReceive().ExecutePostRequest(
			Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
		_logger.Received(1).WriteError(Arg.Is<string>(m => m.Contains("not both")));
	}

	[Test, Category("Unit")]
	[Description("Fails fast without any HTTP call when css-class-name violates the format rule.")]
	public void CreateTheme_FailsFastWithoutHttp_WhenCssClassNameInvalid() {
		// Arrange
		CreateThemeOptions options = ValidOptions();
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
	[Description("Surfaces a css-class-name error (not a misleading caption error) when --css-class-name is empty and --caption is omitted, because the caption is derived from css-class-name.")]
	public void CreateTheme_FailsWithCssClassNameError_WhenCssClassNameEmptyAndCaptionOmitted() {
		// Arrange
		CreateThemeOptions options = new() {
			CssClassName = string.Empty,
			CssContent = ".x{}"
		};

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(1, because: "an empty css-class-name is invalid and must fail before any service call");
		_applicationClient.DidNotReceive().ExecutePostRequest(
			Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
		// The error must point at the real cause (the empty css-class-name), not the derived empty caption.
		_logger.Received(1).WriteError(Arg.Is<string>(m => m.Contains("css-class-name")));
	}

	[Test, Category("Unit")]
	[Description("Returns failure and surfaces the ThemeService error message when the response reports success=false.")]
	public void CreateTheme_ReturnsFailureAndLogsErrorInfoMessage_WhenResponseReportsFailure() {
		// Arrange
		_applicationClient.ExecutePostRequest(
				Arg.Is<string>(u => u.Contains("CreateTheme")), Arg.Any<string>(),
				Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("{\"success\":false,\"errorInfo\":{\"errorCode\":\"InvalidOperationException\",\"message\":\"id already exists\"}}");

		// Act
		int exitCode = _command.Execute(ValidOptions());

		// Assert
		exitCode.Should().Be(1, because: "an explicit success=false must surface as a command failure");
		_logger.Received(1).WriteError(Arg.Is<string>(m => m.Contains("id already exists")));
	}

	[Test, Category("Unit")]
	[Description("Treats a non-JSON response body as success to avoid false negatives if the contract evolves.")]
	public void CreateTheme_ReturnsSuccess_WhenResponseBodyIsNotParseableJson() {
		// Arrange
		_applicationClient.ExecutePostRequest(
				Arg.Is<string>(u => u.Contains("CreateTheme")), Arg.Any<string>(),
				Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("OK");

		// Act
		int exitCode = _command.Execute(ValidOptions());

		// Assert
		exitCode.Should().Be(0, because: "a non-JSON body must not be misread as a failure");
	}

	[Test, Category("Unit")]
	[Description("Derives the caption from css-class-name and sends it in the body when --caption is omitted.")]
	public void CreateTheme_DerivesCaptionFromCssClassName_WhenCaptionOmitted() {
		// Arrange
		string capturedBody = null;
		_applicationClient.ExecutePostRequest(
				Arg.Is<string>(u => u.Contains("CreateTheme")), Arg.Any<string>(),
				Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(ci => { capturedBody = ci.ArgAt<string>(1); return "{\"success\":true}"; });
		CreateThemeOptions options = new() {
			CssClassName = "ocean-theme",
			CssContent = ".ocean-theme{}"
		};

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(0, because: "an omitted caption must not fail the command");
		capturedBody.Should().Contain("\"caption\":\"Ocean\"",
			because: "an omitted caption must be derived from css-class-name (ocean-theme -> Ocean)");
	}
}
