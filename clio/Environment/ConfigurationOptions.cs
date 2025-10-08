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

		//TODO: This wont work for Mac and Linux
		private const string DefaultIisRootPath = @"C:\inetpub\wwwroot\clio";
		private string _iISClioRootPath;

		[JsonProperty("iis-clio-root-path")]
		public string IISClioRootPath {
			get => string.IsNullOrWhiteSpace(_iISClioRootPath) ? DefaultIisRootPath : _iISClioRootPath;
			set => _iISClioRootPath = value;
		}

		[JsonProperty("$schema")]
		public string Schema => "./schema.json";

		public string ActiveEnvironmentKey {
			get; set;
		}


		[JsonProperty("dbConnectionStringKeys")]
		public Dictionary<string, DbServer> DbServers { get; set; }


		public EnvironmentSettings GetActiveEnviroment() {
			if (String.IsNullOrEmpty(ActiveEnvironmentKey)
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

		private Settings _settings = new Settings();
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
		private string SchemaFilePath => Path.Combine(AppSettingsFolderPath, SchemaFileName);

		internal static IFileSystem FileSystem { get; set; } = new FileSystem();

		public SettingsRepository(IFileSystem fileSystem = null) {
			if(fileSystem != null) {
				FileSystem = fileSystem;
			}
			
			InitializeSettingsFile();
			InitSettings();
		}

		private void InitSettings() {
			try {
				var filePath = Path.Combine(Environment.CurrentDirectory, AppSettingsFilePath);
				if (FileSystem.File.Exists(filePath)) {
					var fileContent = FileSystem.File.ReadAllText(filePath);
					if (!String.IsNullOrWhiteSpace(fileContent)) {
						_settings = JsonConvert.DeserializeObject<Settings>(fileContent);
						foreach (var environment in _settings.Environments) {
							if (environment.Value.DbServerKey != null && _settings.DbServers != null && _settings.DbServers.ContainsKey(environment.Value.DbServerKey)) {
								environment.Value.DbServer = _settings.DbServers[environment.Value.DbServerKey];
							}
						}

					}
				}
			} catch (Exception ex) {
				Console.WriteLine($"{ex.Message} Correct or delete settings file before use clio. File path: {AppSettingsFilePath}");
				if (Program.IsCfgOpenCommand) {
					_settings = default;
				} else {
					throw;
				}
			}
		}

		private void InitializeSettingsFile() {
			if (FileSystem.File.Exists(AppSettingsFilePath)) {
				return;
			}
			if (!FileSystem.Directory.Exists(AppSettingsFolderPath)) {
				FileSystem.Directory.CreateDirectory(AppSettingsFolderPath);
			}
			InitDefaultSettings();
			Save();
		}

		private void InitDefaultSettings() {
			_settings = new Settings();
			_settings.Environments.Add("dev", new EnvironmentSettings() {
				Login = "Supervisor",
				Password = "Supervisor",
				Uri = "http://localhost"
			});
			_settings.ActiveEnvironmentKey = "dev";
			SaveSchema();
		}

		/// <summary>
		/// Creates json schema file.
		/// This file is used by intelisence in vs code and other json editors.
		/// </summary>
		private void SaveSchema() {
			var baseDir = AppDomain.CurrentDomain.BaseDirectory;
			var tplPath = Path.Combine(baseDir, "tpl", "jsonschema", "schema.json.tpl");
			var tplContect = File.ReadAllText(tplPath);
			File.WriteAllText(SchemaFilePath, tplContect);
		}

		private void Save() {
			using (StreamWriter fileWriter = FileSystem.File.CreateText(AppSettingsFilePath)) {
				JsonSerializer serializer = new JsonSerializer() {
					Formatting = Formatting.Indented,
					NullValueHandling = NullValueHandling.Ignore
				};

				//_settings.Schema = 
				serializer.Serialize(fileWriter, _settings);
			}

			if (!File.Exists(SchemaFilePath)) {
				SaveSchema();
			}

		}

		public void ShowSettingsTo(TextWriter streamWriter, string environment = null, bool showShort = false) {
			JsonSerializer serializer = new JsonSerializer() {
				Formatting = Formatting.Indented,
				NullValueHandling = NullValueHandling.Ignore
			};
			
			if (String.IsNullOrEmpty(environment) && showShort) {
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
			}
			
			if (String.IsNullOrEmpty(environment) && !showShort) {
				streamWriter.WriteLine($"\"appsetting file path: {AppSettingsFilePath}\"");
				serializer.Serialize(streamWriter, _settings);
			} else {
				serializer.Serialize(streamWriter, _settings.Environments[environment]);
			}
		}

		public EnvironmentSettings GetEnvironment(string name = null) {
			if (string.IsNullOrWhiteSpace(name)) {
				var activeEnvironment = _settings.ActiveEnvironmentKey;
				return _settings.Environments[activeEnvironment];
			}
			if (!_settings.Environments.TryGetValue(name, out EnvironmentSettings environment)) {
				environment = new EnvironmentSettings();
				_settings.Environments[name] = environment;
			}
			return environment;
		}


		public EnvironmentSettings FindEnvironment(string name = null) {
			EnvironmentSettings environment;
			try {
				environment = GetEnvironment(name);
			} catch {
				return null;
			}
			return environment;
		}

		public EnvironmentSettings GetEnvironment(EnvironmentOptions options) {
			var settingsRepository = new SettingsRepository();
			var _settings = settingsRepository.FindEnvironment(options.Environment);
			if (_settings == null) {
				var envName = options.Environment ?? settingsRepository.GetDefaultEnvironmentName();
				if (!settingsRepository.IsEnvironmentExists(envName) && string.IsNullOrEmpty(options.Uri)) {
					throw new Exception($"Environment with key '{envName}' not found. Check youre config file or command arguments.");
				} else {
					_settings = new EnvironmentSettings();
				}
			}
			EnvironmentSettings result = _settings.Fill(options);
			return result;
		}

		public string GetDefaultEnvironmentName() {
			return _settings.ActiveEnvironmentKey;
		}

		public bool IsEnvironmentExists(string name) {
			return _settings.Environments.ContainsKey(name);
		}

		public string FindEnvironmentNameByUri(string uri) {
			string safeUri = uri.TrimEnd('/');
			return _settings.Environments.FirstOrDefault(pair => pair.Value.Uri == safeUri).Key;
		}

		internal bool GetAutoupdate() {
			return _settings.Autoupdate;
		}

		public void ConfigureEnvironment(string name, EnvironmentSettings environment) {
			if (string.IsNullOrEmpty(name)) {
				_settings.GetActiveEnviroment().Merge(environment);
			} else if (_settings.Environments.ContainsKey(name)) {
				_settings.Environments[name].Merge(environment);
			} else {
				_settings.Environments.Add(name, environment);
			}
			Save();
		}

		public void SetActiveEnvironment(string activeEnvironment) {
			_settings.ActiveEnvironmentKey = activeEnvironment;
			Save();
		}

		public void RemoveEnvironment(string environment) {
			if (_settings.Environments.ContainsKey(environment)) {
				_settings.Environments.Remove(environment);
				Save();
			} else {
				throw new KeyNotFoundException($"Application \"{environment}\" not found");
			}
		}

		public static void OpenSettingsFile() {
			FileManager.OpenFile(AppSettingsFile);
		}

		public void OpenFile() {
			OpenSettingsFile();
		}

		void ISettingsRepository.RemoveAllEnvironment() {
			_settings.Environments.Clear();
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
