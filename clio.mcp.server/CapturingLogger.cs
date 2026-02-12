using Clio.Common;
using ConsoleTables;
using FluentValidation.Results;

namespace Clio.McpServer;

internal sealed class CapturingLogger : ILogger {
	private readonly List<string> _logs = new();
	private readonly List<string> _errors = new();

	public IReadOnlyList<string> Logs => _logs;
	public IReadOnlyList<string> ErrorLogs => _errors;

	public void Start(string logFilePath = "") { }
	public void SetCreatioLogStreamer(ILogStreamer creatioLogStreamer) { }
	public void StartWithStream() { }
	public void Stop() { }

	public void Write(string value) {
		Add("LOG", value);
	}

	public void WriteLine() {
		Add("LOG", string.Empty);
	}

	public void WriteLine(string value) {
		Add("LOG", value);
	}

	public void WriteWarning(string value) {
		Add("WARN", value);
	}

	public void WriteError(string value) {
		_errors.Add(value ?? string.Empty);
		Add("ERROR", value);
	}

	public void WriteInfo(string value) {
		Add("INFO", value);
	}

	public void WriteDebug(string value) {
		Add("DEBUG", value);
	}

	public void PrintTable(ConsoleTable table) {
		Add("TABLE", table.ToString());
	}

	public void PrintValidationFailureErrors(IEnumerable<ValidationFailure> errors) {
		foreach (ValidationFailure error in errors) {
			WriteError($"{error.Severity} ({error.ErrorCode}) - {error.ErrorMessage}");
		}
	}

	private void Add(string level, string? value) {
		_logs.Add($"[{level}] {value ?? string.Empty}");
	}
}
