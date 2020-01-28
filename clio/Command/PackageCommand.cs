using System;
using Clio.Feature;
using CommandLine;
using Creatio.Client;

namespace Clio.Command
{
	[Verb("get-pkg-list", Aliases = new[] { "packages" }, HelpText = "Get environments packages")]
	public class PkgListOptions : EnvironmentOptions
	{
		[Option('s', "Search", Required = false, HelpText = "Contains name filter",
		Default = null)]
		public string SearchPattern { get; set; }
	}

	public class GetPkgListCommand : BaseRemoteCommand
	{
		private static string ServiceUrl => _appUrl + @"/rest/CreatioApiGateway/GetPackages";

		public static int GetPkgList(PkgListOptions options) {
			Configure(options);
			var scriptData = "{}";
			string responseFormServer = CreatioClient.ExecutePostRequest(ServiceUrl, scriptData);
			Console.WriteLine(responseFormServer);
			return 0;
		}

	}
}
