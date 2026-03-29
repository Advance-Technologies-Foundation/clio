using System.Linq;
using Clio.Command.McpServer.Tools;
using FluentAssertions;
using ModelContextProtocol.Server;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
public sealed class ToolContractGetToolTests {
	[Test]
	[Category("Unit")]
	public void ToolContractGet_Should_Advertise_Stable_Tool_Name() {
		McpServerToolAttribute attribute = (McpServerToolAttribute)typeof(ToolContractGetTool)
			.GetMethod(nameof(ToolContractGetTool.GetToolContracts))!
			.GetCustomAttributes(typeof(McpServerToolAttribute), false)
			.Single();

		attribute.Name.Should().Be(ToolContractGetTool.ToolName);
	}

	[Test]
	[Category("Unit")]
	public void ToolContractGet_Should_Return_App_Generation_Contracts_When_Request_Is_Empty() {
		ToolContractGetTool tool = new();

		ToolContractGetResponse result = tool.GetToolContracts(new ToolContractGetArgs());

		result.Success.Should().BeTrue();
		result.Error.Should().BeNull();
		result.Tools.Should().NotBeNull();
		result.Tools!.Select(contract => contract.Name).Should().Contain([
			ApplicationCreateTool.ApplicationCreateToolName,
			SchemaSyncTool.ToolName,
			PageSyncTool.ToolName
		]);
		result.Tools!.Select(contract => contract.Name).Should().NotContain(ToolContractGetTool.ToolName);
	}

	[Test]
	[Category("Unit")]
	public void ToolContractGet_Should_Return_Specific_Contract_With_Canonical_Flows() {
		ToolContractGetTool tool = new();

		ToolContractGetResponse result = tool.GetToolContracts(new ToolContractGetArgs([
			ApplicationCreateTool.ApplicationCreateToolName
		]));

		result.Success.Should().BeTrue();
		ToolContractDefinition contract = result.Tools!.Single();
		contract.Name.Should().Be(ApplicationCreateTool.ApplicationCreateToolName);
		contract.Aliases.Should().Contain(alias => alias.Alias == "templateCode");
		contract.PreferredFlow.Tools.Should().Equal(
			ApplicationCreateTool.ApplicationCreateToolName,
			SchemaSyncTool.ToolName,
			ApplicationGetInfoTool.ApplicationGetInfoToolName);
	}

	[Test]
	[Category("Unit")]
	public void ToolContractGet_Should_Return_Structured_Error_For_Unknown_Tool() {
		ToolContractGetTool tool = new();

		ToolContractGetResponse result = tool.GetToolContracts(new ToolContractGetArgs([
			"page-updte"
		]));

		result.Success.Should().BeFalse();
		result.Tools.Should().BeNull();
		result.Error.Should().NotBeNull();
		result.Error!.Code.Should().Be("tool-not-found");
		result.Error.Suggestions.Should().Contain(PageUpdateTool.ToolName);
	}

	[Test]
	[Category("Unit")]
	public void ToolContractGet_Should_Use_Canonical_Required_Key_For_Modify_Entity_Contract() {
		ToolContractGetTool tool = new();

		ToolContractGetResponse result = tool.GetToolContracts(new ToolContractGetArgs([
			ModifyEntitySchemaColumnTool.ModifyEntitySchemaColumnToolName
		]));

		result.Success.Should().BeTrue();
		ToolContractDefinition contract = result.Tools!.Single();
		contract.InputSchema.Properties.Should().Contain(field => field.Name == "required");
		contract.InputSchema.Properties.Should().NotContain(field => field.Name == "is-required");
		contract.Examples.SelectMany(example => example.Arguments.Keys).Should().Contain("required");
		contract.Examples.SelectMany(example => example.Arguments.Keys).Should().NotContain("is-required");
	}

	[Test]
	[Category("Unit")]
	public void ToolContractGet_Should_Return_Field_Level_Error_For_Blank_Tool_Name() {
		ToolContractGetTool tool = new();

		ToolContractGetResponse result = tool.GetToolContracts(new ToolContractGetArgs([
			" "
		]));

		result.Success.Should().BeFalse();
		result.Error.Should().NotBeNull();
		result.Error!.Code.Should().Be("missing-required-parameter");
		result.Error.FieldErrors.Should().ContainSingle();
		result.Error.FieldErrors![0].Field.Should().Be("tool-names[0]");
	}
}
