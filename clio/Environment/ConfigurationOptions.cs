using Clio.UserEnvironment;
using Clio.Utilities;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json.Serialization;
using Clio.Common;
using Clio.Common.db;
using ConsoleTables;
using YamlDotNet.Serialization;
using FileSystem = System.IO.Abstractions.FileSystem;
using IFileSystem = System.IO.Abstractions.IFileSystem;

namespace Clio
{

	public class EnvironmentSettings
	{
		[YamlMember(Alias = "url")]
		public string Uri {
			get; set;
		}

		public string DbName {
			get; set;
		}

		public string BackupFilePath {
			get; set;
		}


		public string Login {
			get; set;
		}

		public string Password {
			get; set;
		}

		public string Maintainer {
			get; set;
		}

		public bool IsNetCore {
			get; set;
		}

		public string ClientId {
			get; set;
		}

		public string DbServerKey {
			get; set;
		}

		[Newtonsoft.Json.JsonIgnore]
		public DbServer DbServer {
			get; set;
		}

		public string ClientSecret {
			get; set;
		}

		public string WorkspacePathes {
			get; set;
		}

		private string _authAppUri;
		[YamlMember(Alias = "authappurl")]
		public string AuthAppUri {
			get {
				if (string.IsNullOrEmpty(_authAppUri)) {
					if (Uri?.ToLower().Contains(".creatio.com") ?? false) {
						return Uri?.ToLower().Replace(".creatio.com", "-is.creatio.com/connect/token");
					}
				}
				return _authAppUri;
			}
			set {
				_authAppUri = value;
			}
		}

		[YamlIgnore]
		[Newtonsoft.Json.JsonIgnore]
		public string SimpleloginUri {
			get {
				if(Uri == null) {
					return "";
				}
				var cleanUri = Uri;
				if (!string.IsNullOrEmpty(cleanUri)) {
					var domain = ".creatio.com";
					var index = cleanUri.IndexOf(domain);
					if (index != -1) {
						cleanUri = cleanUri.Substring(0, index + domain.Length);
					}
				}
				var simpleLoginUriText = cleanUri.TrimEnd('/') + (IsNetCore ? "/Shell/?simplelogin=true" : "/0/Shell/?simplelogin=true");
				return simpleLoginUriText;
			}
		}

		internal void Merge(EnvironmentSettings environment) {
			if (!string.IsNullOrEmpty(environment.Login)) {
				Login = environment.Login;
			}
			if (!string.IsNullOrEmpty(environment.Uri)) {
				Uri = environment.Uri;
			}
			if (!string.IsNullOrEmpty(environment.Password)) {
				Password = environment.Password;
			}
			if (!string.IsNullOrEmpty(environment.Maintainer)) {
				Maintainer = environment.Maintainer;
			}
			if (environment.Safe.HasValue) {
				Safe = environment.Safe;
			}
			if (environment.DeveloperModeEnabled.HasValue) {
				DeveloperModeEnabled = environment.DeveloperModeEnabled;
			}
			IsNetCore = environment.IsNetCore;
			ClientId = environment.ClientId;
			ClientSecret = environment.ClientSecret;
			AuthAppUri = environment.AuthAppUri;
			WorkspacePathes = environment.WorkspacePathes;

			if (!string.IsNullOrWhiteSpace(environment.EnvironmentPath)) {
				EnvironmentPath = environment.EnvironmentPath;
			}
			
			if (!string.IsNullOrEmpty(environment.DbName)) {
				DbName = environment.DbName;
			}
			if (!string.IsNullOrEmpty(environment.DbServerKey)) {
				DbServerKey = environment.DbServerKey;
			}
			if (environment.DbServer?.Uri != null) {
				if (DbServer == null) {
					DbServer = new DbServer();
				}
				DbServer.Uri = environment.DbServer.Uri;
			}
			if (!string.IsNullOrEmpty(environment.BackupFilePath)) {
				BackupFilePath = environment.BackupFilePath;
			}
		}

		public bool? Safe {
			get; set;
		}


		public bool? DeveloperModeEnabled {
			get; set;
		}

