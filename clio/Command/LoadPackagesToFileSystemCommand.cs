namespace Clio.Command
{
	using System;
	using Clio.Common;
	using Clio.Package;
	using CommandLine;

	#region Class: LoadPackagesToFileSystemOptions

	[Verb("pkg-to-file-system", Aliases = new string[] { "tofs", "2fs" },
		HelpText = "Load packages to file system on a web application")]
	public class LoadPackagesToFileSystemOptions : EnvironmentOptions
	{
	}

	#endregion

	#region Class: LoadPackagesToFileSystemCommand
	
	public class LoadPackagesToFileSystemCommand : Command<EnvironmentOptions>
	{
		
		#region Fields: Private

		private readonly IFileDesignModePackages _fileDesignModePackages;

		#endregion

		#region Constructors: Public

		public LoadPackagesToFileSystemCommand(IFileDesignModePackages fileDesignModePackages) {
			fileDesignModePackages.CheckArgumentNull(nameof(fileDesignModePackages));
			_fileDesignModePackages = fileDesignModePackages;
		}

		#endregion

		#region Methods: Public

		public override int Execute(EnvironmentOptions options) {
			try {
				_fileDesignModePackages.LoadPackagesToFileSystem();
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