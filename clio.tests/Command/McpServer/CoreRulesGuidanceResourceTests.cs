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

	[Test]
	[Description("The core-rules guide requires the navigation placement decision before create-app, because that requirement was ignored while it lived only as a reference bullet inside the app-modeling guide (ENG-88474).")]
	public void Guide_Should_RequireNavigationPlacement_BeforeApplicationCreate() {
		// Arrange
		string coreRules = CoreRulesGuidanceResource.Guide.Text;

		// Act
		// Nothing to act on: the guide is static content and the assertions below are the contract.

		// Assert
		coreRules.Should().Contain("Navigation placement is decided BEFORE you create an app",
			because: "a live run carried the app-modeling reminder in context and still built the whole app before asking, so the requirement has to sit among the invariants that are read first");
		coreRules.Should().Contain("in the SAME turn you ask about the environment",
			because: "naming the turn is what makes this actionable — 'ask early' was already stated elsewhere and did not fire");
		coreRules.Should().Contain("System administrators",
			because: "the consequence of skipping the decision is that only administrators can open the app, which is the reason it cannot be deferred");
		coreRules.Should().Contain("get-guidance name=workplaces",
			because: "core-rules states the requirement and must route to the guide that owns the option set and the write recipes");
		coreRules.Should().NotContain("SysModuleInWorkplace",
			because: "the write recipes belong to the workplaces guide; duplicating them here would let the two drift");
	}
}
