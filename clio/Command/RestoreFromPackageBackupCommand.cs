namespace Clio.Command
{
	using Clio.Common;
	using CommandLine;

	#region Class: RestoreFromPackageBackupOptions

	[Verb("restore-configuration", Aliases = new string[] { "restore", "rc" }, HelpText = "Restore configuration from last backup")]
	public class RestoreFromPackageBackupOptions : EnvironmentOptions
	{

		#region Properties: Public

		[Option('d', "skip-rollback-data", Required = false,
			HelpText ="Skip rollback data", Default = false)]
		public bool InstallPackageData {
			get; set;
		}

		[Option('f', "force", Required = false,
			HelpText = "Restore configuration without sql backward compatibility check", Default = false)]
		public bool IgnoreSqlScriptBackwardCompatibilityCheck {
			get; set;
		}

		#endregion

	}

	#endregion


	#region Class: RestoreFromPackageBackupCommand

	internal class RestoreFromPackageBackupCommand : RemoteCommand<RestoreFromPackageBackupOptions>
	{

		public RestoreFromPackageBackupCommand(IApplicationClient applicationClient, EnvironmentSettings settings)
			: base(applicationClient, settings) {
		}

		protected override string ServicePath => @"/ServiceModel/PackageInstallerService.svc/RestoreFromPackageBackup";

		protected override string GetRequestData(RestoreFromPackageBackupOptions options) {
			return "{\"installPackageData\": " + options.InstallPackageData.ToString().ToLower() +
				", \"ignoreSqlScriptBackwardCompatibilityCheck\": " +
				options.IgnoreSqlScriptBackwardCompatibilityCheck.ToString().ToLower() + "}";
		}

	}

	#endregion

}
