using System;
using Clio;
using Clio.Common;
using Clio.Package;
using CommandLine;

namespace Clio.Command;

#region Class: LoadPackagesToFileSystemOptions

[Verb("pkg-to-file-system", Aliases = new[] { "tofs", "2fs" },
	HelpText = "Load packages to file system on a web application")]
public class LoadPackagesToFileSystemOptions : EnvironmentOptions{ }

#endregion

#region Class: LoadPackagesToFileSystemCommand

public class LoadPackagesToFileSystemCommand : Command<EnvironmentOptions>{
	#region Fields: Private

	private readonly IFileDesignModePackages _fileDesignModePackages;
	private readonly ILogger _logger;

	#endregion

	#region Constructors: Public

	public LoadPackagesToFileSystemCommand(IFileDesignModePackages fileDesignModePackages, ILogger logger) {
		fileDesignModePackages.CheckArgumentNull(nameof(fileDesignModePackages));
		logger.CheckArgumentNull(nameof(logger));
		_fileDesignModePackages = fileDesignModePackages;
		_logger = logger;
	}

	#endregion

	#region Methods: Public

	public override int Execute(EnvironmentOptions options) {
		try {
			_fileDesignModePackages.LoadPackagesToFileSystem();
			_logger.WriteLine();
			return 0;
		}
		catch (Exception e) {
			_logger.WriteError(e.GetReadableMessageException(Program.IsDebugMode));
			return 1;
		}
	}

	#endregion
}

#endregion
