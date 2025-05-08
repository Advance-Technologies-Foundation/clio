using System;
using System.Collections.Generic;
using System.IO.Abstractions;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Clio.CreatioModel;
using Clio.Workspaces;
using CreatioModel;
using YamlDotNet.Serialization;

namespace Clio.Command;

public class EnvironmentManager : IEnvironmentManager
{

    #region Fields: Private

    private readonly IFileSystem fileSystem;
    private readonly IDeserializer yamlDesirializer;
    private readonly ISerializer serializer;

    #endregion

    #region Constructors: Public

    public EnvironmentManager(IFileSystem fileSystem, IDeserializer deserializer, ISerializer serializer)
    {
        this.fileSystem = fileSystem;
        yamlDesirializer = deserializer;
        this.serializer = serializer;
    }

    #endregion

    #region Methods: Private

    private static int[] FindMatchingLines(string[] lines, string pattern)
    {
        List<int> matchingLineNumbers = new();

        for (int i = 0; i < lines.Length; i++)
        {
            if (Regex.IsMatch(lines[i], pattern))
            {
                matchingLineNumbers.Add(i + 1); // Add 1 because line numbers start from 1
            }
        }

        return matchingLineNumbers.ToArray();
    }

    private IEnumerable<AppHubInfo> GetAppHubsFromManifest(string manifestFilePath)
    {
        EnvironmentManifest envManiifest = LoadEnvironmentManifestFromFile(manifestFilePath);
        return envManiifest.AppHubs;
    }

    List<CreatioManifestPackage> IEnvironmentManager.GetPackagesGromManifest(string manifestFileName)
    {
        return LoadEnvironmentManifestFromFile(manifestFileName).Packages;
    }

    #endregion

    #region Methods: Public

    public int ApplyManifest(string manifestFilePath)
    {
        throw new NotImplementedException();
    }

    public List<SysInstalledApp> FindApplicationsInAppHub(string manifestFilePath)
    {
        List<SysInstalledApp> applications = GetApplicationsFromManifest(manifestFilePath);
        IEnumerable<AppHubInfo> appHubs = GetAppHubsFromManifest(manifestFilePath);
        List<SysInstalledApp> appsFromAppHub = new();
        foreach (SysInstalledApp app in applications)
        {
            foreach (AppHubInfo app_hub in appHubs)
            {
                if (app_hub.Name == app.AppHubName)
                {
                    string zipFileName = app.Branch switch
                                         {
                                             var _ when app.Branch is not null => app_hub.GetAppZipFileNameWithBranch(
                                                 Workspace.GetSanitizeFileNameFromString(app.Name), app.Version,
                                                 Workspace.GetSanitizeFileNameFromString(app.Branch)),
                                             var _ => app_hub.GetAppZipFileName(
                                                 Workspace.GetSanitizeFileNameFromString(app.Name), app.Version)
                                         };
                    if (!fileSystem.File.Exists(app.ZipFileName) && app.Aliases != null)
                    {
                        foreach (string alias in app.Aliases)
                        {
                            string aliasFileName = app.Branch switch
                                                   {
                                                       var _ when app.Branch is not null =>
                                                           app_hub.GetAppZipFileNameWithBranch(
                                                               Workspace.GetSanitizeFileNameFromString(alias),
                                                               app.Version,
                                                               Workspace.GetSanitizeFileNameFromString(app.Branch)),
                                                       var _ => app_hub.GetAppZipFileName(
                                                           Workspace.GetSanitizeFileNameFromString(alias), app.Version)
                                                   };
                            if (fileSystem.File.Exists(aliasFileName))
                            {
                                zipFileName = aliasFileName;
                                break;
                            }
                        }
                    }
                    app.ZipFileName = zipFileName;
                    appsFromAppHub.Add(app);
                }
            }
        }
        return appsFromAppHub;
    }

    public List<SysInstalledApp> GetApplicationsFromManifest(string manifestFilePath)
    {
        string manifest = fileSystem.File.ReadAllText(manifestFilePath);
        EnvironmentManifest envManifest = yamlDesirializer.Deserialize<EnvironmentManifest>(manifest);
        return envManifest.Applications;
    }

    public EnvironmentManifest GetDiffManifest(EnvironmentManifest sourceManifest, EnvironmentManifest targetManifest)
    {
        EnvironmentManifest diffManifest = new();

        diffManifest.Packages = sourceManifest.Packages
                                              .Where(p => !targetManifest.Packages.Any(sp => sp.Name == p.Name))
                                              .ToList();

        diffManifest.Settings = sourceManifest.Settings
                                              .Where(p => !targetManifest.Settings.Any(sp =>
                                                  sp.Code == p.Code && sp.Value == p.Value))
                                              .ToList();

        diffManifest.Features = sourceManifest.Features
                                              .Where(p => !targetManifest.Features.Any(sp =>
                                                  sp.Code == p.Code && sp.Value == p.Value))
                                              .ToList();

        return diffManifest;
    }

    public EnvironmentSettings GetEnvironmentFromManifest(string manifestFilePath)
    {
        string manifest = fileSystem.File.ReadAllText(manifestFilePath);
        EnvironmentManifest envManifest = yamlDesirializer.Deserialize<EnvironmentManifest>(manifest);
        return envManifest.EnvironmentSettings;
    }

