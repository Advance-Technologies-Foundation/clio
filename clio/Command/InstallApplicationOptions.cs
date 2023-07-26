namespace Clio.Command
{
	using System;
	using Clio.Common;
	using Clio.Package;
	using CommandLine;

	#region class: InstallOptions

	public class InstallOptions : EnvironmentOptions
	{
		[Value(0, MetaName = "Name", Required = false, HelpText = "Package name")]
		public string Name
		{
			get; set;
		}

		[Option('r', "ReportPath", Required = false, HelpText = "Log file path")]
		public string ReportPath
		{
			get; set;
		}

	}

	#endregion

	#region Class: InstallApplicationOptions

	[Verb("install-application", Aliases = new string[] { "install-app", "push-app" }, HelpText = "Install application on a web application")]
	public class InstallApplicationOptions : InstallOptions
	{

	}

	#endregion

	#region Class: PushPackageCommand

	public class InstallApplicationCommand : Command<InstallApplicationOptions>
	{
		#region Fields: Private
		private readonly EnvironmentSettings _environmentSettings;
		private readonly IApplicationInstaller _applicationInstaller;
		private readonly IMarketplace _marketplace;
		private readonly PackageInstallOptions _packageInstallOptionsDefault = new PackageInstallOptions();
		#endregion

		#region Constructors: Public
		public InstallApplicationCommand(EnvironmentSettings environmentSettings, IApplicationInstaller applicationInstaller, IMarketplace marketplace) {
			environmentSettings.CheckArgumentNull(nameof(environmentSettings));
			applicationInstaller.CheckArgumentNull(nameof(applicationInstaller));
			_environmentSettings = environmentSettings;
			_applicationInstaller = applicationInstaller;
			_marketplace = marketplace;
		}

		#endregion

		#region Methods: Private
		private PackageInstallOptions ExtractPackageInstallOptions(InstallApplicationOptions options) {
			var packageInstallOptions = new PackageInstallOptions();
			return packageInstallOptions == _packageInstallOptionsDefault
				? null
				: packageInstallOptions;
		}
		#endregion

		#region Methods: Public

		public override int Execute(InstallApplicationOptions options) {
			bool success = false;
			try {
				success = _applicationInstaller.Install(options.Name, _environmentSettings,
					options.ReportPath);
				
				Console.WriteLine(success ? "Done" : "Error");
				return success ? 0 : 1;
			} catch (Exception e) {
				Console.WriteLine(e.StackTrace);
				return 1;
			}
		}
		#endregion
	}

	#endregion

}