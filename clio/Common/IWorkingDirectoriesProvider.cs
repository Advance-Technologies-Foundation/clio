using System;

namespace Clio.Common;

public interface IWorkingDirectoriesProvider
{
    string ExecutingDirectory { get; }

    string TemplateDirectory { get; }

    string BaseTempDirectory { get; }

    string CurrentDirectory { get; }

    string GetTemplatePath(string templateName);

    string GetTemplateFolderPath(string templateFolderName);

    string CreateTempDirectory();

    void CreateTempDirectory(Action<string> onCreated);

    T CreateTempDirectory<T>(Func<string, T> onCreated);

    void DeleteDirectoryIfExists(string path);
}
