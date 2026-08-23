using System;
using System.Collections.Generic;
using System.Reflection;
using Clio.Command;
using Clio.Command.McpServer.Tools;
using Clio.Common.db;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using ModelContextProtocol.Server;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
[Property("Module", "McpServer")]
public sealed class DbTemplatePruneToolTests {
	[Test]
	[Category("Unit")]
	[Description("Classifies inventory as read-only and pruning as destructive for MCP host approval.")]
	public void ToolMethods_DeclareSafetyMetadata() {
		// Arrange
		MethodInfo listMethod = typeof(DbTemplatePruneTool).GetMethod(nameof(DbTemplatePruneTool.ListDbTemplates));
		MethodInfo pruneMethod = typeof(DbTemplatePruneTool).GetMethod(nameof(DbTemplatePruneTool.PruneDbTemplates));

		// Act
		McpServerToolAttribute list = listMethod.GetCustomAttribute<McpServerToolAttribute>();
		McpServerToolAttribute prune = pruneMethod.GetCustomAttribute<McpServerToolAttribute>();

		// Assert
		list.ReadOnly.Should().BeTrue(because: "inventory does not mutate PostgreSQL");
		list.Destructive.Should().BeFalse(because: "inventory only reads catalog metadata");
		prune.ReadOnly.Should().BeFalse(because: "pruning deletes selected databases");
		prune.Destructive.Should().BeTrue(because: "the MCP host must gate irreversible deletion");
	}

	[Test]
	[Category("Unit")]
	[Description("Returns the service inventory as the structured MCP result.")]
	public void ListDbTemplates_ReturnsStructuredInventory() {
		// Arrange
		IDbTemplatePruneService service = Substitute.For<IDbTemplatePruneService>();
		DbTemplateInventoryResult expected = new(true, "local-pg",
			[new PostgresManagedTemplate("template-a", "Studio.zip", DateTimeOffset.UtcNow, "1.0")]);
		service.Inventory("local-pg").Returns(expected);
		DbTemplatePruneTool tool = new(service);

		// Act
		DbTemplateInventoryResult actual = tool.ListDbTemplates(new ListDbTemplatesArgs("local-pg"));

		// Assert
		actual.Should().BeSameAs(expected,
			because: "MCP clients need the service's success and failure distinction without log parsing");
	}

	[Test]
	[Category("Unit")]
	[Description("Passes only the explicitly supplied database names to the destructive service operation.")]
	public void PruneDbTemplates_PassesExplicitNames() {
		// Arrange
		IDbTemplatePruneService service = Substitute.For<IDbTemplatePruneService>();
		DbTemplatePruneResult expected = new(true, DbTemplatePruneService.CompleteSuccessStatus,
			"local-pg", []);
		service.Prune("local-pg", Arg.Is<IReadOnlyCollection<string>>(names =>
			names.Count == 1 && System.Linq.Enumerable.Contains(names, "template-a")), null)
			.Returns(expected);
		DbTemplatePruneTool tool = new(service);

		// Act
		DbTemplatePruneResult actual = tool.PruneDbTemplates(
			new PruneDbTemplatesArgs("local-pg", ["template-a"]));

		// Assert
		actual.Should().BeSameAs(expected,
			because: "the destructive tool must not infer or broaden the approved selection");
		service.Received(1).Prune("local-pg", Arg.Any<IReadOnlyCollection<string>>(), null);
	}
}
