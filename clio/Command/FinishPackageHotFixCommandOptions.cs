using CommandLine;

namespace Clio.Command;

[Verb("finish-pkg-hotfix", Aliases = new string[] { "finish-pkg-hotfix" }, HelpText = "Finishes hotfix state for package.")]
public class FinishPackageHotFixCommandOptions : EnvironmentOptions
{
	[Value(0, MetaName = "Name", Required = true, HelpText = "Package name")]
	public string PackageName { get; set; }
}