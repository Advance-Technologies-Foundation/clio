using System.IO;
using YamlDotNet.Serialization;

namespace Clio.CreatioModel;

public class AppHubInfo
{

    #region Properties: Public

    [YamlMember(Alias = "name")]
    public string Name { get; set; }

    [YamlMember(Alias = "path")]
    public string RootPath { get; set; }

    [YamlMember(Alias = "url")]
    public string Url { get; set; }

    #endregion

    #region Methods: Internal

    internal string GetAppZipFileName(string name, string version)
    {
        return Path.Combine(RootPath.Replace('/', Path.DirectorySeparatorChar), name, version, $"{name}_{version}.zip");
    }

    internal string GetAppZipFileNameWithBranch(string name, string version, string branch)
    {
        return Path.Combine(RootPath.Replace('/', Path.DirectorySeparatorChar), name, branch,
            $"{name}_{branch}_{version}.zip");
    }

    #endregion

}
