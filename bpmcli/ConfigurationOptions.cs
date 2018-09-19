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
	}

	public class Settings
	{
		public Settings() {
			Environments = new Dictionary<string, EnvironmentSettings>();
		}
		public Dictionary<string, EnvironmentSettings> Environments { get; set; }
	}

	public class SettingsRepository
	{
		public SettingsRepository() {
			InitializeSettingsFile();
		}

		private string fileName = "appsettings.json";

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
			var settings = GetDefaultSettings();
			Save(settings);
		}

		private Settings GetDefaultSettings() {
			var settings = new Settings();
			settings.Environments.Add("dev", new EnvironmentSettings() {
				Login = "Supervisor",
				Password = "Supervisor",
				Uri = "http://localhost"
			});
			return settings;
		}

		public void Save(Settings settings) {
			using (StreamWriter file = File.CreateText(AppSettingsFilePath)) {
				JsonSerializer serializer = new JsonSerializer() {
					Formatting = Formatting.Indented
				};
				serializer.Serialize(file, settings);
			}
		}

		public EnvironmentSettings GetEnvironment(string name = null) {
			var builder = new ConfigurationBuilder()
				.SetBasePath(Directory.GetCurrentDirectory())
				.AddJsonFile(AppSettingsFilePath, optional: false, reloadOnChange: true)
				.AddEnvironmentVariables();
			IConfigurationRoot configuration = builder.Build();
			var settings = new Settings();
			configuration.Bind(settings);
			var environment = String.IsNullOrEmpty(name) ? settings.Environments.First().Value : settings.Environments[name];
			if (settings.Environments.Count == 0) {
				throw new Exception("Could not find enviroment settings in file ");
			}
			return environment;
		}
	}
}
