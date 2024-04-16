using CommandLine;

namespace Clio.Command;

[Verb("start-pkg-hotfix", Aliases = new string[] { "start-pkg-hotfix" }, HelpText = "Starts hotfix state for package.")]
public class StartPackageHotFixCommandOptions : EnvironmentOptions
{
	[Value(0, MetaName = "Name", Required = true, HelpText = "Package name")]
	public string PackageName { get; set; }
}