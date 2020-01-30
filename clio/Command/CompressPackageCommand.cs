using Clio.Common;
using CommandLine;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;

namespace Clio.Command
{
	[Verb("generate-pkg-zip", Aliases = new string[] { "compress" }, HelpText = "Prepare an archive of creatio package")]
	public class GeneratePkgZipOptions
	{
		[Value(0, MetaName = "Name", Required = false, HelpText = "Name of the compressed package")]
		public string Name { get; set; }

		[Option('d', "DestinationPath", Required = false, HelpText = "Full destination path for gz file")]
		public string DestinationPath { get; set; }

		[Option('p', "Packages", Required = false)]
		public string Packages { get; set; }

		[Option('s', "SkipPdb", Required = false, Default = false)]
		public bool SkipPdb { get; set; }
	}

	public class CompressPackageCommand : Command<GeneratePkgZipOptions>
	{
		private readonly IProjectUtilities _projectUtilities;

		public CompressPackageCommand(IProjectUtilities projectUtilities) {
			_projectUtilities = projectUtilities;
		}
		public override int Execute(GeneratePkgZipOptions options) {
			try {
				if (options.Packages == null) {
					var destinationPath = string.IsNullOrEmpty(options.DestinationPath) ? $"{options.Name}.gz" : options.DestinationPath;
					_projectUtilities.CompressProject(options.Name, destinationPath, options.SkipPdb);
				} else {
					var packages = StringParser.ParseArray(options.Packages);
					string zipFileName = $"packages_{DateTime.Now.ToString("yy.MM.dd_hh.mm.ss")}.zip";
					var destinationPath = string.IsNullOrEmpty(options.DestinationPath) ? zipFileName : options.DestinationPath;
					_projectUtilities.CompressProjects(options.Name, destinationPath, packages, options.SkipPdb);
				}
				Console.WriteLine("Done");
				return 0;
			} catch (Exception e) {
				Console.WriteLine(e.Message);
				return 1;
			}
		}
	}
}
