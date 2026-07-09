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

		// Credential-passthrough secret fields (FR-01/FR-02/FR-18). Carried on an ephemeral,
		// per-request EnvironmentSettings only; mirror the SimpleloginUri secret discipline
		// ([YamlIgnore] + [Newtonsoft.Json.JsonIgnore]) so they are never persisted to
		// appsettings.json and never appear in ShowSettingsTo output.
		[YamlIgnore]
		[Newtonsoft.Json.JsonIgnore]
		public string AccessToken {
			get; set;
		}

		[YamlIgnore]
		[Newtonsoft.Json.JsonIgnore]
		public string AccessTokenType {
			get; set;
		} = Clio.Common.AuthenticationScheme.Bearer;

		[YamlIgnore]
		[Newtonsoft.Json.JsonIgnore]
		public string Cookie {
			get; set;
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

		public EnvironmentSettings Fill(EnvironmentOptions options, IInteractiveConsole interactiveConsole) {
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
			if (this.Safe.HasValue && this.Safe.Value
				&& !interactiveConsole.Prompt($"You try to apply the action on the production site {this.Uri}")) {
				// Non-interactive hosts (MCP stdio / CI) fail closed here instead of blocking on
				// Console.ReadKey() or killing the process via Environment.Exit(). A dedicated
				// exception lets the MCP BaseTool convert this into a structured error.
				throw new SafeEnvironmentConfirmationRequiredException(this.Uri);
			}
			result.WorkspacePathes = string.IsNullOrEmpty(options.WorkspacePathes) ? this.WorkspacePathes : options.WorkspacePathes;

			ApplyDbServerOptions(result, options);
			return result;
		}

		private static void ApplyDbServerOptions(EnvironmentSettings result, EnvironmentOptions options) {
			if (System.Uri.TryCreate(options.DbServerUri, UriKind.Absolute, out Uri uri)) {
				result.DbServer ??= new DbServer();
				result.DbServer.Uri = uri;
			}
			if (!string.IsNullOrWhiteSpace(options.DbWorknigFolder)) {
				result.DbServer ??= new DbServer();
				result.DbServer.WorkingFolder = options.DbWorknigFolder;
			}
			if (!string.IsNullOrWhiteSpace(options.DbUser)) {
				result.DbServer ??= new DbServer();
				result.DbServer.Login = options.DbUser;
			}
			if (!string.IsNullOrWhiteSpace(options.DbPassword)) {
				result.DbServer ??= new DbServer();
				result.DbServer.Password = options.DbPassword;
			}
			if (!string.IsNullOrEmpty(options.BackUpFilePath)) {
				result.BackupFilePath = options.BackUpFilePath;
			}
			if (!string.IsNullOrEmpty(options.DbName)) {
				result.DbName = options.DbName;
			}
		}
	}

	/// <summary>
	/// Product telemetry upload configuration stored under the "telemetry" settings key.
	/// </summary>
	public class TelemetrySettings
	{
		/// <summary>
		/// Master switch for product telemetry uploads. <c>false</c> disables uploading entirely —
		/// even with the shipped default endpoint and granted consent; <c>null</c> (the key absent,
		/// the default) leaves uploading enabled. Overridden by the <c>CLIO_TELEMETRY_ENABLED</c>
		/// environment variable.
		/// </summary>
		[JsonProperty("enabled")]
		public bool? Enabled { get; set; }

		/// <summary>
		/// Full OTLP/HTTP logs endpoint URL (for example https://telemetry.example.com/v1/logs);
		/// must be https, or http only for a loopback host. Overrides the shipped production default;
		/// when empty the default endpoint is used unless uploading is disabled via <see cref="Enabled"/>.
		/// </summary>
		[JsonProperty("endpoint")]
		public string Endpoint { get; set; }

		/// <summary>
		/// Optional public ingest key sent as the X-Ingest-Key request header.
		/// </summary>
		[JsonProperty("ingest-key")]
		public string IngestKey { get; set; }
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
			Features = new Dictionary<string, bool>();
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

		/// <summary>
		/// Gets or sets the product telemetry upload configuration.
		/// </summary>
		[JsonProperty("telemetry")]
		public TelemetrySettings Telemetry { get; set; }

		public EnvironmentSettings GetActiveEnvironment() {
			if (string.IsNullOrEmpty(ActiveEnvironmentKey)
				|| !Environments.ContainsKey(ActiveEnvironmentKey)) {
				ActiveEnvironmentKey = Environments.First().Key;
				return Environments.First().Value;
			} else {
				return Environments[ActiveEnvironmentKey];
			}
		}

		public bool? Autoupdate {
			get; set;
		}

		/// <summary>
		/// Settings schema version used to apply one-time settings migrations.
		/// A null value denotes a legacy file written before migrations were introduced.
		/// </summary>
		public int? SettingsVersion {
			get; set;
		}

		public Dictionary<string, EnvironmentSettings> Environments {
			get; set;
		}

		/// <summary>
		/// Feature flags keyed by feature name. A missing key or a <c>false</c> value means the
		/// feature is disabled. Defaults to an empty dictionary so a missing "features" key in the
		/// settings file deserializes to an empty collection rather than <c>null</c>.
		/// </summary>
		[JsonProperty("features")]
		public Dictionary<string, bool> Features {
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
		// Used by GetEnvironment to confirm Safe-environment operations without deadlocking a
		// non-interactive host. Null only for the internal/direct (non-DI) construction sites that
		// never call GetEnvironment; those fall back to RealInteractiveConsole.Shared.
		private readonly IInteractiveConsole _interactiveConsole;
		public static string AppSettingsFolderPath {
			get {
				// CLIO_HOME, when set, overrides the entire root verbatim. This is the single
				// source of truth for clio's home directory; see ClioRuntimePaths and
				// docs/architecture/clio-home-consolidation.md.
				var clioHome = Environment.GetEnvironmentVariable("CLIO_HOME");
				if (!string.IsNullOrWhiteSpace(clioHome)) {
					return clioHome;
				}
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

		public SettingsRepository(IFileSystem fileSystem = null, ISettingsBootstrapService settingsBootstrapService = null, IInteractiveConsole interactiveConsole = null) {
	_interactiveConsole = interactiveConsole;
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
			_settings.Features ??= new Dictionary<string, bool>();
			EnsureFeaturesComparer();
		}

		// Feature keys are compared case-insensitively (see ISettingsRepository.IsFeatureEnabled).
		// JSON deserialization hands back an ordinal-comparer dictionary, so rebuild it once with an
		// OrdinalIgnoreCase comparer. This makes IsFeatureEnabled/SetFeature/GetFeatures all
		// case-insensitive in one place; the convention is: the command writes the key as-given and
		// lookups never depend on casing. The rebuild is idempotent and skipped once applied.
		private void EnsureFeaturesComparer() {
			if (ReferenceEquals(_settings.Features.Comparer, StringComparer.OrdinalIgnoreCase)) {
				return;
			}
			// Rebuild manually rather than via the Dictionary(IDictionary, IEqualityComparer) copy-constructor:
			// that constructor THROWS ArgumentException if the source (e.g. a hand-edited appsettings.json)
			// contains two keys differing only by case. A manual last-wins assignment never throws.
			Dictionary<string, bool> rebuilt = new(StringComparer.OrdinalIgnoreCase);
			// On a case-collision the last-enumerated value wins; this is acceptable because such a
			// state only arises from a manual appsettings.json edit (the command never writes colliding keys).
			foreach (KeyValuePair<string, bool> kvp in _settings.Features) {
				rebuilt[kvp.Key] = kvp.Value;
			}
			_settings.Features = rebuilt;
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
			// Resolve against this repository's own settings (loaded from the filesystem it was
			// constructed with). Earlier this method built a new SettingsRepository(), which re-reads
			// the shared static FileSystem and made the result depend on global state — a race that
			// broke parallel unit tests.
			bool hasExplicitEnvironment = !string.IsNullOrWhiteSpace(options.Environment);
			bool hasDirectUri = !string.IsNullOrEmpty(options.Uri);
			EnvironmentSettings envSettings;
			if (hasExplicitEnvironment) {
				envSettings = FindEnvironment(options.Environment);
			} else if (hasDirectUri) {
				envSettings = null;
			} else {
				envSettings = FindEnvironment(null);
			}
			if (envSettings == null) {
				var envName = options.Environment ?? GetDefaultEnvironmentName();
				if (!IsEnvironmentExists(envName) && !hasDirectUri) {
					if (string.IsNullOrWhiteSpace(envName)) {
						var allEnvs = GetAllEnvironments();
						if (allEnvs.Count > 0) {
							string envList = string.Join(", ", allEnvs.Keys);
							throw new Exception(
								$"No active environment configured. Run 'clio set-active-environment <name>' to activate one of the registered environments ({envList}), or pass --environment <name>.");
						}
						throw new Exception(
							$"No environments are registered. Run 'clio reg-web-app --name <name> --url <url>' to register an environment first.");
					}
					throw new Exception(
						$"Active environment '{envName}' is not found in the registered environments. " +
						$"Run 'clio set-active-environment <name>' to fix this, or pass --environment <name>.");
				} else {
					envSettings = new EnvironmentSettings();
				}
			}
			EnvironmentSettings result = envSettings.Fill(options, _interactiveConsole ?? RealInteractiveConsole.Shared);
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
			return _settings.Autoupdate ?? true;
		}

		public void SetAutoupdate(bool value) {
			_settings.Autoupdate = value;
			Save();
		}

		public bool IsFeatureEnabled(string featureName) {
			if (string.IsNullOrWhiteSpace(featureName)) {
				return false;
			}
			EnsureSettingsCollections();
			return _settings.Features.TryGetValue(featureName, out bool enabled) && enabled;
		}

		public void SetFeature(string featureName, bool enabled) {
			if (string.IsNullOrWhiteSpace(featureName)) {
				throw new ArgumentException("Feature name must be a non-empty value.", nameof(featureName));
			}
			EnsureSettingsCollections();
			_settings.Features[featureName] = enabled;
			Save();
		}

		public IReadOnlyDictionary<string, bool> GetFeatures() {
			EnsureSettingsCollections();
			return new Dictionary<string, bool>(_settings.Features, StringComparer.OrdinalIgnoreCase);
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

		/// <summary>
		/// Gets the product telemetry upload configuration.
		/// </summary>
		/// <returns>The configured telemetry settings; never <c>null</c>.</returns>
		public TelemetrySettings GetTelemetrySettings() {
			return _settings.Telemetry ?? new TelemetrySettings();
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
