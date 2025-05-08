using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Clio.Common;

public interface IExecutablePermissionsActualizer
{
    void Actualize(string directoryPath);
}

public class ExecutablePermissionsActualizer : IExecutablePermissionsActualizer
{
    private readonly IFileSystem _fileSystem;
    private readonly IProcessExecutor _processExecutor;

    public ExecutablePermissionsActualizer(IProcessExecutor processExecutor, IFileSystem fileSystem)
    {
        processExecutor.CheckArgumentNull(nameof(processExecutor));
        fileSystem.CheckArgumentNull(nameof(fileSystem));
        _processExecutor = processExecutor;
        _fileSystem = fileSystem;
    }

    public void Actualize(string directoryPath)
    {
        IEnumerable<string> scriptFiles = GetScriptFiles(directoryPath);
        foreach (string scriptFile in scriptFiles)
        {
            ActualizePermissionsScriptFile(scriptFile);
        }
    }

    private static IEnumerable<string> GetScriptFiles(string directoryPath)
    {
        DirectoryInfo scriptFilesDirectoryInfo = new(directoryPath);
        return scriptFilesDirectoryInfo
            .GetFiles("*.sh", SearchOption.AllDirectories)
            .Select(fileInfo => fileInfo.FullName);
    }

    private void ActualizePermissionsScriptFile(string scriptFile) =>
        _processExecutor.Execute("/bin/bash", $"-c \"sudo chmod +x {scriptFile}\"", false);
}
