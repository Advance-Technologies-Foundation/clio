using System;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Clio.Command;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using Clio.Theming;
using Clio.UserEnvironment;
using FluentAssertions;
using ModelContextProtocol.Server;
using NSubstitute;
using NUnit.Framework;
using BuildThemeCommand = Clio.Command.Theming.BuildThemeCommand;
using IThemeTemplateProvider = Clio.Command.Theming.IThemeTemplateProvider;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
[Category("Unit")]
[Property("Module", "McpServer")]
public sealed class BuildThemeToolTests
{
	private IThemeCssBuilder _themeCssBuilder;
	private IThemeTemplateProvider _themeTemplateProvider;
	private IPlatformVersionResolverFactory _resolverFactory;
	private ISettingsRepository _settingsRepository;
	private BuildThemeTool _tool;

	[SetUp]
	public void SetUp() {
		_themeCssBuilder = Substitute.For<IThemeCssBuilder>();
		_themeTemplateProvider = Substitute.For<IThemeTemplateProvider>();
		_resolverFactory = Substitute.For<IPlatformVersionResolverFactory>();
		_settingsRepository = Substitute.For<ISettingsRepository>();
		_themeTemplateProvider.GetCssTemplate(Arg.Any<string>()).Returns("template-css");
		_themeTemplateProvider.GetJsonTemplate(Arg.Any<string>())
			.Returns("{\"id\":\"<%themeId%>\",\"caption\":\"<%themeCaption%>\",\"cssClassName\":\"<%themeCssClass%>\"}");
		_themeCssBuilder.Build(Arg.Any<string>(), Arg.Any<BuildThemeOptions>()).Returns("built-css");
		BuildThemeCommand command = new(_themeCssBuilder, _themeTemplateProvider, _resolverFactory, _settingsRepository,
			Substitute.For<IFileSystem>(), Substitute.For<ILogger>());
		_tool = new BuildThemeTool(command);
	}

	[Test]
	[Description("Returns success with the built CSS when given a valid primary and css-class-name.")]
	public void BuildTheme_ShouldReturnSuccessWithCss_WhenValidInput() {
		// Act
		BuildThemeResult result = _tool.BuildTheme(primary: "#004fd6", cssClassName: "MyTheme");

		// Assert
		result.Success.Should().BeTrue(because: "valid inputs produce a theme");
		result.Css.Should().Be("built-css", because: "the tool returns the builder's CSS output");
		result.Descriptor.Should().NotBeNull(because: "the tool also returns the theme.json descriptor artifact");
		result.Error.Should().BeNull(because: "a successful build carries no error");
		_themeCssBuilder.Received(1).Build(Arg.Any<string>(),
			Arg.Is<BuildThemeOptions>(o => o.Primary == "#004fd6" && o.ThemeCssClass == "MyTheme"));
	}

	[Test]
	[Description("Returns a non-fatal warning (but still succeeds) when fontWeights is given without a heading or body font.")]
	public void BuildTheme_ShouldReturnWarning_WhenFontWeightsWithoutFamily() {
		// Act
		BuildThemeResult result = _tool.BuildTheme(primary: "#004fd6", cssClassName: "MyTheme",
			fontWeights: new[] { 400, 700 });

		// Assert
		result.Success.Should().BeTrue(because: "font weights without a family is non-fatal");
		result.Css.Should().Be("built-css", because: "the theme is still built");
		result.Warnings.Should().Contain(w => w.Contains("font weights"),
			because: "the tool surfaces the ignored-weights advisory");
	}

