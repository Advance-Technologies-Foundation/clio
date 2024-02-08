using ATF.Repository.Providers;
using Clio.Common;
using CommandLine;

namespace Clio.Command.PackageCommand;

[Verb("uninstall-app-remote", Aliases = new string[] { "uninstall" }, HelpText = "Uninstall application")]
public class UninstallAppOptions : BaseAppCommandOptions{}

	

public class UninstallAppCommand : BaseAppCommand<UninstallAppOptions>
{
	public UninstallAppCommand(IApplicationClient applicationClient, EnvironmentSettings settings, IDataProvider dataProvider, ApplicationManager applicationManager)
		: base(applicationClient, settings, dataProvider, applicationManager) {}
		

	protected override string ServicePath => @"/ServiceModel/AppInstallerService.svc/UninstallApp";
		
	protected override void ExecuteRemoteCommand(UninstallAppOptions options) {
		Logger.WriteInfo("Uninstalling application");
		base.ExecuteRemoteCommand(options);
	}

}