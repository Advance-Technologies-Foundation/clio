namespace Clio.Common;

public class DotnetExecutor : IDotnetExecutor
{
    private readonly IProcessExecutor _processExecutor;

    public DotnetExecutor(IProcessExecutor processExecutor)
    {
        processExecutor.CheckArgumentNull(nameof(processExecutor));
        _processExecutor = processExecutor;
    }

    public string Execute(string command, bool waitForExit, string workingDirectory = null)
    {
        command.CheckArgumentNullOrWhiteSpace(nameof(command));
        return _processExecutor.Execute("dotnet", command, waitForExit, workingDirectory);
    }
}
