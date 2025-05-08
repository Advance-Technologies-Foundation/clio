using System.IO;

namespace Clio.UserEnvironment;

public interface ISettingsRepository
{

    #region Properties: Public

    /// <summary>
    ///     Path to appsettings.json file
    /// </summary>
    string AppSettingsFilePath { get; }

    #endregion

    #region Methods: Public

    void ConfigureEnvironment(string name, EnvironmentSettings environment);

    EnvironmentSettings? FindEnvironment(string name = null);

    string FindEnvironmentNameByUri(string uri);

    string GetCreatioProductsFolder();

    EnvironmentSettings GetEnvironment(string name = null);

    EnvironmentSettings GetEnvironment(EnvironmentOptions options);

    string GetIISClioRootPath();

    string GetRemoteArtefactServerPath();

    bool IsEnvironmentExists(string name);

    void OpenFile();

    void RemoveAllEnvironment();

    void RemoveEnvironment(string name);

    void SetActiveEnvironment(string name);

    void ShowSettingsTo(TextWriter textWriter, string name, bool showShort = false);

    #endregion

}
