using System.Collections.Generic;
using System.Linq;

namespace Clio.Command
{
	using System;
	using CommandLine;
	using Clio.Package;
	using Clio.Common;

	#region Class: LockPackageOptions

	[Verb("lock-package", Aliases = ["lp"], HelpText = "Lock package")]
	public class LockPackageOptions : EnvironmentOptions
	{

		#region Properties: Public

		[Value(0, MetaName = "Name", Required = false, HelpText = "Package name")]
		public string Name { get; set; }

		#endregion

	}

	#endregion

	#region Class: LockPackageCommand

	public class LockPackageCommand : Command<LockPackageOptions>
	{

		#region Fields: Private

		private readonly IPackageLockManager _packageLockManager;
		private readonly ILogger _logger;
		private readonly IClioGateway _clioGateway;

		#endregion

		#region Constructors: Public

		public LockPackageCommand(IPackageLockManager packageLockManager, ILogger logger, IClioGateway clioGateway) {
			_packageLockManager = packageLockManager;
			_logger = logger;
			_clioGateway = clioGateway;
		}

		#endregion

		#region Methods: Private

		private static IEnumerable<string> GetPackagesNames(LockPackageOptions options) =>
			string.IsNullOrWhiteSpace(options.Name)
				? []
				: options.Name.Split(',').Select(i=> i.Trim()); 

		#endregion

		#region Methods: Public

		public override int Execute(LockPackageOptions options) {
			try {
				
				const string minClioGateVersion = "2.0.0.0";
				if(!_clioGateway.IsCompatibleWith(minClioGateVersion)) {
					_logger.WriteError($"lock package feature requires cliogate package version {minClioGateVersion} or higher installed in Creatio.");
					_logger.WriteInfo(string.IsNullOrWhiteSpace(options.Environment)
						?  "To install cliogate use the following command: clio install-gate"
						: $"To install cliogate use the following command: clio install-gate -e {options.Environment}");
					return 0;
				}
				
				_packageLockManager.Lock(GetPackagesNames(options));
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
