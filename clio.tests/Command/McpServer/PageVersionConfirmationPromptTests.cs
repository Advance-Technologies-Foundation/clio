using Clio.Command.McpServer.Prompts;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

/// <summary>
/// Asserts the active get-page prompt carries the ENG-91583 AC#2 rule: before planning any component
/// work the agent must resolve the platform version and, on <c>requiresVersionConfirmation: true</c>
/// (the <c>latest-fallback</c> hard stop), communicate the unknown version and request explicit
/// confirmation rather than silently assuming the <c>latest</c> superset. The rule lives in the
/// rendered prompt text, so a unit test guards it against accidental removal.
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "McpServer")]
public sealed class PageVersionConfirmationPromptTests {
	[Test]
	[Description("The get-page prompt instructs the agent to resolve the platform version and stop on requiresVersionConfirmation before planning component work (ENG-91583 AC#2).")]
	public void GetPage_ShouldContainVersionUnknownConfirmationRule_WhenPagePromptRequested() {
		// Arrange & Act
		string prompt = PagePrompt.GetPage("UsrApp_BlankPage", "dev");

		// Assert
		prompt.Should().Contain("requiresVersionConfirmation",
			because: "the active get-page prompt must route the agent to the machine-readable version-unknown gate (ENG-91583 AC#2)");
		prompt.Should().Contain("request explicit confirmation",
			because: "AC#2 requires the prompt to tell the agent to communicate the unknown version and ask the user before planning");
		prompt.Should().Contain("resolvedFromReason",
			because: "the prompt must point the agent at the transient/stable reason so it can phrase the confirmation ask (ENG-91583 AC#3)");
	}
}
