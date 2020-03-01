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

		public NuGetManager(INuspecFilesGenerator nuspecFilesGenerator, INugetPacker nugetPacker, 
				INugetPackageRestorer nugetPackageRestorer, IPackageInfoProvider packageInfoProvider, 
				IPackageArchiver packageArchiver, DotnetExecutor dotnetExecutor) {
			nuspecFilesGenerator.CheckArgumentNull(nameof(nuspecFilesGenerator));
			nugetPacker.CheckArgumentNull(nameof(nugetPacker));
			nugetPackageRestorer.CheckArgumentNull(nameof(nugetPackageRestorer));
			packageInfoProvider.CheckArgumentNull(nameof(packageInfoProvider));
			packageArchiver.CheckArgumentNull(nameof(packageArchiver));
			dotnetExecutor.CheckArgumentNull(nameof(dotnetExecutor));
			_nuspecFilesGenerator = nuspecFilesGenerator;
			_nugetPacker = nugetPacker;
			_nugetPackageRestorer = nugetPackageRestorer;
			_packageInfoProvider = packageInfoProvider;
			_packageArchiver = packageArchiver;
			_dotnetExecutor = dotnetExecutor;
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

		public string Pack(string packagePath, IEnumerable<PackageDependency> dependencies, bool skipPdb, 
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
			string result = _nugetPacker.Pack(nuspecFilePath, nupkgFilePath);
			SafeFileDelete(nuspecFilePath);
			SafeFileDelete(packedPackagePath);
			return result;
		}

		public string Push(string nupkgFilePath, string apiKey, string nugetSourceUrl) {
			CheckPushArguments(nupkgFilePath, apiKey, nugetSourceUrl);
			if (!File.Exists(nupkgFilePath)) {
				throw new InvalidOperationException($"Invalid nupkg file path '{nupkgFilePath}'");
			}
			string pushCommand = $"nuget push \"{nupkgFilePath}\" -k {apiKey} -s {nugetSourceUrl}";
			return _dotnetExecutor.Execute(pushCommand, true);
		}

		public string Restore(string name, string version, string nugetSourceUrl, string destinationNupkgDirectory) =>
				_nugetPackageRestorer.Restore(name, version, nugetSourceUrl, destinationNupkgDirectory);

	}

}