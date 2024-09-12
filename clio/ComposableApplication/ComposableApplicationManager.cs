using DocumentFormat.OpenXml.Office2010.PowerPoint;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions;
using System.Json;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace Clio.ComposableApplication
{
	public class ComposableApplicationManager: IComposableApplicationManager
	{
		private IFileSystem fileSystem;

		public ComposableApplicationManager(IFileSystem fileSystem1) {
			this.fileSystem = fileSystem1;
		}

		public void SetIcon(string packagesFolderPath, string iconPath, string appName) {
			throw new NotImplementedException();
		}

		public void SetVersion(string appPackagesFolderPath, string version, string packageName = null) {
			string[] appDescriptorPaths = fileSystem.Directory.GetFiles(appPackagesFolderPath, "app-descriptor.json", SearchOption.AllDirectories);
			if (appDescriptorPaths.Length > 1) {
				string code = string.Empty;
				foreach (var descriptor in appDescriptorPaths) {
					string actualCode = JsonObject.Parse(fileSystem.File.ReadAllText(descriptor))["Code"].ToString();
					if (code != actualCode && code != string.Empty) {
						StringBuilder exceptionMessage = new StringBuilder();
						exceptionMessage.AppendLine("Find more than one applications: ");
						foreach (var path in appDescriptorPaths) {
							exceptionMessage.AppendLine(path);
						}
						throw new Exception(exceptionMessage.ToString());
					} else {
						code = actualCode;
					}
				}
				if (string.IsNullOrEmpty(packageName)) {
					StringBuilder exceptionMessage = new StringBuilder();
					exceptionMessage.AppendLine($"Find more than one descriptors for application {code}. Specify package name.");
					foreach (var path in appDescriptorPaths) {
						exceptionMessage.AppendLine(path);
					}
					throw new Exception(exceptionMessage.ToString());
				}
			}
			string appDescriptorPath = appDescriptorPaths[0];
			JsonValue objectJson = JsonObject.Parse(fileSystem.File.ReadAllText(appDescriptorPath));
			objectJson["Version"] = version;
			var jsonObject = JsonConvert.DeserializeObject(objectJson.ToString());
			string formattedJsonString = JsonConvert.SerializeObject(jsonObject, Formatting.Indented);
			fileSystem.File.WriteAllText(appDescriptorPath, formattedJsonString);
		}

		public bool TrySetVersion(string workspacePath, string appVersion) {
			try {
				SetVersion(workspacePath, appVersion);
				return true;
			} catch(Exception) {
				return false;
			}
		}
	}

	public interface IComposableApplicationManager
	{
		void SetIcon(string packagesFolderPath, string iconPath, string appName);
		public void SetVersion(string appPackagesFolderPath, string version, string packageName = null);
		public bool TrySetVersion(string workspacePath, string appVersion);
	}
}
