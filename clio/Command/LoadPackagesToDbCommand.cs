namespace Clio.Command
{
	using System;
	using Clio.Common;
	using Clio.Package;
	using CommandLine;

	#region Class: LoadPackagesToDbOptions

	[Verb("pkg-to-db", Aliases = ["todb", "2db"],
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

		public override int Execute(EnvironmentOptions options) {
			try {
				_fileDesignModePackages.LoadPackagesToDb();
				_logger.WriteLine();
				return 0;
			} catch (Exception e) {
				_logger.WriteError(e.ToString());
				return 1;
			}
		}

		#endregion

	}

	#endregion

}
