using System;
using System.IO;
using System.Net.WebSockets;
using System.Text.Json;
using System.Threading;
using Clio.Common;
using CommandLine;
using Creatio.Client.Dto;
using Terrasoft.Common;

namespace Clio.Command;

#region Class: ListenOptions

[Verb("listen", HelpText = "Subscribe to a websocket")]
public class ListenOptions : EnvironmentOptions
{

    #region Properties: Public

    [Option("FileName", Required = false, HelpText = "File path to save logs into")]
    public string FileName { get; set; }

    [Option("loglevel", Required = false, HelpText = "Log level (ALL, Debug, Error, Fatal, Info, Trace, Warn)",
        Default = "All")]
    public string LogLevel { get; set; }

    [Option("logPattern", Required = false, HelpText = "Log pattern (i.e. ExceptNoisyLoggers)", Default = "")]
    public string LogPattern { get; set; }

    [Option("Silent", Required = false, HelpText = "Disable messages in console", Default = false)]
    public bool Silent { get; set; }

    #endregion

}

#endregion

#region Class: ListenCommand

public class ListenCommand : Command<ListenOptions>
{

    #region Constants: Private

    private const string StartLogBroadcast = "/rest/ATFLogService/StartLogBroadcast";
    private const string StopLogBroadcast = "/rest/ATFLogService/ResetConfiguration";

    #endregion

    #region Fields: Private

    private readonly IApplicationClient _applicationClient;
    private readonly ILogger _logger;
    private readonly EnvironmentSettings _environmentSettings;
    private readonly IFileSystem _fileSystem;
    private string LogFilePath = string.Empty;
    private bool Silent;
    private readonly CancellationTokenSource _cancellationTokenSource = new();

    #endregion

    #region Constructors: Public

    public ListenCommand(IApplicationClient applicationClient, ILogger logger, EnvironmentSettings environmentSettings,
        IFileSystem fileSystem)
    {
        _applicationClient = applicationClient;
        _logger = logger;
        _environmentSettings = environmentSettings;
        _fileSystem = fileSystem;
        _applicationClient.ConnectionStateChanged += OnConnectionStateChanged;
        _applicationClient.MessageReceived += OnMessageReceived;
    }

    #endregion

    #region Methods: Private

    private void HandleTelemetryServiceMessages(WsMessage message)
    {
        if (!Silent)
        {
            _logger.WriteLine(message.Body);
        }
        if (!LogFilePath.IsNullOrEmpty())
        {
            File.AppendAllText(LogFilePath, Environment.NewLine + message.Body);
        }
    }

    private void OnConnectionStateChanged(object sender, WebSocketState state)
    {
        _logger.WriteLine($"Connection state changed to {state}");
    }

    private void OnMessageReceived(object sender, WsMessage message)
    {
        switch (message.Header.Sender)
        {
            case "TelemetryService":
                HandleTelemetryServiceMessages(message);
                break;
        }
    }

    private void StartLogger(ListenOptions options)
    {
        string rootPath = _environmentSettings.IsNetCore ? _environmentSettings.Uri : _environmentSettings.Uri + @"/0";
        string requestUrl = rootPath + StartLogBroadcast;
        var payload = new
        {
            logLevelStr = options.LogLevel, bufferSize = 1, loggerPattern = options.LogPattern
        };
        JsonSerializerOptions serializerOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
        string payloadString = JsonSerializer.Serialize(payload, serializerOptions);
        _applicationClient.ExecutePostRequest(requestUrl, payloadString);
    }

    private void StopLogger()
    {
        string rootPath = _environmentSettings.IsNetCore ? _environmentSettings.Uri : _environmentSettings.Uri + @"/0";
        string requestUrl = rootPath + StopLogBroadcast;
        _applicationClient.ExecutePostRequest(requestUrl, string.Empty);
    }

    #endregion

    #region Methods: Public

    public override int Execute(ListenOptions options)
    {
        CancellationToken token = _cancellationTokenSource.Token;
        LogFilePath = options.FileName;
        Silent = options.Silent;
        _applicationClient.Listen(token);
        StartLogger(options);
        Console.ReadKey();
        _cancellationTokenSource.Cancel();
        StopLogger();
        return 0;
    }

    #endregion

}

#endregion

public record TelemetryMessage(LogPortion[] logPortion, int cpu, int ramMb);

public record LogPortion(string date,
    string level,
    object thread,
    string logger,
    string message,
    object stackTrace);
