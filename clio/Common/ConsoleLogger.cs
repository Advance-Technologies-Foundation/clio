using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json.Serialization;
using System.Threading;
using ConsoleTables;
using FluentValidation.Results;

namespace Clio.Common;

#region Class: ConsoleLogger

/// <inheritdoc cref="ILogger"/>
public class ConsoleLogger : ILogger, IDisposable{

	#region Fields: Private
	private TextWriter _logFileWriter;
	private ILogStreamer _creatioLogStreamer;
	private static readonly Lazy<ILogger> Lazy = new(() => new ConsoleLogger());
	private readonly ConcurrentQueue<LogMessage> _logQueue = new();
	private readonly object _messageBufferLock = new();
	private readonly ConsoleColor _defaultConsoleColor = Console.ForegroundColor;
	// FR-06 (ENG-93208): scoped file sinks are per async-flow so a scoped artifact (opened by
	// restore-db / deploy-creatio via BeginScopedFileSink) receives ONLY the log lines produced by the
	// flow that opened it. Each enqueued message captures its producing flow's active sinks onto
	// LogMessage.ScopedSinks (at enqueue, on the producing flow), and the drain writes each message to
	// ITS OWN sinks — never to every registered sink — so a concurrent tenant's lines can never bleed
	// into another flow's artifact. A drain-time flow-local read would resolve to nothing: the drain
	// runs on the shared _printThread whose async-flow slot differs from the producer's.
	private readonly AsyncLocal<List<SharedAppendFileSinkLease>> _flowScopedSinks = new();
	// Scoped sinks of the message currently being drained. Assigned under _messageBufferLock in
	// FlushQueueCore (the drain is single-threaded and always runs under that lock), read by
	// WriteToAdditionalSinks. It carries the sinks already resolved onto the message at enqueue time.
	private IReadOnlyList<SharedAppendFileSinkLease> _drainingScopedSinks;
	private static readonly string[] SpinnerFrames = ["⠋", "⠙", "⠹", "⠸", "⠼", "⠴", "⠦", "⠧", "⠇", "⠏"];
	private volatile bool _spinnerActive;
	private string _spinnerMessage = string.Empty;
	private Thread? _spinnerThread;
	private CancellationTokenSource? _spinnerCts;
	// FR-06 (ENG-93208): capture state is per async-flow, not process-wide, so concurrent MCP tool
	// invocations (different tenants under the per-tenant execution lock) never see each other's
	// captured log lines. Console RENDERING stays on the shared _printThread; only the capture buffer
	// and its enable flag are flow-local. Capture happens at ENQUEUE time (on the producing flow inside
	// the Write* methods), NOT at drain time — the drain runs on the background _printThread whose
	// async-flow slot differs from the producer's, so a drain-time capture into flow-local storage
	// would capture nothing.
	private readonly AsyncLocal<bool> _preserveMessages = new();
	private readonly AsyncLocal<List<LogMessage>> _captureBuffer = new();

	/// <summary>
	/// The current async-flow's captured log buffer, lazily created per flow.
	/// </summary>
	public List<LogMessage> LogMessages => _captureBuffer.Value ??= [];

	/// <summary>
	/// Enables or disables log capture for the CURRENT async-flow. Setting <see langword="true"/>
	/// establishes a FRESH per-flow buffer so each tool-execution scope is isolated and never inherits
	/// the process-wide MCP-mode set (see <c>Program</c>) or a parent flow's captured lines.
	/// </summary>
	public bool PreserveMessages {
		get => _preserveMessages.Value;
		set {
			_preserveMessages.Value = value;
			if (value) {
				_captureBuffer.Value = [];
			}
		}
	}

	#endregion

	#region Constructors: Private

	private ConsoleLogger(){
		CancellationToken = CancellationTokenSource.Token;
		Console.OutputEncoding = System.Text.Encoding.UTF8;
	}

	#endregion

	#region Properties: Private

	/// <summary>
	/// Ambient run-mode used to decide console-output suppression. Set once at startup by the entry
	/// point (property injection: this logger is a process-wide singleton created before the DI
	/// container, so constructor injection is not possible). <see langword="null"/> ⇒ not MCP mode.
	/// </summary>
	internal IRuntimeMode RuntimeMode { get; set; }

	private bool IsMcpServerMode => RuntimeMode?.IsMcpServerMode ?? false;

	private static bool AddTimeStampToOutput => Program.AddTimeStampToOutput;

	private CancellationToken CancellationToken { get; set; }

	private CancellationTokenSource CancellationTokenSource { get; } = new();

	#endregion

