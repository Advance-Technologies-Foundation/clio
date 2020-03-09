using System;
using Clio.Common;
using Clio.Package;
using Clio.Project.NuGet;
using CommandLine;

namespace Clio.Command
{
	[Verb("install-nuget-pkg", Aliases = new string[] { "installng" }, HelpText = "Install NuGet package to a web application (website)")]
	public class InstallNugetPkgOptions : EnvironmentOptions
	{
		[Value(0, MetaName = "Name", Required = true, HelpText = "Package name")]
		public string Name { get; set; }

		[Option('v', "Version", Required = false, HelpText = "Version NuGet package", Default = "*")]
		public string Version { get; set; }

		[Option('s', "Source", Required = false, HelpText = "Specifies the server URL", 
			Default = "https://www.nuget.org")]
		public string SourceUrl { get; set; }
	}

	public class InstallNugetPackageCommand : Command<InstallNugetPkgOptions>
	{
		private readonly IInstallNugetPackage _installNugetPackage;

		public InstallNugetPackageCommand(IInstallNugetPackage installNugetPackage) {
			installNugetPackage.CheckArgumentNull(nameof(installNugetPackage));
			_installNugetPackage = installNugetPackage;
		}

		public override int Execute(InstallNugetPkgOptions options) {
			try {
				_installNugetPackage.Install(options.Name, options.Version, options.SourceUrl);
				Console.WriteLine("Done");
				return 0;
			} catch (Exception e) {
				Console.WriteLine(e.Message);
				return 1;
			}
		}
	}
}