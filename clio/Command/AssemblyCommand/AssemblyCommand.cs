using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using CommandLine;

namespace clio.Command.AssemblyCommand
{
	[Verb("execute-assembly-code", Aliases = new string[] { "exec" }, HelpText = "Execute an assembly code which implements the IExecutor interface")]
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

		public static int ExecuteCodeFromAssmebly(ExecuteAssemblyOptions options) {
			Configure(options);
			ExecuteCodeFromAssemblyInternal(options);
			return 0;
		}
	}
}