	[Test]
	[Description("Returns the theme.json descriptor alongside the CSS, filled with the supplied caption and id (both optional).")]
	public void BuildTheme_ShouldReturnThemeJsonDescriptor_WithSuppliedCaptionAndId() {
		// Act
		BuildThemeResult result = _tool.BuildTheme(primary: "#004fd6", cssClassName: "MyTheme",
			caption: "My Theme", id: "my-id");

		// Assert
		result.Success.Should().BeTrue(because: "valid inputs produce a theme");
		result.Descriptor.Should().NotBeNull(because: "build-theme returns the theme.json descriptor as a second artifact");
		using JsonDocument descriptor = JsonDocument.Parse(result.Descriptor);
		descriptor.RootElement.GetProperty("id").GetString().Should().Be("my-id",
			because: "the supplied id fills the descriptor");
		descriptor.RootElement.GetProperty("caption").GetString().Should().Be("My Theme",
			because: "the supplied caption fills the descriptor");
		descriptor.RootElement.GetProperty("cssClassName").GetString().Should().Be("MyTheme",
			because: "the css class name is written to the descriptor");
	}

	[Test]
	[Description("Returns a graceful failure (no exception) when the required primary is empty.")]
	public void BuildTheme_ShouldReturnFailure_WhenPrimaryEmpty() {
		// Act
		BuildThemeResult result = _tool.BuildTheme(primary: " ", cssClassName: "MyTheme");

		// Assert
		result.Success.Should().BeFalse(because: "a missing primary is an invalid request");
		result.Error.Should().Contain("primary", because: "the error must name the missing required input");
		_themeCssBuilder.DidNotReceive().Build(Arg.Any<string>(), Arg.Any<BuildThemeOptions>());
	}

	[Test]
	[Description("Returns a graceful failure when the required css-class-name is empty.")]
	public void BuildTheme_ShouldReturnFailure_WhenCssClassNameEmpty() {
		// Act
		BuildThemeResult result = _tool.BuildTheme(primary: "#004fd6", cssClassName: "");

		// Assert
		result.Success.Should().BeFalse(because: "a missing css-class-name is an invalid request");
		result.Error.Should().Contain("cssClassName", because: "the error must name the missing required input");
		_themeCssBuilder.DidNotReceive().Build(Arg.Any<string>(), Arg.Any<BuildThemeOptions>());
	}

	[Test]
	[Description("Resolves the template version from the named environment via the platform-version resolver.")]
	public void BuildTheme_ShouldResolveVersionFromEnvironment_WhenEnvironmentNameProvided() {
		// Arrange
		EnvironmentSettings env = new() { Uri = "http://env" };
		_settingsRepository.FindEnvironment("dev").Returns(env);
		IPlatformVersionResolver resolver = Substitute.For<IPlatformVersionResolver>();
		resolver.ResolveAsync(Arg.Any<CancellationToken>())
			.Returns(Task.FromResult(new PlatformVersionResolution("10.0.1", VersionResolutionSource.Environment)));
		_resolverFactory.Create(env).Returns(resolver);

		// Act
		_tool.BuildTheme(primary: "#004fd6", cssClassName: "MyTheme", environmentName: "dev");

		// Assert
		_themeTemplateProvider.Received(1).GetCssTemplate("10.0.1");
	}

	[Test]
	[Description("Returns a failure result when the named environment's Creatio version cannot be determined (resolver latest-fallback), instead of silently building from the highest bundled template.")]
	public void BuildTheme_ShouldReturnFailure_WhenEnvironmentVersionUnresolved() {
		// Arrange
		EnvironmentSettings env = new() { Uri = "http://env" };
		_settingsRepository.FindEnvironment("dev").Returns(env);
		IPlatformVersionResolver resolver = Substitute.For<IPlatformVersionResolver>();
		resolver.ResolveAsync(Arg.Any<CancellationToken>())
			.Returns(Task.FromResult(new PlatformVersionResolution("latest", VersionResolutionSource.LatestFallback)));
		_resolverFactory.Create(env).Returns(resolver);

		// Act
		BuildThemeResult result = _tool.BuildTheme(primary: "#004fd6", cssClassName: "MyTheme", environmentName: "dev");

		// Assert
		result.Success.Should().BeFalse(because: "an undeterminable environment version must not silently fall back to the highest bundled template");
		result.Error.Should().Contain("could not determine", because: "the failure must explain that the environment version could not be determined");
		_themeTemplateProvider.DidNotReceive().GetCssTemplate(Arg.Any<string>());
	}

