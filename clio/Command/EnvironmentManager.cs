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

public class EnvironmentManager(IFileSystem fileSystem, IDeserializer deserializer, ISerializer serializer): IEnvironmentManager
{
    private readonly IFileSystem fileSystem = fileSystem;
    private readonly IDeserializer yamlDesirializer = deserializer;
    private readonly ISerializer serializer = serializer;

    public int ApplyManifest(string manifestFilePath) => throw new NotImplementedException();

    public List<SysInstalledApp> FindApplicationsInAppHub(string manifestFilePath)
    {
        List<SysInstalledApp> applications = GetApplicationsFromManifest(manifestFilePath);
        IEnumerable<AppHubInfo> appHubs = GetAppHubsFromManifest(manifestFilePath);
        List<SysInstalledApp> appsFromAppHub = [];
        foreach (SysInstalledApp app in applications)
        {
            foreach (AppHubInfo app_hub in appHubs)
            {
                if (app_hub.Name == app.AppHubName)
                {
                    string zipFileName = app.Branch switch
                    {
                        _ when app.Branch is not null => app_hub.GetAppZipFileNameWithBranch(
                            Workspace.GetSanitizeFileNameFromString(app.Name), app.Version,
                            Workspace.GetSanitizeFileNameFromString(app.Branch)),
                        _ => app_hub.GetAppZipFileName(Workspace.GetSanitizeFileNameFromString(app.Name), app.Version)
                    };
                    if (!fileSystem.File.Exists(app.ZipFileName) && app.Aliases != null)
                    {
                        foreach (string alias in app.Aliases)
                        {
                            string aliasFileName = app.Branch switch
                            {
                                _ when app.Branch is not null => app_hub.GetAppZipFileNameWithBranch(
                                    Workspace.GetSanitizeFileNameFromString(alias), app.Version,
                                    Workspace.GetSanitizeFileNameFromString(app.Branch)),
                                _ => app_hub.GetAppZipFileName(
                                    Workspace.GetSanitizeFileNameFromString(alias),
                                    app.Version)
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

    private IEnumerable<AppHubInfo> GetAppHubsFromManifest(string manifestFilePath)
    {
        EnvironmentManifest envManiifest = LoadEnvironmentManifestFromFile(manifestFilePath);
        return envManiifest.AppHubs;
    }

    public EnvironmentManifest LoadEnvironmentManifestFromFile(string manifestFilePath)
    {
        string manifest = fileSystem.File.ReadAllText(manifestFilePath);
        EnvironmentManifest envManifest = yamlDesirializer.Deserialize<EnvironmentManifest>(manifest);

        string[] lines = fileSystem.File.ReadAllLines(manifestFilePath);
        string pattern = @"^\s*- code:\s*[""]?\s*[""]?\s*$";
        int[] lineNumbers = FindMatchingLines(lines, pattern);

        StringBuilder sb = new ();
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

    public List<SysInstalledApp> GetApplicationsFromManifest(string manifestFilePath)
    {
        string manifest = fileSystem.File.ReadAllText(manifestFilePath);
        EnvironmentManifest envManifest = yamlDesirializer.Deserialize<EnvironmentManifest>(manifest);
        return envManifest.Applications;
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

    private static int[] FindMatchingLines(string[] lines, string pattern)
    {
        List<int> matchingLineNumbers = [];

        for (int i = 0; i < lines.Length; i++)
        {
            if (Regex.IsMatch(lines[i], pattern))
            {
                matchingLineNumbers.Add(i + 1); // Add 1 because line numbers start from 1
            }
        }

        return[.. matchingLineNumbers];
    }

    public IEnumerable<CreatioManifestSetting> GetSettingsFromManifest(string manifestFilePath) =>
        LoadEnvironmentManifestFromFile(manifestFilePath).Settings;

    public IEnumerable<CreatioManifestWebService> GetWebServicesFromManifest(string manifestFilePath) =>
        LoadEnvironmentManifestFromFile(manifestFilePath).WebServices;

    public List<CreatioManifestPackage> GetPackagesGromManifest(string manifestFileName) =>
        LoadEnvironmentManifestFromFile(manifestFileName).Packages;

    public void SaveManifestToFile(string manifestFileName, EnvironmentManifest envManifest, bool overwrite = false)
    {
        if (!overwrite && fileSystem.File.Exists(manifestFileName))
        {
            throw new Exception($"Manifest file already exists: {manifestFileName}");
        }

        string manifestContent = serializer.Serialize(envManifest);
        fileSystem.File.WriteAllText(manifestFileName, manifestContent);
    }

    public EnvironmentManifest GetDiffManifest(EnvironmentManifest sourceManifest, EnvironmentManifest targetManifest)
    {
        EnvironmentManifest diffManifest = new ()
        {
            Packages = sourceManifest.Packages
            .Where(p => !targetManifest.Packages.Any(sp => sp.Name == p.Name))
            .ToList(),

            Settings = sourceManifest.Settings
            .Where(p => !targetManifest.Settings.Any(sp => sp.Code == p.Code && sp.Value == p.Value))
            .ToList(),

            Features = sourceManifest.Features
            .Where(p => !targetManifest.Features.Any(sp => sp.Code == p.Code && sp.Value == p.Value))
            .ToList()
        };

        return diffManifest;
    }
}

public interface IEnvironmentManager
{
    List<SysInstalledApp> GetApplicationsFromManifest(string manifestFilePath);

    EnvironmentManifest LoadEnvironmentManifestFromFile(string manifestFilePath);

    int ApplyManifest(string manifestFilePath);

    List<SysInstalledApp> FindApplicationsInAppHub(string manifestFilePath);

    EnvironmentSettings GetEnvironmentFromManifest(string manifestFilePath);

    IEnumerable<Feature> GetFeaturesFromManifest(string manifestFilePath);

    IEnumerable<CreatioManifestSetting> GetSettingsFromManifest(string manifestFilePath);

    IEnumerable<CreatioManifestWebService> GetWebServicesFromManifest(string manifestFilePath);

    List<CreatioManifestPackage> GetPackagesGromManifest(string manifestFileName);

    void SaveManifestToFile(string manifestFileName, EnvironmentManifest envManifest, bool overwrite = false);

    EnvironmentManifest GetDiffManifest(EnvironmentManifest sourceManifest, EnvironmentManifest targetManifest);
}

public class CreatioManifestSetting
{
    [YamlMember(Alias = "code")]
    public string Code { get; set; }

    [YamlMember(Alias = "value")]
    public string Value { get; set; }

    [YamlMember(Alias = "users_values")]
    public Dictionary<string, string> UserValues { get; set; } = [];

    internal bool HasValue() => !string.IsNullOrEmpty(Value) && Value != "undefined";
}

public class CreatioManifestWebService
{
    [YamlMember(Alias = "url")]
    public string Url { get; set; }

    [YamlMember(Alias = "name")]
    public string Name { get; set; }
}

public class Feature
{
    [YamlMember(Alias = "code")]
    public string Code { get; set; }

    [YamlMember(Alias = "value")]
    public bool Value { get; set; }

    [YamlMember(Alias = "users_values")]
    public Dictionary<string, bool> UserValues { get; set; } = [];
}

public class CreatioManifestPackage
{
    [YamlMember(Alias = "name", Order = 1)]
    public string Name { get; set; }

    [YamlMember(Alias = "hash", Order = 2)]
    public string Hash { get; set; }

    [YamlMember(Alias = "maintainer", Order = 3)]
    public string Maintainer { get; set; }

    [YamlMember(Alias = "schemas", Order = 4)]
    public List<CreatioManifestPackageSchema> Schemas { get; set; } = [];
}

public class CreatioManifestPackageSchema
{
    [YamlMember(Alias = "name", Order = 1)]
    public string Name { get; set; }

    [YamlMember(Alias = "hash", Order = 2)]
    public string Hash { get; set; }
}
