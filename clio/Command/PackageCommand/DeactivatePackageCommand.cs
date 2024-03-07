using System;
using Clio.Common;
using Clio.Package;

namespace Clio.Command.PackageCommand
{
	using CommandLine;

	[Verb("deactivate-pkg", Aliases = new[]{"dpkg","deactivate-package", "disable-package"}, HelpText="Deactivate package from a web application. Will be available in 8.1.2")]
	internal class DeactivatePkgOptions : EnvironmentOptions {
		[Value(0, MetaName = "Name", Required = true, HelpText = "Package name")]
		public string PackageName {
			get; set;
		}
	}

	#region Class: DeactivatePackageCommand

	internal class DeactivatePackageCommand : RemoteCommand<DeactivatePkgOptions> {
		private readonly IPackageDeactivator _packageDeactivator;
		private readonly ILogger _logger;

		#region Constructors: Public

		public DeactivatePackageCommand(IPackageDeactivator packageDeactivator, IApplicationClient applicationClient,
			EnvironmentSettings environmentSettings, ILogger logger)
			: base(applicationClient, environmentSettings) {
			_packageDeactivator = packageDeactivator;
			_logger = logger;
		}

		#endregion

		#region Methods: Public

		public override int Execute(DeactivatePkgOptions options) {
			try {
				string packageName = options.PackageName;
				_logger.WriteLine($"Start deactivation package: \"{packageName}\"");
				_packageDeactivator.Deactivate(packageName);
				_logger.WriteLine($"Package \"{packageName}\" successfully deactivated.");
				return 0;
			}
			catch (Exception e) {
				_logger.WriteLine(e.Message);
				return 1;
			}
		}

		#endregion

	}

	#endregion

}
