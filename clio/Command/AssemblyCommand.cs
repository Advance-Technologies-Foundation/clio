using System;
using System.IO;
using CommandLine;

namespace Clio.Command
{
	[Verb("execute-assembly-code", 
		Aliases = new[] { "exec" }, 
		HelpText = "Execute an assembly code which implements the IExecutor interface",
		Hidden = true)]
	internal class ExecuteAssemblyOptions : EnvironmentOptions
	{
		[Value(0, MetaName = "Name", Required = true, HelpText = "Path to executed assembly")]
		public string Name { get; set; }

		[Option('t', "ExecutorType", Required = true, HelpText = "Assembly type name for proceed")]
		public string ExecutorType { get; set; }
	}

	class AssemblyCommand: BaseRemoteCommand
	{
		private static string ExecutorUrl => _appUrl + @"/IDE/ExecuteScript";

		private static void ExecuteCodeFromAssemblyInternal(ExecuteAssemblyOptions options) {
			string filePath = options.Name;
			string executorType = options.ExecutorType;
			var fileContent = File.ReadAllBytes(filePath);
			string body = Convert.ToBase64String(fileContent);
			string requestData = @"{""Body"":""" + body + @""",""LibraryType"":""" + executorType + @"""}";
			var responseFromServer = CreatioClient.ExecutePostRequest(ExecutorUrl, requestData);
			Console.WriteLine(responseFromServer);
		}

		public static int ExecuteCodeFromAssembly(ExecuteAssemblyOptions options) {
			try {
				Configure(options);
				ExecuteCodeFromAssemblyInternal(options);
				Console.WriteLine();
				Console.WriteLine("Done");
				return 0;
			} catch (Exception e) {
				Console.WriteLine(e.Message);
				return 1;
			}
		}
	}
}
