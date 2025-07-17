using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using Clio.Command;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

public class LastCompilationLogCommandTestFixture : BaseCommandTests<LastCompilationLogOptions> {

	#region Fields: Private

	private IApplicationClient _applicationClientMock;
	private StringWriter _textWriter;
	private StringBuilder _sb;
	private TextWriter _originalTextWriter;

	#endregion

	#region Methods: Protected

	
	[OneTimeSetUp]
	public void OneTimeSetUp(){
		ConsoleLogger.Instance.Start();
		_applicationClientMock = Substitute.For<IApplicationClient>();
		_sb = new();
		_textWriter = new(_sb);
		_originalTextWriter = Console.Out;
		Console.SetOut(_textWriter);
	}
	
	
	protected override void AdditionalRegistrations(ContainerBuilder containerBuilder){
		base.AdditionalRegistrations(containerBuilder);
		containerBuilder.RegisterInstance(_applicationClientMock).As<IApplicationClient>();
	}

	[OneTimeTearDown]
	public void TearDown(){
		Console.SetOut(_originalTextWriter);
		_sb.Clear();
		_textWriter.Flush();
		_textWriter.Close();
		_textWriter.Dispose();
	}
	
	public override void Setup(){
		_applicationClientMock = Substitute.For<IApplicationClient>();
		_sb.Clear();
		_textWriter.Flush();
		base.Setup();
	}

	#endregion

	[Test]
	public void Execute_ShouldReturnOne_WhenServiceThrowsException(){
		//Arrange
		const string expectedErrorMessage = "error";
		_applicationClientMock.When(x => x.ExecuteGetRequest(Arg.Any<string>()))
			.Do(x => throw new Exception(expectedErrorMessage));
		LastCompilationLogCommand command = Container.Resolve<LastCompilationLogCommand>();
		
		//Act
		int result = command.Execute(new LastCompilationLogOptions());

		//Assert
		result.Should().Be(1);
		Thread.Sleep(500);
		_textWriter.ToString().Should().Contain($"[ERR] - {expectedErrorMessage}{Environment.NewLine}");
	}

	[TestCase("Examples/CompilationLog/Pair1/pair1-creatio-compilation-log.json","Examples/CompilationLog/Pair1/pair1-desired-output.txt")]
	[TestCase("Examples/CompilationLog/Pair2/pair2-creatio-compilation-log.json","Examples/CompilationLog/Pair2/pair2-desired-output.txt")]
	public void  Execute_ShouldReturnZero_WhenServiceReturnsResult(string input, string expectedOutput){
		//Arrange
		string desiredOutputContent = System.IO.File.ReadAllText(expectedOutput);
		string inputContent = System.IO.File.ReadAllText(input);
		_applicationClientMock.ExecuteGetRequest(Arg.Any<string>())
			.Returns(inputContent);
		
		LastCompilationLogCommand command = Container.Resolve<LastCompilationLogCommand>();

		//Act
		int result = command.Execute(new LastCompilationLogOptions());
		
		string NormalizeLineEndings(string text) =>
		    text.Replace("\r\n", "\n").Replace("\r", "\n");
		//Assert
		result.Should().Be(0);
		Thread.Sleep(500);
		NormalizeLineEndings(_textWriter.ToString()).TrimEnd().Should().Contain(NormalizeLineEndings(desiredOutputContent));
		//_textWriter.ToString().TrimEnd().Should().Contain(desiredOutputContent);
	}
	
	[TestCase("Examples/CompilationLog/Pair1/pair1-creatio-compilation-log.json","Examples/CompilationLog/Pair1/pair1-desired-output.txt")]
	[TestCase("Examples/CompilationLog/Pair2/pair2-creatio-compilation-log.json","Examples/CompilationLog/Pair2/pair2-desired-output.txt")]
	public void  Execute_ShouldReturnRawJson_WhenRawOptionUsed(string input, string expectedOutput){
		//Arrange
		string inputContent = File.ReadAllText(input);
		_applicationClientMock.ExecuteGetRequest(Arg.Any<string>())
			.Returns(inputContent);
		
		LastCompilationLogCommand command = Container.Resolve<LastCompilationLogCommand>();

		//Act
		int result = command.Execute(new LastCompilationLogOptions{IsRaw = true});
		
		
		//Assert
		result.Should().Be(0);
		Thread.Sleep(500);
		_textWriter.ToString().TrimEnd().Should().Be(inputContent);
	}
	

}