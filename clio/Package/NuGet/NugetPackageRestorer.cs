using System;
using System.IO;
using System.Linq;
using Clio.Common;

namespace Clio.Project.NuGet
{
	public class NugetPackageRestorer : INugetPackageRestorer
	{

		private const string NugetRestoreProjName = "NugetRestoreProj.csproj";
		private readonly IDotnetExecutor _dotnetExecutor;
		private readonly ITemplateProvider _templateProvider;
		private readonly IWorkingDirectoriesProvider _workingDirectoriesProvider;
		
		public NugetPackageRestorer(ITemplateProvider templateProvider, IDotnetExecutor dotnetExecutor, 
				IWorkingDirectoriesProvider workingDirectoriesProvider) {
			dotnetExecutor.CheckArgumentNull(nameof(dotnetExecutor));
			templateProvider.CheckArgumentNull(nameof(templateProvider));
			workingDirectoriesProvider.CheckArgumentNull(nameof(workingDirectoriesProvider));
			_dotnetExecutor = dotnetExecutor;
			_templateProvider = templateProvider;
			_workingDirectoriesProvider = workingDirectoriesProvider;
		}

		private string ReplaceMacro(string template, string name, string version) {
			return template.Replace("$name$", name)
				.Replace("$version$", version);
		}

		private void CreateNugetRestoreProj(string nugetPackProjPath, string name, string version) {
			string template = _templateProvider.GetTemplate(NugetRestoreProjName);
			string nugetRestoreProjFileContent = ReplaceMacro(template, name, version);
			File.WriteAllText(nugetPackProjPath, nugetRestoreProjFileContent);
		}

		private string RestorePackage(string nugetRestoreProjPath, string nugetSourceUrl, 
				string destinationNupkgDirectory) {
			string packCommand = $"restore \"{nugetRestoreProjPath}\" --source {nugetSourceUrl} " + 
				$"--packages \"{destinationNupkgDirectory}\" --force";
			return _dotnetExecutor.Execute(packCommand, true);
		}
 
		private string ReplaceInOutputResult(string outputResult, string nugetPackProjPath, 
				string destinationNupkgDirectory) {
			return outputResult.Replace(nugetPackProjPath, $"\"{destinationNupkgDirectory}\"");
		}

		public string Restore(string name, string version, string nugetSourceUrl, string destinationNupkgDirectory) {
			name.CheckArgumentNullOrWhiteSpace(nameof(name));
			version.CheckArgumentNullOrWhiteSpace(nameof(version));
			nugetSourceUrl.CheckArgumentNullOrWhiteSpace(nameof(nugetSourceUrl));
			destinationNupkgDirectory.CheckArgumentNullOrWhiteSpace(nameof(destinationNupkgDirectory));
			string tempDirectory = _workingDirectoriesProvider.CreateTempDirectory();
			try {
				string nugetRestoreProjPath = Path.Combine(tempDirectory, NugetRestoreProjName);
				CreateNugetRestoreProj(nugetRestoreProjPath, name, version);
				string result = RestorePackage(nugetRestoreProjPath, nugetSourceUrl, destinationNupkgDirectory);
				result = ReplaceInOutputResult(result, nugetRestoreProjPath, destinationNupkgDirectory);
				return result;
			} finally {
				_workingDirectoriesProvider.SafeDeleteTempDirectory(tempDirectory);
			}
		}


	}
}