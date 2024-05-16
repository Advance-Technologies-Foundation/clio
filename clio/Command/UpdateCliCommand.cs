using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Json;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Clio.Utilities;
using CommandLine;
using Newtonsoft.Json.Linq;

namespace Clio.Command.UpdateCliCommand
{

	class UpdateCliCommand
	{
		private static string LastVersionUrl => "https://api.github.com/repos/Advance-Technologies-Foundation/clio/releases/latest";

		private static void ShowNugetUpdateMessage() {
			Console.WriteLine($"You should consider upgrading via the \'dotnet tool update clio -g\' command.", ConsoleColor.DarkYellow);
		}

		public static void CheckUpdate() {
			var currentVersion = GetCurrentVersion();
			var latestVersion = GetLatestVersion();
			if (currentVersion != latestVersion) {
				Console.WriteLine($"You are using clio version {currentVersion}, however version {latestVersion} is available.");
				ShowNugetUpdateMessage();
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

		public static string GetLatestVersion() {
			return GetLatestVersionFromNuget();

		}

		internal static string GetLatestVersionFromGitHub() {
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

		internal static string GetCurrentVersion() {
			System.Reflection.Assembly assembly = System.Reflection.Assembly.GetExecutingAssembly();
			var fileVersionInfo = FileVersionInfo.GetVersionInfo(assembly.Location);
			return fileVersionInfo.FileVersion;
		}

		internal static string GetLatestVersionFromNuget() {
			return GetLatestPackageVersionAsync("clio").GetAwaiter().GetResult();
		}

		static async Task<string> GetLatestPackageVersionAsync(string packageName) {
			string searchUrl = $"https://api.nuget.org/v3-flatcontainer/{packageName.ToLower()}/index.json";

			try {
				using (HttpClient client = new HttpClient()) {
					HttpResponseMessage response = await client.GetAsync(searchUrl);
					response.EnsureSuccessStatusCode();

					string responseBody = await response.Content.ReadAsStringAsync();
					JObject data = JObject.Parse(responseBody);

					// Extracting the latest version from the response
					string latestVersion = data["versions"].Last.ToString();

					return latestVersion;
				}
			} catch (HttpRequestException e) {
				Console.WriteLine($"Error fetching data: {e.Message}");
				return null;
			}
		}

	}
}
