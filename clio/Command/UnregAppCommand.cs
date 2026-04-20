using System;
using System.Collections.Generic;
using System.Linq;
using Clio.Common;
using Clio.UserEnvironment;
using CommandLine;

namespace Clio.Command
{
	/// <summary>
	/// Command-line options for removing registered Creatio environments.
	/// </summary>
	[Verb("unreg-web-app", Aliases = ["unreg"], HelpText = "Remove a registered Creatio environment")]
	public class UnregAppOptions : EnvironmentOptions
	{
		/// <summary>
		/// Gets or sets the registered environment name to remove.
		/// </summary>
		[Value(0, MetaName = "Name", Required = false, HelpText = "Application name")]
		public string Name { get; set; }

		/// <summary>
		/// Gets or sets a value indicating whether all registered environments should be removed.
		/// </summary>
		[Option("all", Required = false, HelpText = "Remove all registered environments")]
		public bool UnregAll { get; set; }
	}

	/// <summary>
	/// Removes registered Creatio environments from local clio settings.
	/// </summary>
	public class UnregAppCommand : Command<UnregAppOptions>
	{
		private readonly ISettingsRepository _settingsRepository;
		private readonly ILogger _logger;

		public UnregAppCommand(ISettingsRepository settingsRepository, ILogger logger) {
			_settingsRepository = settingsRepository;
			_logger = logger;
		}

		/// <inheritdoc />
		public override int Execute(UnregAppOptions options) {
			try {
				if (options.UnregAll) {
					_settingsRepository.RemoveAllEnvironment();
					return 0;
				}
				if (!TryResolveEnvironmentName(options, out string environmentName, out int exitCode)) {
					return exitCode;
				}
				_settingsRepository.RemoveEnvironment(environmentName);
				_logger.WriteLine($"Environment {environmentName} was deleted...");
				_logger.WriteLine();
				_logger.WriteLine("Done");
				return 0;
			} catch (Exception e) {
				_logger.WriteError(e.Message);
				return 1;
			}
		}

		private bool TryResolveEnvironmentName(UnregAppOptions options, out string environmentName, out int exitCode) {
			environmentName = null;
			exitCode = 0;
			if (!string.IsNullOrWhiteSpace(options.Name)) {
				environmentName = options.Name;
				return true;
			}
			if (!string.IsNullOrWhiteSpace(options.Environment)) {
				environmentName = options.Environment;
				return true;
			}
			if (options.IsSilent) {
				_logger.WriteError("Environment name is required in --silent mode. Pass <Name>, -e/--Environment, or --all.");
				exitCode = 1;
				return false;
			}
			return TrySelectEnvironmentName(out environmentName, out exitCode);
		}

		private bool TrySelectEnvironmentName(out string environmentName, out int exitCode) {
			environmentName = null;
			exitCode = 0;
			List<KeyValuePair<string, EnvironmentSettings>> environments = _settingsRepository.GetAllEnvironments()
				.OrderBy(environment => environment.Key, StringComparer.OrdinalIgnoreCase)
				.ToList();
			if (environments.Count == 0) {
				_logger.WriteLine("No environments configured");
				return false;
			}
			string activeEnvironmentName = _settingsRepository.GetDefaultEnvironmentName();
			_logger.WriteLine("Registered environments:");
			for (int index = 0; index < environments.Count; index++) {
				KeyValuePair<string, EnvironmentSettings> environment = environments[index];
				_logger.WriteLine($"{index + 1}. {BuildEnvironmentListItem(environment.Key, environment.Value, activeEnvironmentName)}");
			}
#pragma warning disable CLIO002
			Console.Write("Select environment to remove (press Enter to cancel): ");
#pragma warning restore CLIO002
			string input = Console.ReadLine();
			if (string.IsNullOrWhiteSpace(input)) {
				_logger.WriteLine("Operation cancelled");
				return false;
			}
			if (!int.TryParse(input, out int selectedIndex) || selectedIndex < 1 || selectedIndex > environments.Count) {
				_logger.WriteError("Invalid selection. Enter a number from the list.");
				exitCode = 1;
				return false;
			}
			environmentName = environments[selectedIndex - 1].Key;
			return true;
		}

		private static string BuildEnvironmentListItem(string environmentName, EnvironmentSettings environmentSettings,
			string activeEnvironmentName) {
			string environmentDetails = environmentName;
			if (!string.IsNullOrWhiteSpace(environmentSettings?.Uri)) {
				environmentDetails = $"{environmentDetails} - {environmentSettings.Uri}";
			}
			if (string.Equals(environmentName, activeEnvironmentName, StringComparison.OrdinalIgnoreCase)) {
				environmentDetails = $"{environmentDetails} (active)";
			}
			return environmentDetails;
		}
	}
}
