using System;
using Clio.Common;
using Clio.Package;

namespace Clio.Command;

public class StartPackageHotFixCommand : RemoteCommand<StartPackageHotFixCommandOptions>
{
	#region Fields: Private

	private readonly IPackageEditableMutator _packageEditableMutator;
	private readonly ILogger _logger;

	#endregion

	#region Constructors: Public

	public StartPackageHotFixCommand(IPackageEditableMutator packageEditableMutator, ILogger logger,
		EnvironmentSettings environmentSettings)
		: base(environmentSettings)
	{
		_packageEditableMutator = packageEditableMutator;
		_logger = logger;
	}

	#endregion

	#region Methods: Public

	public override int Execute(StartPackageHotFixCommandOptions commandOptions)
	{
		_logger.WriteLine($"Starts hotfix state for package: \"{commandOptions.PackageName}\"");
		try
		{
			_packageEditableMutator.StartPackageHotfix(commandOptions.PackageName);
			_logger.WriteLine("Done");
			return 0;
		}
		catch (Exception e)
		{
			_logger.WriteLine(e.Message);
			return 1;
		}
	}

	#endregion
}