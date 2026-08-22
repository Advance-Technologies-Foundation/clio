using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Clio.Command;
using Clio.Common;
using Clio.Common.db;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using Spectre.Console;

namespace Clio.Tests.Command;

[TestFixture]
[Property("Module", "Command")]
public sealed class PruneDbTemplatesCommandTests {
	[Test]
	[Category("Unit")]
	[Description("Configures Escape to cancel the PostgreSQL server selection prompt.")]
	public void BuildServerSelectionPrompt_ShouldReturnNull_WhenEscapeCancels() {
		// Arrange
		SelectionPrompt<string> prompt = DbTemplatePruneConsole.BuildServerSelectionPrompt(["local-pg"]);

		// Act
		string result = prompt.CancelResult();

		// Assert
		result.Should().BeNull(because: "Spectre uses the configured cancel result when Escape is pressed");
	}

	[Test]
	[Category("Unit")]
	[Description("Configures Escape to cancel the PostgreSQL template multi-selection prompt.")]
	public void BuildTemplateSelectionPrompt_ShouldReturnEmpty_WhenEscapeCancels() {
		// Arrange
		PostgresManagedTemplate template = new("template-a", "8.3.4_Studio.zip",
			DateTimeOffset.UtcNow, "1.0");
		MultiSelectionPrompt<string> prompt = DbTemplatePruneConsole.BuildTemplateSelectionPrompt([template]);

		// Act
		List<string> result = prompt.CancelResult();

		// Assert
		result.Should().BeEmpty(because: "an Escape cancellation must flow through the existing safe no-selection path");
	}

	[Test]
	[Category("Unit")]
	[Description("Escapes terminal control characters before rendering a database server name in plain text.")]
	public void EscapeControlCharacters_ShouldRenderLiteralCode_WhenServerNameContainsEscape() {
		// Arrange
		const string serverName = "trusted\u001B[2Jspoofed";

		// Act
		string result = DbTemplatePruneConsole.EscapeControlCharacters(serverName);

		// Assert
		result.Should().Be("trusted\\u001B[2Jspoofed",
			because: "the destructive confirmation must not execute terminal control sequences from configuration");
	}

	[Test]
	[Category("Unit")]
	[Description("Runs the supplied operation once and returns its result while rendering progress.")]
	public void RunWithProgress_ShouldReturnOperationResult_WhenItemsComplete() {
		// Arrange
		IAnsiConsole console = CreateTestConsole();
		DbTemplatePruneResult expected = new(true, DbTemplatePruneService.CompleteSuccessStatus,
			"local-pg", []);
		int operationCalls = 0;
		int progressReports = 0;

		// Act
		DbTemplatePruneResult actual = DbTemplatePruneConsole.RunWithProgress(console, 2, reportCompleted => {
			operationCalls++;
			reportCompleted();
			progressReports++;
			reportCompleted();
			progressReports++;
			return expected;
		});

		// Assert
		actual.Should().BeSameAs(expected, because: "the progress wrapper must preserve the pruning result");
		operationCalls.Should().Be(1, because: "the wrapper must not duplicate a destructive operation");
		progressReports.Should().Be(2, because: "each processed template should advance the progress bar once");
	}

	[Test]
	[Category("Unit")]
	[Description("Returns a structured request failure without retrying the destructive operation.")]
	public void RunWithProgress_ShouldReturnFailureResult_WhenOperationReturnsBeforeCallbacks() {
		// Arrange
		IAnsiConsole console = CreateTestConsole();
		DbTemplatePruneResult expected = new(false, DbTemplatePruneService.CompleteFailureStatus,
			"local-pg", [], "configuration", "Settings changed.");
		int operationCalls = 0;

		// Act
		DbTemplatePruneResult actual = DbTemplatePruneConsole.RunWithProgress(console, 2, _ => {
			operationCalls++;
			return expected;
		});

		// Assert
		actual.Should().BeSameAs(expected, because: "structured failures remain authoritative after progress rendering");
		operationCalls.Should().Be(1, because: "progress completion must not retry a failed destructive request");
	}

