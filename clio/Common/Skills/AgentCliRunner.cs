using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Clio.Common.Skills;

/// <summary>
/// Default <see cref="IAgentCliRunner"/> over <see cref="IProcessExecutor"/>.
/// </summary>
public sealed class AgentCliRunner(IProcessExecutor processExecutor, IFileSystem fileSystem) : IAgentCliRunner {
	private readonly IProcessExecutor _processExecutor = processExecutor;
	private readonly IFileSystem _fileSystem = fileSystem;

	/// <inheritdoc />
	public bool IsOnPath(string cliName) => ResolveExecutable(cliName) is not null;

	/// <inheritdoc />
	public AgentCliResult Run(string cliName, params string[] args) {
		cliName.CheckArgumentNullOrWhiteSpace(nameof(cliName));
		string resolved = ResolveExecutable(cliName);
		if (resolved is null) {
			return new AgentCliResult(false, null, string.Empty,
				$"'{cliName}' was not found on PATH.");
		}

		(string program, string arguments) = BuildCommand(resolved, args ?? []);
		ProcessExecutionResult result = _processExecutor
			.ExecuteAndCaptureAsync(new ProcessExecutionOptions(program, arguments))
			.GetAwaiter()
			.GetResult();

		bool succeeded = result.Started && result.ExitCode == 0 && !result.Canceled && !result.TimedOut;
		return new AgentCliResult(succeeded, result.ExitCode, result.StandardOutput, result.StandardError);
	}

	/// <summary>
	/// Resolves <paramref name="cliName"/> against PATH (honoring PATHEXT plus
	/// <c>.ps1</c>), returning the full executable path or <c>null</c>.
	/// </summary>
	private string ResolveExecutable(string cliName) {
		if (string.IsNullOrWhiteSpace(cliName)) {
			return null;
		}

		cliName = cliName.Trim();

		// An explicit path with an extension that exists is used as-is.
		if ((cliName.Contains('/', StringComparison.Ordinal) || cliName.Contains('\\', StringComparison.Ordinal))
			&& Path.HasExtension(cliName) && _fileSystem.ExistsFile(cliName)) {
			return _fileSystem.GetFullPath(cliName);
		}

		string pathVariable = Environment.GetEnvironmentVariable("PATH");
		if (string.IsNullOrWhiteSpace(pathVariable)) {
			return null;
		}

		string[] pathDirectories = pathVariable.Split(Path.PathSeparator,
			StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
		foreach (string directory in pathDirectories) {
			foreach (string candidate in CandidateFileNames(cliName)) {
				string fullPath = _fileSystem.Combine(directory, candidate);
				if (_fileSystem.ExistsFile(fullPath)) {
					return _fileSystem.GetFullPath(fullPath);
				}
			}
		}

		return null;
	}

	/// <summary>
	/// Builds the candidate file names for a CLI. On Unix this is the bare name. On
	/// Windows it is each PATHEXT extension (plus <c>.ps1</c>) — never the bare
	/// extensionless file, which is typically an npm shell shim that CreateProcess
	/// cannot launch (and would surface as "not a valid application for this OS").
	/// </summary>
	internal static IEnumerable<string> CandidateFileNames(string cliName) {
		if (Path.HasExtension(cliName)) {
			yield return cliName;
			yield break;
		}

		if (!OperatingSystem.IsWindows()) {
			yield return cliName;
			yield break;
		}

		string pathExt = Environment.GetEnvironmentVariable("PATHEXT") ?? ".COM;.EXE;.BAT;.CMD";
		IEnumerable<string> extensions = pathExt
			.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
			.Append(".PS1");
		foreach (string extension in extensions.Distinct(StringComparer.OrdinalIgnoreCase)) {
			yield return cliName + extension.ToLowerInvariant();
		}
	}

	/// <summary>
	/// Builds the launcher program and full argument string for a resolved executable.
	/// A <c>.ps1</c> launcher runs through PowerShell; a <c>.cmd</c>/<c>.bat</c> launcher
	/// (e.g. an npm shim) runs through <c>cmd.exe /s /c "…"</c> with every token
	/// force-quoted so <c>cmd</c> metacharacters (<c>&amp; | &lt; &gt; ^</c>) in an argument
	/// (such as a marketplace URL) are not interpreted; everything else launches directly.
	/// </summary>
	internal static (string Program, string Arguments) BuildCommand(string resolvedPath, IReadOnlyList<string> args) {
		string extension = Path.GetExtension(resolvedPath);

		if (string.Equals(extension, ".ps1", StringComparison.OrdinalIgnoreCase)) {
			List<string> powershellArgs = ["-ExecutionPolicy", "Bypass", "-File", resolvedPath, .. args];
			return ("powershell", BuildArgumentString(powershellArgs));
		}

		if (string.Equals(extension, ".cmd", StringComparison.OrdinalIgnoreCase)
			|| string.Equals(extension, ".bat", StringComparison.OrdinalIgnoreCase)) {
			List<string> tokens = [resolvedPath, .. args];
			// `/s /c "<line>"` makes cmd strip exactly the outer quote pair and run the
			// rest verbatim; force-quoting each token keeps metacharacters inert.
			string inner = string.Join(' ', tokens.Select(ForceQuote));
			return ("cmd.exe", $"/s /c \"{inner}\"");
		}

		return (resolvedPath, BuildArgumentString(args));
	}

	private static string BuildArgumentString(IEnumerable<string> args) =>
		string.Join(' ', args.Select(Quote));

	private static string Quote(string value) {
		if (string.IsNullOrEmpty(value)) {
			return "\"\"";
		}

		return value.IndexOfAny([' ', '\t', '"']) < 0
			? value
			: $"\"{value.Replace("\"", "\\\"", StringComparison.Ordinal)}\"";
	}

	private static string ForceQuote(string value) =>
		$"\"{(value ?? string.Empty).Replace("\"", "\\\"", StringComparison.Ordinal)}\"";
}