	#region Properties: Public

	public static ILogger Instance => Lazy.Value;

	#endregion

	#region Methods: Private
	
	private void FlushQueue() {
		lock (_messageBufferLock) {
			FlushQueueCore();
		}
	}

	private void FlushQueueCore() {
		while (_logQueue.TryDequeue(out LogMessage item)) {
			// Route the additional-sink writes to the sinks this message captured at enqueue time (the
			// producing flow's scoped sinks) — never the whole registry — so each artifact receives only
			// its own flow's lines (FR-06). Safe as a field: the drain is single-threaded under _messageBufferLock.
			_drainingScopedSinks = item.ScopedSinks;
			Action action = item switch {
				InfoMessage infoMessage => () => WriteInfoInternal(infoMessage.Value.ToString()),
				ErrorMessage errorMessage => () => WriteErrorInternal(errorMessage.Value.ToString()),
				WarningMessage warningMessage => () => WriteWarningInternal(warningMessage.Value.ToString()),
				UndecoratedMessage noneMessage => () => WriteLineInternal(noneMessage.Value.ToString()),
				TableMessage tableMessage => () => PrintTableInternal(tableMessage.Value),
				DebugMessage debugMessage => () => PrintDebugInternal(debugMessage.Value.ToString()),
				var _ => throw new ArgumentOutOfRangeException()
			};
			try {
				action.Invoke();
			} catch (ObjectDisposedException) {
			} finally {
				_drainingScopedSinks = null;
			}
		}
	}

	// Captures a message into the CURRENT async-flow's buffer at enqueue time (on the producing flow),
	// gated by the flow-local PreserveMessages flag. Runs under _messageBufferLock so a concurrent
	// snapshot/clear (FlushAndSnapshotMessages/ClearMessages) or a child-flow write never races the add.
	private void CaptureMessage(LogMessage message) {
		// Attach the producing flow's active scoped sinks at ENQUEUE time (on this flow), so the drain —
		// which runs on the shared _printThread whose async-flow slot differs — writes the line only to
		// the sinks of the flow that produced it (FR-06). Runs regardless of PreserveMessages: the
		// db-operation artifact sink must receive lines even when MCP capture is off.
		message.ScopedSinks = SnapshotFlowScopedSinks();
		if (!_preserveMessages.Value) {
			return;
		}
		lock (_messageBufferLock) {
			(_captureBuffer.Value ??= []).Add(message);
		}
	}

	// Stable snapshot of the current flow's active scoped sinks (empty when the flow opened none), so
	// a later Begin/Dispose on the same flow cannot mutate what an already-enqueued message carries.
	private IReadOnlyList<SharedAppendFileSinkLease> SnapshotFlowScopedSinks() {
		List<SharedAppendFileSinkLease> sinks = _flowScopedSinks.Value;
		return sinks is { Count: > 0 } ? sinks.ToArray() : [];
	}

	private void PrintInternal(){
		while (!CancellationToken.IsCancellationRequested) {
			if (!_spinnerActive) {
				try {
					FlushQueue();
				} catch (ObjectDisposedException) {
					// Console.Out was replaced by a test; the old writer was disposed.
				}
			}
			Thread.Sleep(100);
		}
		try {
			FlushQueue();
		} catch (ObjectDisposedException) { }
	}

	private void PrintTableInternal(object table){
		if (AddTimeStampToOutput) {
			WriteLineInternal(GetTimeStamp());
		}
		WriteLineInternal(table.ToString());
	}

	private void WriteToAdditionalSinks(string value) {
		// Write only to the sinks the currently-draining message captured from its producing flow
		// (FR-06). A sink lease disposed since enqueue throws ObjectDisposedException, which the drain
		// loop swallows — matching the prior best-effort behavior when the registry entry was removed.
		IReadOnlyList<SharedAppendFileSinkLease> sinks = _drainingScopedSinks;
		if (sinks is null) {
			return;
		}
		foreach (SharedAppendFileSinkLease sink in sinks) {
			sink.WriteLine(value);
		}
	}

	private void WriteErrorInternal(string value){
		string linePrefix = GetLinePrefix("[ERR]");
		if (!IsMcpServerMode) {
			Console.ForegroundColor = ConsoleColor.Red;
			Console.Error.Write(linePrefix);
			Console.ForegroundColor = _defaultConsoleColor;
			Console.Error.WriteLine(value);
		}
		//Console.WriteLine(value);
		_logFileWriter?.WriteLine($"{linePrefix}{value}");
		WriteToAdditionalSinks($"{linePrefix}{value}");
	}

