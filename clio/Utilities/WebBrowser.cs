using System;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Clio.Common;

namespace Clio.Utilities
{
	internal static class WebBrowser
	{

		
		public static bool Enabled => OSPlatformChecker.GetIsWindowsEnvironment();
		
		public static async Task<bool> CheckUrl(string url){
			using HttpClient client = new ();
			HttpResponseMessage response = await client.GetAsync(url);
			return response.StatusCode == HttpStatusCode.OK;
		}

		public static void OpenUrl(string url) {
			if (OSPlatformChecker.GetIsWindowsEnvironment()) {
				ConsoleLogger.Instance.WriteInfo($"Open {url}...");
				Process.Start(new ProcessStartInfo("cmd", $"/c start {url}") { CreateNoWindow = true });
			} else {
				throw new NotFiniteNumberException("Command not supported for current platform...");
			}
		}
	}
}
