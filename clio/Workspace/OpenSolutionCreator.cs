namespace Clio.Workspace
{
	using System.IO;
	using Clio.Common;

	#region Class: OpenSolutionCreator

	public class OpenSolutionCreator : IOpenSolutionCreator
	{

		#region Fields: Private

		private readonly ITemplateProvider _templateProvider;
		private readonly IFileSystem _fileSystem;

		#endregion

		#region Constructors: Public

		public OpenSolutionCreator(ITemplateProvider templateProvider, IFileSystem fileSystem) {
			templateProvider.CheckArgumentNull(nameof(templateProvider));
			fileSystem.CheckArgumentNull(nameof(fileSystem));
			_templateProvider = templateProvider;
			_fileSystem = fileSystem;
		}

		#endregion

		#region Methods: Private

		private string ReplaceMacro(string template, string solutionName, string coreLibPath, 
				string coreTargetFramework) {
			return template.Replace("$CoreLibPath$", coreLibPath)
				.Replace("$CoreTargetFramework$", coreTargetFramework)
				.Replace("$SolutionName$", solutionName);
		}

		private void CreateOpenSolutionCmd(string rootPath, string solutionName, string nugetFolderName, 
				string nugetVersion, bool isFramework) {
			string openSolutionCmdFileName = isFramework ? "OpenSolution.cmd" : "OpenNetCoreSolution.cmd";
			string openProjectCmdPath = Path.Combine(rootPath, openSolutionCmdFileName);
			string template = _templateProvider.GetTemplate("OpenSolution.cmd");
			string nugetTargetFramework = isFramework ? "net40" : "netstandard2.0";
			string coreLibPath = Path.Combine("..", "..", "..", nugetFolderName, "creatiosdk", nugetVersion,
				"lib", nugetTargetFramework);
			string coreTargetFramework = isFramework ? "net472" : "netstandard2.0";
			string content = ReplaceMacro(template, solutionName, coreLibPath, coreTargetFramework);
			_fileSystem.WriteAllTextToFile(openProjectCmdPath, content);
		}


		#endregion

		#region Methods: Public

		public void Create(string rootPath, string solutionName, string nugetFolderName, 
				string nugetCreatioSdkVersion) {
			CreateOpenSolutionCmd(rootPath, solutionName, nugetFolderName, nugetCreatioSdkVersion, true);
			CreateOpenSolutionCmd(rootPath, solutionName, nugetFolderName, nugetCreatioSdkVersion, false);
		}

		#endregion

	}

	#endregion

}