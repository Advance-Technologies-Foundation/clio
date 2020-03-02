using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Creatio.Client;

namespace Clio.UserEnvironment
{
	internal class CreatioEnvironment : ICreatioEnvironment
	{
		private const string PathVariableName = "PATH";
		public static string AppUrl {
			get {
				if (CreatioEnvironment.IsNetCore) {
					return Url;
				} else {
					return Url + @"/0";
				}
			}
		}

		public static string Url => Settings.Uri; // Необходимо получить из конфига

		public static string GetZipPackageUrl => AppUrl + @"/ServiceModel/PackageInstallerService.svc/GetZipPackages";

		public static string GetEntityModelsUrl => AppUrl + @"/rest/CreatioApiGateway/GetEntitySchemaModels/{0}";

		public static string ApiVersionUrl => AppUrl + @"/rest/CreatioApiGateway/GetApiVersion";

		public static bool IsNetCore => Settings.IsNetCore;
		public static string EnvironmentName { get; set; }
		public static EnvironmentSettings Settings { get; set; }


		private IResult RegisterPath(string path, EnvironmentVariableTarget target) {
			var result = new EnvironmentResult();
			string pathValue = Environment.GetEnvironmentVariable(PathVariableName, target);
			if (string.IsNullOrEmpty(pathValue)) {
				pathValue = string.Empty;
			}
			if (pathValue.Contains(path)) {
				result.AppendMessage($"{PathVariableName} variable already registered!");
				return result;
			}
			result.AppendMessage($"register path {path} in {PathVariableName} variable.");
			var value = string.Concat(pathValue, Path.PathSeparator + path.Trim(Path.PathSeparator));
			Environment.SetEnvironmentVariable(PathVariableName, value, target);
			result.AppendMessage($"{PathVariableName} variable registered.");
			return result;
		}

		private IResult UnregisterPath(EnvironmentVariableTarget target) {
			var result = new EnvironmentResult();
			string pathValue = Environment.GetEnvironmentVariable(PathVariableName, target);
			var paths = pathValue.Split(Path.PathSeparator);
			string clioPath = string.Empty;
			foreach (var path in paths) {
				if (Directory.Exists(path)) {
					var dir = new DirectoryInfo(path);
					var files = dir.GetFiles("clio.cmd");
					if (files.Length > 0) {
						clioPath = path;
						break;
					}
				}
			}
			if (string.IsNullOrEmpty(clioPath)) {
				result.AppendMessage($"Application already unregistered!");
				return result;
			}
			result.AppendMessage($"Unregister path {clioPath} in {PathVariableName} variable.");
			string newValue = pathValue.Replace(clioPath, string.Empty).Replace(String.Concat(Path.PathSeparator, Path.PathSeparator), Path.PathSeparator.ToString());
			Environment.SetEnvironmentVariable(PathVariableName, newValue, target);
			result.AppendMessage($"{PathVariableName} variable unregistered.");
			return result;
		}

		private static void Configure(EnvironmentOptions options) {
			var settingsRepository = new SettingsRepository();
			EnvironmentName = options.Environment;
			Settings = settingsRepository.GetEnvironment(options);
		}

		private static Version GetAppApiVersion() {
			var apiVersion = new Version("0.0.0.0");
			try {
				string appVersionResponse = CreatioClient.ExecuteGetRequest(ApiVersionUrl).Trim('"');
				apiVersion = new Version(appVersionResponse);
			} catch (Exception) {
			}
			return apiVersion;
		}

		public string GetRegisteredPath() {
			var environmentPath = Environment.GetEnvironmentVariable(PathVariableName);
			string[] cliPath = (environmentPath?.Split(Path.PathSeparator));
			return cliPath?.FirstOrDefault(p => p.Contains("clio"));
		}

		public IResult UserRegisterPath(string path) {
			return RegisterPath(path, EnvironmentVariableTarget.User);
		}

		public IResult MachineRegisterPath(string path) {
			return RegisterPath(path, EnvironmentVariableTarget.Machine);
		}

		public IResult MachineUnregisterPath() {
			return UnregisterPath(EnvironmentVariableTarget.Machine);
		}

		public IResult UserUnregisterPath() {
			return UnregisterPath(EnvironmentVariableTarget.User);
		}

		public string GetAssemblyFolderPath() {
			return Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
		}

		public static void CheckApiVersion() {
			var dir = AppDomain.CurrentDomain.BaseDirectory;
			string versionFilePath = Path.Combine(dir, "cliogate", "version.txt");
			var localApiVersion = new Version(File.ReadAllText(versionFilePath));
			var appApiVersion = GetAppApiVersion();
			if (appApiVersion == new Version("0.0.0.0")) {
				MessageToConsole($"Your app does not contain clio API." +
				 $"{Environment.NewLine}You should consider install it via the \'clio install-gate\' command.", ConsoleColor.DarkYellow);
			} else if (localApiVersion > appApiVersion) {
				MessageToConsole($"You are using clio api version {appApiVersion}, however version {localApiVersion} is available." +
				 $"{Environment.NewLine}You should consider upgrading via the \'clio update-gate\' command.", ConsoleColor.DarkYellow);
			}
		}

		public static void SetupAppConnection(EnvironmentOptions options) {
			Configure(options);
			CheckApiVersion();
		}

	}
}
