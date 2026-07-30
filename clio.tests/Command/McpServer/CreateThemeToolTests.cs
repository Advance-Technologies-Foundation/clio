using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Clio.Command;
using Clio.Command.McpServer.Tools;
using Clio.Command.Theming;
using Clio.Common;
using Clio.Theming;
using Clio.UserEnvironment;
using Clio.Workspaces;
using FluentAssertions;
using ModelContextProtocol.Server;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
[Property("Module", "McpServer")]
public class CreateThemeToolTests {

	[Test]
	[Category("Unit")]
	[Description("Declares the FR-12 safety flags on the create-theme tool method: a write that is not destructive, not idempotent, and closed-world.")]
	public void CreateThemeTool_ShouldDeclareCreateSafetyFlags_WhenInspectingMcpServerToolAttribute() {
		// Arrange & Act
		McpServerToolAttribute attribute = (McpServerToolAttribute)typeof(CreateThemeTool)
			.GetMethod(nameof(CreateThemeTool.CreateTheme))!
			.GetCustomAttributes(typeof(McpServerToolAttribute), false)
			.Single();

		// Assert
		attribute.Name.Should().Be(CreateThemeTool.ToolName, because: "the tool must be published under its canonical kebab-case name");
		attribute.ReadOnly.Should().BeFalse(because: "creating a theme writes to the environment");
		attribute.Destructive.Should().BeFalse(because: "create adds a new theme without destroying existing state");
		attribute.Idempotent.Should().BeFalse(because: "a repeated create yields a different theme (new auto-id) rather than the same end state");
		attribute.OpenWorld.Should().BeFalse(because: "the tool only touches the addressed Creatio environment");
	}

	[Test]
	[Category("Unit")]
	[Description("Marks the single args wrapper as required at the MCP schema level, so a call that omits args fails with a structured error instead of an opaque binding failure.")]
	public void CreateThemeTool_ShouldRequireArgsWrapper_WhenInspectingMethodSignature() {
		// Arrange & Act
		object[] requiredAttributes = typeof(CreateThemeTool)
			.GetMethod(nameof(CreateThemeTool.CreateTheme))!
			.GetParameters()[0]
			.GetCustomAttributes(typeof(RequiredAttribute), false);

		// Assert
		requiredAttributes.Should().NotBeEmpty(
			because: "the args wrapper must be schema-required so an omitted args object fails with a structured error, not an opaque MCP binding failure");
	}

	[Test]
	[Category("Unit")]
	[Description("Contract snapshot of the brand-mode args surface: css-content must stay schema-optional (the brand mode is the alternative CSS source), and the nine brand properties must exist under their advertised kebab-case wire names.")]
	public void CreateThemeArgs_ShouldKeepCssContentOptionalAndDeclareBrandWireNames_WhenInspectingContract() {
		// Arrange
		(string PropertyName, string WireName)[] brandProperties = [
			(nameof(CreateThemeArgs.Primary), "primary"),
			(nameof(CreateThemeArgs.Secondary), "secondary"),
			(nameof(CreateThemeArgs.Accent), "accent"),
			(nameof(CreateThemeArgs.Success), "success"),
			(nameof(CreateThemeArgs.Error), "error"),
			(nameof(CreateThemeArgs.HeadingFont), "heading-font"),
			(nameof(CreateThemeArgs.BodyFont), "body-font"),
			(nameof(CreateThemeArgs.FontWeights), "font-weights"),
			(nameof(CreateThemeArgs.Version), "version")
		];

		// Act
		object[] cssContentRequiredAttributes = typeof(CreateThemeArgs)
			.GetProperty(nameof(CreateThemeArgs.CssContent))!
			.GetCustomAttributes(typeof(RequiredAttribute), false);

		// Assert
		cssContentRequiredAttributes.Should().BeEmpty(
			because: "css-content must not be schema-required — the brand mode (primary) is the alternative CSS source, and a Required css-content would fail every brand-mode call at the MCP schema level");
		foreach ((string propertyName, string wireName) in brandProperties) {
			PropertyInfo property = typeof(CreateThemeArgs).GetProperty(propertyName);
			property.Should().NotBeNull(
				because: $"the advertised brand parameter '{wireName}' must exist on the args record");
			JsonPropertyNameAttribute wireNameAttribute = property!.GetCustomAttribute<JsonPropertyNameAttribute>();
			wireNameAttribute.Should().NotBeNull(
				because: $"the brand parameter '{wireName}' must declare its kebab-case wire name explicitly");
			wireNameAttribute!.Name.Should().Be(wireName,
				because: "the wire name the tool description advertises must match the serialized property name exactly");
		}
	}

	[Test]
	[Description("Resolves the environment-name create-theme MCP tool, forwards the theme fields, and returns the created id as a structured success result.")]
	[Category("Unit")]
	public void CreateTheme_ShouldResolveCommandAndReturnCreatedId() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		FakeCreateThemeCommand defaultCommand = new();
		FakeCreateThemeCommand resolvedCommand = new(createdId: "generated-id");
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<CreateThemeCommand>(Arg.Any<CreateThemeOptions>()).Returns(resolvedCommand);
		commandResolver.Resolve<ICreatioVersionChecker>(Arg.Any<EnvironmentOptions>())
			.Returns(Substitute.For<ICreatioVersionChecker>());
		CreateThemeTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		// Act
		CreateThemeResult result = tool.CreateTheme(new CreateThemeArgs(
			EnvironmentName: "docker_fix2", CssClassName: "ocean-theme", CssContent: ".ocean-theme{}", Caption: "Ocean",
			Id: "explicit-id", PackageName: "UsrBranding"));

