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
public class ClearThemesCacheToolTests {

	[Test]
	[Category("Unit")]
	[Description("Declares the safety flags on the clear-themes-cache tool method: a non-read-only cache refresh that is non-destructive, idempotent, and closed-world.")]
	public void ClearThemesCacheTool_ShouldDeclareClearSafetyFlags_WhenInspectingMcpServerToolAttribute() {
		// Arrange & Act
		McpServerToolAttribute attribute = (McpServerToolAttribute)typeof(ClearThemesCacheTool)
			.GetMethod(nameof(ClearThemesCacheTool.ClearThemesCache))!
			.GetCustomAttributes(typeof(McpServerToolAttribute), false)
			.Single();

		// Assert
		attribute.Name.Should().Be(ClearThemesCacheTool.ToolName, because: "the tool must be published under its canonical kebab-case name");
		attribute.ReadOnly.Should().BeFalse(because: "clearing the cache refreshes server-side theme state");
		attribute.Destructive.Should().BeFalse(because: "a cache refresh rebuilds derived state without destroying themes");
		attribute.Idempotent.Should().BeTrue(because: "repeated cache clears converge on the same refreshed state");
		attribute.OpenWorld.Should().BeFalse(because: "the tool only touches the addressed Creatio environment");
	}

	[Test]
	[Category("Unit")]
	[Description("Marks the single args wrapper as required at the MCP schema level, so a call that omits args fails with a structured error instead of an opaque binding failure.")]
	public void ClearThemesCacheTool_ShouldRequireArgsWrapper_WhenInspectingMethodSignature() {
		// Arrange & Act
		object[] requiredAttributes = typeof(ClearThemesCacheTool)
			.GetMethod(nameof(ClearThemesCacheTool.ClearThemesCache))!
			.GetParameters()[0]
			.GetCustomAttributes(typeof(RequiredAttribute), false);

		// Assert
		requiredAttributes.Should().NotBeEmpty(
			because: "the args wrapper must be schema-required so an omitted args object fails with a structured error, not an opaque MCP binding failure");
	}

	[Test]
	[Description("Resolves the clear-themes-cache MCP tool for the requested environment and forwards the environment key into command options.")]
	[Category("Unit")]
	public void ClearThemesCache_ShouldResolveCommandForRequestedEnvironment() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		FakeClearThemesCacheCommand defaultCommand = new();
		FakeClearThemesCacheCommand resolvedCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<ClearThemesCacheCommand>(Arg.Any<ClearThemesCacheOptions>()).Returns(resolvedCommand);
		commandResolver.Resolve<ICreatioVersionChecker>(Arg.Any<EnvironmentOptions>())
			.Returns(Substitute.For<ICreatioVersionChecker>());
		ClearThemesCacheTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		// Act
		CommandExecutionResult result = tool.ClearThemesCache(new ClearThemesCacheArgs(EnvironmentName: "docker_fix2"));

