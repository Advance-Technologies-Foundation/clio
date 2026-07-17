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
public class SetUserThemeToolTests {

	[Test]
	[Category("Unit")]
	[Description("Declares the safety flags on the set-user-theme tool method: a write that IS destructive (it overwrites/clears an existing profile value, so the MCP host gates it), is idempotent, and closed-world. The destructive flag is what removes it from the durable gate's silent-write baseline (see DurableInvocationGateCompletenessTests).")]
	public void SetUserThemeTool_ShouldDeclareApplySafetyFlags_WhenInspectingMcpServerToolAttribute() {
		// Arrange & Act
		McpServerToolAttribute attribute = (McpServerToolAttribute)typeof(SetUserThemeTool)
			.GetMethod(nameof(SetUserThemeTool.SetUserTheme))!
			.GetCustomAttributes(typeof(McpServerToolAttribute), false)
			.Single();

		// Assert
		attribute.Name.Should().Be(SetUserThemeTool.ToolName, because: "the tool must be published under its canonical kebab-case name");
		attribute.ReadOnly.Should().BeFalse(because: "applying a theme writes to the user's profile");
		attribute.Destructive.Should().BeTrue(because: "applying a theme overwrites (or clears, on reset) the profile's existing Theme value — an in-place modification, not an additive write — so the MCP host must confirm it, consistent with update-theme/delete-theme");
		attribute.Idempotent.Should().BeTrue(because: "applying the same theme twice yields the same profile state");
		attribute.OpenWorld.Should().BeFalse(because: "the tool only touches the addressed Creatio environment");
	}

	[Test]
	[Category("Unit")]
	[Description("Marks the single args wrapper as required at the MCP schema level, so a call that omits args fails with a structured error instead of an opaque binding failure.")]
	public void SetUserThemeTool_ShouldRequireArgsWrapper_WhenInspectingMethodSignature() {
		// Arrange & Act
		object[] requiredAttributes = typeof(SetUserThemeTool)
			.GetMethod(nameof(SetUserThemeTool.SetUserTheme))!
			.GetParameters()[0]
			.GetCustomAttributes(typeof(RequiredAttribute), false);

		// Assert
		requiredAttributes.Should().NotBeEmpty(
			because: "the args wrapper must be schema-required so an omitted args object fails with a structured error, not an opaque MCP binding failure");
	}

	[Test]
	[Category("Unit")]
	[Description("Resolves the environment-name set-user-theme MCP tool, forwards the theme selector, and returns the applied theme as a structured success result.")]
	public void SetUserTheme_ShouldResolveCommandAndReturnAppliedTheme() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		FakeSetUserThemeCommand defaultCommand = new();
		FakeSetUserThemeCommand resolvedCommand = new(new AppliedUserTheme("Ocean", "ocean-theme", "ocean-id"));
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<SetUserThemeCommand>(Arg.Any<SetUserThemeOptions>()).Returns(resolvedCommand);
		commandResolver.Resolve<ICreatioVersionChecker>(Arg.Any<EnvironmentOptions>())
			.Returns(Substitute.For<ICreatioVersionChecker>());
		SetUserThemeTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		// Act
		SetUserThemeResult result = tool.SetUserTheme(new SetUserThemeArgs(
			EnvironmentName: "docker_fix2", Theme: "Ocean"));

