namespace Clio.Command
{
	using CommandLine;
	using System;
	using Clio.Common;

	[Verb("link-to-repository", Aliases = new[] { "l2r", "link2repo" }, HelpText = "Link environment package(s) to repository.")]
	internal class Link2RepoOptions
	{
		[Option('r',"repoPath", Required = true,
			HelpText = "Path to package repository folder", Default = null)]
		public string RepoPath
		{
			get; set;
		}

		[Option('e',"envPkgPath", Required = true,
			HelpText = "Path to environment package folder ({LOCAL_CREATIO_PATH}Terrasoft.WebApp\\Terrasoft.Configuration\\Pkg)", Default = null)]
		public string envPkgPath
		{
			get; set;
		}

	}

	class Link2RepoCommand : Command<Link2RepoOptions>
	{

		public override int Execute(Link2RepoOptions options) {
			try {
				if (OperationSystem.Current.IsWindows) {
					RfsEnvironment.Link2Repo(options.envPkgPath, options.RepoPath);
					Console.WriteLine("Done.");
					return 0;
				}
				Console.WriteLine("Clio mklink command is only supported on: 'windows'.");
				return 1;
			} catch (Exception e) {
				Console.WriteLine(e);
				return 1;
			}
		}

	}
}
