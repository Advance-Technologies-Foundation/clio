using Clio.Command.McpServer.Prompts;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
[Category("Unit")]
[Property("Module", "McpServer")]
public sealed class ProfileLanguageGuidanceTests {
	private const string ToolMention = "get-user-culture";
	private const string AskOnFailureMention = "ASK the user";

	[Test]
	[Description("Keeps profile-language detection and explicit failure handling in the executable entity-schema prompt.")]
	public void CreateEntitySchema_ShouldPreserveProfileLanguageContract_WhenPromptRequested() {
		// Arrange

		// Act
		string prompt = EntitySchemaPrompt.CreateEntitySchema("UsrPkg", "UsrVehicle", "Vehicle", "dev");

		// Assert
		AssertExecutablePromptContract(prompt, "entity-schema prompt");
	}

	[Test]
	[Description("Keeps profile-language detection and explicit failure handling in the executable page prompt.")]
	public void CreatePage_ShouldPreserveProfileLanguageContract_WhenPromptRequested() {
		// Arrange

		// Act
		string prompt = PagePrompt.CreatePage("UsrApp_BlankPage", "UsrPkg", "BlankPageTemplate", "dev");

		// Assert
		AssertExecutablePromptContract(prompt, "page prompt");
	}

	[Test]
	[Description("Keeps profile-language detection and explicit failure handling in the executable app prompt.")]
	public void ApplicationCreate_ShouldPreserveProfileLanguageContract_WhenPromptRequested() {
		// Arrange

		// Act
		string prompt = ApplicationPrompt.ApplicationCreate("dev", "My App", "UsrMyApp", "AppFreedomUI", "#1F5F8B");

		// Assert
		AssertExecutablePromptContract(prompt, "application prompt");
	}

	[Test]
	[Description("Keeps profile-language detection and explicit failure handling in the executable app-section prompt.")]
	public void ApplicationSectionCreate_ShouldPreserveProfileLanguageContract_WhenPromptRequested() {
		// Arrange

		// Act
		string prompt = ApplicationPrompt.ApplicationSectionCreate("dev", "UsrMyApp", "Visits");

		// Assert
		AssertExecutablePromptContract(prompt, "section prompt");
	}

	private static void AssertExecutablePromptContract(string prompt, string family) {
		prompt.Should().Contain(ToolMention,
			because: $"{family} must preserve its executable profile-language lookup step");
		prompt.Should().Contain(AskOnFailureMention,
			because: $"{family} must ask instead of silently defaulting when executable lookup fails");
	}
}