	private string GetTimeStamp() {
		return AddTimeStampToOutput ? DateTime.Now.ToString("HH:mm:ss") + " " : string.Empty;
	}

	// In --json mode, decorated diagnostic lines ([INF]/[WAR]/[DBG]) are routed to stderr so stdout
	// carries exactly one JSON object (the command envelope, emitted via WriteLine). Errors already go
	// to stderr, and undecorated WriteLine (the envelope) stays on stdout.
	private static System.IO.TextWriter DecoratedLogSink =>
		Program.IsJsonOutputMode ? Console.Error : Console.Out;

	private void WriteInfoInternal(string value){
		string linePrefix = GetLinePrefix("[INF]");
		if (!IsMcpServerMode) {
			System.IO.TextWriter sink = DecoratedLogSink;
			Console.ForegroundColor = ConsoleColor.Green;
			sink.Write(linePrefix);
			Console.ForegroundColor = _defaultConsoleColor;
			sink.WriteLine(value);
		}
		_logFileWriter?.WriteLine($"{linePrefix}{value}");
		WriteToAdditionalSinks($"{linePrefix}{value}");
		_creatioLogStreamer?.WriteLine($"{linePrefix}{value}");
	}
	
	private void PrintDebugInternal(string value){
		string linePrefix = GetLinePrefix("[DBG]");
		if (!IsMcpServerMode) {
			System.IO.TextWriter sink = DecoratedLogSink;
			Console.ForegroundColor = ConsoleColor.DarkYellow;
			sink.Write(linePrefix);
			Console.ForegroundColor = _defaultConsoleColor;
			sink.WriteLine(value);
		}
		_logFileWriter?.WriteLine($"{linePrefix}{value}");
		WriteToAdditionalSinks($"{linePrefix}{value}");
		_creatioLogStreamer?.WriteLine($"{linePrefix}{value}");
	}

	private void WriteLineInternal(string value){
		if (!IsMcpServerMode) {
			Console.Out.WriteLine(value);
		}
		string linePrefix = GetLinePrefix();
		_logFileWriter?.WriteLine($"{linePrefix}{value}");
		WriteToAdditionalSinks($"{linePrefix}{value}");
		_creatioLogStreamer?.WriteLine($"{linePrefix}{value}");
	}

	private void WriteWarningInternal(string value){
		string linePrefix = GetLinePrefix("[WAR]");
		if (!IsMcpServerMode) {
			System.IO.TextWriter sink = DecoratedLogSink;
			Console.ForegroundColor = ConsoleColor.DarkYellow;
			sink.Write(linePrefix);
			Console.ForegroundColor = _defaultConsoleColor;
			sink.WriteLine(value);
		}
		_logFileWriter?.WriteLine($"{linePrefix}{value}");
		WriteToAdditionalSinks($"{linePrefix}{value}");
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

	/// <summary>Sets the ambient run-mode on the process-wide logger singleton (see <see cref="RuntimeMode"/>).</summary>
	internal static void UseRuntimeMode(IRuntimeMode runtimeMode) => ((ConsoleLogger)Instance).RuntimeMode = runtimeMode;

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
		TableMessage message = new(table);
		CaptureMessage(message);
		_logQueue.Enqueue(message);
	}

	internal IReadOnlyList<LogMessage> FlushAndSnapshotMessages(bool clearMessages = false) {
		lock (_messageBufferLock) {
			// Drain the queue so any still-pending messages are rendered to the console before we hand
			// back the snapshot; capture itself already happened at enqueue time into the flow buffer.
			FlushQueueCore();
			List<LogMessage> buffer = _captureBuffer.Value ??= [];
			List<LogMessage> snapshot = [.. buffer];
			if (clearMessages) {
				buffer.Clear();
			}
			return snapshot;
		}
	}

	public void ClearMessages() {
		lock (_messageBufferLock) {
			FlushQueueCore();
			(_captureBuffer.Value ??= []).Clear();
		}
	}

	/// <summary>
	/// Adds a scoped additional log file sink.
	/// </summary>
	/// <param name="logFilePath">The file path that should receive a copy of all logger output.</param>
	/// <returns>A scope that unregisters the additional sink on disposal.</returns>
	public IDisposable BeginScopedFileSink(string logFilePath) {
		ArgumentException.ThrowIfNullOrWhiteSpace(logFilePath);
		string? directory = Path.GetDirectoryName(logFilePath);
		if (!string.IsNullOrWhiteSpace(directory)) {
			Directory.CreateDirectory(directory);
		}

		SharedAppendFileSinkLease sink = SharedAppendFileSinkRegistry.Acquire(logFilePath);
		// Register the sink on the CURRENT async-flow so only this flow's enqueued messages carry it
		// (FR-06). The value is a per-flow list (each flow inherits the process-startup null slot), so a
		// concurrent flow's sink registration is invisible here.
		(_flowScopedSinks.Value ??= []).Add(sink);

		return new ScopedFileSink(this, sink);
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
			_logFileWriter = CreateSharedAppendWriter(logFileName);
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
		UndecoratedMessage message = new(value);
		CaptureMessage(message);
		_logQueue.Enqueue(message);
	}

