using Clio.Command;
using Clio.Common;
using Clio.Package;
using CommandLine;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Clio
{

	[Verb("compressApp", HelpText = "Compress application command")]
	internal class CompressAppOptions
	{
		[Option('s', "SourcePath", Required = true, HelpText = "Folder path to package repository")]
		public string RepositoryFolderPath { get; set; }


		[Option('p', "Packages", Required = true)]
		public string Packages { get; set; }

		[Option('d', "DestinationPath", Required = true, HelpText = "Destination folder path for gz files")]
		public string DestinationPath { get; set; }
	
		[Option("SkipPdb", Required = false, Default = true)]
		public bool SkipPdb { get; set; }

		public IEnumerable<string> RootPackageNames =>  StringParser.ParseArray(Packages);
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
			FolderPackageRepository folderPackageRepository = 
				new FolderPackageRepository(options.RepositoryFolderPath, _jsonConverter);
			var appPackageNames = folderPackageRepository.GetRelatedPackagesNames(options.RootPackageNames);
			string destinationPath = options.DestinationPath;
			if (!Directory.Exists(destinationPath)) {
				Directory.CreateDirectory(destinationPath);
			}
			foreach (var appPackageName in appPackageNames) {
				string packageContentFolderPath = folderPackageRepository.GetPackageContentFolderPath(appPackageName);
				_packageArchiver.Pack(packageContentFolderPath, 
					Path.Combine(options.DestinationPath, $"{appPackageName}.gz"), options.SkipPdb);
			}
			return 0;
		}

	}

	internal class FolderPackageRepository
	{
		private readonly string _repositoryFolderPath;
		private readonly IJsonConverter _jsonConverter;

		public FolderPackageRepository(string repositoryFolderPath, IJsonConverter jsonConverter) {
			_jsonConverter = jsonConverter;
			_repositoryFolderPath = repositoryFolderPath;
		}


		public IEnumerable<string> GetRelatedPackagesNames(IEnumerable<string> rootPackageNames) {
			var relatedPackageDescriptors = new HashSet<string>();
			var candidatePackages = GetStackPackageDescriptors(rootPackageNames);
			while (candidatePackages.Count > 0) {
				PackageDescriptor candidatePackage = candidatePackages.Pop();
				if (relatedPackageDescriptors.Contains(candidatePackage.Name)) {
					continue;
				}
				relatedPackageDescriptors.Add(candidatePackage.Name);
				foreach (PackageDependency parent in candidatePackage.DependsOn) {
					candidatePackages.Push(GetPackageDescriptor(parent.Name));
				}
			}
			return relatedPackageDescriptors;
		}

		private Stack<PackageDescriptor> GetStackPackageDescriptors(IEnumerable<string> packageNames) {
			var processedPackages = new Stack<PackageDescriptor>();
			foreach (string packageName in packageNames) {
				var packageDescriptor = GetPackageDescriptor(packageName);
				processedPackages.Push(packageDescriptor);
			}
			return processedPackages;
		}

		public string GetPackageContentFolderPath(string packageName) {
			return PackageUtilities.GetPackageContentFolderPath(_repositoryFolderPath, packageName);
		}

		private PackageDescriptor GetPackageDescriptor(string packageName) {
			string packagePath = GetPackageContentFolderPath(packageName);
			string packageDescriptorPath = PackageUtilities.BuildPackageDescriptorPath(packagePath);
			var packageDescriptor = _jsonConverter
				.DeserializeObjectFromFile<PackageDescriptorDto>(packageDescriptorPath)
				.Descriptor;
			return packageDescriptor;
		}

	}


}