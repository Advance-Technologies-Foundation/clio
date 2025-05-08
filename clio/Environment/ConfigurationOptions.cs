using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json.Serialization;
using Clio.Common;
using Clio.UserEnvironment;
using Clio.Utilities;
using ConsoleTables;
using Newtonsoft.Json;
using YamlDotNet.Serialization;
using FileSystem = System.IO.Abstractions.FileSystem;
using IFileSystem = System.IO.Abstractions.IFileSystem;

namespace Clio;

public class EnvironmentSettings
{
    private string _authAppUri;

    [YamlMember(Alias = "url")] public string Uri { get; set; }

    public string DbName { get; set; }

    public string BackupFilePath { get; set; }

    public string Login { get; set; }

    public string Password { get; set; }

    public string Maintainer { get; set; }

    public bool IsNetCore { get; set; }

    public string ClientId { get; set; }

    public string DbServerKey { get; set; }

    [Newtonsoft.Json.JsonIgnore] public DbServer DbServer { get; set; }

    public string ClientSecret { get; set; }

    public string WorkspacePathes { get; set; }

    [YamlMember(Alias = "authappurl")]
    public string AuthAppUri
    {
        get
        {
            if (string.IsNullOrEmpty(_authAppUri))
            {
                if (Uri?.ToLower().Contains(".creatio.com") ?? false)
                {
                    return Uri?.ToLower().Replace(".creatio.com", "-is.creatio.com/connect/token");
                }
            }

            return _authAppUri;
        }
        set => _authAppUri = value;
    }

    [YamlIgnore]
    [Newtonsoft.Json.JsonIgnore]
    public string SimpleloginUri
    {
        get
        {
            if (Uri == null)
            {
                return string.Empty;
            }

            string cleanUri = Uri;
            if (!string.IsNullOrEmpty(cleanUri))
            {
                string domain = ".creatio.com";
                int index = cleanUri.IndexOf(domain);
                if (index != -1)
                {
                    cleanUri = cleanUri.Substring(0, index + domain.Length);
                }
            }

            string simpleLoginUriText = cleanUri.TrimEnd('/') +
                                        (IsNetCore ? "/Shell/?simplelogin=true" : "/0/Shell/?simplelogin=true");
            return simpleLoginUriText;
        }
    }

    public bool? Safe { get; set; }

    public bool? DeveloperModeEnabled { get; set; }

    [Newtonsoft.Json.JsonIgnore] public bool IsDevMode => DeveloperModeEnabled ?? false;

    internal void Merge(EnvironmentSettings environment)
    {
        if (!string.IsNullOrEmpty(environment.Login))
        {
            Login = environment.Login;
        }

        if (!string.IsNullOrEmpty(environment.Uri))
        {
            Uri = environment.Uri;
        }

        if (!string.IsNullOrEmpty(environment.Password))
        {
            Password = environment.Password;
        }

        if (!string.IsNullOrEmpty(environment.Maintainer))
        {
            Maintainer = environment.Maintainer;
        }

        if (environment.Safe.HasValue)
        {
            Safe = environment.Safe;
        }

        if (environment.DeveloperModeEnabled.HasValue)
        {
            DeveloperModeEnabled = environment.DeveloperModeEnabled;
        }

        IsNetCore = environment.IsNetCore;
        ClientId = environment.ClientId;
        ClientSecret = environment.ClientSecret;
        AuthAppUri = environment.AuthAppUri;
        WorkspacePathes = environment.WorkspacePathes;

        if (!string.IsNullOrEmpty(environment.DbName))
        {
            DbName = environment.DbName;
        }

        if (!string.IsNullOrEmpty(environment.DbServerKey))
        {
            DbServerKey = environment.DbServerKey;
        }

        if (environment.DbServer?.Uri != null)
        {
            DbServer ??= new DbServer();

            DbServer.Uri = environment.DbServer.Uri;
        }

        if (!string.IsNullOrEmpty(environment.BackupFilePath))
        {
            BackupFilePath = environment.BackupFilePath;
        }
    }