	/// <summary>
	/// Enqueues an error message to the log queue.
	/// </summary>
	/// <param name="value">String value to be printed to the log</param>
	public void WriteError(string value){
		if(CancellationToken.IsCancellationRequested) {
			return;
		}
		ErrorMessage message = new(value);
		CaptureMessage(message);
		_logQueue.Enqueue(message);
	}

	/// <summary>
	/// Enqueues an error message to the log queue.
	/// </summary>
	/// <param name="value">String value to be printed to the log</param>
	public void WriteInfo(string value){
		if(CancellationToken.IsCancellationRequested) {
			return;
		}
		InfoMessage message = new(value);
		CaptureMessage(message);
		_logQueue.Enqueue(message);
	}

	/// <summary>
	/// Write a empty line to the log.
	/// </summary>
	public void WriteLine() {
		if (CancellationToken.IsCancellationRequested) {
			return;
		}
		UndecoratedMessage message = new(string.Empty);
		CaptureMessage(message);
		_logQueue.Enqueue(message);
	}

	/// <summary>
	/// Enqueues an error message to the log queue.
	/// </summary>
	/// <param name="value">String value to be printed to the log</param>
	public void WriteLine(string value){
		if(CancellationToken.IsCancellationRequested) {
			return;
		}
		UndecoratedMessage message = new(value);
		CaptureMessage(message);
		_logQueue.Enqueue(message);
	}

	/// <summary>
	/// Enqueues an error message to the log queue.
	/// </summary>
	/// <param name="value">String value to be printed to the log</param>
	public void WriteWarning(string value){
		if(CancellationToken.IsCancellationRequested) {
			return;
		}
		WarningMessage message = new(value);
		CaptureMessage(message);
		_logQueue.Enqueue(message);
	}

	/// <summary>
	/// Enqueues a debug message to the log queue.
	/// </summary>
	/// <param name="value">String value to be printed to the log</param>
	public void WriteDebug(string value){
		if(CancellationToken.IsCancellationRequested) {
			return;
		}
		// Only enqueue debug messages if debug mode is enabled
		if (!Program.IsDebugMode) {
			return;
		}
		DebugMessage message = new(value);
		CaptureMessage(message);
		_logQueue.Enqueue(message);
	}

	public void BeginSpinner(string message) {
		if (IsMcpServerMode || Console.IsOutputRedirected) {
			WriteInfo(message);
			return;
		}
		lock (_messageBufferLock) {
			FlushQueueCore();
		}
		_spinnerMessage = message;
		_spinnerCts = new CancellationTokenSource();
		_spinnerActive = true;
		string prefix = GetLinePrefix("[INF]");
		CancellationToken token = _spinnerCts.Token;
		_spinnerThread = new Thread(() => {
			int i = 0;
			while (!token.IsCancellationRequested) {
				Console.ForegroundColor = ConsoleColor.Green;
				Console.Out.Write($"\r{prefix}");
				Console.ForegroundColor = _defaultConsoleColor;
				Console.Out.Write($"{_spinnerMessage} {SpinnerFrames[i % SpinnerFrames.Length]}");
				Thread.Sleep(80);
				i++;
			}
		}) { IsBackground = true };
		_spinnerThread.Start();
	}

	public void EndSpinner(bool success = true) {
		if (_spinnerCts is null) {
			return;
		}
		_spinnerCts.Cancel();
		_spinnerThread?.Join();
		_spinnerActive = false;
		if (!IsMcpServerMode && !Console.IsOutputRedirected) {
			string prefix = GetLinePrefix("[INF]");
			ConsoleColor iconColor = success ? ConsoleColor.Green : ConsoleColor.Red;
			string icon = success ? "✓" : "✗";
			Console.ForegroundColor = ConsoleColor.Green;
			Console.Out.Write($"\r{prefix}");
			Console.ForegroundColor = _defaultConsoleColor;
			Console.Out.Write($"{_spinnerMessage} ");
			Console.ForegroundColor = iconColor;
			Console.Out.WriteLine(icon);
			Console.ForegroundColor = _defaultConsoleColor;
		}
		_spinnerCts.Dispose();
		_spinnerCts = null;
		_spinnerThread = null;
	}

