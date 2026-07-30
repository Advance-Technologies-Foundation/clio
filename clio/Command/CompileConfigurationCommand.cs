using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using Clio.Common;
using Clio.CreatioModel;
using CommandLine;

namespace Clio.Command;

#region Class: CompileConfigurationOptions

[Verb("compile-configuration", Aliases = ["cc","compile-remote"], HelpText = "Compile configuration")]
public class CompileConfigurationOptions : RemoteCommandOptions
{

	[Option("all", Required = false, HelpText = "Compile configuration all", Default = false)]
	public bool All {
		get; set;
	}
	protected override int DefaultTimeout => Timeout.Infinite;

}

#endregion

#region Interface: CompileConfigurationCommand

public interface ICompileConfigurationCommand {
	int Execute(CompileConfigurationOptions options);

}

#endregion

#region Class: CompileConfigurationCommand

public class CompileConfigurationCommand : RemoteCommand<CompileConfigurationOptions>, ICompileConfigurationCommand {
	private readonly IServiceUrlBuilder _serviceUrlBuilder;
	private readonly ICompilationHistoryPoller _compilationHistoryPoller;
	private readonly ILogger _logger;
	private readonly IInteractiveConsole _interactiveConsole;

	private const string OdataProjName = "Terrasoft.Configuration.ODataEntities.csproj";
	private const string DevProjName = "Terrasoft.Configuration.Dev.csproj";

	/// <summary>
	/// Heavy-operation warning shown on the interactive CLI before a site compilation (ENG-93157). Paired
	/// with the <c>[Y/N]</c> prompt so the user can proceed now or postpone.
	/// </summary>
	internal const string SiteCompilationWarning =
		"WARNING: Compilation is a heavy operation. It recompiles the site configuration and forces a " +
		"runtime reload that may disrupt every user currently connected to this environment.";

	private bool _compileAll;

	private bool _isSuccess = false;

	#region Constructors: Public

	public CompileConfigurationCommand(IApplicationClient applicationClient,
		EnvironmentSettings settings, IServiceUrlBuilder serviceUrlBuilder,
		ICompilationHistoryPoller compilationHistoryPoller, ILogger logger,
		IInteractiveConsole interactiveConsole)
		: base(applicationClient, settings) {
		_serviceUrlBuilder = serviceUrlBuilder;
		_compilationHistoryPoller = compilationHistoryPoller;
		_logger = logger;
		_interactiveConsole = interactiveConsole;
	}

	#endregion

