using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Clio.Common;
using CommandLine;
using Spectre.Console;

namespace Clio.Command.Update;


/// <summary>
/// Options for the update-cli command to update clio to the latest version.
/// </summary>
[Verb("update-cli", Aliases = ["update"], HelpText = "Update clio to the latest available version")]
public class UpdateCliOptions : EnvironmentOptions {

	/// <summary>
	/// Install the tool globally (default: true).
	/// </summary>
	[Option('g', "global", Required = false, Default = true,
		HelpText = "Install clio globally")]
	public bool Global { get; set; }

	/// <summary>
	/// Skip the interactive confirmation prompt and proceed with update (default: true).
	/// </summary>
	[Option('y', "no-prompt", Required = false, Default = true,
		HelpText = "Proceed with update automatically without confirmation (default behavior)")]
	public bool NoPrompt { get; set; } = true;

}


/// <summary>
/// Command to update clio to the latest version automatically without confirmation by default.
/// </summary>
public class UpdateCliCommand : Command<UpdateCliOptions> {

	private const string GitHubRepoUrl = "https://github.com/Advance-Technologies-Foundation/clio";

	private readonly IAppUpdater _appUpdater;
	private readonly IUserPromptService _promptService;
	private readonly ILogger _logger;

	public UpdateCliCommand(IAppUpdater appUpdater, IUserPromptService promptService, ILogger logger) {
		_appUpdater = appUpdater ?? throw new ArgumentNullException(nameof(appUpdater));
		_promptService = promptService ?? throw new ArgumentNullException(nameof(promptService));
		_logger = logger ?? throw new ArgumentNullException(nameof(logger));
	}

	public override int Execute(UpdateCliOptions options) {
		return ExecuteAsync(options).GetAwaiter().GetResult();
	}

	private async Task<int> ExecuteAsync(UpdateCliOptions options) {
		try {
			_logger.WriteInfo("Checking for updates...");

			// Step 1: Get current and latest versions
			string currentVersion = _appUpdater.GetCurrentVersion();
			string latestVersion = _appUpdater.GetLatestVersionFromNuget();

			if (string.IsNullOrEmpty(currentVersion) || string.IsNullOrEmpty(latestVersion)) {
				_logger.WriteError("Unable to check for updates. Please check your internet connection.");
				return 2;
			}

			// Step 2: Check if update is available
			bool updateAvailable = await _appUpdater.IsUpdateAvailableAsync();

			if (!updateAvailable) {
				_logger.WriteInfo("You already have the latest version!");
				return 0;
			}

			// Step 3: Display version information with update type
			string updateType = _appUpdater.GetUpdateType(currentVersion, latestVersion);
			await _promptService.DisplayVersionInfoAsync(currentVersion, latestVersion);
			_logger.WriteInfo($"An update is available! ({updateType})");

			// Step 4: Proceed automatically (default) or prompt user if needed
			bool proceed = options.NoPrompt || await _promptService.PromptForConfirmationAsync(currentVersion, latestVersion);

			if (!proceed) {
				_logger.WriteInfo("Update cancelled.");
				return 1;
			}

			// Step 5: Execute update with spinner and timer
			var stopwatch = Stopwatch.StartNew();
			int updateResult = 1;
			bool verified = false;

			await AnsiConsole.Status()
				.AutoRefresh(true)
				.Spinner(Spinner.Known.Dots)
				.SpinnerStyle(Style.Parse("green"))
				.StartAsync("Updating clio...", async ctx => {
					updateResult = await _appUpdater.ExecuteUpdateAsync(options.Global);

					if (updateResult == 0) {
						// Step 6: Verify installation
						ctx.Status("Verifying installation...");
						verified = await _appUpdater.VerifyInstallationAsync(latestVersion);
					}
				});

			stopwatch.Stop();
			string elapsed = $"{stopwatch.Elapsed.TotalSeconds:F1}s";

			if (updateResult != 0) {
				await _promptService.DisplayResultAsync(false, "Update failed. Please try again or run: dotnet tool update clio -g");
				return 1;
			}

			if (!verified) {
				_logger.WriteWarning("Update completed, but verification failed.");
				_logger.WriteInfo("Please verify by running: clio info --clio");
				return 1;
			}

			// Step 7: Success
			await _promptService.DisplayResultAsync(true, $"Successfully updated to version {latestVersion} ({elapsed})");

			// Step 8: Show diff link
			_logger.WriteLine($"  Compare: {GitHubRepoUrl}/compare/{currentVersion}...{latestVersion}");

			// Step 9: Show release notes
			await DisplayReleaseNotesAsync(latestVersion);

			return 0;

		} catch (Exception e) {
			_logger.WriteError($"Error during update: {e.Message}");
			return 1;
		}
	}

	private async Task DisplayReleaseNotesAsync(string version) {
		try {
			string notes = await _appUpdater.GetReleaseNotesAsync(version);
			if (string.IsNullOrEmpty(notes)) {
				return;
			}

			_logger.WriteLine("");
			_logger.WriteLine("  What's new:");
			foreach (string line in notes.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)) {
				string trimmed = line.Trim();
				if (!string.IsNullOrWhiteSpace(trimmed)) {
					// Preserve markdown list items, prefix others with bullet
					if (trimmed.StartsWith("-") || trimmed.StartsWith("*") || trimmed.StartsWith("•")) {
						_logger.WriteLine($"  • {trimmed.TrimStart('-', '*', '•').Trim()}");
					} else {
						_logger.WriteLine($"  {trimmed}");
					}
				}
			}
			_logger.WriteLine($"  Full changelog: {GitHubRepoUrl}/releases/tag/{version}");
		} catch {
			// Release notes are optional — don't fail the update
		}
	}

}
