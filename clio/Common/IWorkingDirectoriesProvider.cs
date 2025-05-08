using System;

namespace Clio.Common;

public interface IWorkingDirectoriesProvider
{

    #region Properties: Public

    string BaseTempDirectory { get; }

    string CurrentDirectory { get; }

    string ExecutingDirectory { get; }

    string TemplateDirectory { get; }

    #endregion

    #region Methods: Public

    string CreateTempDirectory();

    void CreateTempDirectory(Action<string> onCreated);

    T CreateTempDirectory<T>(Func<string, T> onCreated);

    void DeleteDirectoryIfExists(string path);

    string GetTemplateFolderPath(string templateFolderName);

    string GetTemplatePath(string templateName);

    #endregion

}
