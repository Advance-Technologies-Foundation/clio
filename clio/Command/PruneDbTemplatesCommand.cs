using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Clio.Common;
using Clio.Common.db;
using CommandLine;
using Spectre.Console;

namespace Clio.Command;

/// <summary>Options for interactively pruning clio-managed PostgreSQL template databases.</summary>
[Verb("prune-db-templates", HelpText = "Interactively prune clio-managed PostgreSQL template databases")]
public sealed class PruneDbTemplatesOptions {
	/// <summary>Gets or sets the configured local PostgreSQL server name.</summary>
	[Option("db", Required = false, HelpText = "Configured local PostgreSQL server name")]
	public string DbServerName { get; set; }
}

/// <summary>Renders the Spectre.Console portions of the database-template pruning flow.</summary>
public interface IDbTemplatePruneConsole {
	/// <summary>Prompts the user to choose one configured server.</summary>
	/// <param name="serverNames">Eligible configured PostgreSQL server names.</param>
	/// <returns>The selected server name, or <see langword="null"/> when the user presses Escape.</returns>
	string SelectServer(IReadOnlyList<string> serverNames);

	/// <summary>Shows the managed-template inventory.</summary>
	/// <param name="templates">Templates to display.</param>
	void ShowInventory(IReadOnlyList<PostgresManagedTemplate> templates);

	/// <summary>Prompts the user to choose zero or more templates.</summary>
	/// <param name="templates">Eligible templates.</param>
	/// <returns>The selected database names, or an empty list when the user presses Escape.</returns>
	IReadOnlyList<string> SelectTemplates(IReadOnlyList<PostgresManagedTemplate> templates);

	/// <summary>Shows the complete deletion selection before confirmation.</summary>
	/// <param name="templates">Selected templates.</param>
	void ShowSelection(IReadOnlyList<PostgresManagedTemplate> templates);

	/// <summary>Runs a confirmed pruning operation while showing per-item progress.</summary>
	/// <param name="total">Number of selected templates.</param>
	/// <param name="operation">Operation that receives a callback to report one completed item.</param>
	/// <returns>The pruning result returned by the operation.</returns>
	DbTemplatePruneResult RunWithProgress(int total, Func<Action, DbTemplatePruneResult> operation);

	/// <summary>Shows every per-template deletion outcome.</summary>
	/// <param name="result">Structured pruning result.</param>
	void ShowSummary(DbTemplatePruneResult result);
}

/// <inheritdoc />
public sealed class DbTemplatePruneConsole : IDbTemplatePruneConsole {
	/// <inheritdoc />
	public string SelectServer(IReadOnlyList<string> serverNames) =>
		AnsiConsole.Prompt(BuildServerSelectionPrompt(serverNames));

	/// <inheritdoc />
	public void ShowInventory(IReadOnlyList<PostgresManagedTemplate> templates) {
		AnsiConsole.Write(BuildTemplateTable("Managed PostgreSQL templates",
			OrderTemplatesForSelection(templates)));
	}

	/// <inheritdoc />
	public IReadOnlyList<string> SelectTemplates(IReadOnlyList<PostgresManagedTemplate> templates) {
		return AnsiConsole.Prompt(BuildTemplateSelectionPrompt(templates));
	}

	internal static SelectionPrompt<string> BuildServerSelectionPrompt(IReadOnlyList<string> serverNames) =>
		new SelectionPrompt<string>()
			.Title("Select a configured PostgreSQL server [grey](press [blue]<esc>[/] to cancel)[/]:")
			.AddCancelResult(() => null)
			.UseConverter(EscapeForTerminal)
			.AddChoices(serverNames);

	internal static MultiSelectionPrompt<string> BuildTemplateSelectionPrompt(
		IReadOnlyList<PostgresManagedTemplate> templates) {
		Dictionary<string, PostgresManagedTemplate> byName = templates.ToDictionary(
			template => template.DatabaseName, StringComparer.Ordinal);
		IReadOnlyList<PostgresManagedTemplate> orderedTemplates = OrderTemplatesForSelection(templates);
		return new MultiSelectionPrompt<string>()
				.Title("Select templates to delete:")
				.NotRequired()
				.AddCancelResult()
				.PageSize(15)
				.MoreChoicesText("[grey](Move up and down to reveal more templates)[/]")
				.InstructionsText(
					"[grey](Press [blue]<space>[/] to select, [green]<enter>[/] to accept, [blue]<esc>[/] to cancel)[/]")
				.UseConverter(name => FormatTemplateChoice(byName[name]))
				.AddChoices(orderedTemplates.Select(template => template.DatabaseName));
	}

