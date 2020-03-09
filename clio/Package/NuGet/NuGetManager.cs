using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Clio.Common;

namespace Clio.Project.NuGet
{

	#region Class: NuGetManager

	public class NuGetManager : INuGetManager
	{

		#region Fields: Private

		private readonly INuspecFilesGenerator _nuspecFilesGenerator;
		private readonly INugetPacker _nugetPacker;
		private readonly INugetPackageRestorer _nugetPackageRestorer;
		private readonly IPackageInfoProvider _packageInfoProvider;
		private readonly IPackageArchiver _packageArchiver;
		private readonly IDotnetExecutor _dotnetExecutor;
		private readonly IFileSystem _fileSystem;
		private readonly ILogger _logger;
		private readonly IEnumerable<string> _isNotEmptyPackageInfoFields = new[] {
			nameof(PackageInfo.Name),
			nameof(PackageInfo.Maintainer),
			nameof(PackageInfo.PackageVersion)
		};

	#endregion

		#region Constructors: Public

		public NuGetManager(INuspecFilesGenerator nuspecFilesGenerator, INugetPacker nugetPacker, 
				INugetPackageRestorer nugetPackageRestorer, IPackageInfoProvider packageInfoProvider, 
				IPackageArchiver packageArchiver, IDotnetExecutor dotnetExecutor, IFileSystem fileSystem, 
				ILogger logger) {
			nuspecFilesGenerator.CheckArgumentNull(nameof(nuspecFilesGenerator));
			nugetPacker.CheckArgumentNull(nameof(nugetPacker));
			nugetPackageRestorer.CheckArgumentNull(nameof(nugetPackageRestorer));
			packageInfoProvider.CheckArgumentNull(nameof(packageInfoProvider));
			packageArchiver.CheckArgumentNull(nameof(packageArchiver));
			dotnetExecutor.CheckArgumentNull(nameof(dotnetExecutor));
			fileSystem.CheckArgumentNull(nameof(fileSystem));
			logger.CheckArgumentNull(nameof(logger));
			_nuspecFilesGenerator = nuspecFilesGenerator;
			_nugetPacker = nugetPacker;
			_nugetPackageRestorer = nugetPackageRestorer;
			_packageInfoProvider = packageInfoProvider;
			_packageArchiver = packageArchiver;
			_dotnetExecutor = dotnetExecutor;
			_fileSystem = fileSystem;
			_logger = logger;
		}

		#endregion

		#region Methods: Private

		private static void CheckPackArguments(string packagePath, IEnumerable<PackageDependency> dependencies) {
			packagePath.CheckArgumentNullOrWhiteSpace(nameof(packagePath));
			dependencies.CheckArgumentNull(nameof(dependencies));
		}

		private static void CheckPushArguments(string nupkgFilePath, string apiKey, string nugetSourceUrl) {
			nupkgFilePath.CheckArgumentNullOrWhiteSpace(nameof(nupkgFilePath));
			apiKey.CheckArgumentNullOrWhiteSpace(nameof(apiKey));
			nugetSourceUrl.CheckArgumentNullOrWhiteSpace(nameof(nugetSourceUrl));
		}

		private void CheckDependencies(IEnumerable<PackageDependency> dependencies, 
				IEnumerable<PackageDependency> packageDependencies) {
			StringBuilder sb = null;
			foreach (PackageDependency dependencyInfo in dependencies) {
				if (!packageDependencies.Contains(dependencyInfo)) {
					if (sb == null) {
						sb = new StringBuilder();
						sb.Append("The following dependencies do not exist in the package descriptor:");
					}
					sb.Append($" {dependencyInfo.Name}:{dependencyInfo.PackageVersion};");
				}
			}
			if (sb != null) {
				throw new ArgumentException(sb.ToString());
			}
		}

		private void CheckEmptyFieldPackageInfo(PackageInfo packageInfo, string fieldName) {
			string fieldValue = (string)packageInfo.GetType().GetProperty(fieldName).GetValue(packageInfo);
			if (string.IsNullOrWhiteSpace(fieldValue)) {
				throw new InvalidOperationException(
					$"Field: '{fieldName}' mast be is not empty in package descriptor: '{packageInfo.PackageDescriptorPath}'");
			}
		}

		private void CheckEmptyFieldsPackageInfo(PackageInfo packageInfo) {
			foreach (string isNotEmptyPackageInfoField in _isNotEmptyPackageInfoFields) {
				CheckEmptyFieldPackageInfo(packageInfo, isNotEmptyPackageInfoField);
			}
		}

		#endregion

		#region Methods: Public

		public void Pack(string packagePath, IEnumerable<PackageDependency> dependencies, bool skipPdb, 
				string destinationNupkgDirectory) {
			CheckPackArguments(packagePath, dependencies);
			destinationNupkgDirectory = _fileSystem.GetCurrentDirectoryIfEmpty(destinationNupkgDirectory);
			PackageInfo packageInfo = _packageInfoProvider.GetPackageInfo(packagePath);
			CheckEmptyFieldsPackageInfo(packageInfo);
			CheckDependencies(dependencies, packageInfo.PackageDependencies);
			string packedPackagePath = Path.Combine(destinationNupkgDirectory, 
				_packageArchiver.GetPackedPackageFileName(packageInfo.Name));
			string nuspecFilePath = Path.Combine(destinationNupkgDirectory,
				_nuspecFilesGenerator.GetNuspecFileName(packageInfo));
			string nupkgFilePath = Path.Combine(destinationNupkgDirectory, _nugetPacker.GetNupkgFileName(packageInfo));
			try {
				_packageArchiver.Pack(packagePath, packedPackagePath, skipPdb, true);
				_nuspecFilesGenerator.Create(packageInfo, dependencies, packedPackagePath, nuspecFilePath);
				_nugetPacker.Pack(nuspecFilePath, nupkgFilePath);
			}
			finally {
				_fileSystem.DeleteFileIfExists(nuspecFilePath);
				_fileSystem.DeleteFileIfExists(packedPackagePath);
			}
		}

		public void Push(string nupkgFilePath, string apiKey, string nugetSourceUrl) {
			CheckPushArguments(nupkgFilePath, apiKey, nugetSourceUrl);
			if (!File.Exists(nupkgFilePath)) {
				throw new InvalidOperationException($"Invalid nupkg file path '{nupkgFilePath}'");
			}
			string pushCommand = $"nuget push \"{nupkgFilePath}\" -k {apiKey} -s {nugetSourceUrl}";
			string result = _dotnetExecutor.Execute(pushCommand, true);
			_logger.WriteLine(result);
		}

		public void RestoreToNugetFileStorage(string packageName, string version, string nugetSourceUrl,
				string destinationNupkgDirectory) =>
			_nugetPackageRestorer.RestoreToNugetFileStorage(packageName, version, nugetSourceUrl, destinationNupkgDirectory);

		public void RestoreToDirectory(string packageName, string version, string nugetSourceUrl,
				string destinationNupkgDirectory, bool overwrite) =>
			_nugetPackageRestorer.RestoreToDirectory(packageName, version, nugetSourceUrl, destinationNupkgDirectory, 
				overwrite);

		public void RestoreToPackageStorage(string packageName, string version, string nugetSourceUrl,
			string destinationNupkgDirectory, bool overwrite) =>
			_nugetPackageRestorer.RestoreToPackageStorage(packageName, version, nugetSourceUrl, 
				destinationNupkgDirectory, overwrite);

		#endregion

	}

	#endregion

}