    public EnvironmentSettings Fill(EnvironmentOptions options)
    {
        EnvironmentSettings result = new()
        {
            Uri = string.IsNullOrEmpty(options.Uri) ? Uri : options.Uri,
            IsNetCore = options.IsNetCore ?? IsNetCore,
            DeveloperModeEnabled = options.DeveloperModeEnabled ?? DeveloperModeEnabled,
            Login = string.IsNullOrEmpty(options.Login) ? Login : options.Login,
            Password = string.IsNullOrEmpty(options.Password) ? Password : options.Password,
            ClientId = string.IsNullOrEmpty(options.ClientId) ? ClientId : options.ClientId,
            ClientSecret = string.IsNullOrEmpty(options.ClientSecret) ? ClientSecret : options.ClientSecret,
            AuthAppUri = string.IsNullOrEmpty(options.AuthAppUri) ? AuthAppUri : options.AuthAppUri,
            Maintainer =
                string.IsNullOrEmpty(options.Maintainer) ? Maintainer : options.Maintainer
        };
        if (Safe.HasValue && Safe.Value)
        {
            Console.WriteLine($"You try to apply the action on the production site {Uri}");
            Console.Write("Do you want to continue? [Y/N]:");
            ConsoleKeyInfo answer = Console.ReadKey();
            Console.WriteLine();
            if (answer.KeyChar != 'y' && answer.KeyChar != 'Y')
            {
                Console.WriteLine("Operation was canceled by user");
                Environment.Exit(1);
            }
        }

        result.WorkspacePathes =
            string.IsNullOrEmpty(options.WorkspacePathes) ? WorkspacePathes : options.WorkspacePathes;

        bool isUri = System.Uri.TryCreate(options.DbServerUri, UriKind.Absolute, out Uri uri);
        if (isUri)
        {
            result.DbServer ??= new DbServer();

            result.DbServer.Uri = uri;
        }

        if (!string.IsNullOrWhiteSpace(options.DbWorknigFolder))
        {
            result.DbServer ??= new DbServer();

            result.DbServer.WorkingFolder = options.DbWorknigFolder;
        }

        if (!string.IsNullOrWhiteSpace(options.DbUser))
        {
            result.DbServer ??= new DbServer();

            result.DbServer.Login = options.DbUser;
        }

        if (!string.IsNullOrWhiteSpace(options.DbPassword))
        {
            result.DbServer ??= new DbServer();

            result.DbServer.Password = options.DbPassword;
        }

        if (!string.IsNullOrEmpty(options.BackUpFilePath))
        {
            result.BackupFilePath = options.BackUpFilePath;
        }

        if (!string.IsNullOrEmpty(options.DbName))
        {
            result.DbName = options.DbName;
        }

        return result;
    }
}

public class Settings
{
    // TODO: This wont work for Mac and Linux
    private const string DefaultCreatioProductFolder = @"C:\CreatioProductBuild";

    // TODO: This wont work for Mac and Linux
    private const string DefaultIisRootPath = @"C:\inetpub\wwwroot\clio";
    private string _creatioProductFolder;
    private string _iISClioRootPath;
    public Settings() => Environments = [];

    [JsonProperty("creatio-products")]
    public string CreatioProductFolder
    {
        get => string.IsNullOrWhiteSpace(_creatioProductFolder) ? DefaultCreatioProductFolder : _creatioProductFolder;
        set => _creatioProductFolder = value;
    }

    [JsonProperty("iis-clio-root-path")]
    public string IISClioRootPath
    {
        get => string.IsNullOrWhiteSpace(_iISClioRootPath) ? DefaultIisRootPath : _iISClioRootPath;
        set => _iISClioRootPath = value;
    }

    [JsonProperty("$schema")] public string Schema => "./schema.json";

    public string ActiveEnvironmentKey { get; set; }

    [JsonProperty("dbConnectionStringKeys")]
    public Dictionary<string, DbServer> DbServers { get; set; }

    public bool Autoupdate { get; set; }

    public Dictionary<string, EnvironmentSettings> Environments { get; set; }

    public string RemoteArtefactServerPath { get; set; }

    public EnvironmentSettings GetActiveEnviroment()
    {
        if (string.IsNullOrEmpty(ActiveEnvironmentKey)
            || !Environments.ContainsKey(ActiveEnvironmentKey))
        {
            ActiveEnvironmentKey = Environments.First().Key;
            return Environments.First().Value;
        }

        return Environments[ActiveEnvironmentKey];
    }
}

public class SettingsRepository : ISettingsRepository
{
    private const string FileName = "appsettings.json";
    private const string SchemaFileName = "schema.json";