	[Test]
	[Category("Unit")]
	[Description("Propagates an unexpected operation exception after accepting reported progress.")]
	public void RunWithProgress_ShouldPropagateException_WhenOperationThrows() {
		// Arrange
		IAnsiConsole console = CreateTestConsole();
		int progressReports = 0;

		// Act
		Action act = () => DbTemplatePruneConsole.RunWithProgress(console, 2, reportCompleted => {
			reportCompleted();
			progressReports++;
			throw new InvalidOperationException("unexpected failure");
		});

		// Assert
		act.Should().Throw<InvalidOperationException>(because: "unexpected programming failures must remain visible");
		progressReports.Should().Be(1, because: "the wrapper must preserve progress reported before the exception");
	}

	[TestCase("Managed PostgreSQL templates")]
	[TestCase("Templates selected for deletion")]
	[Category("Unit")]
	[Description("Keeps human-facing template tables focused on source, database, and creation date.")]
	public void BuildTemplateTable_ShouldOmitMetadataVersion_WhenRenderingHumanFacingTable(string title) {
		// Arrange
		PostgresManagedTemplate template = new("template-a", "8.3.4_StudioNet8_PostgreSQL_ENU",
			DateTimeOffset.Parse("2026-04-01T19:25:40Z"), "metadata-value-must-not-render");
		StringWriter output = new();
		IAnsiConsole console = AnsiConsole.Create(new AnsiConsoleSettings {
			Ansi = AnsiSupport.No,
			ColorSystem = ColorSystemSupport.NoColors,
			Out = new AnsiConsoleOutput(output)
		});

		// Act
		console.Write(DbTemplatePruneConsole.BuildTemplateTable(title, [template]));
		string rendered = output.ToString();

		// Assert
		rendered.Should().Contain("Source", because: "the source identifies the Creatio package to the user");
		rendered.Should().Contain("Database", because: "the table must preserve the exact deletion target");
		rendered.Should().Contain("Created", because: "the creation date remains useful inventory context");
		rendered.Should().NotContain("Metadata version",
			because: "metadata version is not useful in the human-facing table");
		rendered.Should().NotContain("metadata-value-must-not-render",
			because: "the internal metadata value must not remain in an unlabeled cell");
	}

	[Test]
	[Category("Unit")]
	[Description("Orders template choices by the Creatio version at the start of the source name.")]
	public void OrderTemplatesForSelection_ShouldGroupOldestVersionsFirst_WhenVersionsAreMixed() {
		// Arrange
		PostgresManagedTemplate[] templates = [
			new("template-10", "10.0.0.802_StudioNet8_PostgreSQL_ENU", DateTimeOffset.UtcNow, "1.0"),
			new("template-834", "8.3.4.2143_StudioNet8_PostgreSQL_ENU", DateTimeOffset.UtcNow, "1.0"),
			new("template-unknown", "custom-template", DateTimeOffset.UtcNow, "1.0"),
			new("template-833", "C:\\backups\\8.3.3.3192_StudioNet8_PostgreSQL_ENU.zip",
				DateTimeOffset.UtcNow, "1.0")
		];

		// Act
		IReadOnlyList<PostgresManagedTemplate> ordered =
			DbTemplatePruneConsole.OrderTemplatesForSelection(templates);

		// Assert
		ordered.Select(template => template.DatabaseName).Should().Equal(
			["template-833", "template-834", "template-10", "template-unknown"],
			because: "older Creatio releases should stay together and unparseable sources should remain last");
	}

	[Test]
	[Category("Unit")]
	[Description("Shows only the source identifier in each interactive template choice.")]
	public void FormatTemplateChoice_ShouldReturnOnlySource_WhenTemplateHasDetails() {
		// Arrange
		PostgresManagedTemplate template = new("internal-database-name", "8.3.4_StudioNet8_PostgreSQL_ENU",
			DateTimeOffset.Parse("2026-04-01T19:25:40Z"), "1.0");

		// Act
		string choice = DbTemplatePruneConsole.FormatTemplateChoice(template);

		// Assert
		choice.Should().Be("8.3.4_StudioNet8_PostgreSQL_ENU",
			because: "database name, creation date, and metadata version already appear in the inventory");
	}