	[Test]
	[Description("Fails when both version and environmentName are supplied (mutually exclusive).")]
	public void BuildTheme_ShouldReturnFailure_WhenBothVersionAndEnvironmentProvided() {
		// Act
		BuildThemeResult result = _tool.BuildTheme(primary: "#004fd6", cssClassName: "MyTheme",
			version: "10.0", environmentName: "dev");

		// Assert
		result.Success.Should().BeFalse(because: "the version source must be unambiguous");
		result.Error.Should().Contain("mutually exclusive", because: "the error must explain the conflict");
		_themeTemplateProvider.DidNotReceive().GetCssTemplate(Arg.Any<string>());
	}

	[Test]
	[Description("Fails with a clear error when the named environment is not registered.")]
	public void BuildTheme_ShouldReturnFailure_WhenEnvironmentNotRegistered() {
		// Arrange
		_settingsRepository.FindEnvironment("ghost").Returns((EnvironmentSettings)null);

		// Act
		BuildThemeResult result = _tool.BuildTheme(primary: "#004fd6", cssClassName: "MyTheme", environmentName: "ghost");

		// Assert
		result.Success.Should().BeFalse(because: "an unregistered environment cannot resolve a version");
		result.Error.Should().Contain("not registered", because: "the error must name the unknown environment");
		_themeTemplateProvider.DidNotReceive().GetCssTemplate(Arg.Any<string>());
	}

	[Test]
	[Description("Returns a graceful failure when the template provider rejects a too-old version.")]
	public void BuildTheme_ShouldReturnFailure_WhenProviderRejectsVersion() {
		// Arrange
		_themeTemplateProvider.GetCssTemplate("9.0")
			.Returns(_ => throw new ArgumentException("Themes require Creatio 10.0 or newer; version 9.0 is not supported."));

		// Act
		BuildThemeResult result = _tool.BuildTheme(primary: "#004fd6", cssClassName: "MyTheme", version: "9.0");

		// Assert
		result.Success.Should().BeFalse(because: "an unsupported version must surface as a failure, not a crash");
		result.Error.Should().Contain("Themes require Creatio 10.0 or newer",
			because: "the provider's diagnostic must reach the caller");
	}

	[Test]
	[Description("Uses the highest bundled template (null target) when neither version source is supplied.")]
	public void BuildTheme_ShouldUseHighestBundled_WhenNeitherVersionNorEnvironment() {
		// Act
		_tool.BuildTheme(primary: "#004fd6", cssClassName: "MyTheme");

		// Assert
		_themeTemplateProvider.Received(1).GetCssTemplate(null);
	}

	[Test]
	[Description("Is a flat, feature-toggled MCP tool named build-theme (ComponentInfoTool shape, not a BaseTool subclass — R-02).")]
	public void BuildThemeTool_ShouldBeFlatGatedMcpTool_WhenInspected() {
		// Arrange
		Type toolType = typeof(BuildThemeTool);

		// Assert
		toolType.BaseType.Should().Be(typeof(object),
			because: "the build-theme tool is a flat ComponentInfoTool-style tool, not a BaseTool subclass (R-02)");
		toolType.GetCustomAttribute<McpServerToolTypeAttribute>().Should().NotBeNull(
			because: "the tool must be discoverable as an MCP tool type");
		toolType.GetCustomAttribute<FeatureToggleAttribute>().Should().NotBeNull(
			because: "the MCP surface is feature-toggled separately from the CLI options class");
		MethodInfo method = toolType.GetMethod(nameof(BuildThemeTool.BuildTheme));
		method.Should().NotBeNull(because: "the tool exposes a single build-theme operation");
		method!.GetCustomAttribute<McpServerToolAttribute>()!.Name.Should().Be("build-theme",
			because: "the advertised MCP tool name is build-theme");
	}
}