    private Settings _settings = new();

    public SettingsRepository(IFileSystem fileSystem = null)
    {
        if (fileSystem != null)
        {
            FileSystem = fileSystem;
        }

        InitializeSettingsFile();
        InitSettings();
    }

    public static string AppSettingsFolderPath
    {
        get
        {
            string? userPath = Environment.GetEnvironmentVariable(
                RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "LOCALAPPDATA" : "HOME");
            Assembly? assy = Assembly.GetEntryAssembly();
            AssemblyCompanyAttribute? companyName = assy.GetCustomAttributes<AssemblyCompanyAttribute>()
                .FirstOrDefault();
            AssemblyProductAttribute? product = assy.GetCustomAttributes<AssemblyProductAttribute>()
                .FirstOrDefault();
            userPath ??= string.Empty;

            return Path.Combine(userPath, companyName?.Company, product?.Product);
        }
    }

    public static string AppSettingsFile => Path.Combine(AppSettingsFolderPath, FileName);

    private string SchemaFilePath => Path.Combine(AppSettingsFolderPath, SchemaFileName);

    internal static IFileSystem FileSystem { get; set; } = new FileSystem();

    public string AppSettingsFilePath => AppSettingsFile;

    public void ShowSettingsTo(TextWriter streamWriter, string environment = null, bool showShort = false)
    {
        JsonSerializer serializer = new()
        {
            Formatting = Formatting.Indented, NullValueHandling = NullValueHandling.Ignore
        };

        if (string.IsNullOrEmpty(environment) && showShort)
        {
            streamWriter.WriteLine($"\"appsetting file path: {AppSettingsFilePath}\"");

            ConsoleTable t = new() { Columns = { "Name", "Url" } };

            _settings.Environments.Select(e => new { name = e.Key, url = e.Value.Uri }).ToList()
                .ForEach(e => { t.Rows.Add([e.name, e.url]); });
            ConsoleLogger.Instance.PrintTable(t);
        }

        if (string.IsNullOrEmpty(environment) && !showShort)
        {
            streamWriter.WriteLine($"\"appsetting file path: {AppSettingsFilePath}\"");
            serializer.Serialize(streamWriter, _settings);
        }
        else
        {
            serializer.Serialize(streamWriter, _settings.Environments[environment]);
        }
    }

