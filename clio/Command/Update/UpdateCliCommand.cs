using System;
using System.Threading.Tasks;
using Clio.Common;

namespace Clio.Command.Update;

/// <summary>
/// Command to update clio to the latest version with interactive confirmation.
/// </summary>
public class UpdateCliCommand : Command<UpdateCliOptions> {

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

			// Step 3: Display version information
			await _promptService.DisplayVersionInfoAsync(currentVersion, latestVersion);
			_logger.WriteInfo("An update is available!");

			// Step 4: Prompt user (unless --no-prompt is set)
			bool proceed = options.NoPrompt || await _promptService.PromptForConfirmationAsync(currentVersion, latestVersion);

			if (!proceed) {
				_logger.WriteInfo("Update cancelled.");
				return 1;
			}

			// Step 5: Execute update
			await _promptService.DisplayProgressAsync("Starting update...");
			int updateResult = await _appUpdater.ExecuteUpdateAsync(options.Global);

			if (updateResult != 0) {
				await _promptService.DisplayResultAsync(false, "Update failed. Please try again or run: dotnet tool update clio -g");
				return 1;
			}

			// Step 6: Verify installation
			await _promptService.DisplayProgressAsync("Verifying installation...");
			bool verified = await _appUpdater.VerifyInstallationAsync(latestVersion);

			if (!verified) {
				_logger.WriteWarning("Update completed, but verification failed.");
				_logger.WriteInfo("Please verify by running: clio --version");
				return 1;
			}

			// Step 7: Success
			await _promptService.DisplayResultAsync(true, $"Successfully updated to version {latestVersion}");
			return 0;

		} catch (Exception e) {
			_logger.WriteError($"Error during update: {e.Message}");
			return 1;
		}
	}

}
