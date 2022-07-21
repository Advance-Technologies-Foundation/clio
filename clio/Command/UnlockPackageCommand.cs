namespace Clio.Command
{
	using System;
	using CommandLine;
	using Clio.Package;

	#region Class: UnlockPackageOptions

	[Verb("unlock-package", Aliases = new string[] { "up" }, HelpText = "Unlock package")]
	public class UnlockPackageOptions : EnvironmentOptions
	{

		#region Properties: Public

		[Value(0, MetaName = "Name", Required = true, HelpText = "Package name")]
		public string Name { get; set; }

		#endregion

	}

	#endregion

	#region Class: UnlockPackageCommand

	public class UnlockPackageCommand : Command<UnlockPackageOptions>
	{

		#region Fields: Private

		private readonly IPackageUnlocker _packageUnlocker;

		#endregion

		#region Constructors: Public

		public UnlockPackageCommand(IPackageUnlocker packageUnlocker) {
			_packageUnlocker = packageUnlocker;
		}

		#endregion

		#region Methods: Public

		public override int Execute(UnlockPackageOptions options) {
			try {
				_packageUnlocker.Unlock(new []{ options.Name });
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