		// Assert
		result.Success.Should().BeTrue(because: "an applied theme must report success");
		result.Id.Should().Be("ocean-id", because: "the applied theme's id (the value written to the profile) must be surfaced");
		result.Caption.Should().Be("Ocean", because: "the human-readable caption must be surfaced to relay to the user");
		commandResolver.Received(1).Resolve<SetUserThemeCommand>(Arg.Is<SetUserThemeOptions>(options =>
			options.Environment == "docker_fix2" && options.Theme == "Ocean" && !options.Reset));
		resolvedCommand.CapturedOptions.Should().NotBeNull(because: "the resolved command instance should apply the theme");
		defaultCommand.CapturedOptions.Should().BeNull(because: "the environment-aware tool path must use the resolved command instance");
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Category("Unit")]
	[Description("Forwards reset=true to the command's Reset option so the user's theme is cleared.")]
	public void SetUserTheme_ShouldForwardReset_WhenResetIsTrue() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		FakeSetUserThemeCommand defaultCommand = new();
		FakeSetUserThemeCommand resolvedCommand = new(new AppliedUserTheme(string.Empty, string.Empty, string.Empty));
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<SetUserThemeCommand>(Arg.Any<SetUserThemeOptions>()).Returns(resolvedCommand);
		commandResolver.Resolve<ICreatioVersionChecker>(Arg.Any<EnvironmentOptions>())
			.Returns(Substitute.For<ICreatioVersionChecker>());
		SetUserThemeTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		// Act
		SetUserThemeResult result = tool.SetUserTheme(new SetUserThemeArgs(
			EnvironmentName: "docker_fix2", Reset: true));

		// Assert
		result.Success.Should().BeTrue(because: "clearing the theme is a valid operation");
		commandResolver.Received(1).Resolve<SetUserThemeCommand>(Arg.Is<SetUserThemeOptions>(options =>
			options.Reset && string.IsNullOrEmpty(options.Theme)));
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Category("Unit")]
	[Description("Returns a structured failure naming environment-name when the required environment name is omitted, without resolving a command.")]
	public void SetUserTheme_ShouldReturnFailure_WhenEnvironmentNameIsMissing() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		FakeSetUserThemeCommand defaultCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		SetUserThemeTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		// Act
		SetUserThemeResult result = tool.SetUserTheme(new SetUserThemeArgs(Theme: "Ocean"));

		// Assert
		result.Success.Should().BeFalse(because: "a request without an environment name is invalid");
		result.Error.Should().Contain("environment-name",
			because: "the failure must name the exact kebab-case field the caller has to add");
		commandResolver.DidNotReceive().Resolve<SetUserThemeCommand>(Arg.Any<SetUserThemeOptions>());
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Category("Unit")]
	[Description("Returns a structured failure without resolving a command when neither theme nor reset is supplied, failing fast before the environment/version round-trip.")]
	public void SetUserTheme_ShouldReturnFailure_WhenNeitherThemeNorResetSupplied() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		FakeSetUserThemeCommand defaultCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		SetUserThemeTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		// Act
		SetUserThemeResult result = tool.SetUserTheme(new SetUserThemeArgs(EnvironmentName: "docker_fix2"));

		// Assert
		result.Success.Should().BeFalse(because: "there is nothing to apply and no reset requested");
		result.Error.Should().Contain("reset=true",
			because: "the failure must point the caller at the two valid inputs (a theme or reset=true)");
		commandResolver.DidNotReceive().Resolve<SetUserThemeCommand>(Arg.Any<SetUserThemeOptions>());
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Category("Unit")]
	[Description("Returns a structured failure without resolving a command when both a theme and reset=true are supplied, since they are mutually exclusive.")]
	public void SetUserTheme_ShouldReturnFailure_WhenBothThemeAndResetSupplied() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		FakeSetUserThemeCommand defaultCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		SetUserThemeTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		// Act
		SetUserThemeResult result = tool.SetUserTheme(new SetUserThemeArgs(
			EnvironmentName: "docker_fix2", Theme: "Ocean", Reset: true));

		// Assert
		result.Success.Should().BeFalse(because: "a theme and reset=true are mutually exclusive");
		result.Error.Should().Contain("not both",
			because: "the failure must tell the caller the two inputs cannot be combined");
		commandResolver.DidNotReceive().Resolve<SetUserThemeCommand>(Arg.Any<SetUserThemeOptions>());
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Category("Unit")]
	[Description("Surfaces the command failure message (e.g. the ChangeTheme-disabled silent no-op) as a structured failure when the resolved command reports failure.")]
	public void SetUserTheme_ShouldReturnFailure_WhenCommandReportsFailure() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		FakeSetUserThemeCommand defaultCommand = new();
		FakeSetUserThemeCommand resolvedCommand = new(success: false, error: "The profile theme was not applied. Ensure the 'ChangeTheme' feature is enabled.");
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<SetUserThemeCommand>(Arg.Any<SetUserThemeOptions>()).Returns(resolvedCommand);
		commandResolver.Resolve<ICreatioVersionChecker>(Arg.Any<EnvironmentOptions>())
			.Returns(Substitute.For<ICreatioVersionChecker>());
		SetUserThemeTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		// Act
		SetUserThemeResult result = tool.SetUserTheme(new SetUserThemeArgs(
			EnvironmentName: "docker_fix2", Theme: "Ocean"));

		// Assert
		result.Success.Should().BeFalse(because: "a command failure must surface as a tool failure");
		result.Error.Should().Contain("ChangeTheme", because: "the server-actionable diagnostic must be forwarded to the caller");
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Category("Unit")]
	[Description("Returns an actionable rename hint instead of silently ignoring a camelCase alias of a kebab-case argument.")]
	public void SetUserTheme_ShouldReturnRenameHint_WhenCamelCaseAliasIsPassed() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		FakeSetUserThemeCommand defaultCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		SetUserThemeTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);
		SetUserThemeArgs args = new(Theme: "Ocean") {
			ExtensionData = new Dictionary<string, JsonElement> {
				["environmentName"] = JsonSerializer.SerializeToElement("docker_fix2")
			}
		};

		// Act
		SetUserThemeResult result = tool.SetUserTheme(args);

		// Assert
		result.Success.Should().BeFalse(because: "a camelCase alias must be rejected, not silently dropped");
		result.Error.Should().Contain("'environmentName' -> 'environment-name'",
			because: "the failure must tell the caller the exact rename that fixes the call");
		commandResolver.DidNotReceive().Resolve<SetUserThemeCommand>(Arg.Any<SetUserThemeOptions>());
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Category("Unit")]
	[Description("Returns a structured failure carrying the version-requirement message and never applies the theme when the target environment does not satisfy the Creatio version floor.")]
	public void SetUserTheme_ShouldReturnFailure_WhenCreatioVersionRequirementIsUnmet() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		FakeSetUserThemeCommand defaultCommand = new();
		FakeSetUserThemeCommand resolvedCommand = new();
		ICreatioVersionChecker versionChecker = Substitute.For<ICreatioVersionChecker>();
		versionChecker
			.When(c => c.EnsureRequirements(Arg.Any<object>()))
			.Do(_ => throw new CreatioVersionRequirementException(
				"This command requires Creatio 10.0.0 or later. The target environment runs 8.1.5. Update Creatio and retry.",
				CreatioVersionRequirementException.VersionTooOldCode));
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<SetUserThemeCommand>(Arg.Any<SetUserThemeOptions>()).Returns(resolvedCommand);
		commandResolver.Resolve<ICreatioVersionChecker>(Arg.Any<EnvironmentOptions>()).Returns(versionChecker);
		SetUserThemeTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		// Act
		SetUserThemeResult result = tool.SetUserTheme(new SetUserThemeArgs(
			EnvironmentName: "docker_fix2", Theme: "Ocean"));

		// Assert
		result.Success.Should().BeFalse(
			because: "an unmet Creatio version requirement must refuse the apply on the MCP surface exactly as the CLI gate does");
		result.Error.Should().Contain("requires Creatio 10.0.0 or later",
			because: "the version-requirement message must be surfaced to the MCP caller");
		result.Error.Should().Contain($"[{CreatioVersionRequirementException.VersionTooOldCode}]",
			because: "the typed result carries no exit code, so the stable machine-readable ErrorCode must travel in the error message");
		resolvedCommand.CapturedOptions.Should().BeNull(
			because: "the theme must never be applied when the environment does not satisfy the version floor");
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Description("Binds the set-user-theme argument record from kebab-case JSON using the real MCP serializer options, and routes camelCase spellings into the overflow bag — the exact JSON->record binding the MCP host performs, which direct method calls bypass.")]
	[Category("Unit")]
	public void SetUserThemeArgs_ShouldBindKebabCaseAndRouteCamelCaseToExtensionData() {
		// Arrange
		JsonSerializerOptions options = Clio.BindingsModule.CreateMcpSerializerOptions();

		// Act
		SetUserThemeArgs kebab = JsonSerializer.Deserialize<SetUserThemeArgs>(
			"""{"environment-name":"docker_fix2","theme":"Ocean","reset":true}""",
			options)!;
		SetUserThemeArgs camel = JsonSerializer.Deserialize<SetUserThemeArgs>(
			"""{"environmentName":"docker_fix2"}""", options)!;

		// Assert
		kebab.EnvironmentName.Should().Be("docker_fix2", because: "the advertised kebab-case environment-name field must bind");
		kebab.Theme.Should().Be("Ocean", because: "the advertised theme field must bind");
		kebab.Reset.Should().BeTrue(because: "the advertised reset field must bind");
		(kebab.ExtensionData is null || kebab.ExtensionData.Count == 0).Should().BeTrue(
			because: "every kebab field binds to a declared parameter, so nothing overflows");
		camel.EnvironmentName.Should().BeNull(
			because: "environmentName is not a declared wire name, so it must not bind");
		camel.ExtensionData.Should().ContainKey("environmentName",
			because: "the unbound camelCase spelling must land in the overflow bag so the tool can return a rename hint");
	}

	private sealed class FakeSetUserThemeCommand : SetUserThemeCommand {
		private readonly bool _success;
		private readonly AppliedUserTheme _applied;
		private readonly string _error;

		public SetUserThemeOptions CapturedOptions { get; private set; }

		public FakeSetUserThemeCommand(AppliedUserTheme applied = null, bool success = true, string error = null)
			: base(
				Substitute.For<IApplicationClient>(),
				new EnvironmentSettings(),
				Substitute.For<IUserThemeApplier>()) {
			_applied = applied ?? new AppliedUserTheme("Ocean", "ocean-theme", "ocean-id");
			_success = success;
			_error = error;
		}

		public override bool TrySetUserTheme(SetUserThemeOptions options, out AppliedUserTheme applied, out string errorMessage) {
			CapturedOptions = options;
			applied = _success ? _applied : null;
			errorMessage = _error;
			return _success;
		}
	}
}
