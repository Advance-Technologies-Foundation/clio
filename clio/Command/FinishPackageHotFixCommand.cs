using System;
using Clio.Common;
using Clio.Package;

namespace Clio.Command;

public class FinishPackageHotFixCommand : RemoteCommand<FinishPackageHotFixCommandOptions>
{
	#region Fields: Private

	private readonly IPackageEditableMutator _packageEditableMutator;
	private readonly ILogger _logger;

	#endregion

	#region Constructors: Public

	public FinishPackageHotFixCommand(IPackageEditableMutator packageEditableMutator, ILogger logger,
		EnvironmentSettings environmentSettings)
		: base(environmentSettings)
	{
		_packageEditableMutator = packageEditableMutator;
		_logger = logger;
	}

	#endregion

	#region Methods: Public

	public override int Execute(FinishPackageHotFixCommandOptions commandOptions)
	{
		_logger.WriteLine($"Finishes hotfix state for package: \"{commandOptions.PackageName}\"");
		try
		{
			_packageEditableMutator.FinishPackageHotfix(commandOptions.PackageName);
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