		// Assert
		result.ExitCode.Should().Be(0, because: "the tool should forward a valid clear-themes-cache command payload");
		commandResolver.Received(1).Resolve<ClearThemesCacheCommand>(Arg.Is<ClearThemesCacheOptions>(options =>
			options.Environment == "docker_fix2"));
		defaultCommand.CapturedOptions.Should().BeNull(
			because: "the environment-aware tool path should use the resolved command instance");
		resolvedCommand.CapturedOptions.Should().NotBeNull(
			because: "the resolved command should receive the forwarded clear-themes-cache options");
		resolvedCommand.CapturedOptions!.Environment.Should().Be("docker_fix2",
			because: "the requested environment key must be preserved");
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Description("Returns a structured error without resolving a command when the environment name is empty.")]
	[Category("Unit")]
	public void ClearThemesCache_ShouldReturnError_WhenEnvironmentNameIsEmpty() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		FakeClearThemesCacheCommand defaultCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		ClearThemesCacheTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		// Act
		CommandExecutionResult result = tool.ClearThemesCache(new ClearThemesCacheArgs(EnvironmentName: "   "));

		// Assert
		result.ExitCode.Should().Be(1, because: "an empty environment name is an expected, caller-actionable validation error");
		commandResolver.DidNotReceive().Resolve<ClearThemesCacheCommand>(Arg.Any<ClearThemesCacheOptions>());
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Description("Returns a structured failure naming environment-name when the required environment name is omitted.")]
	[Category("Unit")]
	public void ClearThemesCache_ShouldReturnFailure_WhenEnvironmentNameIsMissing() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		FakeClearThemesCacheCommand defaultCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		ClearThemesCacheTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		// Act
		CommandExecutionResult result = tool.ClearThemesCache(new ClearThemesCacheArgs());

		// Assert
		result.ExitCode.Should().Be(1, because: "a missing environment name is an expected, caller-actionable validation error");
		result.Output.Should().ContainSingle(message =>
			message.GetType() == typeof(ErrorMessage) &&
			Equals(message.Value, "environment-name is required and cannot be empty."),
			because: "the failure must name the exact kebab-case field the caller has to add");
		commandResolver.DidNotReceive().Resolve<ClearThemesCacheCommand>(Arg.Any<ClearThemesCacheOptions>());
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Description("Returns an actionable rename hint instead of silently ignoring a camelCase alias of a kebab-case argument.")]
	[Category("Unit")]
	public void ClearThemesCache_ShouldReturnRenameHint_WhenCamelCaseAliasIsPassed() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		FakeClearThemesCacheCommand defaultCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		ClearThemesCacheTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);
		ClearThemesCacheArgs args = new() {
			ExtensionData = new Dictionary<string, JsonElement> {
				["environmentName"] = JsonSerializer.SerializeToElement("docker_fix2")
			}
		};

		// Act
		CommandExecutionResult result = tool.ClearThemesCache(args);

		// Assert
		result.ExitCode.Should().Be(1, because: "a rejected alias is an expected, caller-actionable validation error");
		result.Output.Should().ContainSingle(message =>
			message.GetType() == typeof(ErrorMessage) &&
			message.Value.ToString().Contains("'environmentName' -> 'environment-name'"),
			because: "the failure must tell the caller the exact rename that fixes the call");
		commandResolver.DidNotReceive().Resolve<ClearThemesCacheCommand>(Arg.Any<ClearThemesCacheOptions>());
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Description("Binds the clear-themes-cache argument record from kebab-case JSON using the real MCP serializer options, and routes camelCase spellings into the overflow bag — the exact JSON->record binding the MCP host performs, which direct method calls bypass.")]
	[Category("Unit")]
	public void ClearThemesCacheArgs_ShouldBindKebabCaseAndRouteCamelCaseToExtensionData() {
		// Arrange
		JsonSerializerOptions options = Clio.BindingsModule.CreateMcpSerializerOptions();

		// Act
		ClearThemesCacheArgs kebab = JsonSerializer.Deserialize<ClearThemesCacheArgs>(
			"""{"environment-name":"docker_fix2"}""", options)!;
		ClearThemesCacheArgs camel = JsonSerializer.Deserialize<ClearThemesCacheArgs>(
			"""{"environmentName":"docker_fix2"}""", options)!;

		// Assert
		kebab.EnvironmentName.Should().Be("docker_fix2", because: "the advertised kebab-case environment-name field must bind");
		(kebab.ExtensionData is null || kebab.ExtensionData.Count == 0).Should().BeTrue(
			because: "every kebab field binds to a declared parameter, so nothing overflows");
		camel.EnvironmentName.Should().BeNull(
			because: "environmentName is not a declared wire name, so it must not bind");
		camel.ExtensionData.Should().ContainKey("environmentName",
			because: "the unbound camelCase spelling must land in the overflow bag so the tool can return a rename hint");
	}

	[Test]
	[Description("Returns the distinct version exit code with the version-requirement message and never refreshes the cache when the target environment does not satisfy the Creatio version floor.")]
	[Category("Unit")]
	public void ClearThemesCache_ShouldReturnVersionExitCode_WhenCreatioVersionRequirementIsUnmet() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		FakeClearThemesCacheCommand defaultCommand = new();
		FakeClearThemesCacheCommand resolvedCommand = new();
		ICreatioVersionChecker versionChecker = Substitute.For<ICreatioVersionChecker>();
		versionChecker
			.When(c => c.EnsureRequirements(Arg.Any<object>()))
			.Do(_ => throw new CreatioVersionRequirementException(
				"This command requires Creatio 10.0.0 or later. The target environment runs 8.1.5. Update Creatio and retry.",
				CreatioVersionRequirementException.VersionTooOldCode));
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<ClearThemesCacheCommand>(Arg.Any<ClearThemesCacheOptions>()).Returns(resolvedCommand);
		commandResolver.Resolve<ICreatioVersionChecker>(Arg.Any<EnvironmentOptions>()).Returns(versionChecker);
		ClearThemesCacheTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		// Act
		CommandExecutionResult result = tool.ClearThemesCache(new ClearThemesCacheArgs(EnvironmentName: "docker_fix2"));
		string[] messageValues = result.Output.Select(message => message.Value?.ToString() ?? string.Empty).ToArray();

		// Assert
		result.ExitCode.Should().Be(Clio.Program.CreatioVersionRequirementExitCode,
			because: "an unmet Creatio version requirement must refuse the cache refresh with the distinct version exit code, mirroring the CLI gate");
		messageValues.Should().Contain(value => value.Contains("requires Creatio 10.0.0 or later"),
			because: "the version-requirement message must be surfaced to the MCP caller");
		resolvedCommand.CapturedOptions.Should().BeNull(
			because: "the cache must never be refreshed when the environment does not satisfy the version floor");
		ConsoleLogger.Instance.ClearMessages();
	}

	private sealed class FakeClearThemesCacheCommand : ClearThemesCacheCommand {
		public ClearThemesCacheOptions? CapturedOptions { get; private set; }

		public FakeClearThemesCacheCommand()
			: base(
				Substitute.For<IApplicationClient>(),
				new EnvironmentSettings(),
				Substitute.For<IServiceUrlBuilder>()) {
		}

		public override int Execute(ClearThemesCacheOptions options) {
			CapturedOptions = options;
			return 0;
		}
	}
}
