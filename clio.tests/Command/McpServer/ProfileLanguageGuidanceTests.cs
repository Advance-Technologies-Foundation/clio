using System;
using Clio.Command.McpServer.Prompts;
using Clio.Command.McpServer.Resources;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

/// <summary>
/// Asserts the detect-once / reuse / ask-on-failure profile-language guidance (ENG-91044 Story 7)
/// is present across all guidance families: server instructions, the app-modeling resource, and the
/// entity / page / application / section prompts (SM-03: 4/4 prompt families).
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "McpServer")]
public sealed class ProfileLanguageGuidanceTests {
	private const string ToolMention = "get-user-culture";
	private const string AskOnFailureMention = "ASK the user";

	private static void AssertContainsGuidance(string text, string family) {
		text.Should().Contain(ToolMention,
			because: $"{family} must instruct the agent to detect the profile language via get-user-culture (AC-07)");
		text.Should().Contain(AskOnFailureMention,
			because: $"{family} must instruct the agent to ask the user on failure rather than silently defaulting (AC-04)");
	}

	[Test]
	[Description("The core-rules guide (mandated first by the server instructions) carries the detect-once / reuse / ask-on-failure profile-language guidance.")]
	public void CoreRulesGuide_ShouldContainProfileLanguageGuidance_WhenServerInitializes() {
		string coreRules = CoreRulesGuidanceResource.Guide.Text;
		AssertContainsGuidance(coreRules, "core-rules guide");
		coreRules.Should().ContainEquivalentOf("once per session",
			because: "the guidance must enforce detect-once-per-session reuse (AC-05)");
	}

	[Test]
	[Description("The app-modeling guidance resource contains the profile-language rule.")]
	public void GetGuide_ShouldContainProfileLanguageGuidance_WhenAppModelingResourceRead() {
		AssertContainsGuidance(AppModelingGuidanceResource.Guide.Text, "app-modeling resource");
	}

	[Test]
	[Description("The create-entity-schema prompt contains the profile-language guidance.")]
	public void CreateEntitySchema_ShouldContainProfileLanguageGuidance_WhenEntityPromptRequested() {
		string prompt = EntitySchemaPrompt.CreateEntitySchema("UsrPkg", "UsrVehicle", "Vehicle", "dev");
		AssertContainsGuidance(prompt, "entity-schema prompt");
	}

	[Test]
	[Description("The create-page prompt contains the profile-language guidance.")]
	public void CreatePage_ShouldContainProfileLanguageGuidance_WhenPagePromptRequested() {
		string prompt = PagePrompt.CreatePage("UsrApp_BlankPage", "UsrPkg", "BlankPageTemplate", "dev");
		AssertContainsGuidance(prompt, "page prompt");
	}

	[Test]
	[Description("The create-app prompt contains the profile-language guidance.")]
	public void ApplicationCreate_ShouldContainProfileLanguageGuidance_WhenApplicationPromptRequested() {
		string prompt = ApplicationPrompt.ApplicationCreate("dev", "My App", "UsrMyApp", "AppFreedomUI", "#1F5F8B");
		AssertContainsGuidance(prompt, "application prompt");
	}

	[Test]
	[Description("The create-app-section prompt contains the profile-language guidance.")]
	public void ApplicationSectionCreate_ShouldContainProfileLanguageGuidance_WhenSectionPromptRequested() {
		string prompt = ApplicationPrompt.ApplicationSectionCreate("dev", "UsrMyApp", "Visits");
		AssertContainsGuidance(prompt, "section prompt");
	}
}
