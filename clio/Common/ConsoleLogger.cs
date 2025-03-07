using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using ConsoleTables;
using FluentValidation.Results;

namespace Clio.Common;

#region Class: ConsoleLogger

/// <inheritdoc cref="ILogger"/>
public class ConsoleLogger : ILogger, IDisposable
{

	#region Fields: Private
	private TextWriter _logFileWriter;
	private ILogStreamer _creatioLogStreamer;
	private static readonly Lazy<ILogger> Lazy = new(() => new ConsoleLogger());
	private readonly ConcurrentQueue<LogMessage> _logQueue = new();
	private readonly ConsoleColor _defaultConsoleColor = Console.ForegroundColor;
	
	#endregion

	#region Constructors: Private

	private ConsoleLogger(){
		CancellationToken = CancellationTokenSource.Token;
		Console.OutputEncoding = System.Text.Encoding.UTF8;
	}

	#endregion

	#region Properties: Private

	private bool AddTimeStampToOutput {
		get {
			return Program.AddTimeStampToOutput;
		}
	}

	private CancellationToken CancellationToken { get; set; }

	private CancellationTokenSource CancellationTokenSource { get; } = new();

	#endregion

	#region Properties: Public

	public static ILogger Instance => Lazy.Value;

	#endregion

	#region Methods: Private
	
	private void FlushQueue() {
		while (_logQueue.TryPeek( out var _)) {
			bool isItem = _logQueue.TryDequeue(out LogMessage item);
			if (isItem) {
				Action action = item switch {
					InfoMessage infoMessage => () => WriteInfoInternal(infoMessage.Value.ToString()),
					ErrorMessage errorMessage => () => WriteErrorInternal(errorMessage.Value.ToString()),
					WarningMessage warningMessage => () => WriteWarningInternal(warningMessage.Value.ToString()),
					UndecoratedMessage noneMessage => () => WriteLineInternal(noneMessage.Value.ToString()),
					TableMessage tableMessage => () => PrintTableInternal(tableMessage.Value),
					_ => throw new ArgumentOutOfRangeException()
				};
				action.Invoke();
			}
		}
	}
	
	
	private void PrintInternal(){
		while (!CancellationToken.IsCancellationRequested) {
			FlushQueue();
			Thread.Sleep(100);
		}
		FlushQueue();
	}

	private void PrintTableInternal(object table){
		if (AddTimeStampToOutput) {
			WriteLineInternal(GetTimeStamp());
		}
		WriteLineInternal(table.ToString());
	}

	private void WriteErrorInternal(string value){
		Console.ForegroundColor = ConsoleColor.Red;
		string linePrefix = GetLinePrefix("[ERR]");
		Console.Write(linePrefix);
		Console.ForegroundColor = _defaultConsoleColor;
		Console.WriteLine(value);
		_logFileWriter?.WriteLine($"{linePrefix}{value}");
	}

	private string GetTimeStamp() {
		return AddTimeStampToOutput ? DateTime.Now.ToString("HH:mm:ss") + " " : string.Empty;
	}

	private void WriteInfoInternal(string value){
		string linePrefix = GetLinePrefix("[INF]");
		Console.ForegroundColor = ConsoleColor.Green;
		Console.Write(linePrefix);
		Console.ForegroundColor = _defaultConsoleColor;
		Console.WriteLine(value);
		_logFileWriter?.WriteLine($"{linePrefix}{value}");
		_creatioLogStreamer?.WriteLine($"{linePrefix}{value}");
	}

	private void WriteLineInternal(string value){
		Console.WriteLine(value);
		string linePrefix = GetLinePrefix();
		_logFileWriter?.WriteLine($"{linePrefix}{value}");
		_creatioLogStreamer?.WriteLine($"{linePrefix}{value}");
	}

	private void WriteWarningInternal(string value){
		Console.ForegroundColor = ConsoleColor.DarkYellow;
		string linePrefix = GetLinePrefix("[WAR]");
		Console.Write(linePrefix);
		Console.ForegroundColor = _defaultConsoleColor;
		Console.WriteLine(value);
		_logFileWriter?.WriteLine($"{linePrefix}{value}");
		_creatioLogStreamer?.WriteLine($"{linePrefix}{value}");
	}
	
	private string GetLinePrefix(string severity = ""){
		string prefix = $"{GetTimeStamp()}{severity}";
		return string.IsNullOrWhiteSpace(prefix) 
			? string.Empty 
			: $"{prefix} - ";
	}
	
	
	#endregion

	#region Methods: Public
	
	public void PrintValidationFailureErrors(IEnumerable<ValidationFailure> errors) {
		errors.Select(e => new { e.ErrorMessage, e.ErrorCode, e.Severity })
			.ToList().ForEach(e =>
			{
				string msg = $"{e.Severity.ToString().ToUpper(CultureInfo.InvariantCulture)} ({e.ErrorCode}) - {e.ErrorMessage}";
				WriteError(msg);
			});
	}

	private string LogFileName { get; set; }
	public TextWriter LogFileWriter {
		get => _logFileWriter;
		internal set => _logFileWriter = value;
	}

