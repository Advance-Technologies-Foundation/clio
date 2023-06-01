using Clio.Command;
using Clio.Common;
using Clio.Package;
using CommandLine;
using DocumentFormat.OpenXml.Drawing;
using System;
using System.Collections;
using System.Collections.Generic;
using System.DirectoryServices.Protocols;
using System.IO;

namespace Clio
{

	[Verb("compressApp", HelpText = "Compress application command")]
	internal class CompressAppOptions
	{
		internal string repositoryFolderPath;
		internal IEnumerable<string> rootPackageNames;

		public string DestinationPath
		{
			get;
			internal set;
		}
		public bool SkipPdb
		{
			get;
			internal set;
		}
		public bool Overwrite
		{
			get;
			internal set;
		}
	}

	internal class CompressAppCommand : Command<CompressAppOptions>
	{

		private IJsonConverter _jsonConverter;
		private readonly IPackageArchiver _packageArchiver;

		public CompressAppCommand(IJsonConverter jsonConverter, IPackageArchiver packageArchiver) {
			_jsonConverter = jsonConverter;
			_packageArchiver = packageArchiver;
		}

		public override int Execute(CompressAppOptions options) {
			FolderPackageRepository folderPackageRepository = new FolderPackageRepository(options.repositoryFolderPath);
			var appPackagePaths = folderPackageRepository.GetRelatedPackagesPaths(options.rootPackageNames);
			string destinationPath = options.DestinationPath;
			if (!Directory.Exists(destinationPath)) {
				Directory.CreateDirectory(destinationPath);
			}
			foreach (var appPackagePath in appPackagePaths) {
				_packageArchiver.Pack(appPackagePath, options.DestinationPath, options.SkipPdb, options.Overwrite);
			}
			return 0;
		}

	}

	internal class FolderPackageRepository
	{
		private readonly string repositoryFolderPath;

		public FolderPackageRepository(string repositoryFolderPath) {
			this.repositoryFolderPath = repositoryFolderPath;
		}

		internal IEnumerable<string> GetRelatedPackagesPaths(IEnumerable<string> rootPackageNames) {
			
		}
	}


}