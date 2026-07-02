using System;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Clio.Command;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using Clio.Theming;
using Clio.UserEnvironment;
using Clio.Workspaces;
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
	private IWorkspacePathBuilder _workspacePathBuilder;
	private IFileSystem _fileSystem;
	private BuildThemeTool _tool;

	[SetUp]
	public void SetUp() {
		_themeCssBuilder = Substitute.For<IThemeCssBuilder>();
		_themeTemplateProvider = Substitute.For<IThemeTemplateProvider>();
		_resolverFactory = Substitute.For<IPlatformVersionResolverFactory>();
		_settingsRepository = Substitute.For<ISettingsRepository>();
		_workspacePathBuilder = Substitute.For<IWorkspacePathBuilder>();
		_fileSystem = Substitute.For<IFileSystem>();
		_themeTemplateProvider.GetCssTemplate(Arg.Any<string>()).Returns("template-css");
		_themeTemplateProvider.GetJsonTemplate(Arg.Any<string>())
			.Returns("{\"id\":\"<%themeId%>\",\"caption\":\"<%themeCaption%>\",\"cssClassName\":\"<%themeCssClass%>\"}");
		_themeCssBuilder.Build(Arg.Any<string>(), Arg.Any<BuildThemeOptions>()).Returns("built-css");
		BuildThemeCommand command = new(_themeCssBuilder, _themeTemplateProvider, _resolverFactory, _settingsRepository,
			_workspacePathBuilder, _fileSystem, Substitute.For<ILogger>());
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
	[Description("Returns a graceful failure when neither css-class-name nor caption is supplied (at least one is required).")]
	public void BuildTheme_ShouldReturnFailure_WhenCssClassNameAndCaptionBothEmpty() {
		// Act
		BuildThemeResult result = _tool.BuildTheme(primary: "#004fd6", cssClassName: "");

		// Assert
		result.Success.Should().BeFalse(because: "with no css-class-name and no caption there is nothing to name the theme");
		result.Error.Should().Contain("at least one is required", because: "the error must say a caption or a css-class-name is required");
		_themeCssBuilder.DidNotReceive().Build(Arg.Any<string>(), Arg.Any<BuildThemeOptions>());
	}

	[Test]
	[Description("Derives the css-class-name from the caption (slugified) when only a caption is supplied — the theme still builds.")]
	public void BuildTheme_ShouldDeriveCssClassNameFromCaption_WhenOnlyCaptionSupplied() {
		// Act
		BuildThemeResult result = _tool.BuildTheme(primary: "#004fd6", caption: "Ocean Blue");

		// Assert
		result.Success.Should().BeTrue(because: "a caption alone is enough — clio derives the css-class-name");
		_themeCssBuilder.Received(1).Build(Arg.Any<string>(),
			Arg.Is<BuildThemeOptions>(o => o.ThemeCssClass == "ocean-blue"));
		using JsonDocument descriptor = JsonDocument.Parse(result.Descriptor);
		descriptor.RootElement.GetProperty("cssClassName").GetString().Should().Be("ocean-blue",
			because: "the derived slug is written to the descriptor");
		descriptor.RootElement.GetProperty("caption").GetString().Should().Be("Ocean Blue",
			because: "the human caption is preserved, not replaced by the slug");
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
	[Description("In workspace-write mode (workspaceDirectory + packageName) writes theme.css + theme.json into the package's Files/themes/<cssClassName>/ and returns the path without the CSS payload (token cost).")]
	public void BuildTheme_ShouldWriteFilesAndReturnPath_WhenWorkspaceAndPackageProvided() {
		// Arrange
		string workspaceDir = Path.Combine(Path.GetTempPath(), "clio-theme-ws");
		string packagePath = Path.Combine(workspaceDir, "packages", "UsrTheme");
		string themeDir = Path.Combine(packagePath, "Files", "themes", "MyTheme");
		_workspacePathBuilder.IsWorkspace.Returns(true);
		_workspacePathBuilder.BuildPackagePath("UsrTheme").Returns(packagePath);
		_fileSystem.ExistsDirectory(packagePath).Returns(true);

		// Act
		BuildThemeResult result = _tool.BuildTheme(primary: "#004fd6", cssClassName: "MyTheme",
			workspaceDirectory: workspaceDir, packageName: "UsrTheme");

		// Assert
		result.Success.Should().BeTrue(because: "a valid workspace + existing package is a valid workspace-write request");
		result.Path.Should().Be(themeDir, because: "the tool resolves <ws>/packages/<pkg>/Files/themes/<cssClassName> and returns where it wrote");
		result.Css.Should().BeNull(because: "the CSS payload is omitted in workspace-write mode to keep the large string out of the agent context");
		result.Descriptor.Should().BeNull(because: "the descriptor is written to disk, not returned, in workspace-write mode");
		result.Error.Should().BeNull(because: "a successful write carries no error");
		_fileSystem.Received(1).WriteAllTextToFile(Path.Combine(themeDir, "theme.css"), "built-css");
		_fileSystem.Received(1).WriteAllTextToFile(Path.Combine(themeDir, "theme.json"), Arg.Any<string>());
	}

	[Test]
	[Description("Returns a graceful failure (no write) when workspaceDirectory is given without packageName; the two must be provided together to write into a package.")]
	public void BuildTheme_ShouldReturnFailure_WhenWorkspaceProvidedWithoutPackage() {
		// Act
		string workspaceDir = Path.Combine(Path.GetTempPath(), "clio-theme-ws");
		BuildThemeResult result = _tool.BuildTheme(primary: "#004fd6", cssClassName: "MyTheme", workspaceDirectory: workspaceDir);

		// Assert
		result.Success.Should().BeFalse(because: "writing into a package needs both the workspace and the package name");
		result.Error.Should().Contain("together", because: "the error must explain the two are provided together");
		_fileSystem.DidNotReceive().WriteAllTextToFile(Arg.Any<string>(), Arg.Any<string>());
	}

	[Test]
	[Description("Returns a graceful failure (no write, no throw) when workspaceDirectory is not a fully-qualified absolute path, because the MCP server working directory differs from the caller's.")]
	public void BuildTheme_ShouldReturnFailure_WhenWorkspaceDirectoryNotAbsolute() {
		// Act
		BuildThemeResult result = _tool.BuildTheme(primary: "#004fd6", cssClassName: "MyTheme",
			workspaceDirectory: "relative/ws", packageName: "UsrTheme");

		// Assert
		result.Success.Should().BeFalse(because: "a non-absolute workspace path is ambiguous under the MCP server working directory");
		result.Error.Should().Contain("absolute", because: "the error must explain that an absolute path is required");
		_themeCssBuilder.DidNotReceive().Build(Arg.Any<string>(), Arg.Any<BuildThemeOptions>());
		_fileSystem.DidNotReceive().WriteAllTextToFile(Arg.Any<string>(), Arg.Any<string>());
	}

	[Test]
	[Description("Returns a graceful failure (no write) when the workspaceDirectory is not a clio workspace (no .clio/workspaceSettings.json).")]
	public void BuildTheme_ShouldReturnFailure_WhenNotAWorkspace() {
		// Arrange
		string workspaceDir = Path.Combine(Path.GetTempPath(), "clio-theme-notws");
		_workspacePathBuilder.IsWorkspace.Returns(false);

		// Act
		BuildThemeResult result = _tool.BuildTheme(primary: "#004fd6", cssClassName: "MyTheme",
			workspaceDirectory: workspaceDir, packageName: "UsrTheme");

		// Assert
		result.Success.Should().BeFalse(because: "the theme cannot be written outside a clio workspace");
		result.Error.Should().Contain("workspace", because: "the error must explain the directory is not a clio workspace");
		_fileSystem.DidNotReceive().WriteAllTextToFile(Arg.Any<string>(), Arg.Any<string>());
	}

	[Test]
	[Description("Returns a graceful failure (no write) when the named package does not exist in the workspace.")]
	public void BuildTheme_ShouldReturnFailure_WhenPackageMissing() {
		// Arrange
		string workspaceDir = Path.Combine(Path.GetTempPath(), "clio-theme-ws");
		string packagePath = Path.Combine(workspaceDir, "packages", "Ghost");
		_workspacePathBuilder.IsWorkspace.Returns(true);
		_workspacePathBuilder.BuildPackagePath("Ghost").Returns(packagePath);
		_fileSystem.ExistsDirectory(packagePath).Returns(false);

		// Act
		BuildThemeResult result = _tool.BuildTheme(primary: "#004fd6", cssClassName: "MyTheme",
			workspaceDirectory: workspaceDir, packageName: "Ghost");

		// Assert
		result.Success.Should().BeFalse(because: "the theme cannot be written into a package that does not exist");
		result.Error.Should().Contain("Ghost", because: "the error must name the missing package");
		_fileSystem.DidNotReceive().WriteAllTextToFile(Arg.Any<string>(), Arg.Any<string>());
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
