namespace Clio.Command
{
	using Clio.Common;
	using Clio.Package;
	using CommandLine;
	using System;

	#region Class: RestoreFromPackageBackupOptions

	[Verb("create-ui-project", Aliases = new string[] { "createup"}, HelpText = "Create UI project")]
	public class CreateUiProjectOptions : EnvironmentOptions
	{

		#region Properties: Public

		[Value(0, MetaName = "Name", Required = true, HelpText = "Project name")]
		public string ProjectName {
			get; set;
		}

		[Option("vendor-prefix", Required = true,
			HelpText ="Skip rollback data")]
		public string VendorPrefix {
			get; set;
		}

		[Option("package", Required = true,
			HelpText = "Package name")]
		public string PackageName {
			get; set;
		}

		#endregion

	}

	#endregion


	#region Class: CreateUiProjectCommand

	internal class CreateUiProjectCommand
	{

		#region Fields: Private

		private readonly IUiProjectCreator _uiProjectCreator;


		#endregion

		#region Constructors: Public

		public CreateUiProjectCommand(IUiProjectCreator uiProjectCreator) {
			uiProjectCreator.CheckArgumentNull(nameof(uiProjectCreator));
			_uiProjectCreator= uiProjectCreator;
			
		}

		#endregion

		#region Methods: Public

		public int Execute(CreateUiProjectOptions options) {
			try {
				_uiProjectCreator.Create(options.ProjectName, options.PackageName, options.VendorPrefix);
				Console.WriteLine("Done");
				return 0;
			} catch (Exception e) {
				Console.WriteLine(e.Message);
				return 1;
			}
		}

		#endregion

	}

	#endregion

}
