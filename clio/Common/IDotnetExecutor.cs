namespace Clio.Common
{
	public interface IDotnetExecutor
	{ 
		void Execute(string command, bool waitForExit, string workingDirectory = null);

	}

}