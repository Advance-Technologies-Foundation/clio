using System;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using Clio.Common;
using CommandLine;
using Creatio.Client.Dto;
using System.Text.Json;
using Terrasoft.Common;

namespace Clio.Command;

#region Class: ListenOptions

[Verb("listen", HelpText = "Subscribe to a websocket")]
public class ListenOptions : EnvironmentOptions
{

	[Option("loglevel", Required = false, HelpText = "Log level (ALL, Debug, Error, Fatal, Info, Trace, Warn)", Default = "All")]
	public string LogLevel { get; set; }

	[Option("logPattern", Required = false, HelpText = "Log pattern (i.e. ExceptNoisyLoggers)", Default = "")]
	public string LogPattern { get; set; }
	
	[Option("FileName", Required = false, HelpText = "File path to save logs into")]
	public string FileName { get; set; }

	[Option("Silent", Required = false, HelpText = "Disable messages in console", Default = false)]
	public bool Silent { get; set; }

}

#endregion

#region Class: ListenCommand

public class ListenCommand : Command<ListenOptions>, IDisposable
{
	
	private readonly IApplicationClient _applicationClient;
	private readonly ILogger _logger;
	private readonly IFileSystem _fileSystem;
	private readonly IServiceUrlBuilder _serviceUrlBuilder;
	private readonly IConsole _console;
	private string _logFilePath = string.Empty;
	private bool _silent;
	private readonly CancellationTokenSource _cancellationTokenSource = new ();
	private bool _disposed;
	private static readonly JsonSerializerOptions SerializerOptions = new (){PropertyNamingPolicy = JsonNamingPolicy.CamelCase};
	#region Constructors: Public
	
	public ListenCommand(IApplicationClient applicationClient,ILogger logger, IFileSystem fileSystem, IServiceUrlBuilder serviceUrlBuilder, IConsole console = null){
		_applicationClient = applicationClient;
		_logger = logger;
		_fileSystem = fileSystem;
		_serviceUrlBuilder = serviceUrlBuilder;
		_console = console ?? new SystemConsoleAdapter();
		_applicationClient.ConnectionStateChanged += OnConnectionStateChanged;
		_applicationClient.MessageReceived += OnMessageReceived;
	}
	#endregion

	#region Methods: Public

	public override int Execute(ListenOptions options){
		CancellationToken token = _cancellationTokenSource.Token;
		_logFilePath = options.FileName;
		_silent = options.Silent;
		_applicationClient.Listen(token);
		StartLogger(options);
		_console.ReadKey();
		_cancellationTokenSource.Cancel();
		StopLogger();
		return 0;
	}
	
	private void StartLogger(ListenOptions options){
		string requestUrl = _serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.StartLogBroadcast);
		var payload = new {
			logLevelStr = options.LogLevel,
			bufferSize = 1,
			loggerPattern= options.LogPattern
		};
		string payloadString = JsonSerializer.Serialize(payload, SerializerOptions);
		_applicationClient.ExecutePostRequest(requestUrl,payloadString);
	}
	
	private void StopLogger(){
		string requestUrl = _serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.StopLogBroadcast);
		_applicationClient.ExecutePostRequest(requestUrl,string.Empty);
	}

	private void OnMessageReceived(object sender, WsMessage message){
		switch (message.Header.Sender) {
			case "TelemetryService":
				HandleTelemetryServiceMessages(message);
				break;
			default:
				//_logger.WriteLine(message.Body);
				break;
		}
	}
	
	private void HandleTelemetryServiceMessages(WsMessage message){
		if(!_silent) {
			_logger.WriteLine(message.Body);
		}
		if(!_logFilePath.IsNullOrEmpty()) {
			_fileSystem.AppendTextToFile(_logFilePath, Environment.NewLine+message.Body, Encoding.UTF8);
		}
	}
	
	private void OnConnectionStateChanged(object sender, WebSocketState state){
		_logger.WriteLine($"Connection state changed to {state}");
	}

	public void Dispose(){
		Dispose(true);
		GC.SuppressFinalize(this);
	}

	protected virtual void Dispose(bool disposing){
		if (_disposed){
			return;
		}

		if (disposing){
			_applicationClient.ConnectionStateChanged -= OnConnectionStateChanged;
			_applicationClient.MessageReceived -= OnMessageReceived;
			_cancellationTokenSource?.Dispose();
		}

		_disposed = true;
	}
	
	
	#endregion

}

#endregion
