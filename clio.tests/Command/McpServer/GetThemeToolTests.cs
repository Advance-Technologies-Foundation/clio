using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text.Json;
using Clio.Command;
using Clio.Command.McpServer.Tools;
using Clio.Command.Theming;
using Clio.Common;
using FluentAssertions;
using ModelContextProtocol.Server;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
[Property("Module", "McpServer")]
public class GetThemeToolTests {

	[Test]
	[Category("Unit")]
	[Description("Declares the safety flags on the get-theme tool method: write-capable only through the confined output-file (ReadOnly=false), non-destructive, idempotent, closed-world.")]
	public void GetThemeTool_ShouldDeclareReadSafetyFlags_WhenInspectingMcpServerToolAttribute() {
		// Arrange & Act
		McpServerToolAttribute attribute = (McpServerToolAttribute)typeof(GetThemeTool)
			.GetMethod(nameof(GetThemeTool.GetTheme))!
			.GetCustomAttributes(typeof(McpServerToolAttribute), false)
			.Single();

		// Assert
		attribute.Name.Should().Be(GetThemeTool.ToolName,
			because: "the tool must be published under its canonical kebab-case name");
		attribute.ReadOnly.Should().BeFalse(
			because: "with output-file set the tool writes the theme CSS to disk, so it must not advertise readOnlyHint=true");
		attribute.Destructive.Should().BeFalse(
			because: "the only write is confined, atomic, and refuses to overwrite an existing target");
		attribute.Idempotent.Should().BeTrue(
			because: "repeated reads return the same content for unchanged state");
		attribute.OpenWorld.Should().BeFalse(
			because: "the tool only queries the addressed Creatio environment");
	}

	[Test]
	[Category("Unit")]
	[Description("Marks the single args wrapper as required at the MCP schema level, so a call that omits args fails with a structured error instead of an opaque binding failure.")]
	public void GetThemeTool_ShouldRequireArgsWrapper_WhenInspectingMethodSignature() {
		// Arrange & Act
		object[] requiredAttributes = typeof(GetThemeTool)
			.GetMethod(nameof(GetThemeTool.GetTheme))!
			.GetParameters()[0]
			.GetCustomAttributes(typeof(RequiredAttribute), false);

		// Assert
		requiredAttributes.Should().NotBeEmpty(
			because: "the args wrapper must be schema-required so an omitted args object fails with a structured error, not an opaque MCP binding failure");
	}

	[Test]
	[Category("Unit")]
	[Description("Resolves the get-theme MCP tool for the requested environment, forwards id and output-file into the options, and returns the resolved command's envelope.")]
	public void GetTheme_ShouldResolveCommandAndReturnEnvelope() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		GetThemeResponse envelope = new() {
			Success = true, Id = "ocean-theme", Caption = "Ocean", CssClassName = "ocean-theme",
			CssFilePath = "a/theme.css", CssContent = ".ocean-theme {}", CssContentLength = 15
		};
		FakeGetThemeCommand defaultCommand = new();
		FakeGetThemeCommand resolvedCommand = new(envelope);
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<GetThemeCommand>(Arg.Any<GetThemeOptions>()).Returns(resolvedCommand);
		commandResolver.Resolve<ICreatioVersionChecker>(Arg.Any<EnvironmentOptions>())
			.Returns(Substitute.For<ICreatioVersionChecker>());
		GetThemeTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		// Act
		GetThemeResponse result = tool.GetTheme(new GetThemeArgs(
			EnvironmentName: "docker_fix2", Id: "ocean-theme", OutputFile: "out/theme.css"));

