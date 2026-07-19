using Clio.UserEnvironment;
using Clio.Utilities;
using Newtonsoft.Json;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading;
using Clio.Common;
using Clio.Common.db;
using Clio.Common.DbHub;
using Clio.Common.IIS;
using Clio.Command.McpServer.Knowledge;
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
		// appsettings.json and never appear in ShowSettingsTo output. Also [System.Text.Json...JsonIgnore]
		// (review, belt-and-suspenders) so a future System.Text.Json serialization of these settings — the
		// serializer the MCP tool DTOs use — can never emit the transient token/cookie either.
		[YamlIgnore]
		[Newtonsoft.Json.JsonIgnore]
		[System.Text.Json.Serialization.JsonIgnore]
		public string AccessToken {
			get; set;
		}

		[YamlIgnore]
		[Newtonsoft.Json.JsonIgnore]
		[System.Text.Json.Serialization.JsonIgnore]
		public string AccessTokenType {
			get; set;
		} = Clio.Common.AuthenticationScheme.Bearer;

		[YamlIgnore]
		[Newtonsoft.Json.JsonIgnore]
		[System.Text.Json.Serialization.JsonIgnore]
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

	/// <summary>
	/// Default values applied to the <c>deploy-creatio</c> command when the corresponding option is not
	/// supplied on the command line. Stored under the <c>deploy-creatio-defaults</c> settings key.
	/// </summary>
	/// <remarks>
	/// These defaults let the Windows Explorer context-menu action ("clio: deploy Creatio"), which invokes
	/// <c>clio deploy-creatio --zip-file "%1"</c> with no other arguments, target a local database and Redis
	/// instead of falling back to a Kubernetes cluster. Explicit command-line options always take precedence.
	/// </remarks>
	public class DeployCreatioDefaults
	{
		/// <summary>
		/// Gets or sets the default local database server name (a key in the <c>db</c> settings block)
		/// used when <c>--db-server-name</c> is omitted.
		/// </summary>
		[JsonProperty("db-server-name")]
		public string DbServerName { get; set; }

		/// <summary>
		/// Gets or sets the default local Redis server name (a key in the <c>redis</c> settings block)
		/// used when <c>--redis-server-name</c> is omitted.
		/// </summary>
		[JsonProperty("redis-server-name")]
		public string RedisServerName { get; set; }

		/// <summary>
		/// Gets or sets the default site name used when <c>--site-name</c> is omitted. When left empty,
		/// interactive deployment prompts for the site name.
		/// </summary>
		[JsonProperty("site-name")]
		public string SiteName { get; set; }

		/// <summary>
		/// Gets or sets the default site port used when <c>--site-port</c> is omitted. A <c>null</c> value
		/// means no default port is configured.
		/// </summary>
		[JsonProperty("site-port")]
		public int? SitePort { get; set; }

		/// <summary>
		/// Gets or sets the default deployment method (<c>auto</c>, <c>iis</c>, or <c>dotnet</c>) used when
		/// <c>--deployment</c> is omitted or left at its <c>auto</c> default.
		/// </summary>
		[JsonProperty("deployment")]
		public string DeploymentMethod { get; set; }

		/// <summary>
		/// Determines whether every default value is empty, meaning no deploy-creatio defaults are configured.
		/// </summary>
		/// <returns><c>true</c> when no default is set; otherwise <c>false</c>.</returns>
		[Newtonsoft.Json.JsonIgnore]
		public bool IsEmpty =>
			string.IsNullOrWhiteSpace(DbServerName)
			&& string.IsNullOrWhiteSpace(RedisServerName)
			&& string.IsNullOrWhiteSpace(SiteName)
			&& !SitePort.HasValue
			&& string.IsNullOrWhiteSpace(DeploymentMethod);
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
			Knowledge = new KnowledgeConfiguration();
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

		/// <summary>Gets or sets the preferred LocalMachine/My certificate thumbprint for IIS HTTPS deployment.</summary>
		[JsonProperty("iis-certificate-thumbprint")]
		public string IisCertificateThumbprint { get; set; }

		[JsonProperty("workspaces-root")]
		public string WorkspacesRoot {
			get;
			set;
		}

		/// <summary>
		/// Gets or sets the multi-source knowledge configuration.
		/// </summary>
		[JsonProperty("knowledge")]
		public KnowledgeConfiguration Knowledge { get; set; }

		/// <summary>
		/// Gets or sets the legacy knowledge root used only for one-time migration.
		/// </summary>
		[JsonProperty("knowledge-root-path", NullValueHandling = NullValueHandling.Ignore)]
		public string LegacyKnowledgeRootPath { get; set; }

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

		/// <summary>
		/// Gets or sets the default values applied to the <c>deploy-creatio</c> command when the matching
		/// option is not supplied on the command line (used chiefly by the Explorer context-menu action).
		/// </summary>
		[JsonProperty("deploy-creatio-defaults")]
		public DeployCreatioDefaults DeployCreatioDefaults { get; set; }

		/// <summary>Gets or sets local dbHub HTTP server integration settings.</summary>
		[JsonProperty("dbhub")]
		public DbHubSettings DbHub { get; set; }

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
		private const int SettingsLockTimeoutSeconds = 30;
		private static readonly object SchemaFileLock = new ();
		private static readonly ConcurrentDictionary<string, object> ProcessSettingsLocks = new();
		[ThreadStatic]
		private static HashSet<string> _heldSettingsLocks;

		private Settings _settings = new ();
		private readonly IFileSystem _fileSystem;
		private readonly ISettingsBootstrapService _settingsBootstrapService;
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
			_fileSystem = fileSystem ?? FileSystem;
			if (fileSystem != null) {
				FileSystem = fileSystem;
			}
			ISettingsBootstrapService bootstrapService = settingsBootstrapService;
			if (bootstrapService == null) {
				bootstrapService = new SettingsBootstrapService(_fileSystem);
			}
			_settingsBootstrapService = bootstrapService;
			SettingsBootstrapResult bootstrapResult = ExecuteWithSettingsLock(_fileSystem, bootstrapService.GetResult);
			_settings = bootstrapResult.Settings ?? new Settings();
			EnsureSettingsCollections();
			AttachDbServers(_settings);
			TrySaveSchema(_fileSystem);
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

		internal static void SaveSettings(IFileSystem fileSystem, Settings settings, string expectedContent = null,
			bool verifyExpectedContent = false) {
			ExecuteWithSettingsLock(fileSystem, () => {
				SaveSettingsUnlocked(fileSystem, settings, expectedContent, verifyExpectedContent);
				return true;
			});
		}

		private static void SaveSettingsUnlocked(IFileSystem fileSystem, Settings settings, string expectedContent,
			bool verifyExpectedContent) {
			if (!fileSystem.Directory.Exists(AppSettingsFolderPath)) {
				fileSystem.Directory.CreateDirectory(AppSettingsFolderPath);
			}
			bool isRealFileSystem = fileSystem is FileSystem;
			(System.Security.AccessControl.FileSecurity windowsFileSecurity, UnixFileMode? unixFileMode) =
				GetSettingsFileSecurity(isRealFileSystem);
			string tempFilePath = Path.Combine(AppSettingsFolderPath,
				$".{FileName}.{Guid.NewGuid():N}.tmp");
			try {
				WriteSettingsTempFile(fileSystem, tempFilePath, settings, isRealFileSystem,
					windowsFileSecurity, unixFileMode);
				CommitSettingsFile(fileSystem, tempFilePath, expectedContent, verifyExpectedContent,
					isRealFileSystem);
			}
			finally {
				if (fileSystem.File.Exists(tempFilePath)) {
					fileSystem.File.Delete(tempFilePath);
				}
			}
			TrySaveSchema(fileSystem);
		}

		private static void TrySaveSchema(IFileSystem fileSystem) {
			try {
				SaveSchema(fileSystem);
			}
			catch (IOException) {
				// The schema is a derived editor aid. A locked/read-only schema must not block clio commands.
			}
			catch (UnauthorizedAccessException) {
				// The schema is a derived editor aid. A locked/read-only schema must not block clio commands.
			}
		}

		private static (System.Security.AccessControl.FileSecurity WindowsFileSecurity, UnixFileMode? UnixFileMode)
			GetSettingsFileSecurity(bool isRealFileSystem) {
			if (!isRealFileSystem || !System.IO.File.Exists(AppSettingsFile)) {
				return (null, null);
			}
			FileInfo settingsFile = new(AppSettingsFile);
			if (settingsFile.LinkTarget != null) {
				throw new IOException(
					$"Refusing to replace symbolic-link settings file '{AppSettingsFile}'.");
			}
			return RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
				? (settingsFile.GetAccessControl(), null)
				: (null, System.IO.File.GetUnixFileMode(AppSettingsFile));
		}

		private static void CommitSettingsFile(IFileSystem fileSystem, string tempFilePath, string expectedContent,
			bool verifyExpectedContent, bool isRealFileSystem) {
			if (!verifyExpectedContent) {
				fileSystem.File.Move(tempFilePath, AppSettingsFile, overwrite: true);
				return;
			}
			if (expectedContent == null) {
				MoveNewSettingsFile(fileSystem, tempFilePath);
				return;
			}

			using (Stream currentSettingsStream = OpenCurrentSettings(fileSystem)) {
				using StreamReader reader = new(currentSettingsStream, Encoding.UTF8,
					detectEncodingFromByteOrderMarks: true, leaveOpen: true);
				string currentContent = reader.ReadToEnd();
				if (!string.Equals(currentContent, expectedContent, StringComparison.Ordinal)) {
					throw new SettingsFileChangedException();
				}
				if (isRealFileSystem) {
					fileSystem.File.Replace(tempFilePath, AppSettingsFile, destinationBackupFileName: null,
						ignoreMetadataErrors: true);
					return;
				}
			}
			fileSystem.File.Move(tempFilePath, AppSettingsFile, overwrite: true);
		}

		private static void MoveNewSettingsFile(IFileSystem fileSystem, string tempFilePath) {
			try {
				fileSystem.File.Move(tempFilePath, AppSettingsFile);
			}
			catch (IOException) when (fileSystem.File.Exists(AppSettingsFile)) {
				throw new SettingsFileChangedException();
			}
		}

		private static Stream OpenCurrentSettings(IFileSystem fileSystem) {
			try {
				return fileSystem.File.Open(AppSettingsFile, FileMode.Open,
					FileAccess.Read, FileShare.Read | FileShare.Delete);
			}
			catch (FileNotFoundException) {
				throw new SettingsFileChangedException();
			}
		}

		private static void WriteSettingsTempFile(IFileSystem fileSystem, string tempFilePath, Settings settings,
			bool isRealFileSystem, System.Security.AccessControl.FileSecurity windowsFileSecurity,
			UnixFileMode? unixFileMode) {
			JsonSerializer serializer = new() {
				Formatting = Formatting.Indented,
				NullValueHandling = NullValueHandling.Ignore
			};
			if (!isRealFileSystem) {
				using StreamWriter mockWriter = fileSystem.File.CreateText(tempFilePath);
				serializer.Serialize(mockWriter, settings);
				mockWriter.Flush();
				return;
			}

			FileStreamOptions options = new() {
				Mode = FileMode.CreateNew,
				Access = FileAccess.Write,
				Share = FileShare.None
			};
			if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
				options.UnixCreateMode = unixFileMode ?? (UnixFileMode.UserRead | UnixFileMode.UserWrite);
			}
			using FileStream stream = new(tempFilePath, options);
			if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && windowsFileSecurity != null) {
				new FileInfo(tempFilePath).SetAccessControl(windowsFileSecurity);
			}
			using (StreamWriter fileWriter = new(stream, new UTF8Encoding(false), leaveOpen: true)) {
				serializer.Serialize(fileWriter, settings);
				fileWriter.Flush();
			}
			stream.Flush(flushToDisk: true);
		}

		private static void SaveSchema(IFileSystem fileSystem) {
			lock (SchemaFileLock) {
				if (!fileSystem.Directory.Exists(AppSettingsFolderPath)) {
					fileSystem.Directory.CreateDirectory(AppSettingsFolderPath);
				}
				string baseDir = AppDomain.CurrentDomain.BaseDirectory;
				string tplPath = Path.Combine(baseDir, "tpl", "jsonschema", "schema.json.tpl");
				if (!fileSystem.File.Exists(tplPath)) {
					return;
				}
				string templateContent = fileSystem.File.ReadAllText(tplPath);
				if (fileSystem.File.Exists(SchemaFilePath)
					&& string.Equals(fileSystem.File.ReadAllText(SchemaFilePath), templateContent, StringComparison.Ordinal)) {
					return;
				}
				string temporaryPath = SchemaFilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
				try {
					fileSystem.File.WriteAllText(temporaryPath, templateContent, new UTF8Encoding(false));
					fileSystem.File.Move(temporaryPath, SchemaFilePath, overwrite: true);
				}
				finally {
					if (fileSystem.File.Exists(temporaryPath)) {
						fileSystem.File.Delete(temporaryPath);
					}
				}
			}
		}

		private void EnsureSettingsCollections() {
			_settings ??= new Settings();
			_settings.Environments ??= new Dictionary<string, EnvironmentSettings>();
			_settings.Features ??= new Dictionary<string, bool>();
			_settings.Knowledge ??= new KnowledgeConfiguration();
			_settings.Knowledge.Sources ??= new Dictionary<string, KnowledgeSourceConfiguration>(
				StringComparer.OrdinalIgnoreCase);
			_settings.Knowledge.TopicPins ??= new Dictionary<string, string>(StringComparer.Ordinal);
			EnsureFeaturesComparer();
			EnsureKnowledgeComparers();
		}

		private void EnsureKnowledgeComparers() {
			if (!ReferenceEquals(_settings.Knowledge.Sources.Comparer, StringComparer.OrdinalIgnoreCase)) {
				Dictionary<string, KnowledgeSourceConfiguration> sources = new(StringComparer.OrdinalIgnoreCase);
				foreach ((string alias, KnowledgeSourceConfiguration source) in _settings.Knowledge.Sources) {
					sources[alias] = source;
				}
				_settings.Knowledge.Sources = sources;
			}
			if (!ReferenceEquals(_settings.Knowledge.TopicPins.Comparer, StringComparer.Ordinal)) {
				_settings.Knowledge.TopicPins = new Dictionary<string, string>(
					_settings.Knowledge.TopicPins,
					StringComparer.Ordinal);
			}
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

		private void UpdateSettings(Action<Settings> mutation) {
			ExecuteWithSettingsLock(_fileSystem, () => {
				for (int attempt = 0; attempt < 3; attempt++) {
					string expectedContent;
					try {
						_settings = LoadLatestSettings(out expectedContent);
					}
					catch (Newtonsoft.Json.JsonException) when (attempt < 2) {
						Thread.Sleep(10);
						continue;
					}
					catch (Newtonsoft.Json.JsonException exception) {
						throw new InvalidOperationException(
							"Cannot update settings because appsettings.json changed to unreadable content.", exception);
					}
					AttachDbServers(_settings);
					EnsureSettingsCollections();
					mutation(_settings);
					EnsureSettingsCollections();
					try {
						SaveSettings(_fileSystem, _settings, expectedContent, verifyExpectedContent: true);
						return true;
					}
					catch (SettingsFileChangedException) {
						if (attempt == 2) {
							throw new IOException(
								"appsettings.json kept changing while clio was updating it. Try the command again.");
						}
						// An editor changed the file after it was reloaded. Retry the mutation against that version.
					}
				}
				throw new InvalidOperationException("Settings update retry loop ended unexpectedly.");
			});
		}

		private Settings LoadLatestSettings(out string expectedContent) {
			SettingsBootstrapResult latest = _settingsBootstrapService.GetResult();
			if (string.Equals(latest.Report.Status, "broken", StringComparison.OrdinalIgnoreCase)) {
				string issue = latest.Report.Issues.FirstOrDefault()?.Message
					?? "appsettings.json is unreadable.";
				throw new InvalidOperationException(
					$"Cannot update settings because {issue}");
			}

			expectedContent = _fileSystem.File.ReadAllText(AppSettingsFile);
			return JsonConvert.DeserializeObject<Settings>(expectedContent)
				?? throw new Newtonsoft.Json.JsonSerializationException(
					"appsettings.json did not contain a settings object.");
		}

		internal static T ExecuteWithSettingsLock<T>(IFileSystem fileSystem, Func<T> action) {
			string lockFilePath = $"{AppSettingsFile}.lock";
			object processLock = ProcessSettingsLocks.GetOrAdd(lockFilePath, _ => new object());
			Stopwatch stopwatch = Stopwatch.StartNew();
			if (!Monitor.TryEnter(processLock, TimeSpan.FromSeconds(SettingsLockTimeoutSeconds))) {
				throw new TimeoutException(
					$"Timed out waiting to update {AppSettingsFile}. Another clio operation may still be using settings.");
			}
			try {
				_heldSettingsLocks ??= [];
				if (_heldSettingsLocks.Contains(lockFilePath)) {
					return action();
				}

				if (!fileSystem.Directory.Exists(AppSettingsFolderPath)) {
					fileSystem.Directory.CreateDirectory(AppSettingsFolderPath);
				}
				Stream lockStream = null;
				while (lockStream == null) {
					try {
						lockStream = fileSystem.File.Open(lockFilePath, FileMode.OpenOrCreate,
							FileAccess.ReadWrite, FileShare.None);
					}
					catch (IOException) when (stopwatch.Elapsed < TimeSpan.FromSeconds(SettingsLockTimeoutSeconds)) {
						Thread.Sleep(50);
					}
					catch (IOException exception) {
						throw new TimeoutException(
							$"Timed out waiting to update {AppSettingsFile}. Another clio process may still be updating settings.",
							exception);
					}
				}

				using (lockStream) {
					_heldSettingsLocks.Add(lockFilePath);
					try {
						return action();
					}
					finally {
						_heldSettingsLocks.Remove(lockFilePath);
					}
				}
			}
			finally {
				Monitor.Exit(processLock);
			}
		}

		internal sealed class SettingsFileChangedException : IOException { }

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
			UpdateSettings(settings => settings.Autoupdate = value);
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
			UpdateSettings(settings => settings.Features[featureName] = enabled);
		}

		public IReadOnlyDictionary<string, bool> GetFeatures() {
			EnsureSettingsCollections();
			return new Dictionary<string, bool>(_settings.Features, StringComparer.OrdinalIgnoreCase);
		}

		public void ConfigureEnvironment(string name, EnvironmentSettings environment) {
			UpdateSettings(settings => {
				if (string.IsNullOrEmpty(name)) {
					if (settings.Environments.Count == 0) {
						_settings = settings = CreateDefaultSettings(settings);
					}
					settings.GetActiveEnvironment().Merge(environment);
				} else if (!settings.Environments.TryAdd(name, environment)) {
					settings.Environments[name].Merge(environment);
				} else if (string.IsNullOrWhiteSpace(settings.ActiveEnvironmentKey)) {
					settings.ActiveEnvironmentKey = name;
				}
			});
		}

		public void SetActiveEnvironment(string activeEnvironment) {
			UpdateSettings(settings => settings.ActiveEnvironmentKey = activeEnvironment);
		}

		public void RemoveEnvironment(string environment) {
			UpdateSettings(settings => {
				string actualKey = settings.Environments.Keys.FirstOrDefault(key =>
					string.Equals(key, environment, StringComparison.OrdinalIgnoreCase));
				if (actualKey == null) {
					throw new KeyNotFoundException($"Application \"{environment}\" not found");
				}
				if (settings.Environments.Remove(actualKey)
					&& string.Equals(settings.ActiveEnvironmentKey, actualKey, StringComparison.OrdinalIgnoreCase)) {
					settings.ActiveEnvironmentKey = settings.Environments.Keys.FirstOrDefault();
				}
			});
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
			UpdateSettings(settings => {
				settings.Environments.Clear();
				settings.ActiveEnvironmentKey = null;
			});
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

		public string GetKnowledgeRootPath() {
			EnsureSettingsCollections();
			if (!string.IsNullOrWhiteSpace(_settings.Knowledge.RootPath)) {
				return _settings.Knowledge.RootPath;
			}
			if (string.IsNullOrWhiteSpace(_settings.LegacyKnowledgeRootPath)) {
				return null;
			}
			string migrated = NormalizeKnowledgeRootPath(_settings.LegacyKnowledgeRootPath, "knowledge-root-path");
			UpdateSettings(settings => {
				settings.Knowledge ??= new KnowledgeConfiguration();
				if (string.IsNullOrWhiteSpace(settings.Knowledge.RootPath)) {
					settings.Knowledge.RootPath = migrated;
				}
				settings.LegacyKnowledgeRootPath = null;
			});
			return _settings.Knowledge.RootPath;
		}

		public void SetKnowledgeRootPath(string path) {
			string normalized = NormalizeKnowledgeRootPath(path, nameof(path));
			UpdateSettings(settings => {
				settings.Knowledge ??= new KnowledgeConfiguration();
				settings.Knowledge.RootPath = normalized;
				settings.LegacyKnowledgeRootPath = null;
			});
		}

		public string GetOrCreateKnowledgeRootPath(string defaultPath) {
			string normalizedDefault = NormalizeKnowledgeRootPath(defaultPath, nameof(defaultPath));
			string resolved = null;
			UpdateSettings(settings => {
				settings.Knowledge ??= new KnowledgeConfiguration();
				string candidate = !string.IsNullOrWhiteSpace(settings.Knowledge.RootPath)
					? settings.Knowledge.RootPath
					: !string.IsNullOrWhiteSpace(settings.LegacyKnowledgeRootPath)
						? settings.LegacyKnowledgeRootPath
						: normalizedDefault;
				resolved = NormalizeKnowledgeRootPath(candidate, "knowledge.root-path");
				settings.Knowledge.RootPath = resolved;
				settings.LegacyKnowledgeRootPath = null;
			});
			return resolved;
		}

		/// <inheritdoc />
		public KnowledgeConfiguration GetKnowledgeConfiguration() {
			GetKnowledgeRootPath();
			EnsureSettingsCollections();
			return KnowledgeSourceConfigurationValidator.ValidateAndClone(_settings.Knowledge);
		}

		/// <inheritdoc />
		public void SetKnowledgeConfiguration(KnowledgeConfiguration configuration) {
			KnowledgeConfiguration validated = KnowledgeSourceConfigurationValidator.ValidateAndClone(configuration);
			UpdateSettings(settings => {
				settings.Knowledge = KnowledgeSourceConfigurationValidator.ValidateAndClone(validated);
				settings.LegacyKnowledgeRootPath = null;
			});
		}

		/// <inheritdoc />
		public void UpsertKnowledgeSource(string alias, KnowledgeSourceConfiguration source) {
			KnowledgeSourceConfigurationValidator.ValidateAlias(alias);
			KnowledgeSourceConfiguration validated = KnowledgeSourceConfigurationValidator.ValidateAndClone(source);
			UpdateSettings(settings => {
				settings.Knowledge ??= new KnowledgeConfiguration();
				settings.Knowledge.Sources ??= new Dictionary<string, KnowledgeSourceConfiguration>(
					StringComparer.OrdinalIgnoreCase);
				Dictionary<string, KnowledgeSourceConfiguration> sources = new(
					settings.Knowledge.Sources,
					StringComparer.OrdinalIgnoreCase);
				sources[alias] = validated;
				KnowledgeConfiguration candidate = new() {
					RootPath = settings.Knowledge.RootPath,
					Sources = sources,
					TopicPins = settings.Knowledge.TopicPins ?? new Dictionary<string, string>(StringComparer.Ordinal)
				};
				settings.Knowledge = KnowledgeSourceConfigurationValidator.ValidateAndClone(candidate);
				settings.LegacyKnowledgeRootPath = null;
			});
		}

		/// <inheritdoc />
		public bool RemoveKnowledgeSource(string alias) {
			KnowledgeSourceConfigurationValidator.ValidateAlias(alias);
			bool removed = false;
			UpdateSettings(settings => {
				settings.Knowledge ??= new KnowledgeConfiguration();
				settings.Knowledge.Sources ??= new Dictionary<string, KnowledgeSourceConfiguration>(
					StringComparer.OrdinalIgnoreCase);
				removed = settings.Knowledge.Sources.Remove(alias);
				settings.LegacyKnowledgeRootPath = null;
			});
			return removed;
		}

		/// <inheritdoc />
		public bool TryRemoveKnowledgeSource(string alias, KnowledgeSourceConfiguration expected) {
			KnowledgeSourceConfigurationValidator.ValidateAlias(alias);
			KnowledgeSourceConfiguration snapshot = KnowledgeSourceConfigurationValidator.ValidateAndClone(expected);
			bool removed = false;
			UpdateSettings(settings => {
				settings.Knowledge ??= new KnowledgeConfiguration();
				settings.Knowledge.Sources ??= new Dictionary<string, KnowledgeSourceConfiguration>(
					StringComparer.OrdinalIgnoreCase);
				if (settings.Knowledge.Sources.TryGetValue(alias, out KnowledgeSourceConfiguration current)
						&& KnowledgeSourcesEqual(current, snapshot)) {
					removed = settings.Knowledge.Sources.Remove(alias);
				}
				settings.LegacyKnowledgeRootPath = null;
			});
			return removed;
		}

		/// <inheritdoc />
		public bool TrySetKnowledgeSourceBranch(
			string alias,
			KnowledgeSourceConfiguration expected,
			string branch) {
			KnowledgeSourceConfigurationValidator.ValidateAlias(alias);
			KnowledgeSourceConfiguration snapshot = KnowledgeSourceConfigurationValidator.ValidateAndClone(expected);
			if (string.IsNullOrWhiteSpace(branch)) {
				throw new ArgumentException("Knowledge Git branch cannot be empty.", nameof(branch));
			}
			bool updated = false;
			UpdateSettings(settings => {
				settings.Knowledge ??= new KnowledgeConfiguration();
				settings.Knowledge.Sources ??= new Dictionary<string, KnowledgeSourceConfiguration>(
					StringComparer.OrdinalIgnoreCase);
				if (settings.Knowledge.Sources.TryGetValue(alias, out KnowledgeSourceConfiguration current)
						&& KnowledgeSourcesEqual(current, snapshot)) {
					current.Branch = branch.Trim();
					updated = true;
				}
				settings.LegacyKnowledgeRootPath = null;
			});
			return updated;
		}

		/// <inheritdoc />
		public void SetKnowledgeSourceEnabled(string alias, bool enabled) {
			KnowledgeSourceConfigurationValidator.ValidateAlias(alias);
			UpdateSettings(settings => {
				settings.Knowledge ??= new KnowledgeConfiguration();
				settings.Knowledge.Sources ??= new Dictionary<string, KnowledgeSourceConfiguration>(
					StringComparer.OrdinalIgnoreCase);
				if (!settings.Knowledge.Sources.TryGetValue(alias, out KnowledgeSourceConfiguration source)) {
					throw new KeyNotFoundException($"Knowledge source '{alias}' is not configured.");
				}
				source.Enabled = enabled;
				settings.LegacyKnowledgeRootPath = null;
			});
		}

		private static bool KnowledgeSourcesEqual(
			KnowledgeSourceConfiguration left,
			KnowledgeSourceConfiguration right) =>
			string.Equals(left.LibraryId, right.LibraryId, StringComparison.Ordinal)
			&& left.Type == right.Type
			&& string.Equals(left.Location, right.Location, StringComparison.Ordinal)
			&& string.Equals(left.TrustedKeyId, right.TrustedKeyId, StringComparison.Ordinal)
			&& string.Equals(left.TrustedPublicKeyPath, right.TrustedPublicKeyPath, StringComparison.Ordinal)
			&& string.Equals(left.PackageId, right.PackageId, StringComparison.Ordinal)
			&& string.Equals(left.Branch, right.Branch, StringComparison.Ordinal)
			&& string.Equals(left.Tag, right.Tag, StringComparison.Ordinal)
			&& string.Equals(left.Commit, right.Commit, StringComparison.Ordinal)
			&& string.Equals(left.ArtifactPath, right.ArtifactPath, StringComparison.Ordinal)
			&& left.Enabled == right.Enabled
			&& left.Priority == right.Priority
			&& left.Participation == right.Participation;

		private static string NormalizeKnowledgeRootPath(string path, string parameterName) {
			if (string.IsNullOrWhiteSpace(path)) {
				throw new ArgumentException("Knowledge root path cannot be empty.", parameterName);
			}
			if (!Path.IsPathFullyQualified(path)) {
				throw new ArgumentException("Knowledge root path must be absolute.", parameterName);
			}
			return Path.GetFullPath(path);
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

		public DeployCreatioDefaults GetDeployCreatioDefaults() {
			return _settings.DeployCreatioDefaults ?? new DeployCreatioDefaults();
		}

		public void SetDeployCreatioDefaults(DeployCreatioDefaults defaults) {
			UpdateSettings(settings =>
				settings.DeployCreatioDefaults = defaults is null || defaults.IsEmpty ? null : defaults);
		}

		public string GetPinnedIisCertificateThumbprint() => _settings.IisCertificateThumbprint;

		public void SetPinnedIisCertificateThumbprint(string thumbprint) {
			string normalized = string.IsNullOrWhiteSpace(thumbprint)
				? null
				: WindowsIisCertificateProvider.NormalizeThumbprint(thumbprint);
			if (normalized is not null && normalized.Length != 40) {
				throw new ArgumentException("An IIS certificate thumbprint must contain exactly 40 hexadecimal characters.", nameof(thumbprint));
			}
			UpdateSettings(settings => settings.IisCertificateThumbprint = normalized);
		}

		public DbHubSettings GetDbHubSettings() {
			return (_settings.DbHub ?? new DbHubSettings()).Clone();
		}

		public void SetDbHubSettings(DbHubSettings settings) {
			ArgumentNullException.ThrowIfNull(settings);
			UpdateSettings(current => current.DbHub = settings.Clone());
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
