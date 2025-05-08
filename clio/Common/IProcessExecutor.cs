namespace Clio.Common;

public interface IProcessExecutor
{

    #region Methods: Public

    string Execute(string program, string command, bool waitForExit, string workingDirectory = null,
        bool showOutput = false);

    #endregion

}
