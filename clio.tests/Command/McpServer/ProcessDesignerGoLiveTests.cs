using System;
using System.Linq;
using Clio.Command;
using Clio.Command.McpServer;
using Clio.Command.McpServer.Tools;
using Clio.Command.McpServer.Tools.ProcessDesigner;
using Clio.UserEnvironment;
using FluentAssertions;
using ModelContextProtocol.Server;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

/// <summary>
/// Go-live lock-in for the process-designer MCP surface (ENG-96132): the five tools and five prompts
/// shipped by REMOVING <c>[FeatureToggle("process-designer")]</c>, so business-process creation works by
/// default with no feature flag. These tests pin both the attribute absence and its consequence — the
/// surface is registered on a clio whose <c>features</c> map is empty (the shipping default) — so a
/// refactor cannot silently re-gate a GA capability the way "restoring consistency" once re-gated
/// get-process-signature.
/// </summary>
[TestFixture]
[Property("Module", "McpServer")]
public sealed class ProcessDesignerGoLiveTests {

	private static readonly Type[] GoLiveToolTypes = [
		typeof(CreateBusinessProcessTool),
		typeof(ModifyBusinessProcessTool),
		typeof(DescribeProcessTool),
		typeof(ListUserTasksTool),
		typeof(ValidateProcessGraphTool)
	];

	private static readonly Type[] GoLivePromptTypes = [
		typeof(Clio.Command.McpServer.Prompts.ProcessDesigner.CreateBusinessProcessPrompt),
		typeof(Clio.Command.McpServer.Prompts.ProcessDesigner.ModifyBusinessProcessPrompt),
		typeof(Clio.Command.McpServer.Prompts.ProcessDesigner.DescribeProcessPrompt),
		typeof(Clio.Command.McpServer.Prompts.ProcessDesigner.ValidateProcessGraphPrompt),
		typeof(Clio.Command.McpServer.Prompts.ListUserTasksPrompt)
	];

	private static readonly string[] GoLiveToolNames = [
		CreateBusinessProcessTool.CreateBusinessProcessToolName,
		ModifyBusinessProcessTool.ModifyBusinessProcessToolName,
		DescribeProcessTool.ToolName,
		ListUserTasksTool.ListUserTasksToolName,
		ValidateProcessGraphTool.ToolName
	];

	// The REAL feature rule over a settings repository whose features map is empty — the state every
	// fresh clio install ships in. A substitute returning a blanket true would make these tests vacuous.
	private static IFeatureToggleService CreateEveryFeatureOffToggleService() {
		ISettingsRepository settingsRepository = Substitute.For<ISettingsRepository>();
		settingsRepository.IsFeatureEnabled(Arg.Any<string>()).Returns(false);
		return new FeatureToggleService(settingsRepository);
	}

	[Test]
	[Category("Unit")]
	[Description("The five process-designer tool types and five prompt types must not carry [FeatureToggle]: the capability shipped enabled by default at go-live (ENG-96132).")]
	public void ProcessDesignerSurface_Should_Not_Carry_FeatureToggle() {
		// Arrange
		Type[] surface = GoLiveToolTypes.Concat(GoLivePromptTypes).ToArray();

		// Act
		Type[] gated = surface
			.Where(type => type.GetCustomAttributes(typeof(FeatureToggleAttribute), inherit: true).Length > 0)
			.ToArray();

		// Assert
		gated.Should().BeEmpty(
			because: "business process creation is GA (ENG-96132) and must work with no feature flag; a "
				+ "re-added [FeatureToggle] would hide the surface on every fresh install while the shipped "
				+ "guidance, prompts and install-process-builder remediation keep pointing at it");
	}

	[Test]
	[Category("Unit")]
	[Description("With every feature flag off (the shipping default), the invoker registry still resolves all five process-designer tools, so clio-run dispatch and the get-tool-contract index expose them by default.")]
	public void ProcessDesignerTools_Should_Be_Invokable_When_EveryFeatureIsOff() {
		// Arrange
		IServiceProvider provider = Substitute.For<IServiceProvider>();
		McpToolInvokerRegistry registry = new(
			provider,
			typeof(CreateBusinessProcessTool).Assembly,
			CreateEveryFeatureOffToggleService(),
			BindingsModule.CreateMcpSerializerOptions());

		// Act
		string[] missing = GoLiveToolNames
			.Where(toolName => !registry.TryGetTool(toolName, out McpServerTool _))
			.ToArray();

		// Assert
		missing.Should().BeEmpty(
			because: "the registry is what clio-run dispatch and the get-tool-contract index read, so a tool "
				+ "absent here with an empty features map is invisible on every MCP surface of a default "
				+ "install — the exact pre-go-live behavior ENG-96132 removed");
	}

	[Test]
	[Category("Unit")]
	[Description("With every feature flag off (the shipping default), the MCP registration filter still selects all five process-designer prompt types for prompts/list.")]
	public void ProcessDesignerPrompts_Should_Register_When_EveryFeatureIsOff() {
		// Arrange
		IFeatureToggleService featureToggleService = CreateEveryFeatureOffToggleService();

		// Act
		Type[] enabledPromptTypes = McpFeatureToggleFilter.GetEnabledTypes(
			typeof(CreateBusinessProcessTool).Assembly,
			typeof(McpServerPromptTypeAttribute),
			featureToggleService.IsEnabled);

		// Assert
		enabledPromptTypes.Should().Contain(GoLivePromptTypes,
			because: "prompts are registered through this exact filter, so a prompt type it drops with an "
				+ "empty features map never reaches prompts/list on a default install");
	}
}
