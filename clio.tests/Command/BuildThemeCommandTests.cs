namespace Clio.Tests.Command;

using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Clio.Command.McpServer.Tools;
using Clio.Command.Theming;
using Clio.Common;
using Clio.Theming;
using Clio.UserEnvironment;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NUnit.Framework;

[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
public class BuildThemeCommandTests : BaseCommandTests<BuildThemeOptions>
{
	private IThemeCssBuilder _themeCssBuilder;
	private IThemeTemplateProvider _themeTemplateProvider;
	private IPlatformVersionResolverFactory _resolverFactory;
	private ISettingsRepository _settingsRepository;
	private IFileSystem _fileSystem;
	private ILogger _logger;
	private BuildThemeCommand _command;

	public override void Setup() {
		base.Setup();
		_command = Container.GetRequiredService<BuildThemeCommand>();
		_themeTemplateProvider.GetCssTemplate(Arg.Any<string>()).Returns("template-css");
		_themeCssBuilder.Build(Arg.Any<string>(), Arg.Any<BuildThemeInput>()).Returns("built-css");
	}

	public override void TearDown() {
		_themeCssBuilder.ClearReceivedCalls();
		_themeTemplateProvider.ClearReceivedCalls();
		_resolverFactory.ClearReceivedCalls();
		_settingsRepository.ClearReceivedCalls();
		_fileSystem.ClearReceivedCalls();
		_logger.ClearReceivedCalls();
		base.TearDown();
	}

	protected override void AdditionalRegistrations(IServiceCollection containerBuilder) {
		base.AdditionalRegistrations(containerBuilder);
		_themeCssBuilder = Substitute.For<IThemeCssBuilder>();
		_themeTemplateProvider = Substitute.For<IThemeTemplateProvider>();
		_resolverFactory = Substitute.For<IPlatformVersionResolverFactory>();
		_settingsRepository = Substitute.For<ISettingsRepository>();
		_fileSystem = Substitute.For<IFileSystem>();
		_logger = Substitute.For<ILogger>();
		containerBuilder.AddTransient<IThemeCssBuilder>(_ => _themeCssBuilder);
		containerBuilder.AddTransient<IThemeTemplateProvider>(_ => _themeTemplateProvider);
		containerBuilder.AddTransient<IPlatformVersionResolverFactory>(_ => _resolverFactory);
		containerBuilder.AddTransient<ISettingsRepository>(_ => _settingsRepository);
		containerBuilder.AddTransient<IFileSystem>(_ => _fileSystem);
		containerBuilder.AddTransient<ILogger>(_ => _logger);
	}

	private static BuildThemeOptions ValidOptions() => new() {
		Primary = "#004fd6",
		CssClassName = "MyTheme"
	};

	[Test, Category("Unit")]
	[Description("Writes the built theme.css to stdout and touches no files when --output is omitted.")]
	public void Execute_ShouldWriteCssToStdout_WhenOutputOmitted() {
		// Arrange
		BuildThemeOptions options = ValidOptions();

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(0, because: "a successful build returns 0");
		_themeCssBuilder.Received(1).Build(Arg.Any<string>(),
			Arg.Is<BuildThemeInput>(o => o.Primary == "#004fd6" && o.ThemeCssClass == "MyTheme"));
		_logger.Received(1).WriteInfo("built-css");
		_fileSystem.DidNotReceive().WriteAllTextToFile(Arg.Any<string>(), Arg.Any<string>());
	}

	[Test, Category("Unit")]
	[Description("Writes both theme.css and theme.json (filled from the bundled descriptor) into the output directory.")]
	public void Execute_ShouldWriteThemeCssAndThemeJson_WhenOutputIsDirectory() {
		// Arrange
		_themeTemplateProvider.GetJsonTemplate(Arg.Any<string>())
			.Returns("{\"id\":\"<%themeId%>\",\"caption\":\"<%themeCaption%>\",\"cssClassName\":\"<%themeCssClass%>\"}");
		BuildThemeOptions options = ValidOptions();
		options.Output = "out-dir";
		options.Id = "my-id";
		options.Caption = "My Caption";

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(0, because: "writing the theme files succeeds");
		_fileSystem.Received(1).CreateDirectoryIfNotExists("out-dir");
		_fileSystem.Received(1).WriteAllTextToFile(Path.Combine("out-dir", "theme.css"), "built-css");
		_fileSystem.Received(1).WriteAllTextToFile(Path.Combine("out-dir", "theme.json"),
			"{\"id\":\"my-id\",\"caption\":\"My Caption\",\"cssClassName\":\"MyTheme\"}");
	}

	[Test, Category("Unit")]
	[Description("Escapes special characters when writing theme.json so a caption containing a double quote yields valid JSON and cannot inject or overwrite sibling descriptor fields.")]
	public void Execute_ShouldWriteValidJsonThemeDescriptor_WhenCaptionContainsQuotes() {
		// Arrange
		_themeTemplateProvider.GetJsonTemplate(Arg.Any<string>())
			.Returns("{\"id\":\"<%themeId%>\",\"caption\":\"<%themeCaption%>\",\"cssClassName\":\"<%themeCssClass%>\"}");
		string capturedJson = null;
		_fileSystem.WriteAllTextToFile(Path.Combine("out-dir", "theme.json"), Arg.Do<string>(json => capturedJson = json));
		const string maliciousCaption = "evil\",\"cssClassName\":\"injected";
		BuildThemeOptions options = ValidOptions();
		options.Output = "out-dir";
		options.Id = "my-id";
		options.Caption = maliciousCaption;

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(0, because: "a caption with special characters is valid input, not an error");
		capturedJson.Should().NotBeNull(because: "the command must write theme.json in directory-output mode");
		using JsonDocument descriptor = JsonDocument.Parse(capturedJson);
		descriptor.RootElement.GetProperty("caption").GetString().Should().Be(maliciousCaption,
			because: "the caption must round-trip through JSON escaping unchanged, proving the descriptor stays valid JSON");
		descriptor.RootElement.GetProperty("cssClassName").GetString().Should().Be("MyTheme",
			because: "a crafted caption must not break out of its JSON string to overwrite the validated cssClassName field");
	}

	[Test, Category("Unit")]
	[Description("Defaults the theme.json id to an auto-generated UUID and the caption to the CSS class name when --id and --caption are omitted.")]
	public void Execute_ShouldDefaultIdAndCaption_WhenOmitted() {
		// Arrange
		_themeTemplateProvider.GetJsonTemplate(Arg.Any<string>())
			.Returns("{\"id\":\"<%themeId%>\",\"caption\":\"<%themeCaption%>\",\"cssClassName\":\"<%themeCssClass%>\"}");
		string capturedJson = null;
		_fileSystem.WriteAllTextToFile(Path.Combine("out-dir", "theme.json"), Arg.Do<string>(json => capturedJson = json));
		BuildThemeOptions options = ValidOptions();
		options.Output = "out-dir";

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(0, because: "omitting --id and --caption is valid — both are defaulted");
		using JsonDocument descriptor = JsonDocument.Parse(capturedJson);
		descriptor.RootElement.GetProperty("caption").GetString().Should().Be("MyTheme",
			because: "the caption defaults to the CSS class name when --caption is omitted");
		Guid.TryParse(descriptor.RootElement.GetProperty("id").GetString(), out _).Should().BeTrue(
			because: "the id defaults to an auto-generated UUID when --id is omitted");
	}

	[Test, Category("Unit")]
	[Description("Forwards --heading-font, --body-font, and --font-weights into the builder's FontsInput so the font path is not bypassed.")]
	public void Execute_ShouldForwardFonts_WhenFontOptionsProvided() {
		// Arrange
		BuildThemeOptions options = ValidOptions();
		options.HeadingFont = "Inter";
		options.BodyFont = "Roboto";
		options.FontWeights = new[] { 400, 600 };

		// Act
		_command.Execute(options);

		// Assert
		_themeCssBuilder.Received(1).Build(Arg.Any<string>(),
			Arg.Is<BuildThemeInput>(o => o.Fonts != null
				&& o.Fonts.Heading == "Inter"
				&& o.Fonts.Body == "Roboto"
				&& o.Fonts.Weights.Count == 2
				&& o.Fonts.Weights[0] == 400
				&& o.Fonts.Weights[1] == 600));
	}

	[Test, Category("Unit")]
	[Description("Warns and still builds the theme when --font-weights is given without a font family (the weights are ignored, not fatal).")]
	public void Execute_ShouldWarn_WhenFontWeightsWithoutFamily() {
		// Arrange
		BuildThemeOptions options = ValidOptions();
		options.FontWeights = new[] { 400, 700 };

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(0, because: "font weights without a family is a non-fatal advisory, not an error");
		_logger.Received(1).WriteWarning(Arg.Is<string>(m => m.Contains("font weights")));
		_themeCssBuilder.Received(1).Build(Arg.Any<string>(), Arg.Any<BuildThemeInput>());
	}

	[Test, Category("Unit")]
	[Description("Warns and still builds the theme when --accent is omitted and no generated candidate clears the accessibility gates (the degenerate fallback is advisory, not fatal).")]
	public void Execute_ShouldWarn_WhenAutoAccentFailsAccessibilityGates() {
		// Arrange — for #7b1fa2 none of the generated accent candidates clears both the 3:1 contrast and 0.07 distance gates.
		BuildThemeOptions options = ValidOptions();
		options.Primary = "#7b1fa2";

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(0, because: "a degenerate auto-accent is a non-fatal advisory, not an error");
		_logger.Received(1).WriteWarning(Arg.Is<string>(m => m.Contains("auto-selected accent")));
	}

	[Test, Category("Unit")]
	[Description("Emits no auto-accent warning when --accent is supplied explicitly, even for a primary whose generated candidates all fail the gates.")]
	public void Execute_ShouldNotWarnAboutAccent_WhenAccentProvidedExplicitly() {
		// Arrange
		BuildThemeOptions options = ValidOptions();
		options.Primary = "#7b1fa2";
		options.Accent = "#f94e11";

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(0, because: "an explicit accent skips the auto-selection entirely");
		_logger.DidNotReceive().WriteWarning(Arg.Any<string>());
	}

	[Test, Category("Unit")]
	[Description("Passes an explicit --version straight to the template provider without touching any environment.")]
	public void Execute_ShouldUseExplicitVersion_WhenCreatioVersionProvided() {
		// Arrange
		BuildThemeOptions options = ValidOptions();
		options.Version = "11.0";

		// Act
		_command.Execute(options);

		// Assert
		_themeTemplateProvider.Received(1).GetCssTemplate("11.0");
		_settingsRepository.DidNotReceive().FindEnvironment(Arg.Any<string>());
		_resolverFactory.DidNotReceive().Create(Arg.Any<EnvironmentSettings>());
	}

	[Test, Category("Unit")]
	[Description("Resolves the Creatio version from the named environment via the platform-version resolver.")]
	public void Execute_ShouldResolveVersionFromEnvironment_WhenEnvironmentNameProvided() {
		// Arrange
		EnvironmentSettings env = new() { Uri = "http://env" };
		_settingsRepository.FindEnvironment("dev").Returns(env);
		IPlatformVersionResolver resolver = Substitute.For<IPlatformVersionResolver>();
		resolver.ResolveAsync(Arg.Any<CancellationToken>())
			.Returns(Task.FromResult(new PlatformVersionResolution("10.0.1", VersionResolutionSource.Environment)));
		_resolverFactory.Create(env).Returns(resolver);
		BuildThemeOptions options = ValidOptions();
		options.EnvironmentName = "dev";

		// Act
		_command.Execute(options);

		// Assert
		_themeTemplateProvider.Received(1).GetCssTemplate("10.0.1");
	}

	[Test, Category("Unit")]
	[Description("Fails when the named environment's Creatio version cannot be determined (resolver latest-fallback), instead of silently using the highest bundled template.")]
	public void Execute_ShouldFail_WhenEnvironmentVersionUnresolved() {
		// Arrange
		EnvironmentSettings env = new() { Uri = "http://env" };
		_settingsRepository.FindEnvironment("dev").Returns(env);
		IPlatformVersionResolver resolver = Substitute.For<IPlatformVersionResolver>();
		resolver.ResolveAsync(Arg.Any<CancellationToken>())
			.Returns(Task.FromResult(new PlatformVersionResolution("latest", VersionResolutionSource.LatestFallback)));
		_resolverFactory.Create(env).Returns(resolver);
		BuildThemeOptions options = ValidOptions();
		options.EnvironmentName = "dev";

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(1, because: "an undeterminable environment version must not silently fall back to the highest bundled template");
		_logger.Received(1).WriteError(Arg.Is<string>(m => m.Contains("could not determine") && m.Contains("dev")));
		_themeTemplateProvider.DidNotReceive().GetCssTemplate(Arg.Any<string>());
	}

	[Test, Category("Unit")]
	[Description("Fails with a clear error when the named environment is not registered.")]
	public void Execute_ShouldFail_WhenEnvironmentNotRegistered() {
		// Arrange
		_settingsRepository.FindEnvironment("ghost").Returns((EnvironmentSettings)null);
		BuildThemeOptions options = ValidOptions();
		options.EnvironmentName = "ghost";

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(1, because: "an unregistered environment cannot resolve a Creatio version");
		_logger.Received(1).WriteError(Arg.Is<string>(m => m.Contains("not registered")));
		_themeTemplateProvider.DidNotReceive().GetCssTemplate(Arg.Any<string>());
	}

	[Test, Category("Unit")]
	[Description("Fails when both --version and --environment-name are supplied (mutually exclusive).")]
	public void Execute_ShouldFail_WhenBothVersionAndEnvironmentProvided() {
		// Arrange
		BuildThemeOptions options = ValidOptions();
		options.Version = "10.0";
		options.EnvironmentName = "dev";

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(1, because: "the version source must be unambiguous");
		_logger.Received(1).WriteError(Arg.Is<string>(m => m.Contains("mutually exclusive")));
		_themeTemplateProvider.DidNotReceive().GetCssTemplate(Arg.Any<string>());
	}

	[Test, Category("Unit")]
	[Description("Uses the highest bundled template (null target) when neither version source is supplied.")]
	public void Execute_ShouldUseHighestBundled_WhenNeitherVersionNorEnvironment() {
		// Arrange
		BuildThemeOptions options = ValidOptions();

		// Act
		_command.Execute(options);

		// Assert
		_themeTemplateProvider.Received(1).GetCssTemplate(null);
	}

	[Test, Category("Unit")]
	[Description("Fails with a friendly write diagnostic instead of an unhandled exception when the output directory is not writable.")]
	public void Execute_ShouldFailGracefully_WhenOutputDirectoryIsNotWritable() {
		// Arrange
		_themeTemplateProvider.GetJsonTemplate(Arg.Any<string>())
			.Returns("{\"id\":\"<%themeId%>\",\"caption\":\"<%themeCaption%>\",\"cssClassName\":\"<%themeCssClass%>\"}");
		_fileSystem.When(fs => fs.WriteAllTextToFile(Arg.Any<string>(), Arg.Any<string>()))
			.Do(_ => throw new IOException("disk full"));
		BuildThemeOptions options = ValidOptions();
		options.Output = "out-dir";

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(1, because: "a write failure must surface as a command failure, not an unhandled exception");
		_logger.Received(1).WriteError(Arg.Is<string>(m => m.Contains("failed to write theme files") && m.Contains("out-dir")));
	}

	[Test, Category("Unit")]
	[Description("Surfaces the builder's validation error and returns a non-zero exit code instead of crashing.")]
	public void Execute_ShouldFailGracefully_WhenBuilderThrowsArgumentException() {
		// Arrange
		_themeCssBuilder.Build(Arg.Any<string>(), Arg.Any<BuildThemeInput>())
			.Returns(_ => throw new ArgumentException("PRIMARY_REQUIRED: a primary color is required."));
		BuildThemeOptions options = ValidOptions();

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(1, because: "an invalid input must surface as a command failure, not a crash");
		_logger.Received(1).WriteError(Arg.Is<string>(m => m.Contains("PRIMARY_REQUIRED")));
	}
}
