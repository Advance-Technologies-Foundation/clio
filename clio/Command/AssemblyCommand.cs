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

		[Option('w', "WriteResponse", Required = false, HelpText = "")]
		public bool WriteResponse { get; set; }
	}

	class AssemblyCommand : RemoteCommand<ExecuteAssemblyOptions>
	{
		private ExecuteAssemblyOptions _options;
		protected override string ServicePath => @"/IDE/ExecuteScript";


		public AssemblyCommand(IApplicationClient applicationClient, EnvironmentSettings settings)
			: base(applicationClient, settings) {
		}

		protected override string GetRequestData(ExecuteAssemblyOptions options) {
			_options = options;
			string filePath = options.Name;
			string executorType = options.ExecutorType;
			var fileContent = File.ReadAllBytes(filePath);
			string body = Convert.ToBase64String(fileContent);
			return @"{""Body"":""" + body + @""",""LibraryType"":""" + executorType + @"""}";
		}

		protected override void ProceedResponse(string response, ExecuteAssemblyOptions options) {
			base.ProceedResponse(response, options);
			if (options.WriteResponse) {
				Console.WriteLine(response);
			}
		}
	}
}