		[Newtonsoft.Json.JsonIgnore]
		public bool IsDevMode {
			get => DeveloperModeEnabled ?? false;
		}


		//[Newtonsoft.Json.JsonIgnore]
		public string EnvironmentPath { get; set; } = string.Empty;

		public EnvironmentSettings Fill(EnvironmentOptions options) {
			var result = new EnvironmentSettings();
			result.Uri = string.IsNullOrEmpty(options.Uri) ? this.Uri : options.Uri;
			result.IsNetCore = options.IsNetCore ?? this.IsNetCore;
			result.DeveloperModeEnabled = options.DeveloperModeEnabled ?? this.DeveloperModeEnabled;
			result.Login = string.IsNullOrEmpty(options.Login) ? this.Login : options.Login;
			result.Password = string.IsNullOrEmpty(options.Password) ? this.Password : options.Password;
			result.ClientId = string.IsNullOrEmpty(options.ClientId) ? this.ClientId : options.ClientId;
			result.ClientSecret = string.IsNullOrEmpty(options.ClientSecret) ? this.ClientSecret : options.ClientSecret;
			result.AuthAppUri = string.IsNullOrEmpty(options.AuthAppUri) ? this.AuthAppUri : options.AuthAppUri;
			result.Maintainer =
				string.IsNullOrEmpty(options.Maintainer) ? this.Maintainer : options.Maintainer;
			if (this.Safe.HasValue && this.Safe.Value) {
				Console.WriteLine($"You try to apply the action on the production site {this.Uri}");
				Console.Write($"Do you want to continue? [Y/N]:");
				var answer = Console.ReadKey();
				Console.WriteLine();
				if (answer.KeyChar != 'y' && answer.KeyChar != 'Y') {
					Console.WriteLine("Operation was canceled by user");
					System.Environment.Exit(1);
				}
			}
			result.WorkspacePathes = string.IsNullOrEmpty(options.WorkspacePathes) ? this.WorkspacePathes : options.WorkspacePathes;

			bool isUri = System.Uri.TryCreate(options.DbServerUri, UriKind.Absolute, out Uri uri);
			if (isUri) {

				if (result.DbServer == null) {
					result.DbServer = new DbServer();
				}
				result.DbServer.Uri = uri;
			}

			if (!string.IsNullOrWhiteSpace(options.DbWorknigFolder)) {
				if (result.DbServer == null) {
					result.DbServer = new DbServer();
				}
				result.DbServer.WorkingFolder = options.DbWorknigFolder;

			}

			if (!string.IsNullOrWhiteSpace(options.DbUser)) {
				if (result.DbServer == null) {
					result.DbServer = new DbServer();
				}
				result.DbServer.Login = options.DbUser;
			}

			if (!string.IsNullOrWhiteSpace(options.DbPassword)) {
				if (result.DbServer == null) {
					result.DbServer = new DbServer();
				}
				result.DbServer.Password = options.DbPassword;
			}

			if (!string.IsNullOrEmpty(options.BackUpFilePath)) {
				result.BackupFilePath = options.BackUpFilePath;
			}
			if (!string.IsNullOrEmpty(options.DbName)) {
				result.DbName = options.DbName;
			}
			return result;
		}
	}

	public class Settings
	{
		/// <summary>
		/// Supported container image CLIs for build-docker-image.
		/// </summary>
		/// <summary>
		/// Default container image CLI used by build-docker-image.
		/// </summary>
		public const string DefaultContainerImageCli = "docker";

		public Settings() {
			Environments = new Dictionary<string, EnvironmentSettings>();
		}

		//TODO: This wont work for Mac and Linux
		private const string DefaultCreatioProductFolder = @"C:\CreatioProductBuild";
		private string _creatioProductFolder;

		[JsonProperty("creatio-products")]
		public string CreatioProductFolder {
			get => string.IsNullOrWhiteSpace(_creatioProductFolder) ? DefaultCreatioProductFolder : _creatioProductFolder;
			set => _creatioProductFolder = value;
		}

