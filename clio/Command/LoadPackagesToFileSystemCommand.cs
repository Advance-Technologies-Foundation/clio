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
		FileDesignModeLoadResult result = Load(options);
		if (result == FileDesignModeLoadResult.FileDesignModeDisabled) {
			// The loader stays silent on this cause because turn-fsm off treats it as its goal state; for a
			// standalone pkg-to-file-system it is a failure and must carry the Error log line that the
			// command-execution-result contract publishes alongside the non-zero exit code.
			_logger.WriteError(FileDesignModeLoadMessage.Build(
				FileDesignModeLoadMessage.FileSystemStorageName,
				FileDesignModeLoadMessage.DisabledFileDesignModeReason));
		}
		return result == FileDesignModeLoadResult.Completed ? 0 : 1;
	}

	/// <summary>
	/// Runs the same export as <see cref="Execute"/> but reports WHY nothing was exported, so a
	/// composite caller can react to the individual causes. <c>turn-fsm on</c> uses it to tell the
	/// caller that the configuration was written while the environment still reports file system
	/// development mode as disabled.
	/// </summary>
	/// <param name="options">Environment options of the command.</param>
	/// <returns>The outcome of the export.</returns>
	public FileDesignModeLoadResult Load(EnvironmentOptions options) {
		try {
			FileDesignModeLoadResult result = _fileDesignModePackages.LoadPackagesToFileSystem();
			_logger.WriteLine();
			return result;
		}
		catch (Exception e) {
			_logger.WriteError(e.GetReadableMessageException(Program.IsDebugMode));
			return FileDesignModeLoadResult.LoadRefused;
		}
	}

	#endregion
}

#endregion
