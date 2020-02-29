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
		private readonly IProjectUtilities _projectUtilities;
		private readonly IDotnetExecutor _dotnetExecutor;

		public NuGetManager(INuspecFilesGenerator nuspecFilesGenerator, INugetPacker nugetPacker, 
				INugetPackageRestorer nugetPackageRestorer, IPackageInfoProvider packageInfoProvider, 
				IProjectUtilities projectUtilities, DotnetExecutor dotnetExecutor) {
			nuspecFilesGenerator.CheckArgumentNull(nameof(nuspecFilesGenerator));
			nugetPacker.CheckArgumentNull(nameof(nugetPacker));
			nugetPackageRestorer.CheckArgumentNull(nameof(nugetPackageRestorer));
			packageInfoProvider.CheckArgumentNull(nameof(packageInfoProvider));
			projectUtilities.CheckArgumentNull(nameof(projectUtilities));
			dotnetExecutor.CheckArgumentNull(nameof(dotnetExecutor));
			_nuspecFilesGenerator = nuspecFilesGenerator;
			_nugetPacker = nugetPacker;
			_nugetPackageRestorer = nugetPackageRestorer;
			_packageInfoProvider = packageInfoProvider;
			_projectUtilities = projectUtilities;
			_dotnetExecutor = dotnetExecutor;
		}

		private void SafeFileDelete(string s) {
			if (File.Exists(s)) {
				File.Delete(s);
			}
		}

		private void CheckDependencies(IEnumerable<PackageDependency> dependencies, PackageInfo packageInfo) {
			StringBuilder sb = null;
			foreach (PackageDependency dependencyInfo in dependencies) {
				if (!packageInfo.PackageDependencies.Contains(dependencyInfo)) {
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
			PackageInfo packageInfo = _packageInfoProvider.GetPackageInfo(packagePath);
			CheckDependencies(dependencies, packageInfo);
			string compressedPackagePath = Path.Combine(destinationNupkgDirectory, 
				_projectUtilities.GetCompressedPackageName(packageInfo.Name));
			_projectUtilities.CompressProject(packagePath, compressedPackagePath, skipPdb);
			string nuspecFilePath = Path.Combine(destinationNupkgDirectory,
				_nuspecFilesGenerator.GetNuspecFileName(packageInfo));
			_nuspecFilesGenerator.Create(packageInfo, dependencies, compressedPackagePath, nuspecFilePath);
			string nupkgFilePath = Path.Combine(destinationNupkgDirectory, _nugetPacker.GetNupkgFileName(packageInfo));
			string result = _nugetPacker.Pack(nuspecFilePath, nupkgFilePath);
			SafeFileDelete(nuspecFilePath);
			SafeFileDelete(compressedPackagePath);
			return result;
		}

		public string Push(string nupkgFilePath, string apiKey, string nugetSourceUrl) {
			nupkgFilePath.CheckArgumentNullOrWhiteSpace(nameof(nupkgFilePath));
			apiKey.CheckArgumentNullOrWhiteSpace(nameof(apiKey));
			nugetSourceUrl.CheckArgumentNullOrWhiteSpace(nameof(nugetSourceUrl));
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