	[Test]
	[Category("Unit")]
	[Description("Fails without inventory when no configured PostgreSQL server is eligible.")]
	public void Execute_NoEligibleServer_ReturnsFailure() {
		// Arrange
		IDbTemplatePruneService service = Substitute.For<IDbTemplatePruneService>();
		IDbTemplatePruneConsole console = Substitute.For<IDbTemplatePruneConsole>();
		IInteractiveConsole interactive = Substitute.For<IInteractiveConsole>();
		ILogger logger = Substitute.For<ILogger>();
		interactive.IsInteractive.Returns(true);
		service.GetEligibleServers().Returns(new DbTemplateServerListResult(true, []));
		PruneDbTemplatesCommand command = new(service, console, interactive, logger);

		// Act
		int exitCode = command.Execute(new PruneDbTemplatesOptions());

		// Assert
		exitCode.Should().Be(1, because: "the command cannot inventory without an eligible configured server");
		service.DidNotReceive().Inventory(Arg.Any<string>());
	}

	[Test]
	[Category("Unit")]
	[Description("Uses an explicit server name without prompting and propagates its validation failure.")]
	public void Execute_ExplicitInvalidServer_ReturnsFailureWithoutFallback() {
		// Arrange
		IDbTemplatePruneService service = Substitute.For<IDbTemplatePruneService>();
		IDbTemplatePruneConsole console = Substitute.For<IDbTemplatePruneConsole>();
		IInteractiveConsole interactive = Substitute.For<IInteractiveConsole>();
		ILogger logger = Substitute.For<ILogger>();
		interactive.IsInteractive.Returns(true);
		service.Inventory("unknown").Returns(new DbTemplateInventoryResult(false, "unknown", [],
			"configuration", "Database server 'unknown' was not found or is disabled."));
		PruneDbTemplatesCommand command = new(service, console, interactive, logger);

		// Act
		int exitCode = command.Execute(new PruneDbTemplatesOptions { DbServerName = "unknown" });

		// Assert
		exitCode.Should().Be(1, because: "an invalid explicit server is an actionable command failure");
		service.DidNotReceive().GetEligibleServers();
		console.DidNotReceive().SelectServer(Arg.Any<IReadOnlyList<string>>());
	}
	[Test]
	[Category("Unit")]
	[Description("Selects the only configured PostgreSQL server without displaying a server prompt.")]
	public void Execute_OneEligibleServer_SelectsItAutomatically() {
		// Arrange
		IDbTemplatePruneService service = Substitute.For<IDbTemplatePruneService>();
		IDbTemplatePruneConsole console = Substitute.For<IDbTemplatePruneConsole>();
		IInteractiveConsole interactive = Substitute.For<IInteractiveConsole>();
		ILogger logger = Substitute.For<ILogger>();
		interactive.IsInteractive.Returns(true);
		service.GetEligibleServers().Returns(new DbTemplateServerListResult(true, ["local-pg"]));
		service.Inventory("local-pg").Returns(new DbTemplateInventoryResult(true, "local-pg", []));
		PruneDbTemplatesCommand command = new(service, console, interactive, logger);

		// Act
		int exitCode = command.Execute(new PruneDbTemplatesOptions());

		// Assert
		exitCode.Should().Be(0, because: "an empty successful inventory is a normal no-op outcome");
		console.DidNotReceive().SelectServer(Arg.Any<IReadOnlyList<string>>());
	}

	[Test]
	[Category("Unit")]
	[Description("Prompts for a server when several configured PostgreSQL servers are eligible.")]
	public void Execute_MultipleEligibleServers_UsesSelectedServer() {
		// Arrange
		IDbTemplatePruneService service = Substitute.For<IDbTemplatePruneService>();
		IDbTemplatePruneConsole console = Substitute.For<IDbTemplatePruneConsole>();
		IInteractiveConsole interactive = Substitute.For<IInteractiveConsole>();
		ILogger logger = Substitute.For<ILogger>();
		interactive.IsInteractive.Returns(true);
		service.GetEligibleServers().Returns(new DbTemplateServerListResult(true, ["pg-a", "pg-b"]));
		console.SelectServer(Arg.Any<IReadOnlyList<string>>()).Returns("pg-b");
		service.Inventory("pg-b").Returns(new DbTemplateInventoryResult(true, "pg-b", []));
		PruneDbTemplatesCommand command = new(service, console, interactive, logger);

		// Act
		int exitCode = command.Execute(new PruneDbTemplatesOptions());

		// Assert
		exitCode.Should().Be(0, because: "the inventory on the selected server completed successfully");
		service.Received(1).Inventory("pg-b");
	}