	/// <inheritdoc />
	public void ShowSelection(IReadOnlyList<PostgresManagedTemplate> templates) {
		AnsiConsole.Write(BuildTemplateTable("Templates selected for deletion", templates));
	}

	/// <inheritdoc />
	public DbTemplatePruneResult RunWithProgress(int total,
		Func<Action, DbTemplatePruneResult> operation) => RunWithProgress(AnsiConsole.Console, total, operation);

	internal static DbTemplatePruneResult RunWithProgress(IAnsiConsole console, int total,
		Func<Action, DbTemplatePruneResult> operation) {
		Progress progress = console.Progress()
			.AutoClear(false)
			.HideCompleted(false)
			.Columns(
			new TaskDescriptionColumn(),
			new ProgressBarColumn(),
			new PercentageColumn(),
			new SpinnerColumn());
		return progress.Start(context => {
			ProgressTask task = context.AddTask("Deleting PostgreSQL templates", maxValue: total);
			try {
				DbTemplatePruneResult result = operation(() => {
					try {
						task.Increment(1);
					}
					catch (InvalidOperationException) {
						// Progress rendering is cosmetic and must not change a deletion outcome.
					}
				});
				task.Value = task.MaxValue;
				return result;
			}
			finally {
				task.StopTask();
			}
		});
	}

	/// <inheritdoc />
	public void ShowSummary(DbTemplatePruneResult result) {
		Table table = new Table()
			.Title("Template pruning results")
			.Border(TableBorder.Rounded)
			.AddColumn("Database")
			.AddColumn("Outcome")
			.AddColumn("Details");
		foreach (DbTemplatePruneItemResult item in result.Results) {
			string color = item.Outcome switch {
				DbTemplatePruneService.DeletedOutcome => "green",
				DbTemplatePruneService.SkippedOutcome => "yellow",
				_ => "red"
			};
			table.AddRow(
				EscapeForTerminal(item.DatabaseName),
				$"[{color}]{EscapeForTerminal(item.Outcome)}[/]",
				EscapeForTerminal(item.Message));
		}
		AnsiConsole.Write(table);
		AnsiConsole.MarkupLine($"Overall result: [bold]{Markup.Escape(result.Status)}[/]");
	}

	internal static Table BuildTemplateTable(string title, IReadOnlyList<PostgresManagedTemplate> templates) {
		Table table = new Table()
			.Title(title)
			.Border(TableBorder.Rounded)
			.AddColumn("Source")
			.AddColumn("Database")
			.AddColumn("Created");
		foreach (PostgresManagedTemplate template in templates) {
			table.AddRow(
				EscapeForTerminal(template.SourceFile),
				EscapeForTerminal(template.DatabaseName),
				EscapeForTerminal(template.CreatedDate.ToString("u", CultureInfo.InvariantCulture)));
		}
		return table;
	}

	internal static IReadOnlyList<PostgresManagedTemplate> OrderTemplatesForSelection(
		IReadOnlyList<PostgresManagedTemplate> templates) => templates
		.Select(template => (Template: template, Version: ParseCreatioVersion(template.SourceFile)))
		.OrderBy(item => item.Version is null)
		.ThenBy(item => item.Version)
		.ThenBy(item => item.Template.SourceFile, StringComparer.OrdinalIgnoreCase)
		.ThenBy(item => item.Template.DatabaseName, StringComparer.Ordinal)
		.Select(item => item.Template)
		.ToArray();

	internal static string FormatTemplateChoice(PostgresManagedTemplate template) =>
		EscapeForTerminal(template.SourceFile);

	private static Version ParseCreatioVersion(string sourceFile) {
		string fileName = (sourceFile ?? string.Empty).Split(['/', '\\']).LastOrDefault() ?? string.Empty;
		string versionText = fileName.Split('_', 2)[0];
		return Version.TryParse(versionText, out Version version) ? version : null;
	}

	internal static string EscapeControlCharacters(string value) => string.Concat(
		(value ?? string.Empty).Select(character => char.IsControl(character)
			? $"\\u{(int)character:X4}"
			: character.ToString()));