	protected override string ServicePath => _compileAll
		? _serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.CompileAll)
		: _serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.Compile);

	public override int Execute(CompileConfigurationOptions options) {
		if (!ConfirmCompilation(options)) {
			// The user chose to postpone: nothing is compiled and this is a deliberate choice, not an
			// error, so the command exits 0. Only reachable on an interactive terminal — non-interactive
			// hosts (the MCP server that runs this same command, CI, piped stdin) proceed without asking.
			return 0;
		}
		CompilationHistory baseline = _compilationHistoryPoller.GetBaseline();
		_compileAll = options.All;
		options.TimeOut = Timeout.Infinite;
		Stopwatch sw = new();
		sw.Start();
		_logger.WriteLine("=================================================================================");
		_logger.WriteInfo($"At: {DateTime.Now:HH:mm:ss} Starting compilation...");
		_logger.WriteLine();

		using CancellationTokenSource cts = new();
		Thread thread = new(() => {
			_compilationHistoryPoller.Poll(baseline?.CreatedOn ?? DateTime.MinValue, cts.Token, LogRecord);
		});
		thread.Start();

		//This will take a while to return, so we will check compilation history in parallel to get progress of compilation
		int execResult = base.Execute(options);
		sw.Stop();
		cts.Cancel();
		thread.Join(); // Wait for background thread to complete before disposing CancellationTokenSource
		if (CommandSuccess) {
			_logger.WriteLine();
			_logger.WriteInfo($"Compilation finished in {TimeOnly.FromTimeSpan(sw.Elapsed):HH:mm:ss}");
			_logger.WriteLine("=================================================================================");
		}
		return _isSuccess ? execResult : 1;
	}

	/// <summary>
	/// Warns the interactive user that compilation is a heavy operation and asks whether to proceed now
	/// or postpone (ENG-93157). Fails <b>open</b>: a non-interactive host (the MCP server, CI, redirected
	/// stdin) returns <see langword="true"/> without prompting, so the confirmed-compile behavior is
	/// unchanged for those callers. On the interactive CLI a declined prompt returns <see langword="false"/>
	/// after telling the user how to run the compilation later.
	/// </summary>
	private bool ConfirmCompilation(CompileConfigurationOptions options) {
		// --silent explicitly requests "default behavior without user interaction", so it must skip the
		// prompt and proceed even on an interactive terminal — otherwise scripted `clio cc --silent` runs
		// launched from a TTY would block or be postponed (review RC-1).
		if (options.IsSilent || _interactiveConsole.ConfirmOrProceedWhenNonInteractive(SiteCompilationWarning)) {
			return true;
		}
		_logger.WriteInfo(BuildPostponeHint(options));
		return false;
	}

	/// <summary>
	/// Builds the "how to run it later" hint shown when the user postpones the compilation, echoing the
	/// exact <c>clio cc</c> invocation (with environment and <c>--all</c>) that reproduces the request.
	/// </summary>
	private static string BuildPostponeHint(CompileConfigurationOptions options) {
		string environmentPart = string.IsNullOrWhiteSpace(options.Environment)
			? string.Empty
			: $" -e {options.Environment}";
		string allPart = options.All ? " --all" : string.Empty;
		return $"Compilation postponed. Nothing was compiled. Run it later with: clio cc{environmentPart}{allPart}";
	}

	private void LogRecord(CompilationHistory record) {
		string decoratedDuration = record.DurationInSeconds switch {
			>= 10 => ConsoleLogger.WrapRed(record.DurationInSeconds),
			>= 5 => ConsoleLogger.WrapYellow(record.DurationInSeconds),
			var _ => record.DurationInSeconds.ToString("N0", CultureInfo.InvariantCulture)
		};
		List<string> specialProj = [OdataProjName, DevProjName];
		string decoratedProjectName = record.ProjectName switch {
			{ } y when specialProj.Contains(y) => ConsoleLogger.WrapBlue(y) + ConsoleLogger.WrapGreen(" <============"),
			var _ => record.ProjectName
		};
		if (string.Equals(record.ErrorsWarnings, "[]", StringComparison.OrdinalIgnoreCase)) {
			_logger.WriteInfo($"At: {record.CreatedOn:HH:mm:ss} after: {decoratedDuration} sec. {decoratedProjectName}");
		} else {
			_logger.WriteWarning($"At: {record.CreatedOn:HH:mm:ss} after: {decoratedDuration} sec. {decoratedProjectName} with: {ParseErrors(record.ErrorsWarnings)}");
		}
	}

	private static readonly JsonSerializerOptions JsonSerializerOptions = new()
		{ PropertyNameCaseInsensitive = true };
	private static readonly Func<string, string> ParseErrors = (json) => {
		try {
			List<CompError> errors = JsonSerializer.Deserialize<List<CompError>>(json, JsonSerializerOptions);
			StringBuilder sb = new();
			int errorNumber = 1;
			foreach (string message in errors.Select(error => error switch {
				var _ when string.IsNullOrWhiteSpace(error.FileName) && error.IsWarning => $"({ConsoleLogger.WrapYellow(error.ErrorNumber)}): {error.ErrorText}",
				var _ when string.IsNullOrWhiteSpace(error.FileName) && !error.IsWarning => $"({ConsoleLogger.WrapRed(error.ErrorNumber)}): {error.ErrorText}",
				var _ when !string.IsNullOrWhiteSpace(error.FileName) && error.IsWarning => $"({ConsoleLogger.WrapYellow(error.ErrorNumber)}) in {ConsoleLogger.WrapYellow(error.FileName)} at ({error.Line},{error.Column}): {error.ErrorText}",
				var _ when !string.IsNullOrWhiteSpace(error.FileName) && !error.IsWarning => $"({ConsoleLogger.WrapRed(error.ErrorNumber)}) in {ConsoleLogger.WrapYellow(error.FileName)} at ({error.Line},{error.Column}) : {error.ErrorText}",
				var _ => json //We should never be here, this is to make compiler happy
			})) {
				sb.AppendLine().Append('\t').Append($"{errorNumber++} of {errors.Count} ").Append(message);
			}
			return sb.ToString();
		}
		// Could not parse errors, return original json
		catch {
			return json;
		}
	};

	protected override void ProceedResponse(string response, CompileConfigurationOptions options) {
		base.ProceedResponse(response, options);
		try {
			if (string.IsNullOrWhiteSpace(response)) {
				CommandSuccess = _isSuccess = false;
				Logger.WriteError("Empty response received from server during compilation.");
				Logger.WriteError($"Endpoint: {ServiceUri}");
				return;
			}

			string trimmed = response.TrimStart();
			if (trimmed.StartsWith("<", StringComparison.Ordinal)) {
				CommandSuccess = _isSuccess = false;
				Logger.WriteError("Server returned non-JSON response during compilation (looks like HTML).");
				Logger.WriteError($"Endpoint: {ServiceUri}");
				Logger.WriteError("Full response:");
				Logger.WriteLine(trimmed);
				Logger.WriteError("Check environment URI, IsNetCore flag, and credentials (a login/404 page is often returned as HTML).");
				return;
			}

			CreatioResponse model = JsonSerializer.Deserialize<CreatioResponse>(response);
			CommandSuccess = _isSuccess = model.Success;
			if (!model.Success) {
				Logger.WriteError($"{model.ErrorInfo.ErrorCode}: {model.ErrorInfo.Message}");
			}
		}
		catch (Exception e) {
			CommandSuccess = _isSuccess = false;
			Logger.WriteError(e.Message);
			Logger.WriteError($"Endpoint: {ServiceUri}");
			if (!string.IsNullOrWhiteSpace(response)) {
				Logger.WriteError("Full response:");
				Logger.WriteLine(response);
			}
		}
	}
}

#endregion


public class CreatioResponse
{
	[JsonPropertyName("errorInfo")]
	public ErrorInfo ErrorInfo { get; set; }

	[JsonPropertyName("success")]
	public bool Success { get; set; }

	[JsonPropertyName("buildResult")]
	public int BuildResult { get; set; }

	[JsonPropertyName("errors")]
	public object Errors { get; set; }

	[JsonPropertyName("message")]
	public object Message { get; set; }
}

public class ErrorInfo
{
	[JsonPropertyName("errorCode")]
	public string ErrorCode { get; set; }

	[JsonPropertyName("message")]
	public string Message { get; set; }

	[JsonPropertyName("stackTrace")]
	public object StackTrace { get; set; }
}

public class CompError
{
	[JsonPropertyName("Line")]
	public int Line { get; set; }

	[JsonPropertyName("Column")]
	public int Column { get; set; }

	[JsonPropertyName("ErrorNumber")]
	public string ErrorNumber { get; set; }

	[JsonPropertyName("ErrorText")]
	public string ErrorText { get; set; }

	[JsonPropertyName("IsWarning")]
	public bool IsWarning { get; set; }

	[JsonPropertyName("FileName")]
	public string FileName { get; set; }
}
