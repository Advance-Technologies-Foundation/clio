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
		private readonly InstallNugetPackageCommand _installNugetPackageCommand;
		private readonly InstallGatePkgCommand _installGatePkgCommand;

		public InstallTideCommand(
			InstallNugetPackageCommand installNugetPackageCommand,
			InstallGatePkgCommand installGatePkgCommand)
		{
			_installNugetPackageCommand = installNugetPackageCommand;
			_installGatePkgCommand = installGatePkgCommand;
		}

		public override int Execute(InstallTideCommandOptions options)
		{
			int gateResult = InstallGateForEnvironment(options);
			if (gateResult != 0)
			{
				Console.WriteLine("[TIDE] Gate installation failed. Tide installation will not proceed.");
				return gateResult;
			}
			return InstallTideForEnvironment(options);
		}

		private int InstallGateForEnvironment(InstallTideCommandOptions options)
		{
			   var gateOptions = new InstallGateOptions();
			   gateOptions.CopyFromEnvironmentSettings(options);
			   return _installGatePkgCommand.Execute(gateOptions);
		}

		private int InstallTideForEnvironment(InstallTideCommandOptions options)
		{
			var installNugetPackageCommandOptions = new InstallNugetPkgOptions
			{
				Names = "atftide",
			};
			installNugetPackageCommandOptions.CopyFromEnvironmentSettings(options);
			return _installNugetPackageCommand.Execute(installNugetPackageCommandOptions);
		}
	}

}
