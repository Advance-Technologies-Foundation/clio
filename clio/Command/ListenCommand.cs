using System;
using System.Net;
using System.Net.WebSockets;
using System.Threading;
using Clio.Common;
using CommandLine;
using Creatio.Client.Dto;
using System.Text.Json;


namespace Clio.Command;

#region Class: ListenOptions

[Verb("listen", HelpText = "Subscribe to a websocket")]
public class ListenOptions : EnvironmentOptions
{ }

#endregion

#region Class: ListenCommand

public class ListenCommand : Command<EnvironmentOptions>
{
	
	private readonly IApplicationClient _applicationClient;
	private readonly ILogger _logger;
	private readonly EnvironmentSettings _environmentSettings;
	private readonly IFileSystem _fileSystem;
	private const string StartLogBroadcast = "/rest/ATFLogService/StartLogBroadcast";
	private const string StopLogBroadcast = "/rest/ATFLogService/ResetConfiguration";
	#region Constructors: Public

	public ListenCommand(IApplicationClient applicationClient,ILogger logger,EnvironmentSettings environmentSettings, IFileSystem fileSystem){
		_applicationClient = applicationClient;
		_logger = logger;
		_environmentSettings = environmentSettings;
		_fileSystem = fileSystem;
		_applicationClient.ConnectionStateChanged += OnConnectionStateChanged;
		_applicationClient.MessageReceived += OnMessageReceived;
	}
	#endregion

	#region Methods: Public

	public override int Execute(EnvironmentOptions options){
		_applicationClient.Listen(CancellationToken.None);
		StartLogger();
		Console.ReadKey();
		StopLogger();
		return 0;
	}
	
	private void StartLogger(){
		string rootPath = _environmentSettings.IsNetCore ? _environmentSettings.Uri : _environmentSettings.Uri + @"/0";
		string requestUrl = rootPath+StartLogBroadcast;
		var payload = new {
			logLevelStr = "All",
			bufferSize = 1,
		};
		JsonSerializerOptions options = new (){PropertyNamingPolicy = JsonNamingPolicy.CamelCase};
		string payloadString = JsonSerializer.Serialize(payload,options);
		_applicationClient.ExecutePostRequest(requestUrl,payloadString);
		
	}
	
	private void StopLogger(){
		string rootPath = _environmentSettings.IsNetCore ? _environmentSettings.Uri : _environmentSettings.Uri + @"/0";
		string requestUrl = rootPath+StopLogBroadcast;
		_applicationClient.ExecutePostRequest(requestUrl,string.Empty);
	}

	private void OnMessageReceived(object sender, WsMessage message){
		
		switch (message.Header.Sender)
		{
			case "TelemetryService":
				HandleTelemetryServiceMessages(message);
				break;
			default:
				//_logger.WriteLine(message.Body);
				break;
		}
	}
	
	private void HandleTelemetryServiceMessages(WsMessage message){

		//JsonSerializer.Deserialize<TelemetryMessage>(message.Body);
		System.IO.File.AppendAllText("C:\\ws-clio.json", Environment.NewLine+message.Body);
		//_fileSystem.WriteAllTextToFile("C:\\ws-clio.txt", message.Body);
	}
	
	private void OnConnectionStateChanged(object sender, WebSocketState state){
		_logger.WriteLine($"Connection state changed to {state}");
	}
	
	
	#endregion

}

#endregion

public record TelemetryMessage(LogPortion[] logPortion, int cpu, int ramMb);

public record LogPortion(
    string date,
    string level,
    object thread,
    string logger,
    string message,
    object stackTrace
);

