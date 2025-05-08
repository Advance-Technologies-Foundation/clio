using System.Collections.Generic;
using System.Linq;
using Clio.CreatioModel;
using CreatioModel;
using YamlDotNet.Serialization;

namespace Clio.Command;

public class EnvironmentManifest
{

    #region Fields: Private

    private List<Feature> _features = new();
    private List<CreatioManifestSetting> _settings = new();
    private List<CreatioManifestWebService> _webServices = new();
    private List<CreatioManifestPackage> _packages = new();

    #endregion

    #region Properties: Public

    [YamlMember(Alias = "app_hubs")]
    public List<AppHubInfo> AppHubs { get; set; }

    [YamlMember(Alias = "apps")]
    public List<SysInstalledApp> Applications { get; set; }

    [YamlMember(Alias = "environment")]
    public EnvironmentSettings EnvironmentSettings { get; internal set; }

    [YamlMember(Alias = "features")]
    public List<Feature> Features
    {
        get { return _features; }
        set { _features = value ?? new List<Feature>(); }
    }

    [YamlMember(Alias = "packages")]
    public List<CreatioManifestPackage> Packages
    {
        get { return _packages; }
        set { _packages = value?.OrderBy(p => p.Name).ToList() ?? new List<CreatioManifestPackage>(); }
    }

    [YamlMember(Alias = "settings")]
    public List<CreatioManifestSetting> Settings
    {
        get { return _settings; }
        set { _settings = value ?? new List<CreatioManifestSetting>(); }
    }

    [YamlMember(Alias = "webservices")]
    public List<CreatioManifestWebService> WebServices
    {
        get { return _webServices; }
        set { _webServices = value ?? new List<CreatioManifestWebService>(); }
    }

    #endregion

}
