using System;
using System.Collections.Generic;
using System.Linq;
using Clio.Common;
using Clio.Project.NuGet;
using CommandLine;

namespace Clio.Command
{

	#region Class: InstallNugetPkgOptions

	[Verb("install-nuget-pkg", Aliases = new string[] { "installng" }, HelpText = "Install NuGet package to a web application (website)")]
	public class InstallNugetPkgOptions : EnvironmentOptions
	{

		#region Properties: Public

		[Value(0, MetaName = "Names", Required = true, HelpText = "Packages names")]
		public string Names { get; set; }

		[Option('s', "Source", Required = false, HelpText = "Specifies the server URL", 
			Default = "https://www.nuget.org/api/v2")]
		public string SourceUrl { get; set; }

		#endregion

	}

	#endregion

	#region Class: InstallNugetPackageCommand

	public class InstallNugetPackageCommand : Command<InstallNugetPkgOptions>
	{
		#region Fields: Private

		private readonly IInstallNugetPackage _installNugetPackage;

		#endregion

		#region Constructors: Public

		public InstallNugetPackageCommand(IInstallNugetPackage installNugetPackage) {
			installNugetPackage.CheckArgumentNull(nameof(installNugetPackage));
			_installNugetPackage = installNugetPackage;
		}

		#endregion

		#region Methods: Private

		private IEnumerable<NugetPackageFullName> ParseNugetPackageFullNames(string fullNamesDescription) {
			return fullNamesDescription.Split(',').Select(fullName => new NugetPackageFullName(fullName));
		}

		#endregion

		#region Methods: Public

		public override int Execute(InstallNugetPkgOptions options) {
			try {
				_installNugetPackage.Install(ParseNugetPackageFullNames(options.Names), options.SourceUrl);
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