	public void PrintTable(ConsoleTable table){
		_logQueue.Enqueue(new TableMessage(table));
	}

	/// <summary>
	/// Starts the logging process runs.
	/// This method initiates a new thread that continuously dequeues log messages from the queue
	/// and writes them to the console.
	/// </summary>
	public void Start(string logFileName = ""){
		if (_isStarted) {
			return;
		}
		if (!string.IsNullOrEmpty(logFileName)) {
			_logFileWriter = new StreamWriter(logFileName, append: true) {
				AutoFlush = true
			};
		}
		_printThread = new(PrintInternal);
		_printThread.Start();
		_isStarted = true;
		LogFileName = logFileName;
	}

	public void SetCreatioLogStreamer(ILogStreamer creatioLogStreamer) {
		_creatioLogStreamer = creatioLogStreamer;
	}

	public void StartWithStream() {
		if (_isStarted) {
			return;
		}

		_printThread = new(PrintInternal);
		_printThread.Start();
		_isStarted = true;
	}

	private Thread _printThread;
	private bool _isStarted = false; 
	
	/// <summary>
	/// Stops the logging process.
	/// This method signals the cancellation token to stop the logging thread.
	/// </summary>	
	public void Stop(){
		CancellationTokenSource.Cancel();
		CancellationToken = CancellationTokenSource.Token;
		_isStarted = false;
		_printThread.Join();
		_logFileWriter?.Close();
	}

	public void Write(string value){
		if(CancellationToken.IsCancellationRequested) {
			return;
		}
		Console.Write(value);
	}

	/// <summary>
	/// Enqueues an error message to the log queue.
	/// </summary>
	/// <param name="value">String value to be printed to the log</param>
	public void WriteError(string value){
		if(CancellationToken.IsCancellationRequested) {
			return;
		}
		_logQueue.Enqueue(new ErrorMessage(value));
		
	}

	/// <summary>
	/// Enqueues an error message to the log queue.
	/// </summary>
	/// <param name="value">String value to be printed to the log</param>
	public void WriteInfo(string value){
		if(CancellationToken.IsCancellationRequested) {
			return;
		}
		_logQueue.Enqueue(new InfoMessage(value));
	}

	/// <summary>
	/// Write a empty line to the log.
	/// </summary>
	public void WriteLine() {
		if (CancellationToken.IsCancellationRequested) {
			return;
		}
		_logQueue.Enqueue(new UndecoratedMessage(string.Empty));
	}

	/// <summary>
	/// Enqueues an error message to the log queue.
	/// </summary>
	/// <param name="value">String value to be printed to the log</param>
	public void WriteLine(string value){
		if(CancellationToken.IsCancellationRequested) {
			return;
		}
		_logQueue.Enqueue(new UndecoratedMessage(value));
	}

	/// <summary>
	/// Enqueues an error message to the log queue.
	/// </summary>
	/// <param name="value">String value to be printed to the log</param>
	public void WriteWarning(string value){
		if(CancellationToken.IsCancellationRequested) {
			return;
		}
		_logQueue.Enqueue(new WarningMessage(value));
	}

	/// <summary>
	/// Dispose the log file writer.
	/// </summary>
	public void Dispose() {
		_logFileWriter?.Dispose();
		_logFileWriter = null;
	}

	#endregion

}

#endregion

public enum LogDecoratorType
{

	Info,
	Error,
	Warning,
	None,
	Table

}

internal class InfoMessage : LogMessage
{

	#region Constructors: Public

	public InfoMessage(string value)
		: base(value){ }

	#endregion

	#region Properties: Public

	public override LogDecoratorType LogDecoratorType => LogDecoratorType.Info;

	#endregion

}

internal class ErrorMessage : LogMessage
{

	#region Constructors: Public

	public ErrorMessage(string value)
		: base(value){ }

	#endregion

	#region Properties: Public

	public override LogDecoratorType LogDecoratorType => LogDecoratorType.Error;

	#endregion

}

internal class WarningMessage : LogMessage
{

	#region Constructors: Public

	public WarningMessage(string value)
		: base(value){ }

	#endregion

	#region Properties: Public

	public override LogDecoratorType LogDecoratorType => LogDecoratorType.Warning;

	#endregion

}

internal class UndecoratedMessage : LogMessage
{

	#region Constructors: Public

	public UndecoratedMessage(string value)
		: base(value){ }

	#endregion

	#region Properties: Public

	public override LogDecoratorType LogDecoratorType => LogDecoratorType.None;

	#endregion

}

internal class TableMessage : LogMessage
{

	#region Constructors: Public

	public TableMessage(ConsoleTable value)
		: base(value){ }

	#endregion

	#region Properties: Public

	public override LogDecoratorType LogDecoratorType => LogDecoratorType.Table;

	#endregion

}

internal abstract class LogMessage
{

	#region Constructors: Protected

	protected LogMessage(object value){
		Value = value;
	}

	#endregion

	#region Properties: Public

	public abstract LogDecoratorType LogDecoratorType { get; }

	public object Value { get; set; }

	#endregion

}
