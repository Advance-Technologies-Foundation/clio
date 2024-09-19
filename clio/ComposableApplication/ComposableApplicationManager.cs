using System;
using System.IO;
using System.IO.Abstractions;
using System.Json;
using System.Linq;
using System.Text;
using Clio.Common;
using Clio.Package;
using FluentValidation;
using FluentValidation.Results;
using Newtonsoft.Json;
using IFileSystem = System.IO.Abstractions.IFileSystem;

namespace Clio.ComposableApplication;

public class SetIconParameters
{

	#region Properties: Public

	public string AppName { get; set; }

	public string IconPath { get; set; }

	public string AppPath { get; set; }

	#endregion

}

public class SetIconParametersValidator : AbstractValidator<SetIconParameters>
{

	#region Constructors: Public

	public SetIconParametersValidator(IFileSystem fileSystem){
		RuleFor(x => x.AppPath)
			.Cascade(CascadeMode.Stop)
			.NotEmpty().WithMessage("App path is required.")
			.Must(path => fileSystem.Directory.Exists(path) || fileSystem.File.Exists(path))
			.WithMessage(x => $"Path '{x.AppPath}' must exist as a directory or a file.");


		RuleFor(x => x.IconPath)
			.Cascade(CascadeMode.Stop)
			.NotEmpty()
			.WithMessage("Icon path is required.")
			.Must(fileSystem.File.Exists)
			.WithMessage(x => $"Icon file '{x.IconPath}' must exist.");

		RuleFor(x => x.AppName)
			.Cascade(CascadeMode.Stop)
			.NotEmpty()
			.When(x => fileSystem.Directory.Exists(x.AppPath))
			.WithMessage("App name is required when AppPath is a directory.");
	}

	#endregion

}

public class ComposableApplicationManager : IComposableApplicationManager
{

	#region Fields: Private

	private readonly IFileSystem _fileSystem;
	private readonly IValidator<SetIconParameters> _validator;
	private readonly IPackageArchiver _archiver;
	private readonly IWorkingDirectoriesProvider _directoriesProvider;

	#endregion

	#region Constructors: Public

	public ComposableApplicationManager(IFileSystem fileSystem, IValidator<SetIconParameters> validator,
			IPackageArchiver archiver, IWorkingDirectoriesProvider directoriesProvider){
		_fileSystem = fileSystem;
		_validator = validator;
		_archiver = archiver;
		_directoriesProvider = directoriesProvider;
	}

	#endregion

	#region Methods: Public

	public void SetIcon(string appPath, string iconPath, string appName) {
		SetIconParameters parameters = new() {
			AppPath = appPath,
			IconPath = iconPath,
			AppName = appName
		};

		ValidationResult validationResult = _validator.Validate(parameters);
		if (!validationResult.IsValid) {
			throw new ValidationException(validationResult.Errors);
		}
		bool isArchive = _fileSystem.File.Exists(appPath);
		string unzipAppPath = string.Empty;
		if (isArchive) {
			unzipAppPath = _directoriesProvider.CreateTempDirectory();
			_archiver.ExtractPackages(appPath, true, true, false, false, unzipAppPath);
			ChangeIcon(unzipAppPath, iconPath, appName);
			_archiver.ZipPackages(unzipAppPath, appPath, true);
			return;
		}
		ChangeIcon(appPath, iconPath, appName);
	}

	private void ChangeIcon(string appPath, string iconPath, string appName) {
		string[] files = _fileSystem.Directory
					.GetFiles(appPath, "app-descriptor.json", SearchOption.AllDirectories);

		if (files.Length == 0) {
			throw new FileNotFoundException($"No app-descriptor.json file found in the specified packages folder path. {appPath}");
		}

		var matchingFiles = files
			.Select(file => new { File = file, Content = _fileSystem.File.ReadAllText(file) })
			.Select(fileContent => new {
				fileContent.File,
				AppDescriptor = JsonConvert.DeserializeObject<AppDescriptorJson>(fileContent.Content)
			})
			.Where(fileDescriptor => fileDescriptor.AppDescriptor.Code == appName)
			.ToList();

		if (matchingFiles.Count > 1) {
			StringBuilder exceptionMessage = new("More than one app-descriptor.json file found with the same Code:\n");
			foreach (var file in matchingFiles) {
				exceptionMessage.AppendLine(file.File);
			}
			throw new InvalidOperationException(exceptionMessage.ToString());
		}

		if (matchingFiles.Count == 0) {
			throw new ValidationException($"App {appName} not found.");
		}

		var matchingFile = matchingFiles[0];

		string fileExt = Path.GetExtension(iconPath);
		string iconFileName = Path.GetFileNameWithoutExtension(iconPath);
		string timestamp = DateTime.Now.ToString("yyyyMMddHHmmss");
		matchingFile.AppDescriptor.IconName = $"{iconFileName}_{timestamp}{fileExt}";

		string base64EncodedIcon = Convert.ToBase64String(_fileSystem.File.ReadAllBytes(iconPath));
		matchingFile.AppDescriptor.Icon = base64EncodedIcon;
		string formattedJsonString = JsonConvert.SerializeObject(matchingFile.AppDescriptor, Formatting.Indented);
		_fileSystem.File.WriteAllText(matchingFile.File, formattedJsonString);
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
	///  Sets the icon for the specified application by updating the app-descriptor.json file.
	/// </summary>
	/// <param name="packagesFolderPath">The path to the folder containing the application packages.</param>
	/// <param name="iconPath">The path to the icon file to be set.</param>
	/// <param name="appName">The name of the application for which the icon is to be set.</param>
	void SetIcon(string packagesFolderPath, string iconPath, string appName);

	public void SetVersion(string appPackagesFolderPath, string version, string packageName = null);

	public bool TrySetVersion(string workspacePath, string appVersion);

	#endregion

}