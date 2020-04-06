namespace Clio.Common
{
	public interface INugetExecutor
	{
		string Execute(string command, bool waitForExit, string workingDirectory = null);
	}
}