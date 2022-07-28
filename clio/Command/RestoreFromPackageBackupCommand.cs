namespace Clio.Command
{
	using Clio.Common;
	using CommandLine;

	#region Class: RestoreFromPackageBackupOptions

	[Verb("restore-package", Aliases = new string[] { "rp" }, HelpText = "Restore package")]
	public class RestoreFromPackageBackupOptions : EnvironmentOptions
	{

		#region Properties: Public

		[Option("ipd", Required = false,
			HelpText ="Is install package data", Default = false)]
		public bool InstallPackageData {
			get; set;
		}

		[Option("ignireCheck", Required = false,
			HelpText = "Ignore sql script backward compatibility check", Default = false)]
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
			return "{\"installPackageData\": " + options.InstallPackageData +
				", \"ignoreSqlScriptBackwardCompatibilityCheck\": " + options.IgnoreSqlScriptBackwardCompatibilityCheck + "}";
		}

	}

	#endregion

}