		// Get platform-specific default IIS root path
		private static string GetDefaultIisRootPath() {
			if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
				return @"C:\inetpub\wwwroot\clio";
			}
			// For macOS and Linux, use a sensible local path
			// These platforms typically don't use IIS, so this is mainly for consistency
			if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) {
				return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".clio", "iis-root");
			}
			// Linux
			return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".clio", "iis-root");
		}

		private string _iISClioRootPath;

		[JsonProperty("iis-clio-root-path")]
		public string IISClioRootPath {
			get => string.IsNullOrWhiteSpace(_iISClioRootPath) ? GetDefaultIisRootPath() : _iISClioRootPath;
			set => _iISClioRootPath = value;
		}

		[JsonProperty("workspaces-root")]
		public string WorkspacesRoot {
			get;
			set;
		}

		private string _containerImageCli;

		/// <summary>
		/// Gets or sets the default container image CLI used by build-docker-image.
		/// </summary>
		[JsonProperty("container-image-cli")]
		public string ContainerImageCli {
			get => string.IsNullOrWhiteSpace(_containerImageCli) ? DefaultContainerImageCli : _containerImageCli;
			set => _containerImageCli = value;
		}

		[JsonProperty("$schema")]
		public string Schema => "./schema.json";

		public string ActiveEnvironmentKey {
			get; set;
		}


		[JsonProperty("dbConnectionStringKeys")]
		public Dictionary<string, DbServer> DbServers { get; set; }

		[JsonProperty("db")]
		public Dictionary<string, LocalDbServerConfiguration> LocalDbServers { get; set; }

		[JsonProperty("redis")]
		public Dictionary<string, LocalRedisServerConfiguration> LocalRedisServers { get; set; }

		[JsonProperty("defaultRedis")]
		public string DefaultRedisServerName { get; set; }
		
		public EnvironmentSettings GetActiveEnvironment() {
			if (string.IsNullOrEmpty(ActiveEnvironmentKey)
				|| !Environments.ContainsKey(ActiveEnvironmentKey)) {
				ActiveEnvironmentKey = Environments.First().Key;
				return Environments.First().Value;
			} else {
				return Environments[ActiveEnvironmentKey];
			}
		}

		public bool Autoupdate {
			get; set;
		}

		public Dictionary<string, EnvironmentSettings> Environments {
			get; set;
		}
		public string RemoteArtefactServerPath
		{
			get;
			set;
		}
	}

	public class SettingsRepository : ISettingsRepository
	{
		private const string FileName = "appsettings.json";
		private const string SchemaFileName = "schema.json";
		private static readonly object SchemaFileLock = new ();

		private Settings _settings = new ();
		public static string AppSettingsFolderPath {
			get {
				var userPath = Environment.GetEnvironmentVariable(
					RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ?
						"LOCALAPPDATA" : "HOME");
				var assy = Assembly.GetEntryAssembly();
				var companyName = assy.GetCustomAttributes<AssemblyCompanyAttribute>()
					.FirstOrDefault();
				var product = assy.GetCustomAttributes<AssemblyProductAttribute>()
					.FirstOrDefault();
				if (userPath == null) {
					userPath = "";
				}
				return Path.Combine(userPath, companyName?.Company, product?.Product);
			}
		}

		public static string AppSettingsFile => Path.Combine(AppSettingsFolderPath, FileName);
		public string AppSettingsFilePath => AppSettingsFile;

		internal static IFileSystem FileSystem { get; set; } = new FileSystem();

		internal static string SchemaFilePath => Path.Combine(AppSettingsFolderPath, SchemaFileName);

		public SettingsRepository(IFileSystem fileSystem = null, ISettingsBootstrapService settingsBootstrapService = null) {
	if (fileSystem != null) {
		FileSystem = fileSystem;
	}
	ISettingsBootstrapService bootstrapService = settingsBootstrapService;
	if (bootstrapService == null) {
		bootstrapService = new SettingsBootstrapService(FileSystem);
	}
	SettingsBootstrapResult bootstrapResult = bootstrapService.GetResult();
	_settings = bootstrapResult.Settings ?? new Settings();
	EnsureSettingsCollections();
	AttachDbServers(_settings);
}

		internal static Settings CreateDefaultSettings(Settings settings = null) {
			Settings result = settings ?? new Settings();
			result.Environments ??= new Dictionary<string, EnvironmentSettings>();
			result.Environments.Clear();
			result.Environments.Add("dev", new EnvironmentSettings {
				Login = "Supervisor",
				Password = "Supervisor",
				Uri = "http://localhost"
			});
			result.ActiveEnvironmentKey = "dev";
			return result;
		}

		internal static void AttachDbServers(Settings settings) {
			if (settings?.Environments == null || settings.DbServers == null) {
				return;
			}
			foreach (EnvironmentSettings environment in settings.Environments.Values) {
				if (!string.IsNullOrWhiteSpace(environment?.DbServerKey)
					&& settings.DbServers.TryGetValue(environment.DbServerKey, out DbServer dbServer)) {
					environment.DbServer = dbServer;
				}
			}
		}

		internal static void SaveSettings(IFileSystem fileSystem, Settings settings) {
			if (!fileSystem.Directory.Exists(AppSettingsFolderPath)) {
				fileSystem.Directory.CreateDirectory(AppSettingsFolderPath);
			}
			using (StreamWriter fileWriter = fileSystem.File.CreateText(AppSettingsFile)) {
				JsonSerializer serializer = new() {
					Formatting = Formatting.Indented,
					NullValueHandling = NullValueHandling.Ignore
				};
				serializer.Serialize(fileWriter, settings);
			}
			SaveSchema(fileSystem);
		}

		private static void SaveSchema(IFileSystem fileSystem) {
			lock (SchemaFileLock) {
				if (fileSystem.File.Exists(SchemaFilePath)) {
					return;
				}
				if (!fileSystem.Directory.Exists(AppSettingsFolderPath)) {
					fileSystem.Directory.CreateDirectory(AppSettingsFolderPath);
				}
				string baseDir = AppDomain.CurrentDomain.BaseDirectory;
				string tplPath = Path.Combine(baseDir, "tpl", "jsonschema", "schema.json.tpl");
				if (!File.Exists(tplPath)) {
					return;
				}
				string tplContect = File.ReadAllText(tplPath);
				fileSystem.File.WriteAllText(SchemaFilePath, tplContect);
			}
		}

		private void EnsureSettingsCollections() {
			_settings ??= new Settings();
			_settings.Environments ??= new Dictionary<string, EnvironmentSettings>();
		}

		private void Save() {
			EnsureSettingsCollections();
			SaveSettings(FileSystem, _settings);
		}

		public void ShowSettingsTo(TextWriter streamWriter, string environment = null, bool showShort = false) {
			EnsureSettingsCollections();
			JsonSerializer serializer = new () {
				Formatting = Formatting.Indented,
				NullValueHandling = NullValueHandling.Ignore
			};
			
			if (string.IsNullOrEmpty(environment) && showShort) {
				streamWriter.WriteLine($"\"appsetting file path: {AppSettingsFilePath}\"");
				
				ConsoleTable t = new () {
					Columns = { "Name", "Url" },
				};
				
				_settings.Environments.Select(e=> new {
					name = e.Key,
					url = e.Value.Uri,
				}).ToList().ForEach(e => {
					t.Rows.Add([e.name, e.url]);
				});
				ConsoleLogger.Instance.PrintTable(t);
				return;
			}
			
			if (string.IsNullOrEmpty(environment) && !showShort) {
				streamWriter.WriteLine($"\"appsetting file path: {AppSettingsFilePath}\"");
				serializer.Serialize(streamWriter, _settings);
			} else {
				string actualKey = FindEnvironmentKey(environment);
				if (actualKey == null) {
					throw new KeyNotFoundException($"Environment '{environment}' not found");
				}
				serializer.Serialize(streamWriter, _settings.Environments[actualKey]);
			}
		}

		public EnvironmentSettings GetEnvironment(string name = null) {
			EnsureSettingsCollections();
			if (string.IsNullOrWhiteSpace(name)) {
				string activeEnvironment = _settings.ActiveEnvironmentKey;
				if (!string.IsNullOrWhiteSpace(activeEnvironment)
					&& _settings.Environments.TryGetValue(activeEnvironment, out EnvironmentSettings activeEnvironmentSettings)) {
					return activeEnvironmentSettings;
				}
				throw new InvalidOperationException(
					$"Active environment is not configured. Repair {AppSettingsFilePath} or register an environment.");
			}
			string actualKey = FindEnvironmentKey(name);
			if (actualKey != null) {
				return _settings.Environments[actualKey];
			}
			// Create new environment if it doesn't exist
			var environment = new EnvironmentSettings();
			_settings.Environments[name] = environment;
			return environment;
		}


		private string FindEnvironmentKey(string name) {
			if (string.IsNullOrWhiteSpace(name)) {
				return null;
			}
			return _settings.Environments.Keys.FirstOrDefault(key => string.Equals(key, name, StringComparison.OrdinalIgnoreCase));
		}

		public EnvironmentSettings FindEnvironment(string name = null) {
			EnsureSettingsCollections();
			if (string.IsNullOrWhiteSpace(name)) {
				string activeEnvironment = _settings.ActiveEnvironmentKey;
				if (!string.IsNullOrWhiteSpace(activeEnvironment)
					&& _settings.Environments.TryGetValue(activeEnvironment, out EnvironmentSettings activeEnv)) {
					return activeEnv;
				}
				return null;
			}
			string actualKey = FindEnvironmentKey(name);
			if (actualKey != null && _settings.Environments.TryGetValue(actualKey, out EnvironmentSettings environment)) {
				return environment;
			}
			return null;
		}

		public EnvironmentSettings GetEnvironment(EnvironmentOptions options) {
			var settingsRepository = new SettingsRepository();
			bool hasExplicitEnvironment = !string.IsNullOrWhiteSpace(options.Environment);
			bool hasDirectUri = !string.IsNullOrEmpty(options.Uri);
			EnvironmentSettings envSettings;
			if (hasExplicitEnvironment) {
				envSettings = settingsRepository.FindEnvironment(options.Environment);
			} else if (hasDirectUri) {
				envSettings = null;
			} else {
				envSettings = settingsRepository.FindEnvironment(null);
			}
			if (envSettings == null) {
				var envName = options.Environment ?? settingsRepository.GetDefaultEnvironmentName();
				if (!settingsRepository.IsEnvironmentExists(envName) && !hasDirectUri) {
					throw new Exception($"Environment with key '{envName}' not found. Check youre config file or command arguments.");
				} else {
					envSettings = new EnvironmentSettings();
				}
			}
			EnvironmentSettings result = envSettings.Fill(options);
			return result;
		}

		public string GetDefaultEnvironmentName() {
			EnsureSettingsCollections();
			return _settings.ActiveEnvironmentKey;
		}

		public bool IsEnvironmentExists(string name) {
			EnsureSettingsCollections();
			return !string.IsNullOrWhiteSpace(name) && FindEnvironmentKey(name) != null;
		}

		public string GetActualEnvironmentName(string name) {
			EnsureSettingsCollections();
			return FindEnvironmentKey(name);
		}

		public string FindEnvironmentNameByUri(string uri) {
			EnsureSettingsCollections();
			if (string.IsNullOrWhiteSpace(uri)) {
				return null;
			}
			string safeUri = uri.TrimEnd('/');
			return _settings.Environments.FirstOrDefault(pair => pair.Value.Uri == safeUri).Key;
		}

		public bool GetAutoupdate() {
			return _settings.Autoupdate;
		}

		public void ConfigureEnvironment(string name, EnvironmentSettings environment) {
			EnsureSettingsCollections();
			if (string.IsNullOrEmpty(name)) {
				if (_settings.Environments.Count == 0) {
					_settings = CreateDefaultSettings(_settings);
				}
				_settings.GetActiveEnvironment().Merge(environment);
			} else if (!_settings.Environments.TryAdd(name, environment)) {
				_settings.Environments[name].Merge(environment);
			} else if (string.IsNullOrWhiteSpace(_settings.ActiveEnvironmentKey)) {
				_settings.ActiveEnvironmentKey = name;
			}
			Save();
		}

		public void SetActiveEnvironment(string activeEnvironment) {
			EnsureSettingsCollections();
			_settings.ActiveEnvironmentKey = activeEnvironment;
			Save();
		}

		public void RemoveEnvironment(string environment) {
			EnsureSettingsCollections();
			string actualKey = FindEnvironmentKey(environment);
			if (actualKey == null) {
				throw new KeyNotFoundException($"Application \"{environment}\" not found");
			}
			if (_settings.Environments.Remove(actualKey)) {
				if (string.Equals(_settings.ActiveEnvironmentKey, actualKey, StringComparison.OrdinalIgnoreCase)) {
					_settings.ActiveEnvironmentKey = _settings.Environments.Keys.FirstOrDefault();
				}
				Save();
			}
		}

		public static void OpenSettingsFile() {
			FileManager.OpenFile(AppSettingsFile);
		}

		public void OpenFile() {
			OpenSettingsFile();
		}

		public Dictionary<string, EnvironmentSettings> GetAllEnvironments() {
			EnsureSettingsCollections();
			return _settings?.Environments == null
				? new Dictionary<string, EnvironmentSettings>()
				: new Dictionary<string, EnvironmentSettings>(_settings.Environments);
		}

		void ISettingsRepository.RemoveAllEnvironment() {
			EnsureSettingsCollections();
			_settings.Environments.Clear();
			_settings.ActiveEnvironmentKey = null;
			Save();
		}

		public string GetIISClioRootPath() {
			return _settings.IISClioRootPath;
		}
		public string GetCreatioProductsFolder() {
			return _settings.CreatioProductFolder;
		}

		public string GetRemoteArtefactServerPath() {
			return _settings.RemoteArtefactServerPath;
		}

		public string GetWorkspacesRoot() {
			return _settings.WorkspacesRoot;
		}

		/// <summary>
		/// Gets the configured container image CLI used by build-docker-image.
		/// </summary>
		/// <returns>The configured container image CLI name.</returns>
		public string GetContainerImageCli() {
			return _settings.ContainerImageCli;
		}

		public LocalDbServerConfiguration GetLocalDbServer(string name) {
			if (_settings.LocalDbServers == null || !_settings.LocalDbServers.ContainsKey(name)) {
				return null;
			}
			LocalDbServerConfiguration config = _settings.LocalDbServers[name];
			return config.Enabled ? config : null;
		}

		public IEnumerable<string> GetLocalDbServerNames() {
			return _settings.LocalDbServers?
				.Where(kv => kv.Value != null && kv.Value.Enabled)
				.Select(kv => kv.Key)
				?? Enumerable.Empty<string>();
		}

		public LocalRedisServerConfiguration GetLocalRedisServer(string name) {
			if (string.IsNullOrWhiteSpace(name)) {
				return null;
			}
			if (_settings.LocalRedisServers == null || !_settings.LocalRedisServers.ContainsKey(name)) {
				return null;
			}
			LocalRedisServerConfiguration config = _settings.LocalRedisServers[name];
			return config is { Enabled: true } ? config : null;
		}

		public IEnumerable<string> GetLocalRedisServerNames() {
			return _settings.LocalRedisServers?
				.Where(kv => kv.Value != null && kv.Value.Enabled)
				.Select(kv => kv.Key)
				?? Enumerable.Empty<string>();
		}

		public string GetDefaultLocalRedisServerName() {
			return _settings.DefaultRedisServerName;
		}

		public bool HasLocalRedisServersConfiguration() {
			return _settings.LocalRedisServers is { Count: > 0 };
		}

	}


	public class DbServer
	{
		[JsonPropertyName("uri")]
		public Uri Uri { get; set; }

		[JsonPropertyName("workingFolder")]
		public string WorkingFolder { get; set; }
		public string Password { get; internal set; }
		public string Login { get; internal set; }

		public Credentials GetCredentials() =>
			Uri.UserInfo.Split(':') switch {
				var credentials when credentials.Length == 2 => new Credentials(credentials[0], credentials[1]),
				var _ => new Credentials(Login, Password)
			};
	}

	public record Credentials(string Username, string Password);


}
