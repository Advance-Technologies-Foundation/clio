using Clio.Project.NuGet;
using CommandLine;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Clio.Command
{

	[Verb("install-tide", Aliases = new string[] { "tide", "itide" }, HelpText = "Install T.I.D.E. to the environment")]
	public class InstallTideCommandOptions: EnvironmentNameOptions
	{

	}

	internal class InstallTideCommand : Command<InstallTideCommandOptions>
	{
		InstallNugetPackageCommand _installNugetPackageCommand;

		public InstallTideCommand(InstallNugetPackageCommand installNugetPackageCommand) {
			_installNugetPackageCommand = installNugetPackageCommand;
		}

		public override int Execute(InstallTideCommandOptions options) {
			var installNugetPackageCommandOptions = new InstallNugetPkgOptions {
				Names = "atftide",
			};
			installNugetPackageCommandOptions.CopyFromEnvironmentSettings(options);
			return _installNugetPackageCommand.Execute(installNugetPackageCommandOptions);
		}
	}

}
