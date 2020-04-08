namespace Clio.Common
{
	public interface IProcessExecutor
	{
		string Execute(string program, string command, bool waitForExit, string workingDirectory = null);

	}
}