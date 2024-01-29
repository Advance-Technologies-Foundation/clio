namespace Clio.Tests.Command;

using System;
using System.IO;
using System.Text.Json;
using Clio.Command;
using Clio.Common;
using NSubstitute;
using NUnit.Framework;

[TestFixture]
public class AssemblyCommandTestCase
{

	private TextWriter _defaultConsoleWriter;

	private bool CheckRequest(string body, string executorType) {
		try {
			var reqObj = JsonSerializer.Deserialize<ExecuteScriptRequest>(body);
			return reqObj.LibraryType == executorType && reqObj.Body.Length > 0;
		} catch (Exception) {
			return false;
		}
	}

	[SetUp]
	protected void Setup() {
		_defaultConsoleWriter = Console.Out;
	}

	[TearDown]
	protected void TearDown() {
		Console.SetOut(_defaultConsoleWriter);
	}

	[Test, Category("Unit")]
	public void Execute_ShouldWriteResponse_WhenItIsSuccessful() {
		var applicationClient = Substitute.For<IApplicationClient>();
		var command = new AssemblyCommand(applicationClient, new EnvironmentSettings());
		string executorType = typeof(AssemblyCommand).FullName;
		applicationClient.ExecutePostRequest(Arg.Is<string>(path => path.EndsWith("/IDE/ExecuteScript")),
			Arg.Is<string>(request => CheckRequest(request, executorType)))
			.Returns("responseFromServer");
		var output = new StringWriter();
		Console.SetOut(output);
		command.Execute(new ExecuteAssemblyOptions {
			ExecutorType = executorType,
			Name = new Uri(typeof(AssemblyCommand).Assembly.Location).LocalPath,
			WriteResponse = true
		});
		StringAssert.StartsWith("responseFromServer", output.ToString());
	}

}
