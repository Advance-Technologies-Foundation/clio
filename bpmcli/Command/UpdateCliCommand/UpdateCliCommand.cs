using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Json;
using System.Net;
using System.Net.Http;
using System.Text;
using CommandLine;

namespace clio.Command.UpdateCliCommand
{

	[Verb("update-cli", Aliases = new string[] { "update" }, HelpText = "Update bpmcli to new available version")]
	internal class UpdateCliOptions
	{
	}

	class UpdateCliCommand
	{
		private static string LastVersionUrl => "https://api.github.com/repos/Advance-Technologies-Foundation/bpmcli/releases/latest";

		public static int UpdateCli() {
			try {
				var url = GetLastReleaseUrl();
				var dir = AppDomain.CurrentDomain.BaseDirectory;
				string updaterDirPath = Path.Combine(dir, "Update");
				string tempDirPath = Path.Combine(dir, "Update", "Temp");
				string filePath = Path.Combine(updaterDirPath, "update.zip");
				string updaterName = "updater.dll";
				Directory.CreateDirectory(tempDirPath);
				Console.WriteLine("Download update.");
				using (var client = new WebClient()) {
					client.DownloadFile(url, filePath);
				}
				ZipFile.ExtractToDirectory(filePath, tempDirPath, true);
				var updaterFile = new FileInfo(Path.Combine(tempDirPath, updaterName));
				updaterFile.CopyTo(Path.Combine(dir, updaterFile.Name), true);
				var updateCmdPath = Path.Combine(dir, "update.cmd");
				var proc = new Process { StartInfo = { FileName = updateCmdPath } };
				Console.WriteLine("Start update.");
				proc.Start();
				return 0;
			} catch (Exception) {
				Console.WriteLine("Update error.");
				return 1;
			}
		}

		public static void CheckUpdate() {
			var currentVersion = GetCurrentVersion();
			var latestVersion = GetLatestVersion();
			if (currentVersion != latestVersion) {
				Console.WriteLine($"You are using bpmcli version {currentVersion}, however version {latestVersion} is available." +
								 $"{Environment.NewLine}You should consider upgrading via the \'bpmcli update-cli\' command.",
					ConsoleColor.DarkYellow);
			}
		}

		private static string GetLastReleaseUrl() {
			System.Threading.Tasks.Task<byte[]> body;
			using (var client = new HttpClient()) {
				client.DefaultRequestHeaders.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
				client.DefaultRequestHeaders.UserAgent.TryParseAdd("request");
				using (var response = client.GetAsync(LastVersionUrl).Result) {
					response.EnsureSuccessStatusCode();
					body = response.Content.ReadAsByteArrayAsync();
				}
			}
			string json;
			var jsonStream = new MemoryStream(body.Result) { Position = 0 };
			using (var reader = new StreamReader(jsonStream, Encoding.UTF8)) {
				json = reader.ReadToEnd();
			}
			JsonObject jsonDoc = (JsonObject)JsonValue.Parse(json);
			var url = jsonDoc["assets"][0]["browser_download_url"];
			return url;
		}

		private static string GetLatestVersion() {
			System.Threading.Tasks.Task<byte[]> body;
			using (var client = new HttpClient()) {
				client.DefaultRequestHeaders.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
				client.DefaultRequestHeaders.UserAgent.TryParseAdd("request");
				using (var response = client.GetAsync(LastVersionUrl).Result) {
					response.EnsureSuccessStatusCode();
					body = response.Content.ReadAsByteArrayAsync();
				}
			}
			string json;
			var jsonStream = new MemoryStream(body.Result) { Position = 0 };
			using (var reader = new StreamReader(jsonStream, Encoding.UTF8)) {
				json = reader.ReadToEnd();
			}
			var jsonDoc = (JsonObject)JsonValue.Parse(json);
			var version = jsonDoc["tag_name"];
			return version;
		}

		private static string GetCurrentVersion() {
			System.Reflection.Assembly assembly = System.Reflection.Assembly.GetExecutingAssembly();
			var fileVersionInfo = FileVersionInfo.GetVersionInfo(assembly.Location);
			return fileVersionInfo.FileVersion;
		}
	}
}
