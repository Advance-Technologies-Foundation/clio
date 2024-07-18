using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using ConsoleTables;
using FluentValidation.Results;

namespace Clio.Common;

#region Class: ConsoleLogger

/// <inheritdoc cref="ILogger"/>
public class ConsoleLogger : ILogger
{

	#region Fields: Private

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
		while (CancellationToken.IsCancellationRequested == false) {
			FlushQueue();
			Thread.Sleep(100);
		}
		
		FlushQueue();
	}

	private void PrintTableInternal(object table){
		WriteLineInternal(table.ToString());
	}

	private void WriteErrorInternal(string value){
		Console.ForegroundColor = ConsoleColor.Red;
		Console.Write("[ERR] - ");
		Console.ForegroundColor = _defaultConsoleColor;
		Console.WriteLine(value);
	}

	private void WriteInfoInternal(string value){
		Console.ForegroundColor = ConsoleColor.Green;
		Console.Write("[INF] - ");
		Console.ForegroundColor = _defaultConsoleColor;
		Console.WriteLine(value);
	}

	private void WriteLineInternal(string value){
		Console.WriteLine(value);
	}

	private void WriteWarningInternal(string value){
		Console.ForegroundColor = ConsoleColor.DarkYellow;
		Console.Write("[WAR] - ");
		Console.ForegroundColor = _defaultConsoleColor;
		Console.WriteLine(value);
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
	
	
	public void PrintTable(ConsoleTable table){
		_logQueue.Enqueue(new TableMessage(table));
	}

	/// <summary>
	/// Starts the logging process runs.
	/// This method initiates a new thread that continuously dequeues log messages from the queue
	/// and writes them to the console.
	/// </summary>
	public void Start(){
		Thread printThread = new(PrintInternal);
		printThread.Start();
	}

	/// <summary>
	/// Stops the logging process.
	/// This method signals the cancellation token to stop the logging thread.
	/// </summary>	
	public void Stop(){
		CancellationTokenSource.Cancel();
		CancellationToken = CancellationTokenSource.Token;
	}

	public void Write(string value){
		if(CancellationToken.IsCancellationRequested) {
			return;
		}
		Console.Write(value);
		//_logQueue.Enqueue(new UndecoratedMessage(value));
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
