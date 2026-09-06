namespace Clio.Command
{
	using System;
	using Clio;
	using Clio.Common;
	using Clio.Package;
	using CommandLine;

	#region Class: LoadPackagesToDbOptions

	[Verb("pkg-to-db", Aliases = new string[] { "todb", "2db" },
		HelpText = "Load packages to database on a web application")]
	public class LoadPackagesToDbOptions : EnvironmentOptions
	{
	}

	#endregion

	#region Class: LoadPackagesToDbCommand
	
	public class LoadPackagesToDbCommand : Command<EnvironmentOptions>
	{

		#region Fields: Private

		private readonly IFileDesignModePackages _fileDesignModePackages;
		private readonly ILogger _logger;

		#endregion

		#region Constructors: Public

		public LoadPackagesToDbCommand(IFileDesignModePackages fileDesignModePackages, ILogger logger) {
			fileDesignModePackages.CheckArgumentNull(nameof(fileDesignModePackages));
			_fileDesignModePackages = fileDesignModePackages;
			_logger = logger;
		}

		#endregion

		#region Methods: Public

		public override int Execute(EnvironmentOptions options) =>
			Load(options) == FileDesignModeLoadResult.Completed ? 0 : 1;

		/// <summary>
		/// Runs the same import as <see cref="Execute"/> but reports WHY nothing was imported, so a
		/// composite caller can tell an environment that already has file system development mode
		/// disabled apart from a load the platform refused. <c>turn-fsm off</c> needs that distinction:
		/// the first case is its own goal state, the second is a real failure.
		/// </summary>
		/// <param name="options">Environment options of the command.</param>
		/// <returns>The outcome of the import.</returns>
		public FileDesignModeLoadResult Load(EnvironmentOptions options) {
			try {
				FileDesignModeLoadResult result = _fileDesignModePackages.LoadPackagesToDb();
				_logger.WriteLine();
				return result;
			} catch (Exception e) {
				_logger.WriteError(e.GetReadableMessageException(Program.IsDebugMode));
				return FileDesignModeLoadResult.LoadRefused;
			}
		}

		#endregion

	}

	#endregion

}
