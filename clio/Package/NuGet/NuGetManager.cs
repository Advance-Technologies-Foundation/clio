using System;
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

		public string GetNuspecFileName(PackageInfo packageInfo) =>
			_nuspecFilesGenerator.GetNuspecFileName(packageInfo);

		public string GetNupkgFileName(PackageInfo packageInfo) => _nugetPacker.GetNupkgFileName(packageInfo);

		public void CreateNuspecFile(PackageInfo packageInfo, IEnumerable<DependencyInfo> dependencies, 
				string nuspecFilePath) => 
			_nuspecFilesGenerator.Create(packageInfo, dependencies, nuspecFilePath);

		public void Pack(string nuspecFilePath, string nupkgFilePath)
			=> _nugetPacker.Pack(nuspecFilePath, nupkgFilePath);

		public void Push(string nupkgFilePath, string apiKey, string nugetSourceUrl) {
			nupkgFilePath.CheckArgumentNullOrWhiteSpace(nameof(nupkgFilePath));
			apiKey.CheckArgumentNullOrWhiteSpace(nameof(apiKey));
			nugetSourceUrl.CheckArgumentNullOrWhiteSpace(nameof(nugetSourceUrl));
			if (!File.Exists(nupkgFilePath)) {
				throw new InvalidOperationException($"Invalid nupkg file path '{nupkgFilePath}'");
			}
			string pushCommand = $"nuget push \"{nupkgFilePath}\" -k {apiKey} -s {nugetSourceUrl}";
			_dotnetExecutor.Execute(pushCommand, true);
		}

	}

}