		// Assert
		result.Success.Should().BeTrue(because: "a successful read must report success");
		result.CssContent.Should().Be(".ocean-theme {}",
			because: "the resolved command's envelope must be surfaced unchanged");
		commandResolver.Received(1).Resolve<GetThemeCommand>(Arg.Is<GetThemeOptions>(options =>
			options.Environment == "docker_fix2"
			&& options.Id == "ocean-theme"
			&& options.OutputFile == "out/theme.css"));
		resolvedCommand.CapturedOptions.Should().NotBeNull(
			because: "the resolved command instance should have been queried for the theme");
		defaultCommand.CapturedOptions.Should().BeNull(
			because: "the environment-aware tool path should use the resolved command instance, not the injected one");
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Category("Unit")]
	[Description("Returns a structured failure naming environment-name when the required environment name is omitted.")]
	public void GetTheme_ShouldReturnFailure_WhenEnvironmentNameIsMissing() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		FakeGetThemeCommand defaultCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		GetThemeTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		// Act
		GetThemeResponse result = tool.GetTheme(new GetThemeArgs(Id: "ocean-theme"));

		// Assert
		result.Success.Should().BeFalse(because: "a read request without an environment name is invalid");
		result.Error.Should().Contain("environment-name",
			because: "the failure must name the exact kebab-case field the caller has to add");
		commandResolver.DidNotReceive().Resolve<GetThemeCommand>(Arg.Any<GetThemeOptions>());
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Category("Unit")]
	[Description("Returns a structured failure naming id when the required theme id is omitted.")]
	public void GetTheme_ShouldReturnFailure_WhenIdIsMissing() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		FakeGetThemeCommand defaultCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		GetThemeTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		// Act
		GetThemeResponse result = tool.GetTheme(new GetThemeArgs(EnvironmentName: "docker_fix2"));

		// Assert
		result.Success.Should().BeFalse(because: "a read request without a theme id is invalid");
		result.Error.Should().Contain("id",
			because: "the failure must name the exact field the caller has to add");
		commandResolver.DidNotReceive().Resolve<GetThemeCommand>(Arg.Any<GetThemeOptions>());
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Category("Unit")]
	[Description("Returns an actionable rename hint instead of silently ignoring camelCase aliases of the kebab-case arguments.")]
	public void GetTheme_ShouldReturnRenameHint_WhenCamelCaseAliasesArePassed() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		FakeGetThemeCommand defaultCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		GetThemeTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);
		GetThemeArgs args = new(Id: "ocean-theme") {
			ExtensionData = new Dictionary<string, JsonElement> {
				["environmentName"] = JsonSerializer.SerializeToElement("docker_fix2"),
				["outputFile"] = JsonSerializer.SerializeToElement("out/theme.css")
			}
		};

		// Act
		GetThemeResponse result = tool.GetTheme(args);

		// Assert
		result.Success.Should().BeFalse(because: "a camelCase alias must be rejected, not silently dropped");
		result.Error.Should().Contain("'environmentName' -> 'environment-name'",
			because: "the failure must tell the caller the exact rename that fixes the call");
		result.Error.Should().Contain("'outputFile' -> 'output-file'",
			because: "every recognized alias in the call must be reported at once");
		commandResolver.DidNotReceive().Resolve<GetThemeCommand>(Arg.Any<GetThemeOptions>());
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Category("Unit")]
	[Description("Binds the get-theme argument record from kebab-case JSON using the real MCP serializer options, and routes camelCase spellings into the overflow bag — the exact JSON->record binding the MCP host performs, which direct method calls bypass.")]
	public void GetThemeArgs_ShouldBindKebabCaseAndRouteCamelCaseToExtensionData() {
		// Arrange
		JsonSerializerOptions options = Clio.BindingsModule.CreateMcpSerializerOptions();

		// Act
		GetThemeArgs kebab = JsonSerializer.Deserialize<GetThemeArgs>(
			"""{"environment-name":"docker_fix2","id":"ocean-theme","output-file":"out/theme.css"}""", options)!;
		GetThemeArgs camel = JsonSerializer.Deserialize<GetThemeArgs>(
			"""{"environmentName":"docker_fix2","outputFile":"out/theme.css"}""", options)!;

		// Assert
		kebab.EnvironmentName.Should().Be("docker_fix2",
			because: "the advertised kebab-case environment-name field must bind");
		kebab.Id.Should().Be("ocean-theme", because: "the advertised id field must bind");
		kebab.OutputFile.Should().Be("out/theme.css", because: "the advertised output-file field must bind");
		(kebab.ExtensionData is null || kebab.ExtensionData.Count == 0).Should().BeTrue(
			because: "every kebab field binds to a declared parameter, so nothing overflows");
		camel.EnvironmentName.Should().BeNull(
			because: "environmentName is not a declared wire name, so it must not bind");
		camel.ExtensionData.Should().ContainKey("environmentName",
			because: "the unbound camelCase spelling must land in the overflow bag so the tool can return a rename hint");
		camel.ExtensionData.Should().ContainKey("outputFile",
			because: "the unbound camelCase output-file spelling must land in the overflow bag as well");
	}

	[Test]
	[Category("Unit")]
	[Description("Returns a structured failure carrying the version-requirement message and never reads the theme when the target environment does not satisfy the Creatio version floor.")]
	public void GetTheme_ShouldReturnFailure_WhenCreatioVersionRequirementIsUnmet() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		FakeGetThemeCommand defaultCommand = new();
		FakeGetThemeCommand resolvedCommand = new();
		ICreatioVersionChecker versionChecker = Substitute.For<ICreatioVersionChecker>();
		versionChecker
			.When(c => c.EnsureRequirements(Arg.Any<object>()))
			.Do(_ => throw new CreatioVersionRequirementException(
				"This command requires Creatio 10.0.0 or later. The target environment runs 8.1.5. Update Creatio and retry.",
				CreatioVersionRequirementException.VersionTooOldCode));
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<GetThemeCommand>(Arg.Any<GetThemeOptions>()).Returns(resolvedCommand);
		commandResolver.Resolve<ICreatioVersionChecker>(Arg.Any<EnvironmentOptions>()).Returns(versionChecker);
		GetThemeTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		// Act
		GetThemeResponse result = tool.GetTheme(new GetThemeArgs(
			EnvironmentName: "docker_fix2", Id: "ocean-theme"));

		// Assert
		result.Success.Should().BeFalse(
			because: "an unmet Creatio version requirement must refuse the read on the MCP surface exactly as the CLI gate does");
		result.Error.Should().Contain("requires Creatio 10.0.0 or later",
			because: "the version-requirement message must be surfaced to the MCP caller");
		result.Error.Should().Contain($"[{CreatioVersionRequirementException.VersionTooOldCode}]",
			because: "the typed result carries no exit code, so the stable machine-readable ErrorCode must travel in the error message");
		resolvedCommand.CapturedOptions.Should().BeNull(
			because: "the theme must never be read when the environment does not satisfy the version floor");
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Category("Unit")]
	[Description("Redacts a sensitive error message from the command before it crosses into the MCP client transcript (the TryGetTheme errorMessage out-param bypasses ExecuteResolved's exception handling entirely).")]
	public void GetTheme_ShouldRedactSensitiveText_WhenCommandReportsFailure() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		FakeGetThemeCommand defaultCommand = new();
		FakeGetThemeCommand resolvedCommand = new(GetThemeResponse.Failure(
			"Unexpected response from server: https://internal-host.example/ThemeService?token=sekret123"));
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<GetThemeCommand>(Arg.Any<GetThemeOptions>()).Returns(resolvedCommand);
		commandResolver.Resolve<ICreatioVersionChecker>(Arg.Any<EnvironmentOptions>())
			.Returns(Substitute.For<ICreatioVersionChecker>());
		GetThemeTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		// Act
		GetThemeResponse result = tool.GetTheme(new GetThemeArgs(
			EnvironmentName: "docker_fix2", Id: "ocean-theme"));

		// Assert
		result.Success.Should().BeFalse(because: "the command's failure must surface as a tool failure");
		result.Error.Should().NotContain("internal-host.example",
			because: "the error can carry a target host, so it must be redacted before crossing the MCP boundary");
		result.Error.Should().NotContain("sekret123",
			because: "the error can carry a credential value, so it must be redacted before crossing the MCP boundary");
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Category("Unit")]
	[Description("Returns the round-trip payload fields verbatim — unlike list-themes, get-theme must not sanitize caption/cssClassName/cssContent, because a capped or stripped value would be written back differently by update-theme.")]
	public void GetTheme_ShouldReturnPayloadFieldsVerbatim_WhenContentCarriesUnusualCharacters() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		string cssWithTabsAndNewlines = ".ocean-theme {\n\t--crt-test: url(\"data:image/svg+xml;utf8,<svg/>\");\n}";
		string longCaption = new string('c', 250);
		GetThemeResponse envelope = new() {
			Success = true, Id = "ocean-theme", Caption = longCaption, CssClassName = "ocean-theme",
			CssFilePath = "a/theme.css", CssContent = cssWithTabsAndNewlines,
			CssContentLength = cssWithTabsAndNewlines.Length
		};
		FakeGetThemeCommand defaultCommand = new();
		FakeGetThemeCommand resolvedCommand = new(envelope);
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<GetThemeCommand>(Arg.Any<GetThemeOptions>()).Returns(resolvedCommand);
		commandResolver.Resolve<ICreatioVersionChecker>(Arg.Any<EnvironmentOptions>())
			.Returns(Substitute.For<ICreatioVersionChecker>());
		GetThemeTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		// Act
		GetThemeResponse result = tool.GetTheme(new GetThemeArgs(
			EnvironmentName: "docker_fix2", Id: "ocean-theme"));

		// Assert
		result.CssContent.Should().Be(cssWithTabsAndNewlines,
			because: "the CSS must survive the tool boundary byte-for-byte so the read → edit → update round-trip does not corrupt it");
		result.Caption.Should().Be(longCaption,
			because: "the caption feeds update-theme verbatim and must not be length-capped");
		ConsoleLogger.Instance.ClearMessages();
	}

	private sealed class FakeGetThemeCommand : GetThemeCommand {
		private readonly GetThemeResponse _response;

		public GetThemeOptions CapturedOptions { get; private set; }

		public FakeGetThemeCommand(GetThemeResponse response = null)
			: base(
				Substitute.For<IThemeCatalog>(),
				Substitute.For<IApplicationClient>(),
				Substitute.For<IServiceUrlBuilder>(),
				new System.IO.Abstractions.TestingHelpers.MockFileSystem(),
				Substitute.For<ILogger>()) {
			_response = response ?? new GetThemeResponse { Success = true };
		}

		public override bool TryGetTheme(GetThemeOptions options, out GetThemeResponse response) {
			CapturedOptions = options;
			response = _response;
			return _response.Success;
		}
	}
}