	/// <summary>
	/// Dispose the log file writer.
	/// </summary>
	public void Dispose() {
		_logFileWriter?.Dispose();
		_logFileWriter = null;
	}

	private void RemoveScopedFileSink(SharedAppendFileSinkLease sink) {
		_flowScopedSinks.Value?.Remove(sink);
	}

	#endregion

	public static readonly Func<object, string> WrapRed = s => $"\x1b[31m{s}\x1b[0m";
	public static readonly Func<object, string> WrapYellow = s => $"\x1b[33m{s}\x1b[0m";
	public static readonly Func<object, string> WrapBlue = s => $"\x1b[34m{s}\x1b[0m";
	public static readonly Func<object, string> WrapGreen = s => $"\x1b[32m{s}\x1b[0m";
	public static readonly Func<object, string> WrapDarkYellow = s => $"\x1b[93m{s}\x1b[0m";
	public static readonly Func<object, string> WhapCayenne = s => $"\x1b[91m{s}\x1b[0m";
	
	public static readonly Func<object, string> WrapBold = s => $"\x1b[1m{s}\x1b[0m";
	public static readonly Func<object, string> WrapUnderline = s => $"\x1b[4m{s}\x1b[0m";
	public static readonly Func<object, string> WrapItalic = s => $"\x1b[3m{s}\x1b[0m";
	public static readonly Func<object, string> WrapStrikeThrough = s => $"\x1b[9m{s}\x1b[0m";
	
	
	private static bool SupportsAnsiEscapeCodes() =>

		// Rider terminal doesn't set TERM properly
		!Console.IsOutputRedirected &&
		Environment.GetEnvironmentVariable("NO_COLOR") == null &&
		Environment.GetEnvironmentVariable("TERM") != null;

	private static StreamWriter CreateSharedAppendWriter(string logFilePath) {
		FileStream stream = new(
			logFilePath,
			FileMode.Append,
			FileAccess.Write,
			FileShare.ReadWrite | FileShare.Delete);
		return new StreamWriter(stream) {
			AutoFlush = true
		};
	}

	private sealed class ScopedFileSink(ConsoleLogger owner, SharedAppendFileSinkLease sink) : IDisposable {
		private bool _disposed;

		public void Dispose() {
			if (_disposed) {
				return;
			}

			owner.RemoveScopedFileSink(sink);
			sink.Dispose();
			_disposed = true;
		}
	}
}

#endregion

public enum LogDecoratorType{

	Info,
	Error,
	Warning,
	Debug,
	None,
	Table

}

internal class InfoMessage(string value) : LogMessage(value){
	

	#region Properties: Public

	public override LogDecoratorType LogDecoratorType => LogDecoratorType.Info;

	#endregion

}

internal class ErrorMessage(string value) : LogMessage(value){
	

	#region Properties: Public

	public override LogDecoratorType LogDecoratorType => LogDecoratorType.Error;

	#endregion

}

internal class DebugMessage(string value) : LogMessage(value){
	
	#region Properties: Public

	public override LogDecoratorType LogDecoratorType => LogDecoratorType.Debug;

	#endregion

}

internal class WarningMessage(string value) : LogMessage(value){

	#region Properties: Public

	public override LogDecoratorType LogDecoratorType => LogDecoratorType.Warning;

	#endregion

}

internal class UndecoratedMessage(string value) : LogMessage(value){
	
	#region Properties: Public

	public override LogDecoratorType LogDecoratorType => LogDecoratorType.None;

	#endregion

}

internal class TableMessage(ConsoleTable value) : LogMessage(value){

	#region Properties: Public

	public override LogDecoratorType LogDecoratorType => LogDecoratorType.Table;

	#endregion

}

public abstract class LogMessage(object value){
	#region Properties: Public

	[JsonPropertyName("message-type")]
	[Description("Type of log message")]
	public abstract LogDecoratorType LogDecoratorType { get; }

	[Description("Value of the log message" )]
	public object Value { get; set; } = value;

	// FR-06 (ENG-93208): the producing flow's active scoped file sinks, resolved at enqueue time so the
	// drain writes this message only to its own flow's artifact(s). Internal so System.Text.Json (the
	// MCP serialization path) never touches it, and never carries a secret.
	internal IReadOnlyList<SharedAppendFileSinkLease> ScopedSinks { get; set; }

	#endregion
}
