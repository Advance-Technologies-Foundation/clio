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
		private static IAppUpdater _appUpdater = new AppUpdater();

		public static void CheckUpdate() {
			_appUpdater.CheckUpdate();
		}

		public static string GetLatestVersion() {
			return _appUpdater.GetLatestVersionFromNuget();
		}

		public static string GetLatestVersionFromNuget() {
			return _appUpdater.GetLatestVersionFromNuget();
		}

		public static string GetLatestVersionFromGitHub() {
			return _appUpdater.GetLatestVersionFromGitHub();
		}

		public static string GetCurrentVersion() {
			return _appUpdater.GetCurrentVersion();
		}
		
	}
}
