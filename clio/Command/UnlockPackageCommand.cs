using System;
using System.Collections.Generic;
using System.Linq;
using Clio.Common;
using Clio.Package;
using CommandLine;

namespace Clio.Command;

#region Class: UnlockPackageOptions

[Verb("unlock-package", Aliases = ["up"], HelpText = "Unlock package")]
public class UnlockPackageOptions : EnvironmentOptions{
	#region Properties: Public

	[Value(0, MetaName = "Name", Required = false, HelpText = "Package name")]
	public string Name { get; set; }

	#endregion
}

#endregion

#region Class: UnlockPackageCommand

public class UnlockPackageCommand : Command<UnlockPackageOptions>{
	#region Fields: Private

	private readonly ILogger _logger;
	private readonly IPackageLockManager _packageLockManager;
	private readonly ISysSettingsManager _sysSettingsManager;

	#endregion

	#region Constructors: Public

	public UnlockPackageCommand(IPackageLockManager packageLockManager,
		ISysSettingsManager sysSettingsManager, ILogger logger) {
		_packageLockManager = packageLockManager;
		_sysSettingsManager = sysSettingsManager;
		_logger = logger;
	}

	#endregion

	#region Methods: Private

	private static IEnumerable<string> GetPackagesNames(UnlockPackageOptions options) {
		return string.IsNullOrWhiteSpace(options.Name)
			? []
			: options.Name.Split(',').Select(i => i.Trim());
	}

	#endregion

	#region Methods: Public

	public override int Execute(UnlockPackageOptions options) {
		try {
			List<string> packageNames = GetPackagesNames(options).ToList();
			if (!packageNames.Any()) {
				if (string.IsNullOrWhiteSpace(options.Maintainer)) {
					_logger.WriteError("Maintainer is required to unlock all packages. Use -m <MAINTAINER>.");
					return 1;
				}

				_logger.WriteInfo($"Setting Maintainer sys setting to '{options.Maintainer}'.");
				bool isMaintainerUpdated = _sysSettingsManager.UpdateSysSetting("Maintainer", options.Maintainer);
				if (!isMaintainerUpdated) {
					_logger.WriteError("Could not update Maintainer sys setting.");
					return 1;
				}

				_logger.WriteInfo("Setting SchemaNamePrefix sys setting to an empty value.");
				bool isSchemaNamePrefixUpdated = _sysSettingsManager.UpdateSysSetting("SchemaNamePrefix", string.Empty);
				if (!isSchemaNamePrefixUpdated) {
					_logger.WriteError("Could not update SchemaNamePrefix sys setting.");
					return 1;
				}

				_logger.WriteInfo(
					$"Unlocking all packages in environment '{options.Environment}' for maintainer '{options.Maintainer}'.");
			}

			_packageLockManager.Unlock(packageNames);
			_logger.WriteInfo("Done");
			return 0;
		}
		catch (Exception e) {
			_logger.WriteError(e.Message);
			return 1;
		}
	}

	#endregion
}

#endregion
