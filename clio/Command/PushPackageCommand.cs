using System;
using System.IO;
using System.Text;
using System.Threading;
using Clio.Common;
using Clio.Package;
using Clio.UserEnvironment;
using CommandLine;

namespace Clio.Command
{
	[Verb("push-pkg", Aliases = new string[] { "install" }, HelpText = "Install package on a web application")]
	public class PushPkgOptions : EnvironmentOptions
	{
		[Value(0, MetaName = "Name", Required = false, HelpText = "Package name")]
		public string Name { get; set; }

		[Option('r', "ReportPath", Required = false, HelpText = "Log file path")]
		public string ReportPath { get; set; }
	}

	public class PushPackageCommand : Command<PushPkgOptions>
	{
		public PushPackageCommand(IPackageInstaller packageInstaller) {
			packageInstaller.CheckArgumentNull(nameof(packageInstaller));
			_packageInstaller = packageInstaller;

		}
		private readonly IPackageInstaller _packageInstaller;

		public override int Execute(PushPkgOptions options) {
			try {
				bool success = _packageInstaller.Install(options.Name, options.ReportPath);
				Console.WriteLine(success ? "Done" : "Error");
				return success ? 0 : 1;
			} catch (Exception e) {
				Console.WriteLine(e.StackTrace);
				return 1;
			}
		}
	}
}
