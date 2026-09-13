using System.Linq;
using System.Reflection;
using System.Text.Json;
using Clio.Command.McpServer.Tools;
using FluentAssertions;
using ModelContextProtocol.Server;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture, Category("Unit"), Property("Module", "McpServer")]
public sealed class UninstallIdentityToolTests {
	[TestCase(false), TestCase(true)]
	[Description("The published environment and recovery flag deserialize with the production MCP serializer.")]
	public void Args_ShouldBindPublishedContract(bool skip) {
		// Arrange
		string json = "{\"environment-name\":\"chosen\",\"skip-crm-cleanup\":" + (skip ? "true" : "false") + "}";
		// Act
		UninstallIdentityArgs args = JsonSerializer.Deserialize<UninstallIdentityArgs>(json, BindingsModule.CreateMcpSerializerOptions());
		ToolContractDefinition contract = ToolContractCatalog.GetContracts([UninstallIdentityTool.ToolName]).Tools!.Single();
		// Assert
		args.EnvironmentName.Should().Be("chosen", because: "destructive cleanup must bind the explicit environment");
		args.SkipCrmCleanup.Should().Be(skip, because: "recovery is an explicit caller decision");
		contract.InputSchema.Required.Should().Equal(["environment-name"], because: "environment selection must not default silently");
		contract.InputSchema.Properties.Select(field => field.Name).Should().BeEquivalentTo(["environment-name", "skip-crm-cleanup"],
			because: "the curated contract must describe exactly the supported arguments");
	}

	[Test]
	[Description("Identity removal remains destructive and explains the retained CRM database.")]
	public void Contract_ShouldExplainPreservedDatabaseAndDestruction() {
		// Arrange
		MethodInfo method = typeof(UninstallIdentityTool).GetMethod(nameof(UninstallIdentityTool.Uninstall));
		// Act
		McpServerToolAttribute metadata = method.GetCustomAttribute<McpServerToolAttribute>();
		ToolContractDefinition contract = ToolContractCatalog.GetContracts([UninstallIdentityTool.ToolName]).Tools!.Single();
		// Assert
		metadata.Destructive.Should().BeTrue(because: "the tool deletes local IIS artifacts and files");
		contract.Description.Should().Contain("Preserves CRM and its database", because: "standalone removal must not imply a database drop");
	}
}
