using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;

namespace bpmcli
{

	public class EnvironmentSettings
	{
		public string Uri { get; set; }
		public string Login { get; set; }
		public string Password { get; set; }

		internal void Merge(EnvironmentSettings environment) {
			if (!String.IsNullOrEmpty(environment.Login)) {
				Login = environment.Login;
			}
			if (!String.IsNullOrEmpty(environment.Uri)) {
				Uri = environment.Uri;
			}
			if (!String.IsNullOrEmpty(environment.Password)) {
				Password = environment.Password;
			}
		}
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
				.SetBasePath(Directory.GetCurrentDirectory())
				.AddJsonFile(AppSettingsFilePath, optional: false, reloadOnChange: true)
				.AddEnvironmentVariables();
			IConfigurationRoot configuration = builder.Build();
			_settings = new Settings();
			configuration.Bind(_settings);
		}

		private readonly string fileName = "appsettings.json";

		public string AppSettingsFolderPath {
			get {
				var userPath = Environment.GetEnvironmentVariable(
				   RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ?
					   "LOCALAPPDATA" : "Home");
				var assy = Assembly.GetEntryAssembly();
				var companyName = assy.GetCustomAttributes<AssemblyCompanyAttribute>()
					.FirstOrDefault();
				var product = assy.GetCustomAttributes<AssemblyProductAttribute>()
					.FirstOrDefault();
				return $"{userPath}\\{companyName.Company}\\{product.Product}";
			}
		}

		public string AppSettingsFilePath => $"{AppSettingsFolderPath}\\{fileName}";

		public void InitializeSettingsFile() {
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
			using (StreamWriter file = File.CreateText(AppSettingsFilePath)) {
				JsonSerializer serializer = new JsonSerializer() {
					Formatting = Formatting.Indented
				};
				serializer.Serialize(file, _settings);
			}
		}

		public EnvironmentSettings GetEnvironment(string name = null) {
			EnvironmentSettings environment;
			if (String.IsNullOrEmpty(name)) {
				environment = _settings.GetActiveEnviroment();
			} else {
				environment = _settings.Environments[name];
			}
			return environment;
		}

		Settings _settings;

		internal void ConfigureEnvironment(string name, EnvironmentSettings environment) {
			if (String.IsNullOrEmpty(name)) {
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
	}

}