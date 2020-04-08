using System.IO;

namespace Clio.Common
{

	#region Class: NugetExecutor

	public class NugetExecutor : INugetExecutor
	{

		#region Fields: Private

		private readonly EnvironmentSettings _environmentSettings;
		private readonly IProcessExecutor _processExecutor;
		private readonly IWorkingDirectoriesProvider _workingDirectoriesProvider;

		#endregion

		#region Constructors: Public

		public NugetExecutor(EnvironmentSettings environmentSettings, IProcessExecutor processExecutor, 
				IWorkingDirectoriesProvider workingDirectoriesProvider) {
			environmentSettings.CheckArgumentNull(nameof(environmentSettings));
			processExecutor.CheckArgumentNull(nameof(processExecutor));
			workingDirectoriesProvider.CheckArgumentNull(nameof(workingDirectoriesProvider));
			_environmentSettings = environmentSettings;
			_processExecutor = processExecutor;
			_workingDirectoriesProvider = workingDirectoriesProvider;
		}

		#endregion

		#region Methods: Public

		public string Execute(string command, bool waitForExit, string workingDirectory = null) {
			command.CheckArgumentNullOrWhiteSpace(nameof(command));
			string nugetPath = _environmentSettings.IsNetCore
				? "nuget"
				: Path.Combine(_workingDirectoriesProvider.WindowsNugetToolDirectory, "nuget.exe");
			return _processExecutor.Execute(nugetPath, command, waitForExit, workingDirectory);
		}

		#endregion

	}

	#endregion

}