    public EnvironmentSettings GetEnvironment(string name = null)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            string activeEnvironment = _settings.ActiveEnvironmentKey;
            return _settings.Environments[activeEnvironment];
        }

        if (!_settings.Environments.TryGetValue(name, out EnvironmentSettings environment))
        {
            environment = new EnvironmentSettings();
            _settings.Environments[name] = environment;
        }

        return environment;
    }

    public EnvironmentSettings FindEnvironment(string name = null)
    {
        EnvironmentSettings environment;
        try
        {
            environment = GetEnvironment(name);
        }
        catch
        {
            return null;
        }

        return environment;
    }

    public EnvironmentSettings GetEnvironment(EnvironmentOptions options)
    {
        SettingsRepository settingsRepository = new();
        EnvironmentSettings? _settings = settingsRepository.FindEnvironment(options.Environment);
        if (_settings == null)
        {
            string envName = options.Environment ?? settingsRepository.GetDefaultEnvironmentName();
            if (!settingsRepository.IsEnvironmentExists(envName) && string.IsNullOrEmpty(options.Uri))
            {
                throw new Exception(
                    $"Environment with key '{envName}' not found. Check youre config file or command arguments.");
            }

            _settings = new EnvironmentSettings();
        }

        EnvironmentSettings result = _settings.Fill(options);
        return result;
    }

    public bool IsEnvironmentExists(string name) => _settings.Environments.ContainsKey(name);

    public string FindEnvironmentNameByUri(string uri)
    {
        string safeUri = uri.TrimEnd('/');
        return _settings.Environments.FirstOrDefault(pair => pair.Value.Uri == safeUri).Key;
    }

    public void ConfigureEnvironment(string name, EnvironmentSettings environment)
    {
        if (string.IsNullOrEmpty(name))
        {
            _settings.GetActiveEnviroment().Merge(environment);
        }
        else if (_settings.Environments.ContainsKey(name))
        {
            _settings.Environments[name].Merge(environment);
        }
        else
        {
            _settings.Environments.Add(name, environment);
        }

        Save();
    }

    public void SetActiveEnvironment(string activeEnvironment)
    {
        _settings.ActiveEnvironmentKey = activeEnvironment;
        Save();
    }

    public void RemoveEnvironment(string environment)
    {
        if (_settings.Environments.ContainsKey(environment))
        {
            _settings.Environments.Remove(environment);
            Save();
        }
        else
        {
            throw new KeyNotFoundException($"Application \"{environment}\" not found");
        }
    }

    public void OpenFile() => OpenSettingsFile();

    public void RemoveAllEnvironment()
    {
        _settings.Environments.Clear();
        Save();
    }

    public string GetIISClioRootPath() => _settings.IISClioRootPath;

    public string GetCreatioProductsFolder() => _settings.CreatioProductFolder;

    public string GetRemoteArtefactServerPath() => _settings.RemoteArtefactServerPath;

    private void InitSettings()
    {
        try
        {
            string filePath = Path.Combine(Environment.CurrentDirectory, AppSettingsFilePath);
            if (FileSystem.File.Exists(filePath))
            {
                string fileContent = FileSystem.File.ReadAllText(filePath);
                if (!string.IsNullOrWhiteSpace(fileContent))
                {
                    _settings = JsonConvert.DeserializeObject<Settings>(fileContent);
                    foreach (KeyValuePair<string, EnvironmentSettings> environment in _settings.Environments)
                    {
                        if (environment.Value.DbServerKey != null && _settings.DbServers != null &&
                            _settings.DbServers.ContainsKey(environment.Value.DbServerKey))
                        {
                            environment.Value.DbServer = _settings.DbServers[environment.Value.DbServerKey];
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine(
                $"{ex.Message} Correct or delete settings file before use clio. File path: {AppSettingsFilePath}");
            if (Program.IsCfgOpenCommand)
            {
                _settings = default;
            }
            else
            {
                throw;
            }
        }
    }

    private void InitializeSettingsFile()
    {
        if (FileSystem.File.Exists(AppSettingsFilePath))
        {
            return;
        }

        if (!FileSystem.Directory.Exists(AppSettingsFolderPath))
        {
            FileSystem.Directory.CreateDirectory(AppSettingsFolderPath);
        }

        InitDefaultSettings();
        Save();
    }

    private void InitDefaultSettings()
    {
        _settings = new Settings();
        _settings.Environments.Add(
            "dev",
            new EnvironmentSettings { Login = "Supervisor", Password = "Supervisor", Uri = "http://localhost" });
        _settings.ActiveEnvironmentKey = "dev";
        SaveSchema();
    }

    /// <summary>
    ///     Creates json schema file.
    ///     This file is used by intelisence in vs code and other json editors.
    /// </summary>
    private void SaveSchema()
    {
        string baseDir = AppDomain.CurrentDomain.BaseDirectory;
        string tplPath = Path.Combine(baseDir, "tpl", "jsonschema", "schema.json.tpl");
        string tplContect = File.ReadAllText(tplPath);
        File.WriteAllText(SchemaFilePath, tplContect);
    }

    private void Save()
    {
        using (StreamWriter fileWriter = FileSystem.File.CreateText(AppSettingsFilePath))
        {
            JsonSerializer serializer = new()
            {
                Formatting = Formatting.Indented, NullValueHandling = NullValueHandling.Ignore
            };

            // _settings.Schema =
            serializer.Serialize(fileWriter, _settings);
        }

        if (!File.Exists(SchemaFilePath))
        {
            SaveSchema();
        }
    }

    private string GetDefaultEnvironmentName() => _settings.ActiveEnvironmentKey;

    internal bool GetAutoupdate() => _settings.Autoupdate;

    public static void OpenSettingsFile() => FileManager.OpenFile(AppSettingsFile);
}

public class DbServer
{
    [JsonPropertyName("uri")] public Uri Uri { get; set; }

    [JsonPropertyName("workingFolder")] public string WorkingFolder { get; set; }

    public string Password { get; internal set; }

    public string Login { get; internal set; }

    public Credentials GetCredentials() =>
        Uri.UserInfo.Split(':') switch
        {
            var credentials when credentials.Length == 2 => new Credentials(credentials[0], credentials[1]),
            _ => new Credentials(Login, Password)
        };
}

public record Credentials(string username, string password);