	private static string EscapeForTerminal(string value) => Markup.Escape(EscapeControlCharacters(value));
}

/// <summary>Runs the interactive managed-template pruning workflow.</summary>
public sealed class PruneDbTemplatesCommand(
	IDbTemplatePruneService pruneService,
	IDbTemplatePruneConsole pruneConsole,
	IInteractiveConsole interactiveConsole,
	ILogger logger) : Command<PruneDbTemplatesOptions> {
	private readonly IDbTemplatePruneService _pruneService = pruneService
		?? throw new ArgumentNullException(nameof(pruneService));
	private readonly IDbTemplatePruneConsole _pruneConsole = pruneConsole
		?? throw new ArgumentNullException(nameof(pruneConsole));
	private readonly IInteractiveConsole _interactiveConsole = interactiveConsole
		?? throw new ArgumentNullException(nameof(interactiveConsole));
	private readonly ILogger _logger = logger ?? throw new ArgumentNullException(nameof(logger));

	/// <inheritdoc />
	public override int Execute(PruneDbTemplatesOptions options) {
		if (!_interactiveConsole.IsInteractive) {
			_logger.WriteError(
				"prune-db-templates requires an interactive console. Use the list-db-templates and prune-db-templates MCP tools for automation.");
			return 1;
		}

		string dbServerName = ResolveServerName(options.DbServerName, out bool serverSelectionCancelled);
		if (serverSelectionCancelled) {
			_logger.WriteInfo("Server selection was cancelled; nothing was deleted.");
			return 0;
		}
		if (string.IsNullOrWhiteSpace(dbServerName)) {
			return 1;
		}
		DbTemplateInventoryResult inventory = _pruneService.Inventory(dbServerName);
		if (!inventory.Success) {
			_logger.WriteError(inventory.Error);
			return 1;
		}
		if (inventory.Templates.Count == 0) {
			_logger.WriteInfo($"No clio-managed PostgreSQL templates were found on '{dbServerName}'.");
			return 0;
		}

		_pruneConsole.ShowInventory(inventory.Templates);
		IReadOnlyList<string> selectedNames = _pruneConsole.SelectTemplates(inventory.Templates);
		if (selectedNames.Count == 0) {
			_logger.WriteInfo("No templates were selected; nothing was deleted.");
			return 0;
		}
		IReadOnlyList<PostgresManagedTemplate> selectedTemplates = inventory.Templates
			.Where(template => selectedNames.Contains(template.DatabaseName, StringComparer.Ordinal))
			.ToArray();
		_pruneConsole.ShowSelection(selectedTemplates);
		string safeServerName = DbTemplatePruneConsole.EscapeControlCharacters(dbServerName);
		if (!_interactiveConsole.Prompt(
			$"Delete the {selectedTemplates.Count} selected PostgreSQL template database(s) from server '{safeServerName}'? Press Esc to cancel.")) {
			_logger.WriteInfo("Template deletion was cancelled; nothing was deleted.");
			return 0;
		}

		DbTemplatePruneResult result = _pruneConsole.RunWithProgress(selectedNames.Count,
			itemCompleted => _pruneService.Prune(dbServerName, selectedNames, itemCompleted));
		_pruneConsole.ShowSummary(result);
		if (!result.Success) {
			_logger.WriteError($"Template pruning finished with status '{result.Status}'.");
			return 1;
		}
		_logger.WriteInfo("All selected PostgreSQL templates were deleted successfully.");
		return 0;
	}

	private string ResolveServerName(string requestedName, out bool cancelled) {
		cancelled = false;
		if (!string.IsNullOrWhiteSpace(requestedName)) {
			return requestedName;
		}
		DbTemplateServerListResult servers = _pruneService.GetEligibleServers();
		if (!servers.Success) {
			_logger.WriteError(servers.Error);
			return null;
		}
		if (servers.Servers.Count == 0) {
			_logger.WriteError("No enabled PostgreSQL servers are configured in clio settings.");
			return null;
		}
		if (servers.Servers.Count == 1) {
			return servers.Servers[0];
		}
		string selectedServer = _pruneConsole.SelectServer(servers.Servers);
		cancelled = selectedServer is null;
		return selectedServer;
	}
}
