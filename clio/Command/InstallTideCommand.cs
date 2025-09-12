#region

using System.Threading;
using Clio.Common;
using CommandLine;

#endregion

namespace Clio.Command;

#region Class: InstallTideCommandOptions

[Verb("install-tide", Aliases = ["tide", "itide"], HelpText = "Install T.I.D.E. to the environment")]
public class InstallTideCommandOptions : EnvironmentNameOptions{ }

#endregion


#region Class: InstallTideCommand

public class InstallTideCommand : Command<InstallTideCommandOptions>{
	#region Fields: Private

	private readonly HealthCheckCommand _healthCheckCommand;
	private readonly InstallGatePkgCommand _installGatePkgCommand;
	private readonly InstallNugetPackageCommand _installNugetPackageCommand;
	private readonly ILogger _logger;

	#endregion

	#region Constructors: Public

	public InstallTideCommand(
		InstallNugetPackageCommand installNugetPackageCommand,
		InstallGatePkgCommand installGatePkgCommand,
		HealthCheckCommand healthCheckCommand, ILogger logger) {
		_installNugetPackageCommand = installNugetPackageCommand;
		_installGatePkgCommand = installGatePkgCommand;
		_healthCheckCommand = healthCheckCommand;
		_logger = logger;
	}

	#endregion

	#region Methods: Private

	private int InstallGateForEnvironment(InstallTideCommandOptions options) {
		InstallGateOptions gateOptions = new();
		gateOptions.CopyFromEnvironmentSettings(options);

		PushPkgOptions opts = Program.CreateClioGatePkgOptions(gateOptions);
		return _installGatePkgCommand.Execute(opts);
	}

	private int InstallTideForEnvironment(InstallTideCommandOptions options) {
		InstallNugetPkgOptions installNugetPackageCommandOptions = new() {
			Names = "atftide"
		};
		installNugetPackageCommandOptions.CopyFromEnvironmentSettings(options);
		return _installNugetPackageCommand.Execute(installNugetPackageCommandOptions);
	}

	private bool WaitForServerReady(InstallTideCommandOptions options) {
		const int maxAttempts = 3;
		const int delaySeconds = 5;
		for (int attempt = 1; attempt <= maxAttempts; attempt++) {
			HealthCheckOptions healthOptions = new() {
				WebApp = "true"
			};
			healthOptions.CopyFromEnvironmentSettings(options);
			int result = _healthCheckCommand.Execute(healthOptions);
			if (result == 0) {
				_logger.WriteInfo($"[TIDE] Server is available after {attempt} attempt(s).");
				return true;
			}

			_logger.WriteInfo($"[TIDE] Waiting for server to become available... Attempt {attempt}/{maxAttempts}");
			Thread.Sleep(delaySeconds * 1000);
		}

		return false;
	}

	#endregion

	#region Methods: Public

	public override int Execute(InstallTideCommandOptions options) {
		int gateResult = InstallGateForEnvironment(options);
		if (gateResult != 0) {
			_logger.WriteError("[TIDE] Gate installation failed. Tide installation will not proceed.");
			return gateResult;
		}

		Thread.Sleep(5_000);
		if (!WaitForServerReady(options)) {
			_logger.WriteError(
				"[TIDE] Server did not become available after gate install. Tide installation will not proceed.");
			return 1;
		}

		return InstallTideForEnvironment(options);
	}

	#endregion
}

#endregion


