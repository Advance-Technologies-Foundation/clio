using Clio.Command.McpServer.Resources;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

/// <summary>
/// ENG-92761 (F4): the core-rules guide must state, once and canonically, that resident tools
/// (get-tool-contract index: resident=true) are called natively while every other tool is invoked via
/// clio-run. This wording is the canonical string the CAADT sub-task (ENG-92762) mirrors.
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "McpServer")]
public sealed class CoreRulesGuidanceResourceTests {
	[Test]
	[Description("The core-rules guide contains the resident/clio-run invariant with the canonical wording.")]
	public void Guide_Should_ContainResidentClioRunRule() {
		// Arrange
		string coreRules = CoreRulesGuidanceResource.Guide.Text;

		// Act
		const string expectedRule =
			"Resident tools (get-tool-contract index: resident=true) are called natively; " +
			"every other tool is invoked via clio-run <command>. Never wrap a resident tool in clio-run.";

		// Assert
		coreRules.Should().Contain(expectedRule,
			because: "the rule must be present verbatim so agents and the CAADT mirror (ENG-92762) share one canonical string");
	}
}
