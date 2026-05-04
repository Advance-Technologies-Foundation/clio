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
	public void Execute_ShouldReturnOne_WhenServiceThrowsException(){
		//Arrange
		const string expectedErrorMessage = "error";
		_applicationClientMock.When(x => x.ExecuteGetRequest(Arg.Any<string>()))
			.Do(x => throw new Exception(expectedErrorMessage));
		LastCompilationLogCommand command = Container.GetRequiredService<LastCompilationLogCommand>();

		//Act
		int result = command.Execute(new LastCompilationLogOptions());

		//Assert
		result.Should().Be(1);
		IReadOnlyList<LogMessage> messages = Logger.FlushAndSnapshotMessages(clearMessages: true);
		messages.OfType<ErrorMessage>().Should()
			.ContainSingle(m => m.Value.ToString() == expectedErrorMessage);
	}

	[TestCase("Examples/CompilationLog/Pair1/pair1-creatio-compilation-log.json","Examples/CompilationLog/Pair1/pair1-desired-output.txt")]
	[TestCase("Examples/CompilationLog/Pair2/pair2-creatio-compilation-log.json","Examples/CompilationLog/Pair2/pair2-desired-output.txt")]
	public void Execute_ShouldReturnZero_WhenServiceReturnsResult(string input, string expectedOutput){
		//Arrange
		string desiredOutputContent = File.ReadAllText(expectedOutput);
		string inputContent = File.ReadAllText(input);
		_applicationClientMock.ExecuteGetRequest(Arg.Any<string>())
			.Returns(inputContent);
		LastCompilationLogCommand command = Container.GetRequiredService<LastCompilationLogCommand>();

		//Act
		int result = command.Execute(new LastCompilationLogOptions());

		//Assert
		result.Should().Be(0);
		string NormalizeLineEndings(string text) => text.Replace("\r\n", "\n").Replace("\r", "\n");
		IReadOnlyList<LogMessage> messages = Logger.FlushAndSnapshotMessages(clearMessages: true);
		string messageText = NormalizeLineEndings(messages.Single().Value?.ToString() ?? string.Empty).TrimEnd();
		messageText.Should().Contain(NormalizeLineEndings(desiredOutputContent));
	}

	[TestCase("Examples/CompilationLog/Pair1/pair1-creatio-compilation-log.json","Examples/CompilationLog/Pair1/pair1-desired-output.txt")]
	[TestCase("Examples/CompilationLog/Pair2/pair2-creatio-compilation-log.json","Examples/CompilationLog/Pair2/pair2-desired-output.txt")]
	public void Execute_ShouldReturnRawJson_WhenRawOptionUsed(string input, string expectedOutput){
		//Arrange
		string inputContent = File.ReadAllText(input);
		_applicationClientMock.ExecuteGetRequest(Arg.Any<string>())
			.Returns(inputContent);
		LastCompilationLogCommand command = Container.GetRequiredService<LastCompilationLogCommand>();

		//Act
		int result = command.Execute(new LastCompilationLogOptions{IsRaw = true});

		//Assert
		result.Should().Be(0);
		IReadOnlyList<LogMessage> messages = Logger.FlushAndSnapshotMessages(clearMessages: true);
		messages.Should().ContainSingle(m => m.Value.ToString() == inputContent);
	}

}
