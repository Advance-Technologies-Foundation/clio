using Clio.Package;

namespace Clio.Command
{
	using System;
	using Clio.UserEnvironment;
	using CommandLine;

	#region Class: AddPackageOptions

	[Verb("add-package", Aliases = new string[] { "ap" }, HelpText = "Add package to workspace or local folder")]
	public class AddPackageOptions : EnvironmentOptions
	{

		#region Properties: Public

		[Value(0, MetaName = "Name", Required = true, HelpText = "Package name")]
		public string Name { get; set; }

		#endregion

	}

	#endregion

	#region Class: AddPackageCommand

	public class AddPackageCommand : Command<AddPackageOptions>
	{

		#region Fields: Private

		private readonly IPackageCreator _packageCreator;

		#endregion

		#region Constructors: Public

		public AddPackageCommand(IPackageCreator packageCreator) {
			_packageCreator = packageCreator;
		}

		#endregion

		#region Methods: Public

		public override int Execute(AddPackageOptions options) {
			try {
				_packageCreator.Create(options.Name);
				Console.WriteLine();
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
