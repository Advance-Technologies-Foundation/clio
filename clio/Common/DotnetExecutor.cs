namespace Clio.Common
{

	#region Class: DotnetExecutor

	public class DotnetExecutor : IDotnetExecutor
	{

		#region Fields: Private

		private readonly IProcessExecutor _processExecutor;

		#endregion

		#region Constructors: Public

		public DotnetExecutor(IProcessExecutor processExecutor) {
			processExecutor.CheckArgumentNull(nameof(processExecutor));
			_processExecutor = processExecutor;
		}

		#endregion

		#region Methods: Public

		public string Execute(string command, bool waitForExit, string workingDirectory = null) {
			command.CheckArgumentNullOrWhiteSpace(nameof(command));
			return _processExecutor.Execute("dotnet", command, waitForExit, workingDirectory);
		}

		#endregion

	}

	#endregion

}