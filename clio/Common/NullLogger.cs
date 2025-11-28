using System;
using System.Collections.Generic;
using ConsoleTables;
using FluentValidation.Results;

namespace Clio.Common;

/// <summary>
/// Logger that discards all log messages. Used for silent operations.
/// </summary>
public class NullLogger : ILogger
{
	public static readonly ILogger Instance = new NullLogger();

	private NullLogger() { }

	public void Start(string logFilePath = "") { }
	public void StartWithStream() { }
	public void Stop() { }
	public void Write(string value) { }
	public void WriteLine() { }
	public void WriteLine(string value) { }
	public void WriteWarning(string value) { }
	public void WriteError(string value) { }
	public void WriteInfo(string value) { }
	public void WriteDebug(string value) { }
	public void WriteDebug(string message, Dictionary<string, object> metadata) { }
	public void PrintTable(ConsoleTable table) { }
	public void PrintValidationFailureErrors(IEnumerable<ValidationFailure> errors) { }
	public void Dispose() { }
	public void PrintError(Exception error) { }
	public void PrintValidationResults(List<ValidationFailure> failures) { }
	public void SetOutputToFile(string fileName) { }
	public void SetCreatioLogStreamer(ILogStreamer creatioLogStreamer) { }
}