		// Assert
		result.Success.Should().BeTrue(because: "a created theme must report success");
		result.Id.Should().Be("generated-id", because: "the effective (possibly auto-generated) id must be surfaced for follow-up calls");
		commandResolver.Received(1).Resolve<CreateThemeCommand>(Arg.Is<CreateThemeOptions>(options =>
			options.Environment == "docker_fix2" &&
			options.Caption == "Ocean" &&
			options.CssClassName == "ocean-theme" &&
			options.CssContent == ".ocean-theme{}" &&
			options.Id == "explicit-id" &&
			options.PackageName == "UsrBranding"));
		resolvedCommand.CapturedOptions.Should().NotBeNull(because: "the resolved command instance should create the theme");
		defaultCommand.CapturedOptions.Should().BeNull(because: "the environment-aware tool path must use the resolved command instance");
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Description("Returns a structured failure without resolving a command when the environment name is empty.")]
	[Category("Unit")]
	public void CreateTheme_ShouldReturnFailure_WhenEnvironmentNameIsEmpty() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		FakeCreateThemeCommand defaultCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		CreateThemeTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		// Act
		CreateThemeResult result = tool.CreateTheme(new CreateThemeArgs(
			EnvironmentName: "   ", CssClassName: "ocean-theme", CssContent: ".ocean-theme{}", Caption: "Ocean"));

		// Assert
		result.Success.Should().BeFalse(because: "an empty environment name is an invalid request");
		result.Error.Should().Contain("environment-name",
			because: "the failure must name the exact kebab-case field the caller has to fix");
		commandResolver.DidNotReceive().Resolve<CreateThemeCommand>(Arg.Any<CreateThemeOptions>());
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Description("Returns a structured failure naming environment-name when the required environment name is omitted.")]
	[Category("Unit")]
	public void CreateTheme_ShouldReturnFailure_WhenEnvironmentNameIsMissing() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		FakeCreateThemeCommand defaultCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		CreateThemeTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		// Act
		CreateThemeResult result = tool.CreateTheme(new CreateThemeArgs(
			CssClassName: "ocean-theme", CssContent: ".ocean-theme{}", Caption: "Ocean"));

		// Assert
		result.Success.Should().BeFalse(because: "a create request without an environment name is invalid");
		result.Error.Should().Contain("environment-name",
			because: "the failure must name the exact kebab-case field the caller has to add");
		commandResolver.DidNotReceive().Resolve<CreateThemeCommand>(Arg.Any<CreateThemeOptions>());
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Description("Surfaces the command failure message as a structured failure when the resolved command reports failure.")]
	[Category("Unit")]
	public void CreateTheme_ShouldReturnFailure_WhenCommandReportsFailure() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		FakeCreateThemeCommand defaultCommand = new();
		FakeCreateThemeCommand resolvedCommand = new(success: false, error: "id already exists");
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<CreateThemeCommand>(Arg.Any<CreateThemeOptions>()).Returns(resolvedCommand);
		commandResolver.Resolve<ICreatioVersionChecker>(Arg.Any<EnvironmentOptions>())
			.Returns(Substitute.For<ICreatioVersionChecker>());
		CreateThemeTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		// Act
		CreateThemeResult result = tool.CreateTheme(new CreateThemeArgs(
			EnvironmentName: "docker_fix2", CssClassName: "ocean-theme", CssContent: ".ocean-theme{}", Caption: "Ocean"));

		// Assert
		result.Success.Should().BeFalse(because: "a command failure must surface as a tool failure");
		result.Error.Should().Contain("id already exists", because: "the server-provided message must be forwarded");
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Description("Redacts a sensitive ThemeService error body before it crosses into the MCP client transcript (review: b-horodyskyi — the TryCreateTheme errorMessage out-param bypassed ExecuteResolved's exception handling entirely).")]
	[Category("Unit")]
	public void CreateTheme_ShouldRedactSensitiveText_WhenCommandReportsFailure() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		FakeCreateThemeCommand defaultCommand = new();
		FakeCreateThemeCommand resolvedCommand = new(success: false,
			error: "Unexpected response from server: https://internal-host.example/ThemeService?token=sekret123");
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<CreateThemeCommand>(Arg.Any<CreateThemeOptions>()).Returns(resolvedCommand);
		commandResolver.Resolve<ICreatioVersionChecker>(Arg.Any<EnvironmentOptions>())
			.Returns(Substitute.For<ICreatioVersionChecker>());
		CreateThemeTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		// Act
		CreateThemeResult result = tool.CreateTheme(new CreateThemeArgs(
			EnvironmentName: "docker_fix2", CssClassName: "ocean-theme", CssContent: ".ocean-theme{}", Caption: "Ocean"));

		// Assert
		result.Success.Should().BeFalse(because: "a command failure must surface as a tool failure");
		result.Error.Should().NotContain("internal-host.example",
			because: "the server-provided errorMessage can carry a target host, so it must be redacted before crossing the MCP boundary");
		result.Error.Should().NotContain("sekret123",
			because: "the server-provided errorMessage can carry a credential value, so it must be redacted before crossing the MCP boundary");
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Description("Returns a structured failure naming css-content when the required CSS payload is omitted.")]
	[Category("Unit")]
	public void CreateTheme_ShouldReturnFailure_WhenCssContentIsMissing() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		FakeCreateThemeCommand defaultCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		CreateThemeTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		// Act
		CreateThemeResult result = tool.CreateTheme(new CreateThemeArgs(
			EnvironmentName: "docker_fix2", Caption: "Ocean"));

		// Assert
		result.Success.Should().BeFalse(because: "a create request without CSS content is invalid");
		result.Error.Should().Contain("css-content", because: "the failure must name the exact kebab-case field the caller has to add");
		commandResolver.DidNotReceive().Resolve<CreateThemeCommand>(Arg.Any<CreateThemeOptions>());
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Description("Returns a structured failure naming css-content when the CSS payload is explicitly empty.")]
	[Category("Unit")]
	public void CreateTheme_ShouldReturnFailure_WhenCssContentIsEmpty() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		FakeCreateThemeCommand defaultCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		CreateThemeTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		// Act
		CreateThemeResult result = tool.CreateTheme(new CreateThemeArgs(
			EnvironmentName: "docker_fix2", CssClassName: "ocean-theme", CssContent: "", Caption: "Ocean"));

		// Assert
		result.Success.Should().BeFalse(because: "an explicitly empty css-content is invalid");
		result.Error.Should().Contain("css-content", because: "the failure must name the exact kebab-case field the caller has to fix");
		commandResolver.DidNotReceive().Resolve<CreateThemeCommand>(Arg.Any<CreateThemeOptions>());
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Description("Returns an actionable rename hint instead of silently ignoring a camelCase alias of a kebab-case argument.")]
	[Category("Unit")]
	public void CreateTheme_ShouldReturnRenameHint_WhenCamelCaseAliasIsPassed() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		FakeCreateThemeCommand defaultCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		CreateThemeTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);
		CreateThemeArgs args = new(CssContent: ".ocean-theme{}") {
			ExtensionData = new Dictionary<string, JsonElement> {
				["environmentName"] = JsonSerializer.SerializeToElement("docker_fix2")
			}
		};

		// Act
		CreateThemeResult result = tool.CreateTheme(args);

		// Assert
		result.Success.Should().BeFalse(because: "a camelCase alias must be rejected, not silently dropped");
		result.Error.Should().Contain("'environmentName' -> 'environment-name'",
			because: "the failure must tell the caller the exact rename that fixes the call");
		commandResolver.DidNotReceive().Resolve<CreateThemeCommand>(Arg.Any<CreateThemeOptions>());
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Description("Forwards a null caption when caption is omitted, leaving the command to derive it from cssClassName.")]
	[Category("Unit")]
	public void CreateTheme_Should_ForwardNullCaption_WhenCaptionOmitted() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		FakeCreateThemeCommand defaultCommand = new();
		FakeCreateThemeCommand resolvedCommand = new(createdId: "ocean");
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<CreateThemeCommand>(Arg.Any<CreateThemeOptions>()).Returns(resolvedCommand);
		commandResolver.Resolve<ICreatioVersionChecker>(Arg.Any<EnvironmentOptions>())
			.Returns(Substitute.For<ICreatioVersionChecker>());
		CreateThemeTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		// Act
		CreateThemeResult result = tool.CreateTheme(new CreateThemeArgs(
			EnvironmentName: "docker_fix2", CssClassName: "ocean-theme", CssContent: ".ocean-theme{}"));

		// Assert
		result.Success.Should().BeTrue(because: "caption is optional at the MCP surface");
		commandResolver.Received(1).Resolve<CreateThemeCommand>(Arg.Is<CreateThemeOptions>(options =>
			options.Caption == null && options.CssClassName == "ocean-theme"));
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Description("Binds the create-theme argument record from kebab-case JSON using the real MCP serializer options, and routes camelCase spellings into the overflow bag — the exact JSON->record binding the MCP host performs, which direct method calls bypass.")]
	[Category("Unit")]
	public void CreateThemeArgs_ShouldBindKebabCaseAndRouteCamelCaseToExtensionData() {
		// Arrange
		JsonSerializerOptions options = Clio.BindingsModule.CreateMcpSerializerOptions();

		// Act
		CreateThemeArgs kebab = JsonSerializer.Deserialize<CreateThemeArgs>(
			"""{"environment-name":"docker_fix2","css-content":".ocean-theme{}","css-class-name":"ocean-theme","caption":"Ocean","id":"ocean","package-name":"UsrBranding"}""",
			options)!;
		CreateThemeArgs camel = JsonSerializer.Deserialize<CreateThemeArgs>(
			"""{"cssContent":".ocean-theme{}"}""", options)!;

		// Assert
		kebab.EnvironmentName.Should().Be("docker_fix2", because: "the advertised kebab-case environment-name field must bind");
		kebab.CssContent.Should().Be(".ocean-theme{}", because: "the advertised kebab-case css-content field must bind");
		kebab.CssClassName.Should().Be("ocean-theme", because: "the advertised kebab-case css-class-name field must bind");
		kebab.Caption.Should().Be("Ocean", because: "the advertised caption field must bind");
		kebab.Id.Should().Be("ocean", because: "the advertised id field must bind");
		kebab.PackageName.Should().Be("UsrBranding", because: "the advertised kebab-case package-name field must bind");
		(kebab.ExtensionData is null || kebab.ExtensionData.Count == 0).Should().BeTrue(
			because: "every kebab field binds to a declared parameter, so nothing overflows");
		camel.CssContent.Should().BeNull(
			because: "cssContent is not a declared wire name, so it must not bind");
		camel.ExtensionData.Should().ContainKey("cssContent",
			because: "the unbound camelCase spelling must land in the overflow bag so the tool can return a rename hint");
	}

	[Test]
	[Description("Returns a structured failure carrying the version-requirement message and never creates the theme when the target environment does not satisfy the Creatio version floor.")]
	[Category("Unit")]
	public void CreateTheme_ShouldReturnFailure_WhenCreatioVersionRequirementIsUnmet() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		FakeCreateThemeCommand defaultCommand = new();
		FakeCreateThemeCommand resolvedCommand = new(createdId: "generated-id");
		ICreatioVersionChecker versionChecker = Substitute.For<ICreatioVersionChecker>();
		versionChecker
			.When(c => c.EnsureRequirements(Arg.Any<object>()))
			.Do(_ => throw new CreatioVersionRequirementException(
				"This command requires Creatio 10.0.0 or later. The target environment runs 8.1.5. Update Creatio and retry.",
				CreatioVersionRequirementException.VersionTooOldCode));
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<CreateThemeCommand>(Arg.Any<CreateThemeOptions>()).Returns(resolvedCommand);
		commandResolver.Resolve<ICreatioVersionChecker>(Arg.Any<EnvironmentOptions>()).Returns(versionChecker);
		CreateThemeTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		// Act
		CreateThemeResult result = tool.CreateTheme(new CreateThemeArgs(
			EnvironmentName: "docker_fix2", CssClassName: "ocean-theme", CssContent: ".ocean-theme{}", Caption: "Ocean"));

		// Assert
		result.Success.Should().BeFalse(
			because: "an unmet Creatio version requirement must refuse the create on the MCP surface exactly as the CLI gate does");
		result.Error.Should().Contain("requires Creatio 10.0.0 or later",
			because: "the version-requirement message must be surfaced to the MCP caller");
		result.Error.Should().Contain($"[{CreatioVersionRequirementException.VersionTooOldCode}]",
			because: "the typed result carries no exit code, so the stable machine-readable ErrorCode must travel in the error message");
		resolvedCommand.CapturedOptions.Should().BeNull(
			because: "the theme must never be created when the environment does not satisfy the version floor");
		ConsoleLogger.Instance.ClearMessages();
	}

	// Brand mode (ENG-93989): create-theme composes BuildThemeCommand's resolvedSettings-aware TryBuildTheme
	// overload (virtual so these tests can substitute the build phase) to build the CSS server-side and create
	// the theme in one call, so the CSS never crosses the MCP client boundary. The inline css-content path
	// tested above stays unchanged.

	[Test]
	[Description("Builds the CSS server-side in the brand mode — mapping every brand argument 1:1 onto BuildThemeOptions and threading the create target's resolved settings into the build — and passes the built CSS verbatim into CreateThemeOptions.CssContent.")]
	[Category("Unit")]
	public void CreateTheme_ShouldBuildCssServerSideAndCreateTheme_WhenBrandModeSupplied() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		const string builtCss = ".ocean-theme { --crt-palette-primary-500: #004fd6; }";
		FakeCreateThemeCommand defaultCommand = new();
		FakeCreateThemeCommand resolvedCommand = new(createdId: "generated-id");
		FakeBuildThemeCommand buildCommand = new(css: builtCss);
		EnvironmentSettings resolvedSettings = new() { Uri = "https://docker-fix2.creatio.com" };
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<CreateThemeCommand>(Arg.Any<CreateThemeOptions>()).Returns(resolvedCommand);
		commandResolver.Resolve<ICreatioVersionChecker>(Arg.Any<EnvironmentOptions>())
			.Returns(Substitute.For<ICreatioVersionChecker>());
		commandResolver.Resolve<EnvironmentSettings>(Arg.Is<EnvironmentOptions>(o => o.Environment == "docker_fix2"))
			.Returns(resolvedSettings);
		commandResolver.Resolve<BuildThemeCommand>(Arg.Is<EnvironmentOptions>(o => o.Environment == "docker_fix2"))
			.Returns(buildCommand);
		CreateThemeTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		// Act
		CreateThemeResult result = tool.CreateTheme(new CreateThemeArgs(
			EnvironmentName: "docker_fix2", CssClassName: "ocean-theme", Caption: "Ocean", Id: "ocean",
			Primary: "#004fd6", Secondary: "#0d2e4e", Accent: "#f94e11", Success: "#0b8500", Error: "#d2310d",
			HeadingFont: "Inter", BodyFont: "Roboto", FontWeights: new[] { 400, 700 }));

		// Assert
		result.Success.Should().BeTrue(because: "a brand-mode create builds the CSS server-side and creates the theme in one call");
		result.Id.Should().Be("generated-id", because: "the effective id must be surfaced exactly as in the inline mode");
		buildCommand.CapturedOptions.Should().BeEquivalentTo(new BuildThemeOptions {
			Primary = "#004fd6", Secondary = "#0d2e4e", Accent = "#f94e11", Success = "#0b8500",
			Error = "#d2310d", CssClassName = "ocean-theme", Caption = "Ocean", Id = "ocean",
			HeadingFont = "Inter", BodyFont = "Roboto", FontWeights = new[] { 400, 700 }
		}, because: "every brand argument must map 1:1 onto BuildThemeOptions, with EnvironmentName kept null (the environment reaches the build only as resolvedSettings)");
		buildCommand.CapturedResolvedSettings.Should().BeSameAs(resolvedSettings,
			because: "without an explicit version the tool resolves the create target's settings so the template-version probe reaches the correct tenant");
		resolvedCommand.CapturedOptions.CssContent.Should().Be(builtCss,
			because: "the built CSS must flow verbatim into CreateThemeOptions.CssContent");
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Description("Returns the theme-css-source-conflict failure without resolving a command when css-content and primary are both supplied.")]
	[Category("Unit")]
	public void CreateTheme_ShouldReturnConflictFailure_WhenCssContentAndPrimaryBothSupplied() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		FakeCreateThemeCommand defaultCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		CreateThemeTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		// Act
		CreateThemeResult result = tool.CreateTheme(new CreateThemeArgs(
			EnvironmentName: "docker_fix2", CssClassName: "ocean-theme", CssContent: ".ocean-theme{}",
			Primary: "#004fd6"));

		// Assert
		result.Success.Should().BeFalse(because: "the CSS source must be unambiguous — inline CSS or brand colours, not both");
		result.Error.Should().Contain("theme-css-source-conflict",
			because: "the stable kebab-case code must travel in the message, matching the version-too-old convention");
		commandResolver.DidNotReceive().Resolve<CreateThemeCommand>(Arg.Any<CreateThemeOptions>());
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Description("Returns the theme-css-source-conflict failure when css-content is combined with a non-primary brand parameter (any brand parameter conflicts, not just primary).")]
	[Category("Unit")]
	public void CreateTheme_ShouldReturnConflictFailure_WhenCssContentAndNonPrimaryBrandParameterSupplied() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		FakeCreateThemeCommand defaultCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		CreateThemeTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		// Act
		CreateThemeResult result = tool.CreateTheme(new CreateThemeArgs(
			EnvironmentName: "docker_fix2", CssClassName: "ocean-theme", CssContent: ".ocean-theme{}",
			HeadingFont: "Poppins"));

		// Assert
		result.Success.Should().BeFalse(because: "a brand parameter alongside inline CSS is ambiguous even without primary");
		result.Error.Should().Contain("theme-css-source-conflict",
			because: "every brand parameter is mutually exclusive with css-content, not only primary");
		commandResolver.DidNotReceive().Resolve<CreateThemeCommand>(Arg.Any<CreateThemeOptions>());
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Description("Returns the theme-css-source-missing failure naming both accepted sources when neither css-content nor primary is supplied.")]
	[Category("Unit")]
	public void CreateTheme_ShouldReturnMissingSourceFailure_WhenNeitherCssContentNorPrimarySupplied() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		FakeCreateThemeCommand defaultCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		CreateThemeTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		// Act
		CreateThemeResult result = tool.CreateTheme(new CreateThemeArgs(
			EnvironmentName: "docker_fix2", Caption: "Ocean"));

		// Assert
		result.Success.Should().BeFalse(because: "a create request needs a CSS source");
		result.Error.Should().Contain("theme-css-source-missing",
			because: "the stable kebab-case code must travel in the message, matching the version-too-old convention");
		result.Error.Should().Contain("css-content", because: "the failure must name the inline source the caller can provide");
		result.Error.Should().Contain("primary", because: "the failure must name the brand-mode source the caller can provide");
		commandResolver.DidNotReceive().Resolve<CreateThemeCommand>(Arg.Any<CreateThemeOptions>());
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Description("Returns the theme-css-source-missing failure with the primary hint when a brand parameter is supplied without primary (secondary alone does not enable the brand mode).")]
	[Category("Unit")]
	public void CreateTheme_ShouldReturnMissingSourceFailureWithPrimaryHint_WhenSecondarySuppliedWithoutPrimary() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		FakeCreateThemeCommand defaultCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		CreateThemeTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		// Act
		CreateThemeResult result = tool.CreateTheme(new CreateThemeArgs(
			EnvironmentName: "docker_fix2", Caption: "Ocean", Secondary: "#0d2e4e"));

		// Assert
		result.Success.Should().BeFalse(because: "a brand parameter without primary enables no mode");
		result.Error.Should().Contain("theme-css-source-missing",
			because: "a stray brand parameter is still a missing-source failure, not a conflict");
		result.Error.Should().Contain("primary is required for the brand mode",
			because: "the failure must hint that primary is what enables the brand mode the caller evidently wants");
		commandResolver.DidNotReceive().Resolve<CreateThemeCommand>(Arg.Any<CreateThemeOptions>());
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Description("Keeps BuildThemeOptions.EnvironmentName null and skips settings resolution when environment-name and version are both supplied — the environment reaches the build only as resolvedSettings, so BuildThemeCommand's version/environment-name mutual-exclusion guard can never trip.")]
	[Category("Unit")]
	public void CreateTheme_ShouldKeepBuildEnvironmentNameNull_WhenEnvironmentNameAndVersionBothSupplied() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		FakeCreateThemeCommand defaultCommand = new();
		FakeCreateThemeCommand resolvedCommand = new(createdId: "generated-id");
		FakeBuildThemeCommand buildCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<CreateThemeCommand>(Arg.Any<CreateThemeOptions>()).Returns(resolvedCommand);
		commandResolver.Resolve<ICreatioVersionChecker>(Arg.Any<EnvironmentOptions>())
			.Returns(Substitute.For<ICreatioVersionChecker>());
		commandResolver.Resolve<BuildThemeCommand>(Arg.Any<EnvironmentOptions>()).Returns(buildCommand);
		CreateThemeTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		// Act
		CreateThemeResult result = tool.CreateTheme(new CreateThemeArgs(
			EnvironmentName: "docker_fix2", Caption: "Ocean", Primary: "#004fd6", Version: "10.0"));

		// Assert
		result.Success.Should().BeTrue(because: "an explicit version plus an environment-name is a valid brand-mode request");
		buildCommand.CapturedOptions.EnvironmentName.Should().BeNull(
			because: "copying the environment name alongside an explicit version would throw the command's 'mutually exclusive' ArgumentException on every such call");
		buildCommand.CapturedOptions.Version.Should().Be("10.0",
			because: "the explicit version must reach the build and pick the template");
		buildCommand.CapturedResolvedSettings.Should().BeNull(
			because: "an explicit version short-circuits template-version resolution, so no settings are resolved");
		commandResolver.DidNotReceive().Resolve<EnvironmentSettings>(Arg.Any<EnvironmentOptions>());
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Description("Returns the theme-build-failed failure embedding the build error text verbatim — the failure is local (before any HTTP call), so nothing is created and nothing is redacted.")]
	[Category("Unit")]
	public void CreateTheme_ShouldReturnBuildFailureAndNeverCreate_WhenBrandBuildFails() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		const string buildError = "COLOR_INVALID: \"https://brand.example/logo.png\" is not a valid colour.";
		FakeCreateThemeCommand defaultCommand = new();
		FakeCreateThemeCommand resolvedCommand = new(createdId: "generated-id");
		FakeBuildThemeCommand buildCommand = new(success: false, error: buildError);
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<CreateThemeCommand>(Arg.Any<CreateThemeOptions>()).Returns(resolvedCommand);
		commandResolver.Resolve<ICreatioVersionChecker>(Arg.Any<EnvironmentOptions>())
			.Returns(Substitute.For<ICreatioVersionChecker>());
		commandResolver.Resolve<BuildThemeCommand>(Arg.Any<EnvironmentOptions>()).Returns(buildCommand);
		CreateThemeTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		// Act
		CreateThemeResult result = tool.CreateTheme(new CreateThemeArgs(
			EnvironmentName: "docker_fix2", Caption: "Ocean", Primary: "#004fd6"));

		// Assert
		result.Success.Should().BeFalse(because: "a failed build must refuse the create");
		result.Error.Should().Be($"theme-build-failed: {buildError}",
			because: "the build failed locally before any HTTP call, so the code-prefixed message embeds the build error text verbatim — unredacted, it carries only the caller's own inputs");
		resolvedCommand.CapturedOptions.Should().BeNull(
			because: "the theme must never be created when the brand build fails — no HTTP happened");
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Description("Refuses the brand-mode create with the version-code failure BEFORE any build work — the fail-closed [RequiresCreatioVersion] gate runs ahead of the server-side CSS build, so neither the build command nor the environment settings are ever resolved.")]
	[Category("Unit")]
	public void CreateTheme_ShouldReturnVersionFailureWithoutBuilding_WhenBrandModeVersionRequirementIsUnmet() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		FakeCreateThemeCommand defaultCommand = new();
		FakeCreateThemeCommand resolvedCommand = new(createdId: "generated-id");
		FakeBuildThemeCommand buildCommand = new();
		ICreatioVersionChecker versionChecker = Substitute.For<ICreatioVersionChecker>();
		versionChecker
			.When(c => c.EnsureRequirements(Arg.Any<object>()))
			.Do(_ => throw new CreatioVersionRequirementException(
				"This command requires Creatio 10.0.0 or later. The target environment runs 8.1.5. Update Creatio and retry.",
				CreatioVersionRequirementException.VersionTooOldCode));
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<CreateThemeCommand>(Arg.Any<CreateThemeOptions>()).Returns(resolvedCommand);
		commandResolver.Resolve<BuildThemeCommand>(Arg.Any<EnvironmentOptions>()).Returns(buildCommand);
		commandResolver.Resolve<ICreatioVersionChecker>(Arg.Any<EnvironmentOptions>()).Returns(versionChecker);
		CreateThemeTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		// Act
		CreateThemeResult result = tool.CreateTheme(new CreateThemeArgs(
			EnvironmentName: "docker_fix2", CssClassName: "ocean-theme", Caption: "Ocean", Primary: "#004fd6"));

		// Assert
		result.Success.Should().BeFalse(
			because: "an unmet Creatio version requirement must refuse the brand-mode create exactly as it refuses the inline mode");
		result.Error.Should().Contain("requires Creatio 10.0.0 or later",
			because: "the version-requirement message must be surfaced to the MCP caller");
		result.Error.Should().Contain($"[{CreatioVersionRequirementException.VersionTooOldCode}]",
			because: "the typed result carries no exit code, so the stable machine-readable ErrorCode must travel in the error message");
		buildCommand.CapturedOptions.Should().BeNull(
			because: "the version gate runs before the brand build, so no CSS may be built for an environment that fails the floor");
		resolvedCommand.CapturedOptions.Should().BeNull(
			because: "the theme must never be created when the environment does not satisfy the version floor");
		commandResolver.DidNotReceive().Resolve<BuildThemeCommand>(Arg.Any<EnvironmentOptions>());
		commandResolver.DidNotReceive().Resolve<EnvironmentSettings>(Arg.Any<EnvironmentOptions>());
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Description("Propagates the non-fatal build advisories into the result's warnings — the CSS never crosses the boundary, so the warnings are the caller's only signal.")]
	[Category("Unit")]
	public void CreateTheme_ShouldPropagateBuildWarnings_WhenBrandBuildReportsAdvisories() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		FakeCreateThemeCommand defaultCommand = new();
		FakeCreateThemeCommand resolvedCommand = new(createdId: "generated-id");
		FakeBuildThemeCommand buildCommand = new(warnings: new[] {
			"build-theme: font weights were ignored — they apply only to a custom heading or body font."
		});
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<CreateThemeCommand>(Arg.Any<CreateThemeOptions>()).Returns(resolvedCommand);
		commandResolver.Resolve<ICreatioVersionChecker>(Arg.Any<EnvironmentOptions>())
			.Returns(Substitute.For<ICreatioVersionChecker>());
		commandResolver.Resolve<BuildThemeCommand>(Arg.Any<EnvironmentOptions>()).Returns(buildCommand);
		CreateThemeTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		// Act
		CreateThemeResult result = tool.CreateTheme(new CreateThemeArgs(
			EnvironmentName: "docker_fix2", Caption: "Ocean", Primary: "#004fd6", FontWeights: new[] { 400, 700 }));

		// Assert
		result.Success.Should().BeTrue(because: "build advisories are non-fatal");
		result.Warnings.Should().ContainSingle(w => w.Contains("font weights"),
			because: "build advisories must reach the MCP caller or they are silently lost");
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Description("Leaves the inline css-content path exactly as before the brand mode existed: no build is resolved, no settings are resolved, the CSS flows through unchanged, and the result carries no warnings (regression canary).")]
	[Category("Unit")]
	public void CreateTheme_ShouldNotInvokeBuildAndCarryNoWarnings_WhenInlineModeSupplied() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		FakeCreateThemeCommand defaultCommand = new();
		FakeCreateThemeCommand resolvedCommand = new(createdId: "generated-id");
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<CreateThemeCommand>(Arg.Any<CreateThemeOptions>()).Returns(resolvedCommand);
		commandResolver.Resolve<ICreatioVersionChecker>(Arg.Any<EnvironmentOptions>())
			.Returns(Substitute.For<ICreatioVersionChecker>());
		CreateThemeTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		// Act
		CreateThemeResult result = tool.CreateTheme(new CreateThemeArgs(
			EnvironmentName: "docker_fix2", CssClassName: "ocean-theme", CssContent: ".ocean-theme{}", Caption: "Ocean"));

		// Assert
		result.Success.Should().BeTrue(because: "the inline mode must keep working unchanged");
		result.Warnings.Should().BeNull(because: "the inline mode performs no build, so the result must omit warnings exactly as before");
		resolvedCommand.CapturedOptions.CssContent.Should().Be(".ocean-theme{}",
			because: "inline CSS flows through to the command unchanged");
		commandResolver.DidNotReceive().Resolve<BuildThemeCommand>(Arg.Any<EnvironmentOptions>());
		commandResolver.DidNotReceive().Resolve<EnvironmentSettings>(Arg.Any<EnvironmentOptions>());
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Description("Returns an actionable rename hint instead of silently ignoring a camelCase alias of a new brand-mode kebab-case argument.")]
	[Category("Unit")]
	public void CreateTheme_ShouldReturnRenameHint_WhenCamelCaseHeadingFontAliasIsPassed() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		FakeCreateThemeCommand defaultCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		CreateThemeTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);
		CreateThemeArgs args = new(EnvironmentName: "docker_fix2", Primary: "#004fd6") {
			ExtensionData = new Dictionary<string, JsonElement> {
				["headingFont"] = JsonSerializer.SerializeToElement("Poppins")
			}
		};

		// Act
		CreateThemeResult result = tool.CreateTheme(args);

		// Assert
		result.Success.Should().BeFalse(because: "a camelCase alias of a brand argument must be rejected, not silently dropped");
		result.Error.Should().Contain("'headingFont' -> 'heading-font'",
			because: "the failure must tell the caller the exact rename that fixes the call");
		commandResolver.DidNotReceive().Resolve<CreateThemeCommand>(Arg.Any<CreateThemeOptions>());
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Description("Binds the brand-mode kebab-case fields — including font-weights as a typed JSON int array — from the real MCP serializer options, the exact JSON->record binding the MCP host performs, which direct method calls bypass.")]
	[Category("Unit")]
	public void CreateThemeArgs_ShouldBindBrandKebabCaseFields_WhenBrandJsonSupplied() {
		// Arrange
		JsonSerializerOptions options = Clio.BindingsModule.CreateMcpSerializerOptions();

		// Act
		CreateThemeArgs args = JsonSerializer.Deserialize<CreateThemeArgs>(
			"""{"environment-name":"docker_fix2","caption":"Ocean","primary":"#004fd6","secondary":"#0d2e4e","accent":"#f94e11","success":"#0b8500","error":"#d2310d","heading-font":"Poppins","body-font":"Inter","font-weights":[400,600],"version":"10.0"}""",
			options)!;

		// Assert
		args.Primary.Should().Be("#004fd6", because: "the advertised primary field must bind");
		args.Secondary.Should().Be("#0d2e4e", because: "the advertised secondary field must bind");
		args.Accent.Should().Be("#f94e11", because: "the advertised accent field must bind");
		args.Success.Should().Be("#0b8500", because: "the advertised success field must bind");
		args.Error.Should().Be("#d2310d", because: "the advertised error field must bind");
		args.HeadingFont.Should().Be("Poppins", because: "the advertised kebab-case heading-font field must bind");
		args.BodyFont.Should().Be("Inter", because: "the advertised kebab-case body-font field must bind");
		args.FontWeights.Should().Equal(new[] { 400, 600 },
			because: "font-weights is a typed JSON int array on the MCP surface, not the CLI's comma-separated string");
		args.Version.Should().Be("10.0", because: "the advertised version field must bind");
		(args.ExtensionData is null || args.ExtensionData.Count == 0).Should().BeTrue(
			because: "every brand kebab field binds to a declared parameter, so nothing overflows");
	}

	[Test]
	[Description("AC3 determinism (real engine, no build substitutes): the brand-mode create path yields CSS byte-identical to a direct TryBuildTheme call on the same real BuildThemeCommand — real ThemeCssBuilder over the real bundled 10.0 template — and the pinned Poppins heading font emits its Google Fonts @import.")]
	[Category("Unit")]
	public void CreateTheme_ShouldProduceCssIdenticalToDirectBuild_WhenBrandModeRunsRealEngine() {
		// Arrange — the real bundled template, linked into the test output the same way ThemeCssBuilderTests uses it.
		ConsoleLogger.Instance.ClearMessages();
		string template = File.ReadAllText(
			Path.Combine(TestContext.CurrentContext.TestDirectory, "Theming/Fixtures/theme.css.tpl"));
		IThemeTemplateProvider templateProvider = Substitute.For<IThemeTemplateProvider>();
		templateProvider.GetCssTemplate("10.0").Returns(template);
		templateProvider.GetJsonTemplate(Arg.Any<string>())
			.Returns("{\"id\":\"<%themeId%>\",\"caption\":\"<%themeCaption%>\",\"cssClassName\":\"<%themeCssClass%>\"}");
		BuildThemeCommand realBuildCommand = new(new ThemeCssBuilder(), templateProvider,
			Substitute.For<IPlatformVersionResolverFactory>(), Substitute.For<ISettingsRepository>(),
			Substitute.For<IWorkspacePathBuilder>(), Substitute.For<IFileSystem>(), Substitute.For<ILogger>());
		FakeCreateThemeCommand defaultCommand = new();
		FakeCreateThemeCommand resolvedCommand = new(createdId: "ocean");
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<CreateThemeCommand>(Arg.Any<CreateThemeOptions>()).Returns(resolvedCommand);
		commandResolver.Resolve<ICreatioVersionChecker>(Arg.Any<EnvironmentOptions>())
			.Returns(Substitute.For<ICreatioVersionChecker>());
		commandResolver.Resolve<BuildThemeCommand>(Arg.Any<EnvironmentOptions>()).Returns(realBuildCommand);
		CreateThemeTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		// Act
		CreateThemeResult result = tool.CreateTheme(new CreateThemeArgs(
			EnvironmentName: "docker_fix2", CssClassName: "ocean-theme", Caption: "Ocean", Id: "ocean",
			Primary: "#004fd6", HeadingFont: "Poppins", FontWeights: new[] { 400, 600 }, Version: "10.0"));
		bool directOk = realBuildCommand.TryBuildTheme(
			new BuildThemeOptions {
				Primary = "#004fd6", CssClassName = "ocean-theme", Caption = "Ocean", Id = "ocean",
				HeadingFont = "Poppins", FontWeights = new[] { 400, 600 }, Version = "10.0"
			},
			null, out string expectedCss, out _, out _, out _);

		// Assert
		directOk.Should().BeTrue(because: "the pinned inputs are valid for the real engine");
		result.Success.Should().BeTrue(because: "the brand path over the real engine must succeed on the same inputs");
		resolvedCommand.CapturedOptions.CssContent.Should().Be(expectedCss,
			because: "the tool's brand path must be byte-identical to a direct TryBuildTheme call — same inputs, same engine, same CSS");
		resolvedCommand.CapturedOptions.CssContent.Should().Contain(
			"@import url('https://fonts.googleapis.com/css2?family=Poppins:wght@400;600&display=swap');",
			because: "the pinned Poppins heading font must emit its Google Fonts import");
		resolvedCommand.CapturedOptions.CssContent.Should().Contain("--crt-palette-primary-500: #004fd6;",
			because: "the pinned primary hex must land on the primary-500 stop");
		ConsoleLogger.Instance.ClearMessages();
	}

	private sealed class FakeCreateThemeCommand : CreateThemeCommand {
		private readonly bool _success;
		private readonly string _createdId;
		private readonly string _error;

		public CreateThemeOptions CapturedOptions { get; private set; }

		public FakeCreateThemeCommand(string createdId = "auto-id", bool success = true, string error = null)
			: base(
				Substitute.For<IApplicationClient>(),
				new EnvironmentSettings(),
				Substitute.For<IServiceUrlBuilder>(),
				Substitute.For<IFileSystem>()) {
			_createdId = createdId;
			_success = success;
			_error = error;
		}

		public override bool TryCreateTheme(CreateThemeOptions options, out string createdId, out string errorMessage) {
			CapturedOptions = options;
			createdId = _success ? _createdId : null;
			errorMessage = _error;
			return _success;
		}
	}

	private sealed class FakeBuildThemeCommand : BuildThemeCommand {
		private readonly bool _success;
		private readonly string _css;
		private readonly IReadOnlyList<string> _warnings;
		private readonly string _error;

		public BuildThemeOptions CapturedOptions { get; private set; }

		public EnvironmentSettings CapturedResolvedSettings { get; private set; }

		public FakeBuildThemeCommand(string css = "built-css", bool success = true,
			IReadOnlyList<string> warnings = null, string error = null)
			: base(
				Substitute.For<IThemeCssBuilder>(),
				Substitute.For<IThemeTemplateProvider>(),
				Substitute.For<IPlatformVersionResolverFactory>(),
				Substitute.For<ISettingsRepository>(),
				Substitute.For<IWorkspacePathBuilder>(),
				Substitute.For<IFileSystem>(),
				Substitute.For<ILogger>()) {
			_css = css;
			_success = success;
			_warnings = warnings ?? [];
			_error = error;
		}

		public override bool TryBuildTheme(BuildThemeOptions options, EnvironmentSettings resolvedSettings,
			out string css, out string descriptor, out IReadOnlyList<string> warnings, out string error) {
			CapturedOptions = options;
			CapturedResolvedSettings = resolvedSettings;
			css = _success ? _css : null;
			descriptor = _success ? "{}" : null;
			warnings = _success ? _warnings : [];
			error = _error;
			return _success;
		}
	}
}
