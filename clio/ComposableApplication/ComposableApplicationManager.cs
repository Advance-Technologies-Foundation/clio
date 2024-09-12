using System;
using System.IO;
using System.IO.Abstractions;
using System.Json;
using System.Text;
using Clio.Package;
using Newtonsoft.Json;

namespace Clio.ComposableApplication;

public class ComposableApplicationManager : IComposableApplicationManager
{

	#region Fields: Private

	private readonly IFileSystem _fileSystem;

	#endregion

	#region Constructors: Public

	public ComposableApplicationManager(IFileSystem fileSystem1){
		_fileSystem = fileSystem1;
	}

	#endregion

	#region Methods: Public

	
	public void SetIcon(string packagesFolderPath, string iconPath, string appName){
		string[] files = _fileSystem.Directory
			.GetFiles(packagesFolderPath, "app-descriptor.json", SearchOption.AllDirectories);

		foreach (string file in files) {
			string appDescriptorContent = _fileSystem.File.ReadAllText(file);
			AppDescriptorJson appDescriptor = JsonConvert.DeserializeObject<AppDescriptorJson>(appDescriptorContent);
			if (appDescriptor.Name == appName || appDescriptor.Code == appName) {
				string iconFileName = Path.GetFileName(iconPath);
				appDescriptor.IconName = iconFileName;

				string base64EncodedIcon = Convert.ToBase64String(_fileSystem.File.ReadAllBytes(iconPath));
				appDescriptor.Icon = base64EncodedIcon;
				string formattedJsonString = JsonConvert.SerializeObject(appDescriptor, Formatting.Indented);
				_fileSystem.File.WriteAllText(file, formattedJsonString);
			}
		}
	}

	public void SetVersion(string appPackagesFolderPath, string version, string packageName = null){
		string[] appDescriptorPaths = _fileSystem.Directory.GetFiles(appPackagesFolderPath, "app-descriptor.json",
			SearchOption.AllDirectories);
		if (appDescriptorPaths.Length > 1) {
			string code = string.Empty;
			foreach (string descriptor in appDescriptorPaths) {
				string actualCode = JsonValue.Parse(_fileSystem.File.ReadAllText(descriptor))["Code"].ToString();
				if (code != actualCode && code != string.Empty) {
					StringBuilder exceptionMessage = new();
					exceptionMessage.AppendLine("Find more than one applications: ");
					foreach (string path in appDescriptorPaths) {
						exceptionMessage.AppendLine(path);
					}
					throw new Exception(exceptionMessage.ToString());
				}
				code = actualCode;
			}
			if (string.IsNullOrEmpty(packageName)) {
				StringBuilder exceptionMessage = new();
				exceptionMessage.AppendLine(
					$"Find more than one descriptors for application {code}. Specify package name.");
				foreach (string path in appDescriptorPaths) {
					exceptionMessage.AppendLine(path);
				}
				throw new Exception(exceptionMessage.ToString());
			}
		}
		string appDescriptorPath = appDescriptorPaths[0];
		JsonValue objectJson = JsonValue.Parse(_fileSystem.File.ReadAllText(appDescriptorPath));
		objectJson["Version"] = version;
		object jsonObject = JsonConvert.DeserializeObject(objectJson.ToString());
		string formattedJsonString = JsonConvert.SerializeObject(jsonObject, Formatting.Indented);
		_fileSystem.File.WriteAllText(appDescriptorPath, formattedJsonString);
	}

	public bool TrySetVersion(string workspacePath, string appVersion){
		try {
			SetVersion(workspacePath, appVersion);
			return true;
		} catch (Exception) {
			return false;
		}
	}

	#endregion

}

public interface IComposableApplicationManager
{

	#region Methods: Public

	/// <summary>
	/// Sets the icon for the specified application by updating the app-descriptor.json file.
	/// </summary>
	/// <param name="packagesFolderPath">The path to the folder containing the application packages.</param>
	/// <param name="iconPath">The path to the icon file to be set.</param>
	/// <param name="appName">The name of the application for which the icon is to be set.</param>
	void SetIcon(string packagesFolderPath, string iconPath, string appName);

	public void SetVersion(string appPackagesFolderPath, string version, string packageName = null);

	public bool TrySetVersion(string workspacePath, string appVersion);

	#endregion

}