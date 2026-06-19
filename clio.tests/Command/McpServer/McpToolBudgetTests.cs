using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Clio.Command.McpServer.Tools;
using FluentAssertions;
using ModelContextProtocol.Server;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

/// <summary>
/// ENG-90312 budget ratchet for MCP tools registered by clio.
/// The Anthropic / MCP host limit is 128 tools; the Jira stretch target is 65.
/// Every consolidation block in the consolidation plan must lower the ratchet
/// before the next block lands.
/// </summary>
[TestFixture]
[Property("Module", "McpServer")]
public sealed class McpToolBudgetTests {

	// ENG-90312 ratchet. Lower this constant in every commit that removes
	// or consolidates an MCP tool; do not raise it without ticket approval.
	// Raised to 25: odata-read was added by ENG-88584 (merged after Phase 2 spec was written).
	// Raised to 29: ENG-88584 also added odata-create, odata-update, odata-delete, and
	// create-ui-project as standalone flat tools after the Phase 2 spec locked the surface.
	// Set to 37 after the origin/master merge:
	//   - The 6 data/workspace write tools add-package-dependency, install-gate, create-ui-project,
	//     odata-create, odata-update and odata-delete were FOLDED into clio-run during the merge, so
	//     they no longer count against the flat surface.
	//   - master concurrently introduced read-only newcomers that stay flat (find-app, list-creatio-builds,
	//     assert-infrastructure, show-passing-infrastructure, find-empty-iis-port, get-process-signature,
	//     dataforge-* reads) which raise the flat count.
	//   - The auth/session/admin non-read-only tools get-browser-session, clear-browser-session,
	//     get-identity-assertion, regenerate-identity-signing-key and experimental remain flat by an
	//     explicit product decision; folding them into clio-run is a deliberate follow-up.
	// Net result: the flat (still-[McpServerTool]) surface is exactly 37 distinct tools.
	private const int ToolBudget = 37;

	[Test]
	[Category("Unit")]
	[Description("Caps the number of MCP tools registered by clio at the ENG-90312 ratchet; tighten the ratchet after every consolidation block.")]
	public void Mcp_Tool_Count_Should_Not_Exceed_The_Budget() {
		// Arrange
		Assembly clioAssembly = typeof(BaseTool<>).Assembly;

		// Act
		string[] toolNames = DiscoverMcpToolNames(clioAssembly)
			.OrderBy(name => name)
			.ToArray();

		// Assert
		toolNames.Length.Should().BeLessThanOrEqualTo(ToolBudget,
			because: "ENG-90312 caps the MCP tool registry at {0} (current registry exposes {1} tools: {2}); " +
				"lower the ratchet whenever a consolidation block removes or merges tools",
			ToolBudget,
			toolNames.Length,
			string.Join(", ", toolNames));
	}

	[Test]
	[Category("Unit")]
	[Description("Asserts every MCP tool has a unique Name so the registry has no shadow registrations after consolidation.")]
	public void Mcp_Tool_Names_Should_Be_Unique() {
		// Arrange
		Assembly clioAssembly = typeof(BaseTool<>).Assembly;

		// Act
		string[] duplicates = DiscoverMcpToolNames(clioAssembly)
			.GroupBy(name => name)
			.Where(group => group.Count() > 1)
			.Select(group => $"{group.Key} (×{group.Count()})")
			.OrderBy(entry => entry)
			.ToArray();

		// Assert
		duplicates.Should().BeEmpty(
			because: "every [McpServerTool] Name must be unique; collisions would shadow each other at runtime ({0})",
			string.Join(", ", duplicates));
	}

	/// <summary>
	/// Mirrors <c>WithToolsFromAssembly</c> from the ModelContextProtocol SDK:
	/// types decorated (directly or via inheritance) with <see cref="McpServerToolTypeAttribute"/>,
	/// then their public and non-public, instance and static methods marked with
	/// <see cref="McpServerToolAttribute"/>.
	/// </summary>
	private static IEnumerable<string> DiscoverMcpToolNames(Assembly assembly) {
		const BindingFlags methodScope = BindingFlags.Public
			| BindingFlags.NonPublic
			| BindingFlags.Instance
			| BindingFlags.Static;

		return assembly.GetTypes()
			.Where(type => type.GetCustomAttribute<McpServerToolTypeAttribute>() is not null)
			.SelectMany(type => type.GetMethods(methodScope))
			.Select(method => method.GetCustomAttribute<McpServerToolAttribute>())
			.Where(attribute => attribute is not null)
			.Select(attribute => attribute!.Name ?? string.Empty);
	}
}