	[Test]
	[Category("Unit")]
	[Description("Cancels without inventory or deletion when Escape closes the server selection prompt.")]
	public void Execute_ShouldReturnSuccessWithoutMutation_WhenServerSelectionIsCancelled() {
		// Arrange
		IDbTemplatePruneService service = Substitute.For<IDbTemplatePruneService>();
		IDbTemplatePruneConsole console = Substitute.For<IDbTemplatePruneConsole>();
		IInteractiveConsole interactive = Substitute.For<IInteractiveConsole>();
		ILogger logger = Substitute.For<ILogger>();
		interactive.IsInteractive.Returns(true);
		service.GetEligibleServers().Returns(new DbTemplateServerListResult(true, ["pg-a", "pg-b"]));
		console.SelectServer(Arg.Any<IReadOnlyList<string>>()).Returns((string)null);
		PruneDbTemplatesCommand command = new(service, console, interactive, logger);

		// Act
		int exitCode = command.Execute(new PruneDbTemplatesOptions());

		// Assert
		exitCode.Should().Be(0, because: "Escape is an intentional safe cancellation");
		service.DidNotReceive().Inventory(Arg.Any<string>());
		console.DidNotReceive().ShowInventory(Arg.Any<IReadOnlyList<PostgresManagedTemplate>>());
		logger.Received(1).WriteInfo(Arg.Is<string>(message => message.Contains("cancelled")));
	}

	[Test]
	[Category("Unit")]
	[Description("Fails before inventory when the CLI has no interactive console.")]
	public void Execute_NonInteractive_FailsClosed() {
		// Arrange
		IDbTemplatePruneService service = Substitute.For<IDbTemplatePruneService>();
		IDbTemplatePruneConsole console = Substitute.For<IDbTemplatePruneConsole>();
		IInteractiveConsole interactive = Substitute.For<IInteractiveConsole>();
		ILogger logger = Substitute.For<ILogger>();
		interactive.IsInteractive.Returns(false);
		PruneDbTemplatesCommand command = new(service, console, interactive, logger);

		// Act
		int exitCode = command.Execute(new PruneDbTemplatesOptions { DbServerName = "local-pg" });

		// Assert
		exitCode.Should().Be(1, because: "the CLI must not guess selections or confirmation without a terminal");
		service.DidNotReceive().Inventory(Arg.Any<string>());
	}

	[Test]
	[Category("Unit")]
	[Description("Inventories, reviews, confirms, and deletes only the templates selected by the user.")]
	public void Execute_SelectedTemplate_ConfirmsAndPrunes() {
		// Arrange
		IDbTemplatePruneService service = Substitute.For<IDbTemplatePruneService>();
		IDbTemplatePruneConsole console = Substitute.For<IDbTemplatePruneConsole>();
		IInteractiveConsole interactive = Substitute.For<IInteractiveConsole>();
		ILogger logger = Substitute.For<ILogger>();
		PostgresManagedTemplate template = new("template-a", "Studio.zip", DateTimeOffset.UtcNow, "1.0");
		interactive.IsInteractive.Returns(true);
		interactive.Prompt(Arg.Any<string>()).Returns(true);
		service.Inventory("local-pg").Returns(new DbTemplateInventoryResult(true, "local-pg", [template]));
		console.SelectTemplates(Arg.Any<IReadOnlyList<PostgresManagedTemplate>>()).Returns(["template-a"]);
		ConfigureProgress(console);
		service.Prune("local-pg", Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<Action>()).Returns(new DbTemplatePruneResult(true,
			DbTemplatePruneService.CompleteSuccessStatus, "local-pg",
			[new DbTemplatePruneItemResult("template-a", DbTemplatePruneService.DeletedOutcome, "Deleted.")]));
		PruneDbTemplatesCommand command = new(service, console, interactive, logger);

		// Act
		int exitCode = command.Execute(new PruneDbTemplatesOptions { DbServerName = "local-pg" });

		// Assert
		exitCode.Should().Be(0, because: "the approved selected template was deleted successfully");
		interactive.Received(1).Prompt(Arg.Is<string>(message => message.Contains("local-pg")));
		service.Received(1).Prune("local-pg", Arg.Is<IReadOnlyCollection<string>>(names =>
			names.Count == 1 && System.Linq.Enumerable.Contains(names, "template-a")), Arg.Any<Action>());
	}

