using System.Collections.Generic;
using System.Linq;
using Clio.Common;

namespace Clio.Command
{
	using System;
	using CommandLine;
	using Clio.Package;

	#region Class: UnlockPackageOptions

	[Verb("unlock-package", Aliases = ["up"], HelpText = "Unlock package")]
	public class UnlockPackageOptions : EnvironmentOptions
	{

		#region Properties: Public

		[Value(0, MetaName = "Name", Required = false, HelpText = "Package name")]
		public string Name { get; set; }

		#endregion

	}

	#endregion

	#region Class: UnlockPackageCommand

	public class UnlockPackageCommand : Command<UnlockPackageOptions>
	{

		#region Fields: Private

		private readonly IPackageLockManager _packageLockManager;
		private readonly IClioGateway _clioGateway;
		private readonly ILogger _logger;

		#endregion

		#region Constructors: Public

		public UnlockPackageCommand(IPackageLockManager packageLockManager, IClioGateway clioGateway, ILogger logger) {
			_packageLockManager = packageLockManager;
			_clioGateway = clioGateway;
			_logger = logger;
		}

		#endregion

		#region Methods: Private

		private static IEnumerable<string> GetPackagesNames(UnlockPackageOptions options) =>
			string.IsNullOrWhiteSpace(options.Name)
				? []
				: options.Name.Split(',').Select(i=> i.Trim()); 

		#endregion

		#region Methods: Public

		public override int Execute(UnlockPackageOptions options) {
			try {
				
				const string minClioGateVersion = "2.0.0.0";
				if(!_clioGateway.IsCompatibleWith(minClioGateVersion)) {
					_logger.WriteError($"Unlock package feature requires cliogate package version {minClioGateVersion} or higher installed in Creatio.");

					_logger.WriteInfo(string.IsNullOrWhiteSpace(options.Environment)
						?  "To install cliogate use the following command: clio install-gate"
						: $"To install cliogate use the following command: clio install-gate -e {options.Environment}");
					return 0;
				}
				_packageLockManager.Unlock(GetPackagesNames(options));
				_logger.WriteInfo("Done");
				return 0;
			} catch (Exception e) {
				_logger.WriteError(e.Message);
				return 1;
			}
		}

		#endregion

	}

	#endregion

}
