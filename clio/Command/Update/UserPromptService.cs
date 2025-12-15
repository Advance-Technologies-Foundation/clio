using System;
using System.Threading.Tasks;
using Clio.Common;

namespace Clio.Command.Update;

/// <summary>
/// Service for handling user prompts in interactive mode.
/// </summary>
public interface IUserPromptService {

	/// <summary>
	/// Displays version information to the user.
	/// </summary>
	/// <param name="currentVersion">Current installed version</param>
	/// <param name="latestVersion">Latest available version</param>
	Task DisplayVersionInfoAsync(string currentVersion, string latestVersion);

	/// <summary>
	/// Prompts the user for update confirmation.
	/// </summary>
	/// <param name="currentVersion">Current installed version</param>
	/// <param name="latestVersion">Latest available version</param>
	/// <returns>True if user confirms, false if user declines</returns>
	Task<bool> PromptForConfirmationAsync(string currentVersion, string latestVersion);

	/// <summary>
	/// Displays update progress message.
	/// </summary>
	/// <param name="message">Progress message to display</param>
	Task DisplayProgressAsync(string message);

	/// <summary>
	/// Displays update result message.
	/// </summary>
	/// <param name="success">True if update succeeded</param>
	/// <param name="message">Result message</param>
	Task DisplayResultAsync(bool success, string message);

}

/// <summary>
/// Implements user prompting for the update-cli command.
/// </summary>
public class UserPromptService : IUserPromptService {

	private readonly ILogger _logger;

	public UserPromptService(ILogger logger) {
		_logger = logger ?? throw new ArgumentNullException(nameof(logger));
	}

	public async Task DisplayVersionInfoAsync(string currentVersion, string latestVersion) {
		_logger.WriteLine("");
		_logger.WriteInfo($"Current version: {currentVersion}");
		_logger.WriteInfo($"Latest version:  {latestVersion}");
		_logger.WriteLine("");
		await Task.CompletedTask;
	}

	public async Task<bool> PromptForConfirmationAsync(string currentVersion, string latestVersion) {
		while (true) {
			_logger.Write("Would you like to update? (Y/n): ");
			string input = Console.ReadLine()?.Trim().ToLower() ?? string.Empty;

			// Default to yes if empty input (just pressing Enter)
			if (string.IsNullOrEmpty(input) || input == "y" || input == "yes") {
				return await Task.FromResult(true);
			}

			if (input == "n" || input == "no") {
				return await Task.FromResult(false);
			}

			_logger.WriteError("Invalid input. Please enter Y/yes or N/no.");
		}
	}

	public async Task DisplayProgressAsync(string message) {
		_logger.WriteInfo(message);
		await Task.CompletedTask;
	}

	public async Task DisplayResultAsync(bool success, string message) {
		if (success) {
			_logger.WriteInfo($"✓ {message}");
		} else {
			_logger.WriteError($"✗ {message}");
		}
		await Task.CompletedTask;
	}

}
