using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;

namespace Clio
{

	public class EnvironmentSettings
	{
		public string Uri { get; set; }
		public string Login { get; set; }
		public string Password { get; set; }
		public string Maintainer { get; set; }
		public bool IsNetCore { get; set; }
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
			if (environment.Safe.HasValue)
			{
				Safe = environment.Safe;
			}
			if (environment.DeveloperModeEnabled.HasValue) {
				DeveloperModeEnabled = environment.DeveloperModeEnabled;
			}
			IsNetCore = environment.IsNetCore;
		}

		public bool? Safe { get; set; }

		public bool? DeveloperModeEnabled { get; set; }
	}

	public class Settings
	{
		public Settings() {
			Environments = new Dictionary<string, EnvironmentSettings>();
		}

		public string ActiveEnvironmentKey { get; set; }

		public EnvironmentSettings GetActiveEnviroment() {
			if (String.IsNullOrEmpty(ActiveEnvironmentKey)
				|| !Environments.ContainsKey(ActiveEnvironmentKey)) {
				ActiveEnvironmentKey = Environments.First().Key;
				return Environments.First().Value;
			} else {
				return Environments[ActiveEnvironmentKey];
			}
		}

		public bool Autoupdate { get; set; }

		public Dictionary<string, EnvironmentSettings> Environments { get; set; }
	}

	public class SettingsRepository
	{
		public SettingsRepository() {
			InitializeSettingsFile();
			InitSettings();
		}

		private void InitSettings() {
			var builder = new ConfigurationBuilder()
				.SetBasePath(Environment.CurrentDirectory)
				.AddJsonFile(AppSettingsFilePath, optional: false, reloadOnChange: true)
				.AddEnvironmentVariables();
			IConfigurationRoot configuration = builder.Build();
			_settings = new Settings();
			configuration.Bind(_settings);
		}

		private const string FileName = "appsettings.json";

		private string AppSettingsFolderPath {
			get {
				var userPath = Environment.GetEnvironmentVariable(
				   RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ?
					   "LOCALAPPDATA" : "Home");
				var assy = Assembly.GetEntryAssembly();
				var companyName = assy.GetCustomAttributes<AssemblyCompanyAttribute>()
					.FirstOrDefault();
				var product = assy.GetCustomAttributes<AssemblyProductAttribute>()
					.FirstOrDefault();
				if (userPath == null)
				{
					userPath = "";
				}
				return Path.Combine(userPath, companyName?.Company, product?.Product);
			}
		}

		private string AppSettingsFilePath => Path.Combine(AppSettingsFolderPath, FileName);

		private void InitializeSettingsFile() {
			if (File.Exists(AppSettingsFilePath)) {
				return;
			}
			if (!Directory.Exists(AppSettingsFolderPath)) {
				Directory.CreateDirectory(AppSettingsFolderPath);
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
		}

		private void Save() {
			using (StreamWriter fileWriter = File.CreateText(AppSettingsFilePath)) {
				JsonSerializer serializer = new JsonSerializer() {
					Formatting = Formatting.Indented
				};
				serializer.Serialize(fileWriter, _settings);
			}
		}

		internal void ShowSettingsTo(TextWriter streamWriter, string environment = null) {
			JsonSerializer serializer = new JsonSerializer() {
				Formatting = Formatting.Indented
			};
			if (String.IsNullOrEmpty(environment))
			{
				streamWriter.WriteLine($"\"appsetting file path: {AppSettingsFilePath}\"");
				serializer.Serialize(streamWriter, _settings);
			} else {
				serializer.Serialize(streamWriter, _settings.Environments[environment]);
			}
		}

		internal EnvironmentSettings GetEnvironment(string name = null) {
			EnvironmentSettings environment;
			if (string.IsNullOrEmpty(name)) {
				environment = _settings.GetActiveEnviroment();
			} else {
				environment = _settings.Environments[name];
			}
			return environment;
		}

		internal EnvironmentSettings GetEnvironment(EnvironmentOptions options) {
				var result = new EnvironmentSettings();
				var settingsRepository = new SettingsRepository();
				var _settings = settingsRepository.GetEnvironment(options.Environment);
				result.Uri  = string.IsNullOrEmpty(options.Uri) ? _settings.Uri : options.Uri;
				result.IsNetCore = options.IsNetCore.HasValue 
					? options.IsNetCore.Value
					: _settings.IsNetCore;
				result.Login = string.IsNullOrEmpty(options.Login) ? _settings.Login : options.Login;
				result.Password  = string.IsNullOrEmpty(options.Password) ? _settings.Password : options.Password;
				result.Maintainer =
					string.IsNullOrEmpty(options.Maintainer) ? _settings.Maintainer : options.Maintainer;
				if (_settings.Safe.HasValue && _settings.Safe.Value) {
					Console.WriteLine($"You try to apply the action on the production site {_settings.Uri}");
					Console.Write($"Do you want to continue? [Y/N]:");
					var answer = Console.ReadKey();
					Console.WriteLine();
					if (answer.KeyChar != 'y' && answer.KeyChar != 'Y') {
						Console.WriteLine("Operation was canceled by user");
						Environment.Exit(1);
					}
				}
				return result;
		}

		internal bool IsExistInEnvironment(string name)
		{
			return _settings.Environments.ContainsKey(name);
		}

		internal bool GetAutoupdate()
		{
			return _settings.Autoupdate;
		}

		Settings _settings;

		internal void ConfigureEnvironment(string name, EnvironmentSettings environment) {
			if (string.IsNullOrEmpty(name)) {
				_settings.GetActiveEnviroment().Merge(environment);
			} else if (_settings.Environments.ContainsKey(name)) {
				_settings.Environments[name].Merge(environment);
			} else {
				_settings.Environments.Add(name, environment);
			}
			Save();
		}

		internal void SetActiveEnvironment(string activeEnvironment) {
			_settings.ActiveEnvironmentKey = activeEnvironment;
			Save();
		}

		internal void RemoveEnvironment(string environment) {
			if (_settings.Environments.ContainsKey(environment))
			{
				_settings.Environments.Remove(environment);
				Save();
			}
			else {
				throw new KeyNotFoundException($"Application \"{environment}\" not found");
			}
		}

	}

}