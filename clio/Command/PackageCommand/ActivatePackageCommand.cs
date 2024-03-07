using System;
using Clio.Common;
using Clio.Package;

namespace Clio.Command.PackageCommand
{
	using CommandLine;

	[Verb("activate-pkg", Aliases = new[]{"apkg","activate-package", "enable-package"}, HelpText="Activate package in a web application. Will be available in 8.1.2")]
	internal class ActivatePkgOptions : EnvironmentOptions {
		[Value(0, MetaName = "Name", Required = true, HelpText = "Package name")]
		public string PackageName {
			get; init;
		}
	}

	#region Class: ActivatePackageCommand

	internal class ActivatePackageCommand : RemoteCommand<ActivatePkgOptions> {
		private readonly IPackageActivator _packageActivator;
		private readonly ILogger _logger;

		#region Constructors: Public

		public ActivatePackageCommand(IPackageActivator packageActivator, IApplicationClient applicationClient,
			EnvironmentSettings environmentSettings, ILogger logger)
			: base(applicationClient, environmentSettings) {
			_packageActivator = packageActivator;
			_logger = logger;
		}

		#endregion

		#region Methods: Public

		public override int Execute(ActivatePkgOptions options) {
			try {
				string packageName = options.PackageName;
				_logger.WriteLine($"Start activation package: \"{packageName}\"");
				var activationResults = _packageActivator.Activate(packageName);
				foreach (var activationResult in activationResults)
				{
					string message = activationResult.Success switch
					{
						true when string.IsNullOrEmpty(activationResult.Message) =>
							$"Package \"{activationResult.PackageName}\" successfully activated.",
						true when !string.IsNullOrEmpty(activationResult.Message) =>
							$"Package \"{activationResult.PackageName}\" was activated with errors.",
						_ => $"Package \"{activationResult.PackageName}\" was not activated."
					};
					_logger.WriteLine(message);
				}
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
