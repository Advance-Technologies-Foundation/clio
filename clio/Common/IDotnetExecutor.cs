namespace Clio.Common;

public interface IDotnetExecutor
{

    #region Methods: Public

    string Execute(string command, bool waitForExit, string workingDirectory = null);

    #endregion

}
