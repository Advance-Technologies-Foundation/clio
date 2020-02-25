using System.Collections.Generic;
using System.IO;
using Clio.Common;

namespace Clio.Project.NuGet
{
	public class NuGetManager : INuGetManager
	{
		private readonly INuspecFilesGenerator _nuspecFilesGenerator;
		private readonly INugetPacker _nugetPacker;
		private readonly IDotnetExecutor _dotnetExecutor;

		public NuGetManager(INuspecFilesGenerator nuspecFilesGenerator, INugetPacker nugetPacker, 
				IDotnetExecutor dotnetExecutor) {
			nuspecFilesGenerator.CheckArgumentNull(nameof(nuspecFilesGenerator));
			nugetPacker.CheckArgumentNull(nameof(nugetPacker));
			dotnetExecutor.CheckArgumentNull(nameof(dotnetExecutor));
			_nuspecFilesGenerator = nuspecFilesGenerator;
			_nugetPacker = nugetPacker;
			_dotnetExecutor = dotnetExecutor;
		}

		public IEnumerable<string> GetNuspecFilesPaths(string nuspecFilesDirectory) {
			nuspecFilesDirectory.CheckArgumentNullOrWhiteSpace(nameof(nuspecFilesDirectory));
			return Directory.EnumerateFiles(nuspecFilesDirectory, $"*.{NuspecFilesGenerator.NuspecExtension}");
		}

		public IEnumerable<string> GetNupkgFilesPaths(string nupkgFilesDirectory) {
			nupkgFilesDirectory.CheckArgumentNullOrWhiteSpace(nameof(nupkgFilesDirectory));
			return Directory.EnumerateFiles(nupkgFilesDirectory, $"*.{NugetPacker.NupkgExtension}");
		}

		public void CreateNuspecFiles(string packagesPath, IDictionary<string, PackageInfo> packagesInfo, 
			string version) => _nuspecFilesGenerator.Create(packagesPath, packagesInfo, version);

		public void Pack(IEnumerable<string> nuspecFilesPaths, string destinationNupkgDirectory)
			=> _nugetPacker.Pack(nuspecFilesPaths, destinationNupkgDirectory);

		public void Push(IEnumerable<string> nupkgFilesPaths, string apiKey, string nugetSourceUrl) {
			nupkgFilesPaths.CheckArgumentNull(nameof(nupkgFilesPaths));
			apiKey.CheckArgumentNullOrWhiteSpace(nameof(apiKey));
			nugetSourceUrl.CheckArgumentNullOrWhiteSpace(nameof(nugetSourceUrl));
			foreach (string nupkgFilePath in nupkgFilesPaths) {
				string pushCommand = $"nuget push {nupkgFilePath} -k {apiKey} -s {nugetSourceUrl}";
				_dotnetExecutor.Execute(pushCommand, true);
			}
		}

	}

}