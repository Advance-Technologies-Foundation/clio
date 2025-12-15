using CommandLine;
using Clio.Common;

namespace Clio.Command;

/// <summary>
/// Options for the update-cli command to update clio to the latest version.
/// </summary>
[Verb("update-cli", Aliases = new[] { "update" }, HelpText = "Update clio to the latest available version")]
public class UpdateCliOptions : EnvironmentOptions {

	/// <summary>
	/// Install the tool globally (default: true).
	/// </summary>
	[Option('g', "global", Required = false, Default = true, 
		HelpText = "Install clio globally")]
	public bool Global { get; set; }

	/// <summary>
	/// Skip the interactive confirmation prompt and proceed with update (default: false).
	/// </summary>
	[Option('y', "no-prompt", Required = false, Default = false,
		HelpText = "Skip confirmation prompt and proceed with update automatically")]
	public bool NoPrompt { get; set; }

}
