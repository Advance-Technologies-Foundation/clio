using Clio.Common;
using CommandLine;
using System;
using Clio.Project.NuGet;

namespace Clio.Command
{
	[Verb("push-nuget-pkg", Aliases = new string[] { "push-n", "push-nuget" }, 
		HelpText = "Push package on NuGet server")]
	public class PushNuGetPkgsOptions : EnvironmentOptions
	{

		[Value(0, MetaName = "NugetPkgPath", Required = true, HelpText = "Nuget package file path")]
		public string NugetPkgPath { get; set; }

		[Option('k', "ApiKey", Required = true, HelpText = "The API key for the server")]
		public string ApiKey { get; set; }

		[Option('s', "Source", Required = true, HelpText = "Specifies the server URL")]
		public string SourceUrl { get; set; }

	}

	public class PushNuGetPackagesCommand : Command<PushNuGetPkgsOptions>
	{
		private INuGetManager _nugetManager;

		public PushNuGetPackagesCommand(INuGetManager nugetManager) {
			nugetManager.CheckArgumentNull(nameof(nugetManager));
			_nugetManager = nugetManager;
		}

		public override int Execute(PushNuGetPkgsOptions options) {
			try {
				_nugetManager.Push(options.NugetPkgPath, options.ApiKey, options.SourceUrl);
				Console.WriteLine("Done");
				return 0;
			} catch (Exception e) {
				Console.WriteLine(e.Message);
				return 1;
			}
		}

	}

}