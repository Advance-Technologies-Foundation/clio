namespace Clio.Common
{
	public interface IDotnetExecutor
	{ 
		string Execute(string command, bool waitForExit, string workingDirectory = null);

	}

}