using System;
using System.Linq;
using System.Net.WebSockets;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using Clio.Command;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using Creatio.Client.Dto; // WsMessage type

namespace Clio.Tests.Command;

[TestFixture]
public class ListenCommandTestCase : BaseCommandTests<ListenOptions>, IDisposable
{
	#region Fields: Private
	private IApplicationClient _applicationClient;
	private ILogger _logger;
	private IFileSystem _fileSystem;
	private IServiceUrlBuilder _serviceUrlBuilder;
	private ListenCommand _sut;
	#endregion

	#region Setup / Teardown
	[SetUp]
	public void SetUp() {
		_applicationClient = Substitute.For<IApplicationClient>();
		_logger = Substitute.For<ILogger>();
		_fileSystem = Substitute.For<IFileSystem>();
		_serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		_serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.StartLogBroadcast)
			.Returns("http://test.domain.com/rest/ATFLogService/StartLogBroadcast");
		_serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.StopLogBroadcast)
			.Returns("http://test.domain.com/rest/ATFLogService/ResetConfiguration");
		_sut = new ListenCommand(_applicationClient, _logger, _fileSystem, _serviceUrlBuilder);
	}

	[TearDown]
	public void TearDown() => _sut?.Dispose();
	#endregion

	#region Helpers
	private static ListenOptions CreateOptions(
		string logLevel = "Debug",
		string logPattern = "ExceptNoisyLoggers",
		string fileName = "",
		bool silent = false) => new() {
		LogLevel = logLevel,
		LogPattern = logPattern,
		FileName = fileName,
		Silent = silent
	};

	private static MethodInfo GetPrivateMethod(string name) => typeof(ListenCommand)
		.GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!;

	private static FieldInfo GetPrivateField(string name) => typeof(ListenCommand)
		.GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!;

	private static WsMessage CreateWsMessage(string sender, string body) {
		var msg = Activator.CreateInstance<WsMessage>();
		// Body property
		msg.GetType().GetProperty("Body")?.SetValue(msg, body);
		// Header + Sender
		var headerProp = msg.GetType().GetProperty("Header");
		var headerObj = headerProp?.GetValue(msg);
		if (headerObj == null && headerProp != null) {
			headerObj = Activator.CreateInstance(headerProp.PropertyType);
			headerProp.SetValue(msg, headerObj);
		}
		headerObj?.GetType().GetProperty("Sender")?.SetValue(headerObj, sender);
		return msg;
	}

	private void InvokeStartLogger(ListenOptions options) => GetPrivateMethod("StartLogger").Invoke(_sut, new object[] { options });
	private void InvokeStopLogger() => GetPrivateMethod("StopLogger").Invoke(_sut, Array.Empty<object>());
	private void SetPrivateField(string fieldName, object value) => GetPrivateField(fieldName).SetValue(_sut, value);
	#endregion

	[Test]
	[Description("StartLogger sends POST request with correct URL and camelCase JSON payload")]
	public void StartLogger_SendsExpectedPayload() {
		// Arrange
		var options = CreateOptions();
		string capturedUrl = null;
		string capturedPayload = null;
		_applicationClient
			.When(x => x.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>()))
			.Do(ci => {
				capturedUrl = ci.ArgAt<string>(0);
				capturedPayload = ci.ArgAt<string>(1);
			});

		// Act
		InvokeStartLogger(options);

		// Assert
		capturedUrl.Should().Be("http://test.domain.com/rest/ATFLogService/StartLogBroadcast",
			"because StartLogger must use StartLogBroadcast route");
		capturedPayload.Should().NotBeNullOrWhiteSpace("because payload must be serialized");
		using var doc = JsonDocument.Parse(capturedPayload);
		var root = doc.RootElement;
		root.GetProperty("logLevelStr").GetString().Should().Be(options.LogLevel);
		root.GetProperty("bufferSize").GetInt32().Should().Be(1);
		root.GetProperty("loggerPattern").GetString().Should().Be(options.LogPattern);
		root.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(new[] {
			"logLevelStr", "bufferSize", "loggerPattern"
		}, "because property naming policy must be camelCase and include only required fields");
	}

	[Test]
	[Description("StopLogger sends POST request with empty payload to ResetConfiguration route")]
	public void StopLogger_SendsEmptyPayload() {
		// Arrange
		string capturedUrl = null;
		string capturedPayload = null;
		_applicationClient
			.When(x => x.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>()))
			.Do(ci => {
				capturedUrl = ci.ArgAt<string>(0);
				capturedPayload = ci.ArgAt<string>(1);
			});

		// Act
		InvokeStopLogger();

		// Assert
		capturedUrl.Should().Be("http://test.domain.com/rest/ATFLogService/ResetConfiguration",
			"because StopLogger must use StopLogBroadcast route");
		capturedPayload.Should().BeEmpty("because StopLogger sends empty payload");
	}

	[Test]
	[Description("Telemetry message triggers console logging when not silent and file not set")]
	public void TelemetryMessage_LogsToConsole_WhenNotSilent() {
		// Arrange
		var message = CreateWsMessage("TelemetryService", "Test telemetry message");
		// _silent defaults to false

		// Act
		_applicationClient.MessageReceived += Raise.Event<EventHandler<WsMessage>>(this, message);

		// Assert
		_logger.Received(1).WriteLine("Test telemetry message");
		_fileSystem.DidNotReceive().AppendTextToFile(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<System.Text.Encoding>());
	}

	[Test]
	[Description("Telemetry message writes to file when FileName is provided")]
	public void TelemetryMessage_WritesToFile_WhenFileNameProvided() {
		// Arrange
		SetPrivateField("_logFilePath", "logs.txt");
		var message = CreateWsMessage("TelemetryService", "Msg1");

		// Act
		_applicationClient.MessageReceived += Raise.Event<EventHandler<WsMessage>>(this, message);

		// Assert
		_fileSystem.Received(1).AppendTextToFile(
			"logs.txt",
			Arg.Is<string>(s => s.Contains("Msg1")),
			Arg.Any<System.Text.Encoding>());
	}

	[Test]
	[Description("Silent option suppresses console output but still writes to file if path set")]
	public void SilentMode_SuppressesConsoleOutput() {
		// Arrange
		SetPrivateField("_logFilePath", "logs.txt");
		SetPrivateField("_silent", true);
		var message = CreateWsMessage("TelemetryService", "HiddenMsg");

		// Act
		_applicationClient.MessageReceived += Raise.Event<EventHandler<WsMessage>>(this, message);

		// Assert
		_logger.DidNotReceive().WriteLine(Arg.Is<string>(s => s.Contains("HiddenMsg")));
		_fileSystem.Received(1).AppendTextToFile(
			"logs.txt",
			Arg.Is<string>(s => s.Contains("HiddenMsg")),
			Arg.Any<System.Text.Encoding>());
	}

	[Test]
	[Description("Silent option with no file suppresses all output and no file operations occur")]
	public void SilentMode_NoFile_NoConsoleNoFileWrite() {
		// Arrange
		SetPrivateField("_silent", true);
		var message = CreateWsMessage("TelemetryService", "StealthMsg");

		// Act
		_applicationClient.MessageReceived += Raise.Event<EventHandler<WsMessage>>(this, message);

		// Assert
		_logger.DidNotReceive().WriteLine(Arg.Is<string>(s => s.Contains("StealthMsg")));
		_fileSystem.DidNotReceive().AppendTextToFile(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<System.Text.Encoding>());
	}

	[Test]
	[Description("Non telemetry message is ignored")]
	public void NonTelemetryMessage_IsIgnored() {
		// Arrange
		var message = CreateWsMessage("OtherService", "Noise");

		// Act
		_applicationClient.MessageReceived += Raise.Event<EventHandler<WsMessage>>(this, message);

		// Assert
		_logger.DidNotReceive().WriteLine("Noise");
		_fileSystem.DidNotReceive().AppendTextToFile(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<System.Text.Encoding>());
	}

	[Test]
	[Description("Connection state change writes expected message")]
	public void ConnectionStateChanged_LogsState() {
		// Act
		_applicationClient.ConnectionStateChanged += Raise.Event<EventHandler<WebSocketState>>(this, WebSocketState.Open);

		// Assert
		_logger.Received(1).WriteLine("Connection state changed to Open");
	}

	[Test]
	[Description("Dispose unsubscribes events so further raises do not log")]
	public void Dispose_UnsubscribesEvents() {
		// Arrange
		_applicationClient.ConnectionStateChanged += Raise.Event<EventHandler<WebSocketState>>(this, WebSocketState.Open);
		_logger.ClearReceivedCalls();

		// Act
		_sut.Dispose();
		_applicationClient.ConnectionStateChanged += Raise.Event<EventHandler<WebSocketState>>(this, WebSocketState.Closed);

		// Assert
		_logger.DidNotReceive().WriteLine("Connection state changed to Closed");
	}

	[Test]
	[Description("Dispose unsubscribes MessageReceived so telemetry after dispose is not logged")]
	public void Dispose_UnsubscribesMessageReceived() {
		// Arrange
		var message = CreateWsMessage("TelemetryService", "AfterDisposeMsg");
		_sut.Dispose();

		// Act
		_applicationClient.MessageReceived += Raise.Event<EventHandler<WsMessage>>(this, message);

		// Assert
		_logger.DidNotReceive().WriteLine("AfterDisposeMsg");
	}

	[Test]
	[Description("Dispose can be called multiple times without throwing")]
	public void Dispose_IsIdempotent() {
		// Act
		Action act = () => {
			_sut.Dispose();
			_sut.Dispose();
			_sut.Dispose();
		};

		// Assert
		act.Should().NotThrow("because disposal must be idempotent");
	}

	[Test]
	[Description("StartLogger honors Silent option only for message output, still sends start request")]
	public void StartLogger_IgnoresSilentForStartRequest() {
		// Arrange
		var options = CreateOptions(silent: true);

		// Act
		InvokeStartLogger(options);

		// Assert
		_applicationClient.Received(1).ExecutePostRequest(
			"http://test.domain.com/rest/ATFLogService/StartLogBroadcast",
			Arg.Is<string>(p => p.Contains("\"logLevelStr\":\"Debug\"")));
	}

	private class TestConsole : IConsole {
		public bool KeyRead { get; private set; }
		public bool KeyAvailable => true;
		public ConsoleKeyInfo ReadKey() { KeyRead = true; return new ConsoleKeyInfo('q', ConsoleKey.Q, false,false,false); }
	}

	[Test]
	[Description("Execute performs full lifecycle: sets internal fields, calls Listen, sends start and stop requests, cancels token after key press")]
	public void Execute_PerformsLifecycle() {
		// Arrange
		var testConsole = new TestConsole();
		CancellationToken capturedToken = default;
		_applicationClient
			.When(a => a.Listen(Arg.Any<CancellationToken>()))
			.Do(ci => capturedToken = ci.ArgAt<CancellationToken>(0));
		_serviceUrlBuilder.ClearReceivedCalls();
		_serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.StartLogBroadcast)
			.Returns("http://test/start");
		_serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.StopLogBroadcast)
			.Returns("http://test/stop");
		var command = new ListenCommand(_applicationClient, _logger, _fileSystem, _serviceUrlBuilder, testConsole);
		var options = new ListenOptions { LogLevel = "Info", LogPattern = "NoiseFilter", FileName = "exec.log", Silent = true };
		string startPayload = null;
		_applicationClient
			.When(x => x.ExecutePostRequest("http://test/start", Arg.Any<string>()))
			.Do(ci => startPayload = ci.ArgAt<string>(1));
		// Act
		int result = command.Execute(options);
		// Assert
		result.Should().Be(0, "because Execute should return 0 on success");
		_applicationClient.Received(1).Listen(Arg.Any<CancellationToken>());
		capturedToken.IsCancellationRequested.Should().BeTrue("because token must be cancelled after key press");
		testConsole.KeyRead.Should().BeTrue("because Execute must read a key to terminate listening");
		_applicationClient.Received(1).ExecutePostRequest("http://test/start", Arg.Any<string>());
		_applicationClient.Received(1).ExecutePostRequest("http://test/stop", string.Empty);
		startPayload.Should().NotBeNull("because start payload must be serialized");
		startPayload.Should().Contain("\"logLevelStr\":\"Info\"", "because it must include provided log level");
		startPayload.Should().Contain("\"loggerPattern\":\"NoiseFilter\"", "because it must include provided pattern");
		// Internal fields reflection
		var logFileField = typeof(ListenCommand).GetField("_logFilePath", BindingFlags.NonPublic | BindingFlags.Instance);
		var silentField = typeof(ListenCommand).GetField("_silent", BindingFlags.NonPublic | BindingFlags.Instance);
		logFileField.GetValue(command).Should().Be("exec.log", "because Execute must store FileName internally");
		silentField.GetValue(command).Should().Be(true, "because Execute must store Silent flag internally");
	}

	public void Dispose() { _sut?.Dispose(); }
}
