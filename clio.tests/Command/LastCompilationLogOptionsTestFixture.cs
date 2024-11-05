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

public class LastCompilationLogOptionsTestFixture : BaseCommandTests<LastCompilationLogOptions> {

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
		_textWriter.ToString().Should().Be($"[ERR] - {expectedErrorMessage}{Environment.NewLine}");
	}

	[Test]
	public void  Execute_ShouldReturnZero_WhenServiceReturnsResult(){
		//Arrange
		const string expectedResult = "result";
		_applicationClientMock.ExecuteGetRequest(Arg.Any<string>())
			.Returns(expectedResult);

		LastCompilationLogCommand command = Container.Resolve<LastCompilationLogCommand>();

		//Act
		int result = command.Execute(new LastCompilationLogOptions());
		
		
		//Assert
		result.Should().Be(0);
		Thread.Sleep(500);
		_textWriter.ToString().Should().Be(expectedResult + Environment.NewLine);
	}
	

}