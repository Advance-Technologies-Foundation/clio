using System;
using System.Linq;
using Clio.Command.McpServer.Tools;
using FluentAssertions;
using ModelContextProtocol.Server;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
[Property("Module", "McpServer")]
public class ThemeBackgroundToolsTests {

	[TestCase(typeof(RemoveBackgroundTool), nameof(RemoveBackgroundTool.RemoveBackground))]
	[Category("Unit")]
	[Description("Declares the theme background tools a destructive, idempotent, closed-world write, because each replaces or removes a stored background and a repeat leaves the same state.")]
	public void Tool_ShouldDeclareDestructiveIdempotentWrite(Type toolType, string methodName) {
		McpServerToolAttribute attribute = toolType.GetMethod(methodName)!
			.GetCustomAttributes(typeof(McpServerToolAttribute), false).Cast<McpServerToolAttribute>().Single();

		attribute.Should().BeEquivalentTo(new { ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = false },
			because: "the host must confirm a call that changes a stored background");
	}
}
