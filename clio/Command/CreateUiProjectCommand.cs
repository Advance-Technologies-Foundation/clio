namespace Clio.Command
{
	using Clio.Common;
	using Clio.Package;
	using CommandLine;
	using System;

	#region Class: CreateUiProjectOptions

	[Verb("new-ui-project", Aliases = new string[] { "create-ui-project", "new-ui", "createup", "uiproject", "ui" },
		HelpText = "Add new UI project")]
	public class CreateUiProjectOptions : EnvironmentOptions
	{

		#region Properties: Public

		[Value(0, MetaName = "Name", Required = true, HelpText = "Project name")]
		public string ProjectName {
			get; set;
		}

		[Option('v', "vendor-prefix", Required = true,
			HelpText = "Vendor prefix")]
		public string VendorPrefix {
			get; set;
		}

		[Option("package", Required = true,
			HelpText = "Package name")]
		public string PackageName {
			get; set;
		}

		[Option("empty", Required = false, Default = false,
			HelpText = "Create empty package")]
		public bool IsEmpty {
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

		public CreateUiProjectCommand(IUiProjectCreator uiProjectCreator)
		{
			uiProjectCreator.CheckArgumentNull(nameof(uiProjectCreator));
			_uiProjectCreator = uiProjectCreator;
		}

		#endregion

		#region Methods: Private

		private static bool EnableDownloadPackage(string packageName)
		{
			Console.WriteLine($"Do you wont download package [{packageName}] ? (y/n):");
			string result;
			do
			{
				result = Console.ReadLine().Trim().ToLower();
			} while (result != "y" && result != "n");
			return result == "y";
		}

		#endregion

		#region Methods: Public

		public int Execute(CreateUiProjectOptions options)
		{
			try
			{

				_uiProjectCreator.Create(options.ProjectName, options.PackageName, options.VendorPrefix,
					options.IsEmpty, (options.IsSilent ? (a) => { return false; }
				: EnableDownloadPackage));
				Console.WriteLine("Done");
				return 0;
			}
			catch (Exception e)
			{
				Console.WriteLine(e.Message);
				return 1;
			}
		}

		#endregion

	}

	#endregion

}
