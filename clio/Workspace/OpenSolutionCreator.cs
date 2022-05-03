namespace Clio.Workspace
{
	using System;
	using System.IO;
	using Clio.Common;

	#region Class: OpenSolutionCreator

	public class OpenSolutionCreator : IOpenSolutionCreator
	{

		#region Fields: Private

		private readonly ITemplateProvider _templateProvider;
		private readonly IWorkspacePathBuilder _workspacePathBuilder;
		private readonly IFileSystem _fileSystem;

		#endregion

		#region Constructors: Public

		public OpenSolutionCreator(ITemplateProvider templateProvider, IWorkspacePathBuilder workspacePathBuilder,
				IFileSystem fileSystem) {
			templateProvider.CheckArgumentNull(nameof(templateProvider));
			workspacePathBuilder.CheckArgumentNull(nameof(workspacePathBuilder));
			fileSystem.CheckArgumentNull(nameof(fileSystem));
			_templateProvider = templateProvider;
			_workspacePathBuilder = workspacePathBuilder;
			_fileSystem = fileSystem;
		}

		#endregion

		#region Methods: Private

		private string ReplaceMacro(string template, string solutionRelativePath, string coreLibPath, 
				string coreTargetFramework) {
			return template.Replace("$CoreLibPath$", coreLibPath)
				.Replace("$CoreTargetFramework$", coreTargetFramework)
				.Replace("$SolutionRelativePath$", solutionRelativePath);
		}

		private void CreateOpenSolutionCmd(string path, string openSolutionCmdFileName, string solutionRelativePath, 
				string creatioSdkRelativePath, string coreTargetFramework) {
			string openProjectCmdPath = Path.Combine(path, openSolutionCmdFileName);
			string template = _templateProvider.GetTemplate("OpenSolution.cmd");
			string coreLibPath = Path.Combine("..", "..", "..", creatioSdkRelativePath);
			string content = ReplaceMacro(template, solutionRelativePath, coreLibPath, coreTargetFramework);
			_fileSystem.WriteAllTextToFile(openProjectCmdPath, content);
		}

		private void CreateFrameworkOpenSolutionCmd(string rootPath, Version nugetVersion) {
			string clioDirectoryPath = _workspacePathBuilder.BuildClioDirectoryPath(rootPath);
			string solutionPath = _workspacePathBuilder.BuildSolutionPath(clioDirectoryPath);
			string solutionRelativePath = _fileSystem.ConvertToRelativePath(solutionPath, rootPath);
			string coreCreatioSdkPath = _workspacePathBuilder.BuildFrameworkCreatioSdkPath(rootPath, nugetVersion);
			string creatioSdkRelativePath = _fileSystem.ConvertToRelativePath(coreCreatioSdkPath, rootPath);
			CreateOpenSolutionCmd(rootPath, "OpenSolution.cmd",
				solutionRelativePath, creatioSdkRelativePath, "net472");
		}

		private void CreateCoreOpenSolutionCmd(string rootPath, Version nugetVersion) {
			string clioDirectoryPath = _workspacePathBuilder.BuildClioDirectoryPath(rootPath);
			string solutionPath = _workspacePathBuilder.BuildSolutionPath(rootPath);
			string solutionRelativePath = _fileSystem.ConvertToRelativePath(solutionPath, rootPath);
			string coreCreatioSdkPath = _workspacePathBuilder.BuildCoreCreatioSdkPath(rootPath, nugetVersion);
			string creatioSdkRelativePath = _fileSystem.ConvertToRelativePath(coreCreatioSdkPath, rootPath);
			CreateOpenSolutionCmd(clioDirectoryPath, "OpenNetCoreSolution.cmd",
				solutionRelativePath, creatioSdkRelativePath, "netstandard2.0");
		}

		#endregion

		#region Methods: Public

		public void Create(string rootPath, Version nugetCreatioSdkVersion) {
			CreateFrameworkOpenSolutionCmd(rootPath, nugetCreatioSdkVersion);
			CreateCoreOpenSolutionCmd(rootPath, nugetCreatioSdkVersion);
		}

		#endregion

	}

	#endregion

}