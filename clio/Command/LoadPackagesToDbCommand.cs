namespace Clio.Command
{
	using System;
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

		#endregion

		#region Constructors: Public

		public LoadPackagesToDbCommand(IFileDesignModePackages fileDesignModePackages) {
			fileDesignModePackages.CheckArgumentNull(nameof(fileDesignModePackages));
			_fileDesignModePackages = fileDesignModePackages;
		}

		#endregion

		#region Methods: Public

		public override int Execute(EnvironmentOptions options) {
			try {
				_fileDesignModePackages.LoadPackagesToDb();
				Console.WriteLine();
				return 0;
			} catch (Exception e) {
				Console.WriteLine(e);
				return 1;
			}
		}

		#endregion

	}

	#endregion

}