namespace Clio.Tests.Command;

using System;
using Clio.Command;
using Clio.Command.Theming;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

[TestFixture]
[Property("Module", "Command")]
public sealed class CreateThemeCommandTests : BaseCommandTests<CreateThemeOptions>
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
	public void CreateTheme_ShouldFormCorrectApplicationRequest_WhenApplicationRunsUnderNetFramework() {
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
	public void CreateTheme_ShouldFormCorrectApplicationRequest_WhenApplicationRunsUnderNetCore() {
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
	public void CreateTheme_ShouldSendCamelCaseBodyWithoutPackageUIdAndAutoId_WhenIdAndPackageOmitted() {
		// Arrange
		string capturedBody = null;
		_applicationClient.ExecutePostRequest(
				Arg.Is<string>(u => u.Contains("CreateTheme")), Arg.Any<string>(),
				Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(ci => { capturedBody = ci.ArgAt<string>(1); return "{\"success\":true}"; });

		// Act
		bool succeeded = _command.TryCreateTheme(ValidOptions(), out string createdId, out _);

		// Assert
		succeeded.Should().BeTrue(because: "a success=true response means the theme was created");
		capturedBody.Should().Contain("\"caption\":\"Ocean\"", because: "the caption must be serialized in camelCase");
		capturedBody.Should().Contain("\"cssClassName\":\"ocean-theme\"", because: "the css class name key must be camelCase 'cssClassName'");
		capturedBody.Should().NotContain("packageUId",
			because: "an omitted --package-name must omit packageUId so the server falls back to CurrentPackageId");
		Guid.TryParse(createdId, out _).Should().BeTrue(
			because: "an omitted --id must yield an auto-generated UUID v4 that satisfies the id contract");
		createdId.Should().NotBeNullOrWhiteSpace(because: "the generated id must be reported back");
	}

	[Test, Category("Unit")]
	[Description("Preserves an explicitly supplied --id in the request body and the reported created id.")]
	public void CreateTheme_ShouldPreserveExplicitId_WhenIdSupplied() {
		// Arrange
		string capturedBody = null;
		_applicationClient.ExecutePostRequest(
				Arg.Is<string>(u => u.Contains("CreateTheme")), Arg.Any<string>(),
				Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(ci => { capturedBody = ci.ArgAt<string>(1); return "{\"success\":true}"; });
		CreateThemeOptions options = ValidOptions();
		options.Id = "my-explicit-theme";

		// Act
		bool succeeded = _command.TryCreateTheme(options, out string createdId, out _);

		// Assert
		succeeded.Should().BeTrue(because: "a valid explicit id is created successfully");
		capturedBody.Should().Contain("\"id\":\"my-explicit-theme\"", because: "an explicit id must be sent verbatim");
		createdId.Should().Be("my-explicit-theme", because: "the effective id is the supplied one");
	}

	[Test, Category("Unit")]
	[Description("Resolves --package-name to its UId via SysPackage and sends that packageUId in the CreateTheme body.")]
	public void CreateTheme_ShouldResolvePackageNameToUId_WhenPackageNameSupplied() {
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
	[Description("Fails with exit code 1 and never posts CreateTheme when --package-name does not resolve to a SysPackage, so the theme is not silently created in the fallback package.")]
	public void CreateTheme_ShouldFailWithoutCreateThemePost_WhenPackageNameIsUnknown() {
		// Arrange
		_applicationClient.ExecutePostRequest(Arg.Is<string>(u => u.Contains("SelectQuery")), Arg.Any<string>())
			.Returns("{\"success\":true,\"rows\":[]}");
		CreateThemeOptions options = ValidOptions();
		options.PackageName = "NoSuchPackage";

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(1, because: "an unresolvable --package-name must fail the command");
		// Verifies the error names the package the user asked for, so the failure is actionable.
		_logger.Received(1).WriteError(Arg.Is<string>(message => message.Contains("NoSuchPackage")));
		_applicationClient.DidNotReceive().ExecutePostRequest(
			Arg.Is<string>(u => u.Contains("CreateTheme")), Arg.Any<string>(),
			Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
	}

	[Test, Category("Unit")]
	[Description("Fails fast without any HTTP call when both --css-content and --css-content-file are supplied.")]
	public void CreateTheme_ShouldFailFastWithoutHttp_WhenBothCssInputsSupplied() {
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
	public void CreateTheme_ShouldFailFastWithoutHttp_WhenCssClassNameInvalid() {
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
	[Description("Fails before any service call with an 'at least one is required' error when both --css-class-name and --caption are empty.")]
	public void CreateTheme_ShouldFailWithAtLeastOneRequired_WhenCssClassNameAndCaptionBothEmpty() {
		// Arrange
		CreateThemeOptions options = new() {
			CssClassName = string.Empty,
			CssContent = ".x{}"
		};

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(1, because: "with no css-class-name and no caption there is nothing to name the theme");
		_applicationClient.DidNotReceive().ExecutePostRequest(
			Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
		_logger.Received(1).WriteError(Arg.Is<string>(m => m.Contains("at least one is required")));
	}

	[Test, Category("Unit")]
	[Description("Derives the css-class-name from the caption (lowercased and hyphenated) and sends both when --css-class-name is omitted.")]
	public void CreateTheme_ShouldDeriveCssClassNameFromCaption_WhenCssClassNameOmitted() {
		// Arrange
		string capturedBody = null;
		_applicationClient.ExecutePostRequest(
				Arg.Is<string>(u => u.Contains("CreateTheme")), Arg.Any<string>(),
				Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(ci => { capturedBody = ci.ArgAt<string>(1); return "{\"success\":true}"; });
		CreateThemeOptions options = new() {
			Caption = "Ocean Blue",
			CssContent = ".x{}"
		};

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(0, because: "a caption alone is enough — clio derives a valid css-class-name from it");
		capturedBody.Should().Contain("\"cssClassName\":\"ocean-blue\"",
			because: "the css class name is the slug of the caption");
		capturedBody.Should().Contain("\"caption\":\"Ocean Blue\"",
			because: "the human caption is sent as-is, not replaced by the slug");
	}

	[Test, Category("Unit")]
	[Description("Returns failure and surfaces the ThemeService error message when the response reports success=false.")]
	public void CreateTheme_ShouldReturnFailureAndLogErrorInfoMessage_WhenResponseReportsFailure() {
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
	[Description("Fails the command when the response body is a non-empty non-JSON payload (e.g. an auth redirect): ThemeService always answers with JSON, so a non-JSON body means the create never reached the service.")]
	public void CreateTheme_ShouldReturnFailureAndLogError_WhenResponseBodyIsNotParseableJson() {
		// Arrange
		_applicationClient.ExecutePostRequest(
				Arg.Is<string>(u => u.Contains("CreateTheme")), Arg.Any<string>(),
				Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("OK");

		// Act
		int exitCode = _command.Execute(ValidOptions());

		// Assert
		exitCode.Should().Be(1, because: "a non-JSON body signals the create did not reach ThemeService and must surface as a failure");
		_logger.Received(1).WriteError(Arg.Is<string>(m => m.Contains("Unexpected response from server")));
	}

	[Test, Category("Unit")]
	[Description("Derives the caption from css-class-name and sends it in the body when --caption is omitted.")]
	public void CreateTheme_ShouldDeriveCaptionFromCssClassName_WhenCaptionOmitted() {
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

	[Test, Category("Unit")]
	[Description("Returns success and still reports the client-generated id when the response body is empty — an empty body is the ThemeService contract default for success.")]
	public void CreateTheme_ShouldReturnSuccess_WhenResponseBodyIsEmpty() {
		// Arrange
		_applicationClient.ExecutePostRequest(
				Arg.Is<string>(u => u.Contains("CreateTheme")), Arg.Any<string>(),
				Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(string.Empty);

		// Act
		bool succeeded = _command.TryCreateTheme(ValidOptions(), out string createdId, out _);

		// Assert
		succeeded.Should().BeTrue(because: "an empty body is the contract default for a successful create");
		createdId.Should().NotBeNullOrWhiteSpace(
			because: "the client-generated id is reported even when the server echoes an empty body");
	}
}
