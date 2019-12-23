using CommandLine;
using CommandLine.Text;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Clio.Command
{
	[Verb("new-pkg", Aliases = new string[] { "init" }, HelpText = "Create a new creatio package in local file system")]
	public class NewPkgOptions
	{
		[Value(0, MetaName = "Name", Required = true, HelpText = "Name of the created instance")]
		public string Name { get; set; }

		[Option('r', "References", Required = false, HelpText = "Set references to local bin assemblies for development")]
		public string Rebase { get; set; }

		[Usage(ApplicationAlias = "clio")]
		public static IEnumerable<Example> Examples =>
			new List<Example> {
				new Example("Create new package with name 'ATF'",
					new NewPkgOptions { Name = "ATF" }
				),
				new Example("Create new package with name 'ATF' and with links on local installation creatio with file design mode",
					new NewPkgOptions { Name = "ATF", Rebase = "bin"}
				)
			};
	}

	public class NewPkgCommand : Command<NewPkgOptions>
	{
		public override int Execute(NewPkgOptions options) {
			var settings = new SettingsRepository().GetEnvironment();
			try {
				var packageName = options.Name;
				var packageDirectory = Directory.CreateDirectory(packageName);
				Directory.SetCurrentDirectory(packageDirectory.FullName);
				var pkg = CreatioPackage.CreatePackage(options.Name, settings.Maintainer);
				pkg.Create();
				if (!string.IsNullOrEmpty(options.Rebase) && options.Rebase != "nuget") {
					new ReferenceCommand().Execute(new ReferenceOptions { ReferenceType = options.Rebase });
					pkg.RemovePackageConfig();
				}
				Console.WriteLine("Done");
				return 0;
			} catch (Exception e) {
				Console.WriteLine(e);
				return 1;
			}
		}
	}
}