	[TestCase(false, 0)]
	[TestCase(true, 1)]
	[Category("Unit")]
	[Description("Performs no deletion for an empty selection or a declined final confirmation.")]
	public void Execute_SelectionNotApproved_DoesNotPrune(bool selectTemplate, int expectedPromptCount) {
		// Arrange
		IDbTemplatePruneService service = Substitute.For<IDbTemplatePruneService>();
		IDbTemplatePruneConsole console = Substitute.For<IDbTemplatePruneConsole>();
		IInteractiveConsole interactive = Substitute.For<IInteractiveConsole>();
		ILogger logger = Substitute.For<ILogger>();
		PostgresManagedTemplate template = new("template-a", "Studio.zip", DateTimeOffset.UtcNow, "1.0");
		interactive.IsInteractive.Returns(true);
		interactive.Prompt(Arg.Any<string>()).Returns(false);
		service.Inventory("local-pg").Returns(new DbTemplateInventoryResult(true, "local-pg", [template]));
		console.SelectTemplates(Arg.Any<IReadOnlyList<PostgresManagedTemplate>>())
			.Returns(selectTemplate ? ["template-a"] : Array.Empty<string>());
		PruneDbTemplatesCommand command = new(service, console, interactive, logger);

		// Act
		int exitCode = command.Execute(new PruneDbTemplatesOptions { DbServerName = "local-pg" });

		// Assert
		exitCode.Should().Be(0, because: "an unapproved destructive operation is a successful no-op");
		interactive.Received(expectedPromptCount).Prompt(Arg.Any<string>());
		if (!selectTemplate) {
			console.DidNotReceive().ShowSelection(Arg.Any<IReadOnlyList<PostgresManagedTemplate>>());
		}
		service.DidNotReceive().Prune(Arg.Any<string>(), Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<Action>());
	}

	[TestCase(DbTemplatePruneService.PartialFailureStatus)]
	[TestCase(DbTemplatePruneService.CompleteFailureStatus)]
	[Category("Unit")]
	[Description("Returns a failing exit code and renders the summary for incomplete pruning batches.")]
	public void Execute_IncompleteBatch_ReturnsFailure(string status) {
		// Arrange
		IDbTemplatePruneService service = Substitute.For<IDbTemplatePruneService>();
		IDbTemplatePruneConsole console = Substitute.For<IDbTemplatePruneConsole>();
		IInteractiveConsole interactive = Substitute.For<IInteractiveConsole>();
		ILogger logger = Substitute.For<ILogger>();
		PostgresManagedTemplate template = new("template-a", "Studio.zip", DateTimeOffset.UtcNow, "1.0");
		interactive.IsInteractive.Returns(true);
		interactive.Prompt(Arg.Any<string>()).Returns(true);
		service.Inventory("local-pg").Returns(new DbTemplateInventoryResult(true, "local-pg", [template]));
		console.SelectTemplates(Arg.Any<IReadOnlyList<PostgresManagedTemplate>>()).Returns(["template-a"]);
		DbTemplatePruneResult pruneResult = new(false, status, "local-pg",
			[new DbTemplatePruneItemResult("template-a", DbTemplatePruneService.FailedOutcome, "Failed.")]);
		ConfigureProgress(console);
		service.Prune("local-pg", Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<Action>()).Returns(pruneResult);
		PruneDbTemplatesCommand command = new(service, console, interactive, logger);

		// Act
		int exitCode = command.Execute(new PruneDbTemplatesOptions { DbServerName = "local-pg" });

		// Assert
		exitCode.Should().Be(1, because: "partial and complete batch failures must be visible to automation");
		console.Received(1).ShowSummary(pruneResult);
	}

	private static void ConfigureProgress(IDbTemplatePruneConsole console) {
		console.RunWithProgress(Arg.Any<int>(), Arg.Any<Func<Action, DbTemplatePruneResult>>())
			.Returns(callInfo => callInfo.ArgAt<Func<Action, DbTemplatePruneResult>>(1).Invoke(() => { }));
	}

	private static IAnsiConsole CreateTestConsole() {
		IAnsiConsole console = AnsiConsole.Create(new AnsiConsoleSettings {
			Ansi = AnsiSupport.Yes,
			ColorSystem = ColorSystemSupport.NoColors,
			Interactive = InteractionSupport.Yes,
			Out = new AnsiConsoleOutput(TextWriter.Null)
		});
		console.Profile.Width = 120;
		return console;
	}
}
