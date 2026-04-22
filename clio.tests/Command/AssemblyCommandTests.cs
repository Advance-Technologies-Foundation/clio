using System;
using System.Text.Json;
using Clio.Command;
using Clio.Common;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
[Property("Module", "Command")]
public class AssemblyCommandTestCase : BaseCommandTests<ExecuteAssemblyOptions>
{

	#region Fields: Private

	private readonly IApplicationClient _applicationClientMock = Substitute.For<IApplicationClient>();
	private readonly ILogger _loggerMock = Substitute.For<ILogger>();

	#endregion

	#region Methods: Private

	private bool CheckRequest(string body, string executorType){
		try {
			ExecuteScriptRequest reqObj = JsonSerializer.Deserialize<ExecuteScriptRequest>(body);
			return reqObj.LibraryType == executorType && reqObj.Body.Length > 0;
		} catch (Exception) {
			return false;
		}
	}

	#endregion

	#region Methods: Protected

	protected override void AdditionalRegistrations(IServiceCollection containerBuilder){
		containerBuilder.AddSingleton<IApplicationClient>(_applicationClientMock);
		containerBuilder.AddSingleton<ILogger>(_loggerMock);
		base.AdditionalRegistrations(containerBuilder);
	}

	#endregion

	[Test]
	[Category("Unit")]
	public void Execute_ShouldWriteResponse_WhenItIsSuccessful(){
		AssemblyCommand command = Container.GetRequiredService<AssemblyCommand>();
		command.Logger = _loggerMock;

		string executorType = typeof(AssemblyCommand).FullName;
		string assemblyPath = "/test-assembly.dll";
		FileSystem.AddFile(assemblyPath, new System.IO.Abstractions.TestingHelpers.MockFileData([0x4D, 0x5A, 0x90, 0x00]));

		_applicationClientMock.ExecutePostRequest(
				Arg.Is<string>(path => path.EndsWith("/IDE/ExecuteScript")),
				Arg.Is<string>(request => CheckRequest(request, executorType)),
				Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>()
				)
			.Returns("responseFromServer");

		command.Execute(new ExecuteAssemblyOptions {
			ExecutorType = executorType,
			Name = assemblyPath,
			WriteResponse = true,
		});

		_loggerMock.Received(1).WriteInfo("responseFromServer");
		_loggerMock.ClearReceivedCalls();
	}

}