    public IEnumerable<Feature> GetFeaturesFromManifest(string manifestFilePath)
    {
        string manifest = fileSystem.File.ReadAllText(manifestFilePath);
        EnvironmentManifest envManifest = yamlDesirializer.Deserialize<EnvironmentManifest>(manifest);
        return envManifest.Features;
    }

    public IEnumerable<CreatioManifestSetting> GetSettingsFromManifest(string manifestFilePath)
    {
        return LoadEnvironmentManifestFromFile(manifestFilePath).Settings;
    }

    public IEnumerable<CreatioManifestWebService> GetWebServicesFromManifest(string manifestFilePath)
    {
        return LoadEnvironmentManifestFromFile(manifestFilePath).WebServices;
    }

    public EnvironmentManifest LoadEnvironmentManifestFromFile(string manifestFilePath)
    {
        string manifest = fileSystem.File.ReadAllText(manifestFilePath);
        EnvironmentManifest envManifest = yamlDesirializer.Deserialize<EnvironmentManifest>(manifest);

        string[] lines = fileSystem.File.ReadAllLines(manifestFilePath);
        string pattern = @"^\s*- code:\s*[""]?\s*[""]?\s*$";
        int[] lineNumbers = FindMatchingLines(lines, pattern);

        StringBuilder sb = new();
        bool hasError = false;
        if (lineNumbers.Any())
        {
            sb.AppendLine(
                $"Setting code cannot be null or empty. Found invalid values on lines {string.Join(',', lineNumbers)}");
            hasError = true;
        }

        foreach (CreatioManifestSetting setting in envManifest.Settings)
        {
            if (setting.Value is null)
            {
                sb.AppendLine($"Setting value cannot be null for: [{setting.Code}]");
                hasError = true;
            }
        }
        if (hasError)
        {
            throw new Exception(sb.ToString());
        }
        return envManifest;
    }

    public void SaveManifestToFile(string manifestFileName, EnvironmentManifest envManifest, bool overwrite = false)
    {
        if (!overwrite && fileSystem.File.Exists(manifestFileName))
        {
            throw new Exception($"Manifest file already exists: {manifestFileName}");
        }
        string manifestContent = serializer.Serialize(envManifest);
        fileSystem.File.WriteAllText(manifestFileName, manifestContent);
    }

    #endregion

}

public interface IEnvironmentManager
{

    #region Methods: Public

    int ApplyManifest(string manifestFilePath);

    List<SysInstalledApp> FindApplicationsInAppHub(string manifestFilePath);

    List<SysInstalledApp> GetApplicationsFromManifest(string manifestFilePath);

    EnvironmentManifest GetDiffManifest(EnvironmentManifest sourceManifest, EnvironmentManifest targetManifest);

    EnvironmentSettings GetEnvironmentFromManifest(string manifestFilePath);

    IEnumerable<Feature> GetFeaturesFromManifest(string manifestFilePath);

    List<CreatioManifestPackage> GetPackagesGromManifest(string manifestFileName);

    IEnumerable<CreatioManifestSetting> GetSettingsFromManifest(string manifestFilePath);

    IEnumerable<CreatioManifestWebService> GetWebServicesFromManifest(string manifestFilePath);

    EnvironmentManifest LoadEnvironmentManifestFromFile(string manifestFilePath);

    void SaveManifestToFile(string manifestFileName, EnvironmentManifest envManifest, bool overwrite = false);

    #endregion

}

public class CreatioManifestSetting
{

    #region Properties: Public

    [YamlMember(Alias = "code")]
    public string Code { get; set; }

    [YamlMember(Alias = "users_values")]
    public Dictionary<string, string> UserValues { get; set; } = new();

    [YamlMember(Alias = "value")]
    public string Value { get; set; }

    #endregion

    #region Methods: Internal

    internal bool HasValue()
    {
        return !string.IsNullOrEmpty(Value) && Value != "undefined";
    }

    #endregion

}

public class CreatioManifestWebService
{

    #region Properties: Public

    [YamlMember(Alias = "name")]
    public string Name { get; set; }

    [YamlMember(Alias = "url")]
    public string Url { get; set; }

    #endregion

}

public class Feature
{

    #region Properties: Public

    [YamlMember(Alias = "code")]
    public string Code { get; set; }

    [YamlMember(Alias = "users_values")]
    public Dictionary<string, bool> UserValues { get; set; } = new();

    [YamlMember(Alias = "value")]
    public bool Value { get; set; }

    #endregion

}

public class CreatioManifestPackage
{

    #region Properties: Public

    [YamlMember(Alias = "hash", Order = 2)]
    public string Hash { get; set; }

    [YamlMember(Alias = "maintainer", Order = 3)]
    public string Maintainer { get; set; }

    [YamlMember(Alias = "name", Order = 1)]
    public string Name { get; set; }

    [YamlMember(Alias = "schemas", Order = 4)]
    public List<CreatioManifestPackageSchema> Schemas { get; set; } = new();

    #endregion

}

public class CreatioManifestPackageSchema
{

    #region Properties: Public

    [YamlMember(Alias = "hash", Order = 2)]
    public string Hash { get; set; }

    [YamlMember(Alias = "name", Order = 1)]
    public string Name { get; set; }

    #endregion

}
