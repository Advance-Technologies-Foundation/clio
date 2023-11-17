using System;
using Clio.Common;
using CommandLine;

namespace Clio.Command;

[Verb("link-from-repository", Aliases = new[] {"l4r", "link4repo"},
	HelpText = "Link repository package(s) to environment.")]
internal class Link4RepoOptions
{

	#region Properties: Public

	[Option('e', "envPkgPath", Required = true,
		HelpText
			= @"Path to environment package folder ({LOCAL_CREATIO_PATH}Terrasoft.WebApp\Terrasoft.Configuration\Pkg)",
		Default = null)]
	public string EnvPkgPath { get; set; }

	[Option('p', "packages", Required = false,
		HelpText = "Package(s)", Default = null)]
	public string Packages { get; set; }

	[Option('r', "repoPath", Required = true,
		HelpText = "Path to package repository folder", Default = null)]
	public string RepoPath { get; set; }

	#endregion

}

internal class Link4RepoCommand : Command<Link4RepoOptions>
{

	#region Methods: Public

	public override int Execute(Link4RepoOptions options){
		if (OperationSystem.Current.IsWindows) {
			RfsEnvironment.Link4Repo(options.EnvPkgPath, options.RepoPath, options.Packages);
			Console.WriteLine("Done.");
			return 0;
		}
		Console.WriteLine("Clio mklink command is only supported on: 'windows'.");
		return 1;
	}

	#endregion

}