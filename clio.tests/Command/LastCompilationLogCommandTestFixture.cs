using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Clio.Command;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

[NonParallelizable]
[Property("Module", "Command")]
public class LastCompilationLogCommandTestFixture : BaseCommandTests<LastCompilationLogOptions> {

	#region Fields: Private

	private IApplicationClient _applicationClientMock;
	private static ConsoleLogger Logger => (ConsoleLogger)ConsoleLogger.Instance;

	#endregion

	#region Methods: Protected

	protected override void AdditionalRegistrations(IServiceCollection containerBuilder){
		base.AdditionalRegistrations(containerBuilder);
		containerBuilder.AddSingleton<IApplicationClient>(_applicationClientMock);
	}

	[OneTimeTearDown]
	public void OneTimeTearDown(){
		Logger.ClearMessages();
		Logger.PreserveMessages = false;
	}

	public override void Setup(){
		_applicationClientMock = Substitute.For<IApplicationClient>();
		Logger.PreserveMessages = true;
		Logger.ClearMessages();
		base.Setup();
	}

	#endregion

	[Test]
	[Description("Returns exit code one and logs the service error when the compilation-result endpoint fails.")]
	public void Execute_ShouldReturnOne_WhenServiceThrowsException(){
		// Arrange
		const string expectedErrorMessage = "error";
		_applicationClientMock.When(x => x.ExecuteGetRequest(Arg.Any<string>()))
			.Do(x => throw new Exception(expectedErrorMessage));
		LastCompilationLogCommand command = Container.GetRequiredService<LastCompilationLogCommand>();

		// Act
		int result = command.Execute(new LastCompilationLogOptions());

		// Assert
		result.Should().Be(1, because: "endpoint failures must produce a non-zero CLI exit code");
		IReadOnlyList<LogMessage> messages = Logger.FlushAndSnapshotMessages(clearMessages: true);
		messages.OfType<ErrorMessage>().Should()
			.ContainSingle(m => m.Value.ToString() == expectedErrorMessage,
				because: "the CLI should surface the endpoint failure to the user");
	}

	[TestCase("Examples/CompilationLog/Pair1/pair1-creatio-compilation-log.json","Examples/CompilationLog/Pair1/pair1-desired-output.txt")]
	[TestCase("Examples/CompilationLog/Pair2/pair2-creatio-compilation-log.json","Examples/CompilationLog/Pair2/pair2-desired-output.txt")]
	[Description("Formats a valid Creatio compilation-result response and exits successfully.")]
	public void Execute_ShouldReturnZero_WhenServiceReturnsResult(string input, string expectedOutput){
		// Arrange
		string desiredOutputContent = File.ReadAllText(expectedOutput);
		string inputContent = File.ReadAllText(input);
		_applicationClientMock.ExecuteGetRequest(Arg.Any<string>())
			.Returns(inputContent);
		LastCompilationLogCommand command = Container.GetRequiredService<LastCompilationLogCommand>();

		// Act
		int result = command.Execute(new LastCompilationLogOptions());

		// Assert
		result.Should().Be(0, because: "retrieving and formatting a valid payload is successful");
		string NormalizeLineEndings(string text) => text.Replace("\r\n", "\n").Replace("\r", "\n");
		IReadOnlyList<LogMessage> messages = Logger.FlushAndSnapshotMessages(clearMessages: true);
		string messageText = NormalizeLineEndings(messages.Single().Value?.ToString() ?? string.Empty).TrimEnd();
		messageText.Should().Contain(NormalizeLineEndings(desiredOutputContent).TrimEnd(),
			because: "the CLI should print the formatted compilation diagnostics");
	}

	[TestCase("Examples/CompilationLog/Pair1/pair1-creatio-compilation-log.json")]
	[TestCase("Examples/CompilationLog/Pair2/pair2-creatio-compilation-log.json")]
	[Description("Prints the untouched Creatio JSON payload when raw output is requested.")]
	public void Execute_ShouldReturnRawJson_WhenRawOptionUsed(string input){
		// Arrange
		string inputContent = File.ReadAllText(input);
		_applicationClientMock.ExecuteGetRequest(Arg.Any<string>())
			.Returns(inputContent);
		LastCompilationLogCommand command = Container.GetRequiredService<LastCompilationLogCommand>();

		// Act
		int result = command.Execute(new LastCompilationLogOptions{IsRaw = true});

		// Assert
		result.Should().Be(0, because: "retrieving a raw payload is successful");
		IReadOnlyList<LogMessage> messages = Logger.FlushAndSnapshotMessages(clearMessages: true);
		messages.Should().ContainSingle(m => m.Value.ToString() == inputContent,
			because: "raw mode must preserve the endpoint response exactly");
	}

	[Test]
	[Description("Returns a typed compilation result for MCP callers using the same Creatio endpoint as the CLI.")]
	public void GetLastCompilationResult_ShouldReturnTypedResponse_WhenServiceReturnsJson(){
		// Arrange
		const string input = """
			{"errors":[],"buildResult":0,"success":true}
			""";
		_applicationClientMock.ExecuteGetRequest(Arg.Any<string>()).Returns(input);
		LastCompilationLogCommand command = Container.GetRequiredService<LastCompilationLogCommand>();

		// Act
		CreatioCompilationLogResponse response = command.GetLastCompilationResult();

		// Assert
		response.success.Should().BeTrue(because: "the typed result must preserve Creatio's success flag");
		response.errors.Should().BeEmpty(because: "the typed result must preserve the diagnostics collection");
		_applicationClientMock.Received(1).ExecuteGetRequest(Arg.Is<string>(url =>
			url.EndsWith("/api/ConfigurationStatus/GetLastCompilationResult", StringComparison.Ordinal)));
	}

}
