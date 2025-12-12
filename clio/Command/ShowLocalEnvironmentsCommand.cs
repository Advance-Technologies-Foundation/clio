using System;
using System.Collections.Generic;
using System.Linq;
using Clio.Common;
using Clio.UserEnvironment;
using CommandLine;
using ConsoleTables;
using System.Text;
using System.Text.RegularExpressions;
using IFileSystem = System.IO.Abstractions.IFileSystem;

namespace Clio.Command
{
	[Verb("show-local-envs", HelpText = "Show local environments with filesystem and auth status")]
	public class ShowLocalEnvironmentsOptions
	{
	}

	public class ShowLocalEnvironmentsCommand : Command<ShowLocalEnvironmentsOptions>
	{
		private const int PingTimeoutMs = 5_000;
		private readonly ISettingsRepository _settingsRepository;
		private readonly IApplicationClientFactory _applicationClientFactory;
		private readonly IFileSystem _fileSystem;
		private readonly ILogger _logger;

		public ShowLocalEnvironmentsCommand(
			ISettingsRepository settingsRepository,
			IApplicationClientFactory applicationClientFactory,
			IFileSystem fileSystem,
			ILogger logger) {
			_settingsRepository = settingsRepository;
			_applicationClientFactory = applicationClientFactory;
			_fileSystem = fileSystem;
			_logger = logger;
		}

		public override int Execute(ShowLocalEnvironmentsOptions options) {
			Dictionary<string, EnvironmentSettings> environments = _settingsRepository.GetAllEnvironments();
			List<KeyValuePair<string, EnvironmentSettings>> localEnvironments = environments
				.Where(pair => !string.IsNullOrWhiteSpace(pair.Value.EnvironmentPath))
				.ToList();

			if (!localEnvironments.Any()) {
				_logger.WriteInfo("No local environments with EnvironmentPath configured.");
				return 0;
			}

			List<string[]> rows = new();
			foreach (KeyValuePair<string, EnvironmentSettings> environment in localEnvironments) {
				LocalEnvironmentResult result = EvaluateEnvironment(environment.Value);
				rows.Add(new[] {
					environment.Key,
					FormatStatus(result.Status),
					environment.Value.Uri ?? string.Empty,
					environment.Value.EnvironmentPath ?? string.Empty,
					result.Reason
				});
			}

			string table = RenderAnsiAwareTable(new[] { "Name", "Status", "Url", "Path", "Reason" }, rows);
			_logger.Write(table);
			return 0;
		}

		private string RenderAnsiAwareTable(string[] headers, List<string[]> rows) {
			int columnCount = headers.Length;
			int[] widths = new int[columnCount];

			for (int i = 0; i < columnCount; i++) {
				widths[i] = headers[i].Length;
			}

			foreach (string[] row in rows) {
				for (int i = 0; i < columnCount; i++) {
					int cellLength = StripAnsi(row[i]).Length;
					if (cellLength > widths[i]) {
						widths[i] = cellLength;
					}
				}
			}

			string divider = "+" + string.Join("+", widths.Select(w => new string('-', w + 2))) + "+";
			StringBuilder sb = new();
			sb.AppendLine(divider);
			sb.AppendLine("| " + string.Join(" | ", headers.Select((h, i) => h.PadRight(widths[i]))) + " |");
			sb.AppendLine(divider);
			foreach (string[] row in rows) {
				sb.AppendLine("| " + string.Join(" | ", row.Select((cell, i) => PadVisible(cell, widths[i]))) + " |");
			}
			sb.AppendLine(divider);
			return sb.ToString();
		}

		private static string PadVisible(string value, int width) {
			string plain = StripAnsi(value);
			int padding = Math.Max(0, width - plain.Length);
			return value + new string(' ', padding);
		}

		private static string StripAnsi(string value) {
			return AnsiRegex.Replace(value ?? string.Empty, string.Empty);
		}

		private LocalEnvironmentResult EvaluateEnvironment(EnvironmentSettings environment) {
			DirectoryState directoryState = EvaluateDirectory(environment.EnvironmentPath);
			if (directoryState.IsDeleted) {
				return new LocalEnvironmentResult(LocalEnvironmentStatus.Deleted, directoryState.Reason);
			}

			OperationResult pingResult = TryPing(environment);
			if (pingResult.Success) {
				OperationResult loginResult = TryLogin(environment);
				return loginResult.Success
					? new LocalEnvironmentResult(LocalEnvironmentStatus.Ok, "healthy")
					: new LocalEnvironmentResult(LocalEnvironmentStatus.ErrorAuthData, loginResult.Reason);
			}

			return new LocalEnvironmentResult(LocalEnvironmentStatus.NotRunned, pingResult.Reason);
		}

