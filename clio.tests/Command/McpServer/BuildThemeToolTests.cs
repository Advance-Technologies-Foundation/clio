using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
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
		_themeCssBuilder.Build(Arg.Any<string>(), Arg.Any<BuildThemeInput>()).Returns("built-css");
		BuildThemeCommand command = new(_themeCssBuilder, _themeTemplateProvider, _resolverFactory, _settingsRepository,
			_workspacePathBuilder, _fileSystem, Substitute.For<ILogger>());
		_tool = new BuildThemeTool(command, Substitute.For<ILogger>());
	}

	[Test]
	[Description("Derives from BaseTool so execution holds the shared MCP lock (the workspace-write mode mutates the singleton IWorkspacePathBuilder.RootPath), and is advertised as build-theme.")]
	public void BuildThemeTool_ShouldDeriveFromBaseTool_WhenInspected() {
		// Arrange
		Type toolType = typeof(BuildThemeTool);

		// Assert
		toolType.BaseType.Should().Be(typeof(BaseTool<Clio.Command.Theming.BuildThemeOptions>),
			because: "BaseTool.ExecuteWithCleanLog serializes the workspace-write mode's mutation of the singleton IWorkspacePathBuilder.RootPath against concurrent MCP tool invocations");
		toolType.GetCustomAttribute<McpServerToolTypeAttribute>().Should().NotBeNull(
			because: "the tool must be discoverable as an MCP tool type");
		MethodInfo method = toolType.GetMethod(nameof(BuildThemeTool.BuildTheme));
		method.Should().NotBeNull(because: "the tool exposes a single build-theme operation");
		method!.GetCustomAttribute<McpServerToolAttribute>()!.Name.Should().Be("build-theme",
			because: "the advertised MCP tool name is build-theme");
	}

	[Test]
	[Description("Declares the safety flags on the build-theme tool method: a workspace-write that is non-destructive, idempotent, and closed-world.")]
	public void BuildThemeTool_Should_DeclareBuildSafetyFlags_WhenInspectingMcpServerToolAttribute() {
		// Arrange & Act
		McpServerToolAttribute attribute = typeof(BuildThemeTool)
			.GetMethod(nameof(BuildThemeTool.BuildTheme))!
			.GetCustomAttribute<McpServerToolAttribute>();

		// Assert
		attribute!.ReadOnly.Should().BeFalse(because: "build-theme can write theme.css and theme.json into a workspace package");
		attribute.Destructive.Should().BeFalse(because: "building writes its own theme artifacts without destroying unrelated state");
		attribute.Idempotent.Should().BeTrue(because: "re-building with the same inputs yields the same theme artifacts");
		attribute.OpenWorld.Should().BeFalse(because: "build-theme works offline over a bundled template and never reaches an open set of hosts");
	}

	[Test]
	[Description("Marks the single args wrapper as required at the MCP schema level, so a call that omits args fails with a structured error instead of an opaque binding failure.")]
	public void BuildThemeTool_Should_RequireArgsWrapper_WhenInspectingMethodSignature() {
		// Arrange & Act
		object[] requiredAttributes = typeof(BuildThemeTool)
			.GetMethod(nameof(BuildThemeTool.BuildTheme))!
			.GetParameters()[0]
			.GetCustomAttributes(typeof(RequiredAttribute), false);

		// Assert
		requiredAttributes.Should().NotBeEmpty(
			because: "the args wrapper must be schema-required so an omitted args object fails with a structured error, not an opaque MCP binding failure");
	}

	[Test]
	[Description("Returns success with the built CSS when given a valid primary and css-class-name.")]
	public void BuildTheme_ShouldReturnSuccessWithCss_WhenValidInput() {
		// Act
		BuildThemeResult result = _tool.BuildTheme(new BuildThemeArgs(Primary: "#004fd6", CssClassName: "MyTheme"));

		// Assert
		result.Success.Should().BeTrue(because: "valid inputs produce a theme");
		result.Css.Should().Be("built-css", because: "the tool returns the builder's CSS output");
		result.Descriptor.Should().NotBeNull(because: "the tool also returns the theme.json descriptor artifact");
		result.Error.Should().BeNull(because: "a successful build carries no error");
		_themeCssBuilder.Received(1).Build(Arg.Any<string>(),
			Arg.Is<BuildThemeInput>(o => o.Primary == "#004fd6" && o.ThemeCssClass == "MyTheme"));
	}

	[Test]
	[Description("Forwards every brand colour and font field from the args record to the CSS builder, so no optional input is silently dropped or cross-wired.")]
	public void BuildTheme_ShouldForwardAllBrandAndFontFields_WhenAllOptionalInputsSupplied() {
		// Act
		BuildThemeResult result = _tool.BuildTheme(new BuildThemeArgs(
			Primary: "#004fd6", CssClassName: "MyTheme", Secondary: "#0d2e4e", Accent: "#f94e11",
			Success: "#0b8500", Error: "#d2310d", HeadingFont: "Inter", BodyFont: "Roboto",
			FontWeights: new[] { 400, 700 }));

		// Assert
		result.Success.Should().BeTrue(because: "a fully-specified build request is valid");
		_themeCssBuilder.Received(1).Build(Arg.Any<string>(), Arg.Is<BuildThemeInput>(o =>
			o.Primary == "#004fd6" &&
			o.Secondary == "#0d2e4e" &&
			o.Accent == "#f94e11" &&
			o.Success == "#0b8500" &&
			o.Error == "#d2310d" &&
			o.ThemeCssClass == "MyTheme" &&
			o.Fonts != null &&
			o.Fonts.Heading == "Inter" &&
			o.Fonts.Body == "Roboto" &&
			o.Fonts.Weights != null &&
			o.Fonts.Weights.SequenceEqual(new[] { 400, 700 })));
	}

	[Test]
	[Description("Returns a non-fatal warning (but still succeeds) when font-weights is given without a heading or body font.")]
	public void BuildTheme_ShouldReturnWarning_WhenFontWeightsWithoutFamily() {
		// Act
		BuildThemeResult result = _tool.BuildTheme(new BuildThemeArgs(Primary: "#004fd6", CssClassName: "MyTheme",
			FontWeights: new[] { 400, 700 }));

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
		BuildThemeResult result = _tool.BuildTheme(new BuildThemeArgs(Primary: "#004fd6", CssClassName: "MyTheme",
			Caption: "My Theme", Id: "my-id"));

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
		BuildThemeResult result = _tool.BuildTheme(new BuildThemeArgs(Primary: " ", CssClassName: "MyTheme"));

		// Assert
		result.Success.Should().BeFalse(because: "a missing primary is an invalid request");
		result.Error.Should().Contain("primary", because: "the error must name the missing required input");
		_themeCssBuilder.DidNotReceive().Build(Arg.Any<string>(), Arg.Any<BuildThemeInput>());
	}

	[Test]
	[Description("Returns a structured failure naming primary when the required field is omitted entirely.")]
	public void BuildTheme_ShouldReturnFailure_WhenPrimaryOmitted() {
		// Act
		BuildThemeResult result = _tool.BuildTheme(new BuildThemeArgs(CssClassName: "MyTheme"));

		// Assert
		result.Success.Should().BeFalse(because: "a build request without the required primary is invalid");
		result.Error.Should().Contain("primary is required",
			because: "the failure must name the exact required field the caller has to add");
		_themeCssBuilder.DidNotReceive().Build(Arg.Any<string>(), Arg.Any<BuildThemeInput>());
	}

	[Test]
	[Description("Returns an actionable rename hint instead of silently ignoring a camelCase alias of a kebab-case argument.")]
	public void BuildTheme_ShouldReturnRenameHint_WhenCamelCaseAliasPassed() {
		// Arrange
		BuildThemeArgs args = new(Primary: "#004fd6") {
			ExtensionData = new Dictionary<string, JsonElement> {
				["cssClassName"] = JsonSerializer.SerializeToElement("MyTheme")
			}
		};

		// Act
		BuildThemeResult result = _tool.BuildTheme(args);

		// Assert
		result.Success.Should().BeFalse(because: "a camelCase alias must be rejected, not silently dropped");
		result.Error.Should().Contain("'cssClassName' -> 'css-class-name'",
			because: "the failure must tell the caller the exact rename that fixes the call");
		_themeCssBuilder.DidNotReceive().Build(Arg.Any<string>(), Arg.Any<BuildThemeInput>());
	}

	[Test]
	[Description("Returns a graceful failure when neither css-class-name nor caption is supplied (at least one is required).")]
	public void BuildTheme_ShouldReturnFailure_WhenCssClassNameAndCaptionBothEmpty() {
		// Act
		BuildThemeResult result = _tool.BuildTheme(new BuildThemeArgs(Primary: "#004fd6", CssClassName: ""));

		// Assert
		result.Success.Should().BeFalse(because: "with no css-class-name and no caption there is nothing to name the theme");
		result.Error.Should().Contain("at least one is required", because: "the error must say a caption or a css-class-name is required");
		_themeCssBuilder.DidNotReceive().Build(Arg.Any<string>(), Arg.Any<BuildThemeInput>());
	}

	[Test]
	[Description("Derives the css-class-name from the caption (lowercased and hyphenated) when only a caption is supplied — the theme still builds.")]
	public void BuildTheme_ShouldDeriveCssClassNameFromCaption_WhenOnlyCaptionSupplied() {
		// Act
		BuildThemeResult result = _tool.BuildTheme(new BuildThemeArgs(Primary: "#004fd6", Caption: "Ocean Blue"));

		// Assert
		result.Success.Should().BeTrue(because: "a caption alone is enough — clio derives the css-class-name");
		_themeCssBuilder.Received(1).Build(Arg.Any<string>(),
			Arg.Is<BuildThemeInput>(o => o.ThemeCssClass == "ocean-blue"));
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
		_tool.BuildTheme(new BuildThemeArgs(Primary: "#004fd6", CssClassName: "MyTheme", EnvironmentName: "dev"));

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
		BuildThemeResult result = _tool.BuildTheme(new BuildThemeArgs(Primary: "#004fd6", CssClassName: "MyTheme",
			EnvironmentName: "dev"));

		// Assert
		result.Success.Should().BeFalse(because: "an undeterminable environment version must not silently fall back to the highest bundled template");
		result.Error.Should().Contain("could not determine", because: "the failure must explain that the environment version could not be determined");
		_themeTemplateProvider.DidNotReceive().GetCssTemplate(Arg.Any<string>());
	}

	[Test]
	[Description("Fails when both version and environment-name are supplied (mutually exclusive).")]
	public void BuildTheme_ShouldReturnFailure_WhenBothVersionAndEnvironmentProvided() {
		// Act
		BuildThemeResult result = _tool.BuildTheme(new BuildThemeArgs(Primary: "#004fd6", CssClassName: "MyTheme",
			Version: "10.0", EnvironmentName: "dev"));

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
		BuildThemeResult result = _tool.BuildTheme(new BuildThemeArgs(Primary: "#004fd6", CssClassName: "MyTheme",
			EnvironmentName: "ghost"));

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
		BuildThemeResult result = _tool.BuildTheme(new BuildThemeArgs(Primary: "#004fd6", CssClassName: "MyTheme",
			Version: "9.0"));

		// Assert
		result.Success.Should().BeFalse(because: "an unsupported version must surface as a failure, not a crash");
		result.Error.Should().Contain("Themes require Creatio 10.0 or newer",
			because: "the provider's diagnostic must reach the caller");
	}

	[Test]
	[Description("Uses the highest bundled template (null target) when neither version source is supplied.")]
	public void BuildTheme_ShouldUseHighestBundled_WhenNeitherVersionNorEnvironment() {
		// Act
		_tool.BuildTheme(new BuildThemeArgs(Primary: "#004fd6", CssClassName: "MyTheme"));

		// Assert
		_themeTemplateProvider.Received(1).GetCssTemplate(null);
	}

	[Test]
	[Description("In workspace-write mode (workspace-directory + package-name) writes theme.css + theme.json into the package's Files/themes/<css-class-name>/ and returns the path without the CSS payload (token cost).")]
	public void BuildTheme_ShouldWriteFilesAndReturnPath_WhenWorkspaceAndPackageProvided() {
		// Arrange
		string workspaceDir = Path.Combine(Path.GetTempPath(), "clio-theme-ws");
		string packagePath = Path.Combine(workspaceDir, "packages", "UsrTheme");
		string themeDir = Path.Combine(packagePath, "Files", "themes", "MyTheme");
		_workspacePathBuilder.IsWorkspace.Returns(true);
		_workspacePathBuilder.BuildPackagePath("UsrTheme").Returns(packagePath);
		_fileSystem.ExistsDirectory(packagePath).Returns(true);

		// Act
		BuildThemeResult result = _tool.BuildTheme(new BuildThemeArgs(Primary: "#004fd6", CssClassName: "MyTheme",
			WorkspaceDirectory: workspaceDir, PackageName: "UsrTheme"));

		// Assert
		result.Success.Should().BeTrue(because: "a valid workspace + existing package is a valid workspace-write request");
		result.Path.Should().Be(themeDir, because: "the tool resolves <ws>/packages/<pkg>/Files/themes/<css-class-name> and returns where it wrote");
		result.Css.Should().BeNull(because: "the CSS payload is omitted in workspace-write mode to keep the large string out of the agent context");
		result.Descriptor.Should().BeNull(because: "the descriptor is written to disk, not returned, in workspace-write mode");
		result.Error.Should().BeNull(because: "a successful write carries no error");
		_fileSystem.Received(1).WriteAllTextToFile(Path.Combine(themeDir, "theme.css"), "built-css");
		_fileSystem.Received(1).WriteAllTextToFile(Path.Combine(themeDir, "theme.json"), Arg.Any<string>());
	}

	[Test]
	[Description("Returns a graceful failure (no write) when workspace-directory is given without package-name; the two must be provided together to write into a package.")]
	public void BuildTheme_ShouldReturnFailure_WhenWorkspaceProvidedWithoutPackage() {
		// Act
		string workspaceDir = Path.Combine(Path.GetTempPath(), "clio-theme-ws");
		BuildThemeResult result = _tool.BuildTheme(new BuildThemeArgs(Primary: "#004fd6", CssClassName: "MyTheme",
			WorkspaceDirectory: workspaceDir));

		// Assert
		result.Success.Should().BeFalse(because: "writing into a package needs both the workspace and the package name");
		result.Error.Should().Contain("workspace-directory and package-name must be provided together",
			because: "the error must name the two kebab-case arguments that are provided together");
		_fileSystem.DidNotReceive().WriteAllTextToFile(Arg.Any<string>(), Arg.Any<string>());
	}

	[Test]
	[Description("Returns a graceful failure (no write) when package-name is given without workspace-directory; the two must be provided together to write into a package.")]
	public void BuildTheme_ShouldReturnFailure_WhenPackageProvidedWithoutWorkspace() {
		// Act
		BuildThemeResult result = _tool.BuildTheme(new BuildThemeArgs(Primary: "#004fd6", CssClassName: "MyTheme",
			PackageName: "UsrTheme"));

		// Assert
		result.Success.Should().BeFalse(because: "a package name alone must not silently fall back to compute mode the caller did not ask for");
		result.Error.Should().Contain("workspace-directory and package-name must be provided together",
			because: "the error must name the two kebab-case arguments that are provided together");
		_fileSystem.DidNotReceive().WriteAllTextToFile(Arg.Any<string>(), Arg.Any<string>());
	}

	[Test]
	[Description("Returns a graceful failure (no write, no throw) when workspace-directory is not a fully-qualified absolute path, because the MCP server working directory differs from the caller's.")]
	public void BuildTheme_ShouldReturnFailure_WhenWorkspaceDirectoryNotAbsolute() {
		// Act
		BuildThemeResult result = _tool.BuildTheme(new BuildThemeArgs(Primary: "#004fd6", CssClassName: "MyTheme",
			WorkspaceDirectory: "relative/ws", PackageName: "UsrTheme"));

		// Assert
		result.Success.Should().BeFalse(because: "a non-absolute workspace path is ambiguous under the MCP server working directory");
		result.Error.Should().Contain("workspace-directory must be a fully-qualified absolute path",
			because: "the error must name the kebab-case argument and explain that an absolute path is required");
		_themeCssBuilder.DidNotReceive().Build(Arg.Any<string>(), Arg.Any<BuildThemeInput>());
		_fileSystem.DidNotReceive().WriteAllTextToFile(Arg.Any<string>(), Arg.Any<string>());
	}

	[Test]
	[Description("Returns a graceful failure (no write) when package-name is not a simple identifier, keeping the write inside the workspace.")]
	public void BuildTheme_ShouldReturnFailure_WhenPackageNameIsNotSimpleIdentifier() {
		// Act
		BuildThemeResult result = _tool.BuildTheme(new BuildThemeArgs(Primary: "#004fd6", CssClassName: "MyTheme",
			WorkspaceDirectory: Path.GetTempPath(), PackageName: "../UsrTheme"));

		// Assert
		result.Success.Should().BeFalse(because: "path separators in the package name could escape the workspace");
		result.Error.Should().Contain("package-name must be a simple identifier",
			because: "the error must name the kebab-case argument and the identifier constraint");
		_themeCssBuilder.DidNotReceive().Build(Arg.Any<string>(), Arg.Any<BuildThemeInput>());
		_fileSystem.DidNotReceive().WriteAllTextToFile(Arg.Any<string>(), Arg.Any<string>());
	}

	[Test]
	[Description("Returns a graceful failure (no write) when package-name carries a trailing newline, which the identifier rule must not tolerate.")]
	public void BuildTheme_ShouldReturnFailure_WhenPackageNameHasTrailingNewline() {
		// Act
		BuildThemeResult result = _tool.BuildTheme(new BuildThemeArgs(Primary: "#004fd6", CssClassName: "MyTheme",
			WorkspaceDirectory: Path.GetTempPath(), PackageName: "UsrTheme\n"));

		// Assert
		result.Success.Should().BeFalse(because: "a trailing newline is not part of a simple identifier");
		result.Error.Should().Contain("package-name must be a simple identifier",
			because: "the error must name the kebab-case argument and the identifier constraint");
		_fileSystem.DidNotReceive().WriteAllTextToFile(Arg.Any<string>(), Arg.Any<string>());
	}

	[Test]
	[Description("Returns a graceful failure (no build, no write) when an explicit css-class-name could escape the theme directory, because the resolved css-class-name becomes a filesystem path segment.")]
	public void BuildTheme_ShouldReturnFailure_WhenCssClassNameEscapesWorkspace() {
		// Act — a valid absolute workspace and simple package name clear the earlier gates; the tool-boundary
		// css-class-name guard then rejects the traversal value before any path is written.
		BuildThemeResult result = _tool.BuildTheme(new BuildThemeArgs(Primary: "#004fd6", CssClassName: "../evil",
			WorkspaceDirectory: Path.GetTempPath(), PackageName: "UsrTheme"));

		// Assert
		result.Success.Should().BeFalse(because: "path separators in the css-class-name could escape the theme directory");
		result.Error.Should().Contain("css-class-name",
			because: "the error must name the kebab-case argument the caller has to fix");
		_themeCssBuilder.DidNotReceive().Build(Arg.Any<string>(), Arg.Any<BuildThemeInput>());
		_fileSystem.DidNotReceive().WriteAllTextToFile(Arg.Any<string>(), Arg.Any<string>());
	}

	[Test]
	[Description("Returns a graceful failure (no write) when the workspace-directory is not a clio workspace (no .clio/workspaceSettings.json).")]
	public void BuildTheme_ShouldReturnFailure_WhenNotAWorkspace() {
		// Arrange
		string workspaceDir = Path.Combine(Path.GetTempPath(), "clio-theme-notws");
		_workspacePathBuilder.IsWorkspace.Returns(false);

		// Act
		BuildThemeResult result = _tool.BuildTheme(new BuildThemeArgs(Primary: "#004fd6", CssClassName: "MyTheme",
			WorkspaceDirectory: workspaceDir, PackageName: "UsrTheme"));

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
		BuildThemeResult result = _tool.BuildTheme(new BuildThemeArgs(Primary: "#004fd6", CssClassName: "MyTheme",
			WorkspaceDirectory: workspaceDir, PackageName: "Ghost"));

		// Assert
		result.Success.Should().BeFalse(because: "the theme cannot be written into a package that does not exist");
		result.Error.Should().Contain("Ghost", because: "the error must name the missing package");
		_fileSystem.DidNotReceive().WriteAllTextToFile(Arg.Any<string>(), Arg.Any<string>());
	}

	[Test]
	[Description("Binds the build-theme argument record from kebab-case JSON using the real MCP serializer options, and routes camelCase spellings into the overflow bag — the exact JSON->record binding the MCP host performs, which direct method calls bypass.")]
	public void BuildThemeArgs_ShouldBindKebabAndRouteCamelToExtensionData_WhenDeserializedFromRawJson() {
		// Arrange
		JsonSerializerOptions options = Clio.BindingsModule.CreateMcpSerializerOptions();

		// Act
		BuildThemeArgs kebab = JsonSerializer.Deserialize<BuildThemeArgs>(
			"""{"primary":"#004fd6","css-class-name":"MyTheme","heading-font":"Inter","body-font":"Roboto","font-weights":[400,700],"environment-name":"dev","workspace-directory":"C:/ws","package-name":"UsrTheme"}""",
			options)!;
		BuildThemeArgs camel = JsonSerializer.Deserialize<BuildThemeArgs>(
			"""{"fontWeights":[400,700]}""", options)!;

		// Assert
		kebab.Primary.Should().Be("#004fd6", because: "the advertised primary field must bind");
		kebab.CssClassName.Should().Be("MyTheme", because: "the advertised kebab-case css-class-name field must bind");
		kebab.HeadingFont.Should().Be("Inter", because: "the advertised kebab-case heading-font field must bind");
		kebab.BodyFont.Should().Be("Roboto", because: "the advertised kebab-case body-font field must bind");
		kebab.FontWeights.Should().BeEquivalentTo(new[] { 400, 700 },
			because: "the advertised kebab-case font-weights array field must bind");
		kebab.EnvironmentName.Should().Be("dev", because: "the advertised kebab-case environment-name field must bind");
		kebab.WorkspaceDirectory.Should().Be("C:/ws", because: "the advertised kebab-case workspace-directory field must bind");
		kebab.PackageName.Should().Be("UsrTheme", because: "the advertised kebab-case package-name field must bind");
		(kebab.ExtensionData is null || kebab.ExtensionData.Count == 0).Should().BeTrue(
			because: "every kebab field binds to a declared parameter, so nothing overflows");
		camel.FontWeights.Should().BeNull(
			because: "fontWeights is not a declared wire name, so it must not bind");
		camel.ExtensionData.Should().ContainKey("fontWeights",
			because: "the unbound camelCase spelling must land in the overflow bag so the tool can return a rename hint");
	}

}
