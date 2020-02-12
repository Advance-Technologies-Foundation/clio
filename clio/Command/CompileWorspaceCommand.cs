using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using CommandLine;
using Creatio.Client;
using Newtonsoft.Json;

namespace Clio.Command
{
	[Verb("build-workspace", Aliases = new[] { "build", "compile", "compile-all", "rebuild" }, HelpText = "Build/Rebuild worksapce for selected environment")]
	public class CompileOptions : EnvironmentOptions
	{
	}

	public class CompileWorkspaceCommand : BaseRemoteCommand
	{
		private static string ServiceUrl => AppUrl + @"/rest/CreatioApiGateway/CompileWorkspace";

		public static int Compile(CompileOptions options) {
			try {
				Configure(options);
				var scriptData = "{}";
				string responseFormServer = CreatioClient.ExecutePostRequest(ServiceUrl, scriptData, Timeout.Infinite);
				Console.WriteLine(responseFormServer);
				Console.WriteLine($"Done");
				return 0;
			} catch (Exception e) {
				Console.WriteLine("Error execute command...");
				Console.WriteLine(e.Message);
				return 1;
			}
		}

	}
}