		private DirectoryState EvaluateDirectory(string path) {
			if (string.IsNullOrWhiteSpace(path)) {
				return DirectoryState.Deleted("environment path is empty");
			}

			try {
				if (!_fileSystem.Directory.Exists(path)) {
					return DirectoryState.Deleted("directory not found");
				}

				List<string> entries = _fileSystem.Directory.EnumerateFileSystemEntries(path).ToList();
				bool hasContentOutsideLogs = entries.Any(entry => !IsLogsPath(path, entry));
				if (!hasContentOutsideLogs) {
					return DirectoryState.Deleted("directory contains only Logs");
				}

				return DirectoryState.Present();
			} catch (UnauthorizedAccessException) {
				return DirectoryState.Deleted("access denied");
			} catch (Exception ex) {
				return DirectoryState.Deleted(Summarize(ex.Message, "failed to inspect directory"));
			}
		}

		private OperationResult TryPing(EnvironmentSettings environment) {
			try {
				IApplicationClient client = _applicationClientFactory.CreateEnvironmentClient(environment);
				string baseUri = (environment.Uri ?? string.Empty).TrimEnd('/');
				string root = environment.IsNetCore ? baseUri : $"{baseUri}/0";
				string url = $"{root}/ping";

				if (environment.IsNetCore) {
					client.ExecuteGetRequest(url, PingTimeoutMs, 1, 1);
				} else {
					client.ExecutePostRequest(url, "{}", PingTimeoutMs, 1, 1);
				}

				return OperationResult.CreateSuccess();
			} catch (Exception ex) {
				return OperationResult.CreateFail(Summarize(ex.Message, "ping failed"));
			}
		}

		private OperationResult TryLogin(EnvironmentSettings environment) {
			try {
				IApplicationClient client = _applicationClientFactory.CreateClient(environment);
				client.Login();
				return OperationResult.CreateSuccess();
			} catch (Exception ex) {
				return OperationResult.CreateFail(Summarize(ex.Message, "login failed"));
			}
		}

		private bool IsLogsPath(string rootPath, string entryPath) {
			string logsPath = _fileSystem.Path.Combine(rootPath, "Logs");
			string normalizedEntry = NormalizePath(entryPath);
			string normalizedLogs = NormalizePath(logsPath);
			return string.Equals(normalizedEntry, normalizedLogs, StringComparison.OrdinalIgnoreCase);
		}

		private string NormalizePath(string path) {
			return _fileSystem.Path.GetFullPath(path).TrimEnd(
				_fileSystem.Path.DirectorySeparatorChar,
				_fileSystem.Path.AltDirectorySeparatorChar);
		}

		private string FormatStatus(LocalEnvironmentStatus status) {
			(string label, string color) = status switch {
				LocalEnvironmentStatus.Ok => ("OK", AnsiColor.Green),
				LocalEnvironmentStatus.ErrorAuthData => ("Error Auth data", AnsiColor.Red),
				LocalEnvironmentStatus.Deleted => ("Deleted", AnsiColor.Yellow),
				LocalEnvironmentStatus.NotRunned => ("Not runned", AnsiColor.Cyan),
				_ => ("Unknown", AnsiColor.Reset)
			};
			return $"{color}[{label}]{AnsiColor.Reset}";
		}

		private static string Summarize(string message, string fallback) {
			if (string.IsNullOrWhiteSpace(message)) {
				return fallback;
			}
			return message.Replace(Environment.NewLine, " ").Trim();
		}

		private record DirectoryState(bool IsDeleted, string Reason) {
			public static DirectoryState Deleted(string reason) => new DirectoryState(true, reason);
			public static DirectoryState Present() => new DirectoryState(false, string.Empty);
		}

		private record OperationResult(bool Success, string Reason) {
			public static OperationResult CreateSuccess() => new OperationResult(true, string.Empty);
			public static OperationResult CreateFail(string reason) => new OperationResult(false, reason);
		}

		private record LocalEnvironmentResult(LocalEnvironmentStatus Status, string Reason);

		private enum LocalEnvironmentStatus {
			Ok,
			ErrorAuthData,
			Deleted,
			NotRunned
		}

		private static class AnsiColor {
			public const string Green = "\u001b[32m";
			public const string Red = "\u001b[31m";
			public const string Yellow = "\u001b[33m";
			public const string Cyan = "\u001b[36m";
			public const string Reset = "\u001b[0m";
		}

		private static readonly Regex AnsiRegex = new("\u001B\\[[0-?]*[ -/]*[@-~]", RegexOptions.Compiled);
	}
}
