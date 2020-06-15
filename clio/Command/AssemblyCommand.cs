using System;
using System.IO;
using Clio.Common;
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

	class AssemblyCommand : RemoteCommand<ExecuteAssemblyOptions>
	{
		protected override string ServicePath => @"/IDE/ExecuteScript";


		public AssemblyCommand(IApplicationClient applicationClient, EnvironmentSettings settings)
			: base(applicationClient, settings) {
		}

		protected override string GetRequestData(ExecuteAssemblyOptions options) {
			string filePath = options.Name;
			string executorType = options.ExecutorType;
			var fileContent = File.ReadAllBytes(filePath);
			string body = Convert.ToBase64String(fileContent);
			return @"{""Body"":""" + body + @""",""LibraryType"":""" + executorType + @"""}";
		}
	}
}
