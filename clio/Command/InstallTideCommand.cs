using Clio.Project.NuGet;
using CommandLine;
using System;
using System.Threading;

namespace Clio.Command
{
	[Verb("install-tide", Aliases = new string[] { "tide", "itide" }, HelpText = "Install T.I.D.E. to the environment")]
	public class InstallTideCommandOptions: EnvironmentNameOptions
	{
	}

	internal class InstallTideCommand : RemoteCommand<InstallTideCommandOptions>
	{
		private readonly InstallNugetPackageCommand _installNugetPackageCommand;
		private readonly InstallGatePkgCommand _installGatePkgCommand;
		private readonly HealthCheckCommand _healthCheckCommand;

		public InstallTideCommand(
			InstallNugetPackageCommand installNugetPackageCommand,
			InstallGatePkgCommand installGatePkgCommand,
			HealthCheckCommand healthCheckCommand)
		{
			_installNugetPackageCommand = installNugetPackageCommand;
			_installGatePkgCommand = installGatePkgCommand;
			_healthCheckCommand = healthCheckCommand;
		}

		public override int Execute(InstallTideCommandOptions options)
		{
			int gateResult = InstallGateForEnvironment(options);
			if (gateResult != 0)
			{
				Logger.WriteError("[TIDE] Gate installation failed. Tide installation will not proceed.");
				return gateResult;
			}

			if (!WaitForServerReady(options))
			{
				Logger.WriteError("[TIDE] Server did not become available after gate install. Tide installation will not proceed.");
				return 1;
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

		private bool WaitForServerReady(InstallTideCommandOptions options)
		{
			const int maxAttempts = 3;
			const int delaySeconds = 5;
			for (int attempt = 1; attempt <= maxAttempts; attempt++)
			{
				var healthOptions = new HealthCheckOptions
				{
					WebApp = "true"
				};
				healthOptions.CopyFromEnvironmentSettings(options);
				int result = _healthCheckCommand.Execute(healthOptions);
				if (result == 0)
				{
					Logger.WriteInfo($"[TIDE] Server is available after {attempt} attempt(s).");
					return true;
				}
				Logger.WriteInfo($"[TIDE] Waiting for server to become available... Attempt {attempt}/{maxAttempts}");
				Thread.Sleep(delaySeconds * 1000);
			}
			return false;
		}
	}

}

