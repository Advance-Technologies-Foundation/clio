using Clio;
using Clio.UserEnvironment;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Common;

[TestFixture]
[Category("Unit")]
[Property("Module", "Common")]
public sealed class BootstrapDiagnosticMessageTests {
	private static SettingsBootstrapReport Report(string status, params SettingsIssue[] issues) =>
		new(status, "/tmp/appsettings.json", "dev", "dev", 1, issues, [], true, true);

	[Test]
	[Description("Reports a shape mismatch on the CLI without the fix-or-delete advice, because the settings file is valid and editing it is the wrong action.")]
	public void BuildBootstrapDiagnosticMessage_Should_Omit_Repair_Advice_For_A_Shape_Mismatch() {
		// Arrange
		SettingsBootstrapReport report = Report("broken",
			new SettingsIssue(SettingsBootstrapService.SettingsShapeMismatchCode,
				"appsettings.json is valid JSON, but this clio version cannot bind autoupdate.clio.enabled."));

		// Act
		string message = BindingsModule.BuildBootstrapDiagnosticMessage(report);

		// Assert
		message.Should().Contain("cannot bind autoupdate.clio.enabled",
			because: "the CLI diagnostic must name the section that could not be bound");
		message.Should().NotContain("Fix or delete",
			because: "asking a user to hand-fix or delete a valid settings file is the misleading advice issue #1462 reported");
	}

	[Test]
	[Description("Still reports a shape mismatch when the configuration only degraded, so a silently dropped section is not invisible outside the MCP health tool.")]
	public void BuildBootstrapDiagnosticMessage_Should_Report_A_Degraded_Shape_Mismatch() {
		// Arrange
		SettingsBootstrapReport report = Report("issues-detected",
			new SettingsIssue(SettingsBootstrapService.SettingsShapeMismatchCode,
				"appsettings.json is valid JSON, but this clio version cannot bind autoupdate."));

		// Act
		string message = BindingsModule.BuildBootstrapDiagnosticMessage(report);

		// Assert
		message.Should().NotBeNull(
			because: "the command keeps working in degraded mode, so the dropped section is only ever mentioned here");
		message.Should().Contain("degraded",
			because: "the line must say the configuration is partly unusable rather than fine");
	}

	[Test]
	[Description("Keeps the fix-or-delete advice for a genuinely unreadable settings file.")]
	public void BuildBootstrapDiagnosticMessage_Should_Keep_Repair_Advice_For_An_Unreadable_File() {
		// Arrange
		SettingsBootstrapReport report = Report("broken",
			new SettingsIssue("settings-file-unreadable", "appsettings.json could not be parsed."));

		// Act
		string message = BindingsModule.BuildBootstrapDiagnosticMessage(report);

		// Assert
		message.Should().Contain("Fix or delete",
			because: "a damaged file is exactly the case where a hand fix or a deletion IS the way forward");
	}

	[Test]
	[Description("Says nothing about a healthy bootstrap that applied no repairs.")]
	public void BuildBootstrapDiagnosticMessage_Should_Say_Nothing_When_Bootstrap_Is_Healthy() {
		// Arrange
		SettingsBootstrapReport report = Report("healthy");

		// Act
		string message = BindingsModule.BuildBootstrapDiagnosticMessage(report);

		// Assert
		message.Should().BeNull(
			because: "a healthy startup must stay silent; a warning on every command is noise, not a diagnostic");
	}
}
