using Clio.Command;
using Clio.Common;
using CommandLine;
using System;

namespace Clio
{

	[Verb("link-from-repository", Aliases = new[] { "l4r", "link4repo" }, HelpText = "Link repository package(s) to environment.")]
	internal class Link4RepoOptions
	{

		[Option('r', "repoPath", Required = true,
			HelpText = "Path to package repository folder", Default = null)]
		public string RepoPath
		{
			get; set;
		}

		[Option('e', "envPkgPath", Required = true,
			HelpText = "Path to environment package folder ({LOCAL_CREATIO_PATH}Terrasoft.WebApp\\Terrasoft.Configuration\\Pkg)", Default = null)]
		public string envPkgPath
		{
			get; set;
		}

		[Option('p', "packages", Required = false,
			HelpText = "Package(s)", Default = null)]
		public string Packages { get; set; }
	}

	internal class Link4RepoCommand : Command<Link4RepoOptions>
	{
		public override int Execute(Link4RepoOptions options) {
			if (OperationSystem.Current.IsWindows) {
				RfsEnvironment.Link4Repo(options.envPkgPath, options.RepoPath, options.Packages);
				Console.WriteLine("Done.");
				return 0;
			}
			Console.WriteLine("Clio mklink command is only supported on: 'windows'.");
			return 1;
		}
	}
}