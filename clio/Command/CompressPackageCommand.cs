using System;
using System.Collections.Generic;
using Clio.Common;
using CommandLine;

namespace Clio.Command
{
	[Verb("generate-pkg-zip", Aliases = ["comp-pkg","compress"], HelpText = "Prepare an archive of creatio package")]
	public class GeneratePkgZipOptions
	{
		[Value(0, MetaName = "Name", Required = false, HelpText = "Name of the compressed package")]
		public string Name { get; set; }

		[Option('d', "destination-path", Required = false, HelpText = "Full destination path for gz file")]
		public string DestinationPath { get; set; }

		[Option("DestinationPath", Required = false, Hidden = true, HelpText = "Alias for --destination-path")]
		public string DestinationPathAlias {
			get => DestinationPath;
			set { if (!string.IsNullOrEmpty(value)) DestinationPath = value; }
		}

		[Option('p', "packages", Required = false)]
		public string Packages { get; set; }

		[Option("Packages", Required = false, Hidden = true, HelpText = "Alias for --packages")]
		public string PackagesAlias {
			get => Packages;
			set { if (!string.IsNullOrEmpty(value)) Packages = value; }
		}

		[Option('s', "skip-pdb", Required = false, Default = false)]
		public bool SkipPdb { get; set; }

		[Option("SkipPdb", Required = false, Hidden = true, HelpText = "Alias for --skip-pdb")]
		public bool SkipPdbAlias {
			get => SkipPdb;
			set { if (value) SkipPdb = value; }
		}
	}

	public class CompressPackageCommand : Command<GeneratePkgZipOptions>
	{
		private readonly IPackageArchiver _packageArchiver;
		private readonly ILogger _logger;

		public CompressPackageCommand(IPackageArchiver packageArchiver, ILogger logger) {
			_packageArchiver = packageArchiver;
			_logger = logger;
		}
		public override int Execute(GeneratePkgZipOptions options) {
			if (options.Packages == null) {
				string destinationPath = string.IsNullOrEmpty(options.DestinationPath) ? $"{options.Name}.gz" : options.DestinationPath;
				_packageArchiver.Pack(options.Name, destinationPath, options.SkipPdb, true);
			} else {
				IEnumerable<string> packages = StringParser.ParseArray(options.Packages);
				string zipFileName = $"packages_{DateTime.Now:yy.MM.dd_hh.mm.ss}.zip";
				string destinationPath = string.IsNullOrEmpty(options.DestinationPath) ? zipFileName : options.DestinationPath;
				_packageArchiver.Pack(options.Name, destinationPath, packages, options.SkipPdb, true);
			}
			_logger.WriteInfo("Done");
			return 0;
		}
	}
}
