using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Clio.Common;

namespace Clio.Project.NuGet
{
	public class NuGetManager : INuGetManager
	{
		private readonly INuspecFilesGenerator _nuspecFilesGenerator;
		private readonly INugetPacker _nugetPacker;
		private readonly INugetPackageRestorer _nugetPackageRestorer;
		private readonly IPackageInfoProvider _packageInfoProvider;
		private readonly IPackageArchiver _packageArchiver;
		private readonly IDotnetExecutor _dotnetExecutor;
		private readonly ILogger _logger;

		public NuGetManager(INuspecFilesGenerator nuspecFilesGenerator, INugetPacker nugetPacker, 
				INugetPackageRestorer nugetPackageRestorer, IPackageInfoProvider packageInfoProvider, 
				IPackageArchiver packageArchiver, DotnetExecutor dotnetExecutor, ILogger logger) {
			nuspecFilesGenerator.CheckArgumentNull(nameof(nuspecFilesGenerator));
			nugetPacker.CheckArgumentNull(nameof(nugetPacker));
			nugetPackageRestorer.CheckArgumentNull(nameof(nugetPackageRestorer));
			packageInfoProvider.CheckArgumentNull(nameof(packageInfoProvider));
			packageArchiver.CheckArgumentNull(nameof(packageArchiver));
			dotnetExecutor.CheckArgumentNull(nameof(dotnetExecutor));
			logger.CheckArgumentNull(nameof(logger));
			_nuspecFilesGenerator = nuspecFilesGenerator;
			_nugetPacker = nugetPacker;
			_nugetPackageRestorer = nugetPackageRestorer;
			_packageInfoProvider = packageInfoProvider;
			_packageArchiver = packageArchiver;
			_dotnetExecutor = dotnetExecutor;
			_logger = logger;
		}

		private static void CheckPackArguments(string packagePath, IEnumerable<PackageDependency> dependencies, 
				string destinationNupkgDirectory) {
			packagePath.CheckArgumentNullOrWhiteSpace(nameof(packagePath));
			dependencies.CheckArgumentNull(nameof(dependencies));
			destinationNupkgDirectory.CheckArgumentNullOrWhiteSpace(nameof(destinationNupkgDirectory));
		}

		private static void CheckPushArguments(string nupkgFilePath, string apiKey, string nugetSourceUrl) {
			nupkgFilePath.CheckArgumentNullOrWhiteSpace(nameof(nupkgFilePath));
			apiKey.CheckArgumentNullOrWhiteSpace(nameof(apiKey));
			nugetSourceUrl.CheckArgumentNullOrWhiteSpace(nameof(nugetSourceUrl));
		}

		private void SafeFileDelete(string s) {
			if (File.Exists(s)) {
				File.Delete(s);
			}
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

		public void Pack(string packagePath, IEnumerable<PackageDependency> dependencies, bool skipPdb, 
				string destinationNupkgDirectory) {
			CheckPackArguments(packagePath, dependencies, destinationNupkgDirectory);
			PackageInfo packageInfo = _packageInfoProvider.GetPackageInfo(packagePath);
			CheckDependencies(dependencies, packageInfo.PackageDependencies);
			string packedPackagePath = Path.Combine(destinationNupkgDirectory, 
				_packageArchiver.GetPackedPackageFileName(packageInfo.Name));
			_packageArchiver.Pack(packagePath, packedPackagePath, skipPdb, true);
			string nuspecFilePath = Path.Combine(destinationNupkgDirectory,
				_nuspecFilesGenerator.GetNuspecFileName(packageInfo));
			_nuspecFilesGenerator.Create(packageInfo, dependencies, packedPackagePath, nuspecFilePath);
			string nupkgFilePath = Path.Combine(destinationNupkgDirectory, _nugetPacker.GetNupkgFileName(packageInfo));
			_nugetPacker.Pack(nuspecFilePath, nupkgFilePath);
			SafeFileDelete(nuspecFilePath);
			SafeFileDelete(packedPackagePath);
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

	}

}