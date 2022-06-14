using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Clio.Common
{

	#region Interface: IExecutablePermissionsActualizer

	public interface IExecutablePermissionsActualizer
	{

		#region Methods: Public

		void Actualize(string directoryPath);

		#endregion

	}

	#endregion

	#region Class: ExecutablePermissionsActualizer

	public class ExecutablePermissionsActualizer : IExecutablePermissionsActualizer
	{

		#region Fields: Private

		private readonly IProcessExecutor _processExecutor;
		private readonly IFileSystem _fileSystem;

		#endregion

		#region Constructors: Public

		public ExecutablePermissionsActualizer(IProcessExecutor processExecutor, IFileSystem fileSystem) {
			processExecutor.CheckArgumentNull(nameof(processExecutor));
			fileSystem.CheckArgumentNull(nameof(fileSystem));
			_processExecutor = processExecutor;
			_fileSystem = fileSystem;
		}

		#endregion

		#region Methods: Private

		private static IEnumerable<string> GetScriptFiles(string directoryPath) {
			DirectoryInfo scriptFilesDirectoryInfo = new DirectoryInfo(directoryPath);
			return scriptFilesDirectoryInfo
				.GetFiles("*.sh", SearchOption.AllDirectories)
				.Select(fileInfo => fileInfo.FullName);
		}

		private void ActualizePermissionsScriptFile(string scriptFile) {
			_processExecutor.Execute("/bin/bash", $"-c \"sudo chmod +x {scriptFile}\"", false);
		}

		#endregion

		#region Methods: Public

		public void Actualize(string directoryPath) {
			IEnumerable<string> scriptFiles = GetScriptFiles(directoryPath);
			foreach (string scriptFile in scriptFiles) {
				ActualizePermissionsScriptFile(scriptFile);
			}
		}

		#endregion

	}

	#endregion

}