using System.Collections.Generic;
using System.Linq;

namespace Clio.Command
{
	
}

namespace Clio.Command
{
	using System;
	using CommandLine;
	using Clio.Package;

	#region Class: LockPackageOptions

	[Verb("lock-package", Aliases = new string[] { "lp" }, HelpText = "Lock package")]
	public class LockPackageOptions : EnvironmentOptions
	{

		#region Properties: Public

		[Value(0, MetaName = "Name", Required = false, HelpText = "Package name")]
		public string Name { get; set; }

		#endregion

	}

	#endregion

	#region Class: LockPackageCommand

	public class LockPackageCommand : Command<LockPackageOptions>
	{

		#region Fields: Private

		private readonly IPackageLockManager _packageLockManager;

		#endregion

		#region Constructors: Public

		public LockPackageCommand(IPackageLockManager packageLockManager) {
			_packageLockManager = packageLockManager;
		}

		#endregion

		#region Methods: Private

		public IEnumerable<string> GetPackagesNames(LockPackageOptions options) =>
			string.IsNullOrWhiteSpace(options.Name)
				? Enumerable.Empty<string>()
				: new[] { options.Name }; 

		#endregion

		#region Methods: Public

		public override int Execute(LockPackageOptions options) {
			try {
				_packageLockManager.Lock(GetPackagesNames(options));
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
