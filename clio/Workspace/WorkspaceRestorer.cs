using System;
using System.IO;
using System.Xml;
using Clio.Command;
using Clio.Common;
using Clio.Package;
using Clio.Project.NuGet;
using Clio.Workspaces;
using IFileSystem = System.IO.Abstractions.IFileSystem;

namespace Clio.Workspace;

#region Class: WorkspaceSettings

public class WorkspaceRestorer : IWorkspaceRestorer{
	#region Fields: Private

	private readonly ICreatioSdk _creatioSdk;
	private readonly IEnvironmentScriptCreator _environmentScriptCreator;
	private readonly IFileSystem _fileSystem;
	private readonly ILogger _logger;

	private readonly INuGetManager _nugetManager;
	private readonly IPackageDownloader _packageDownloader;
	private readonly IWorkspacePathBuilder _workspacePathBuilder;
	private readonly IWorkspaceSolutionCreator _workspaceSolutionCreator;

	#endregion

	#region Constructors: Public

	public WorkspaceRestorer(INuGetManager nugetManager, IWorkspacePathBuilder workspacePathBuilder,
		IEnvironmentScriptCreator environmentScriptCreator, IWorkspaceSolutionCreator workspaceSolutionCreator,
		IPackageDownloader packageDownloader, ICreatioSdk creatioSdk, IFileSystem fileSystem, ILogger logger) {
		nugetManager.CheckArgumentNull(nameof(nugetManager));
		workspacePathBuilder.CheckArgumentNull(nameof(workspacePathBuilder));
		environmentScriptCreator.CheckArgumentNull(nameof(environmentScriptCreator));
		workspaceSolutionCreator.CheckArgumentNull(nameof(workspaceSolutionCreator));
		packageDownloader.CheckArgumentNull(nameof(packageDownloader));
		creatioSdk.CheckArgumentNull(nameof(creatioSdk));
		_nugetManager = nugetManager;
		_workspacePathBuilder = workspacePathBuilder;
		_environmentScriptCreator = environmentScriptCreator;
		_workspaceSolutionCreator = workspaceSolutionCreator;
		_packageDownloader = packageDownloader;
		_creatioSdk = creatioSdk;
		_fileSystem = fileSystem;
		_logger = logger;
	}

	#endregion

	#region Methods: Private

	private void AddPropsImport(XmlNode project, XmlDocument doc) {
		XmlElement importElement = doc.CreateElement("Import");
		importElement.SetAttribute("Project", @"..\..\..\.build-props\env.$(Configuration).props");
		importElement.SetAttribute("Condition", @"Exists('..\..\..\.build-props\env.$(Configuration).props')");
		project.InsertBefore(importElement, project.FirstChild);
	}

	private void AppendProps(string scProjPath) {
		string csProjContent = _fileSystem.File.ReadAllText(scProjPath);
		if (string.IsNullOrWhiteSpace(csProjContent)) {
			_logger.WriteWarning($"[WARNING] Project file {scProjPath} is empty or contains only whitespace.");
			return;
		}

		XmlDocument doc = new();
		doc.LoadXml(csProjContent);
		XmlNode project = doc.SelectSingleNode("Project");

		if (project is null) {
			_logger.WriteWarning($"[WARNING] Project file {scProjPath} does not contain a root <Project> node.");
			return;
		}

		XmlNodeList imports = project.SelectNodes("Import");
		bool propsImportExists = false;
		if (imports == null || imports.Count == 0) {
			AddPropsImport(project, doc);
			doc.Save(scProjPath);
		}
		else {
			foreach (XmlNode import in imports) {
				if (import.Attributes != null) {
					string p = import.Attributes["Project"]?.Value ?? string.Empty;
					string c = import.Attributes["Condition"]?.Value ?? string.Empty;
					if (
						string.Equals(p, @"..\..\..\.build-props\env.$(Configuration).props", StringComparison.Ordinal)
						&&
						string.Equals(c, @"Exists('..\..\..\.build-props\env.$(Configuration).props')")
					) {
						propsImportExists = true;
						break;
					}
				}
			}

			if (!propsImportExists) {
				AddPropsImport(project, doc);
				doc.Save(scProjPath);
			}
		}
	}

	private void CreateEnvironmentScript(Version nugetCreatioSdkVersion) {
		_environmentScriptCreator.Create(nugetCreatioSdkVersion);
	}

	private void CreateSolution() {
		_workspaceSolutionCreator.Create();
	}

	private void RestoreNugetCreatioSdk(Version nugetCreatioSdkVersion) {
		const string nugetSourceUrl = "https://api.nuget.org/v3/index.json";
		const string packageName = "CreatioSDK";
		NugetPackageFullName nugetPackageFullName = new() {
			Name = packageName,
			Version = nugetCreatioSdkVersion.ToString()
		};
		_nugetManager.RestoreToNugetFileStorage(nugetPackageFullName, nugetSourceUrl,
			_workspacePathBuilder.NugetFolderPath);
	}

	#endregion

	#region Methods: Public

	public void Restore(WorkspaceSettings workspaceSettings, EnvironmentSettings environmentSettings,
		WorkspaceOptions restoreWorkspaceOptions) {
		Version creatioSdkVersion = _creatioSdk.FindLatestSdkVersion(workspaceSettings.ApplicationVersion);
		_packageDownloader.DownloadPackages(workspaceSettings.Packages, environmentSettings,
			_workspacePathBuilder.PackagesFolderPath);
		if (restoreWorkspaceOptions.IsNugetRestore == true) {
			RestoreNugetCreatioSdk(creatioSdkVersion);
			CreateEnvironmentScript(creatioSdkVersion);
		}

		if (restoreWorkspaceOptions.IsCreateSolution == true) {
			CreateSolution();
		}

		if (restoreWorkspaceOptions.AddBuildProps) {
			EnumerationOptions options = new() {
				MaxRecursionDepth = 2,
				RecurseSubdirectories = true
			};
			string[] csProjPaths
				= _fileSystem.Directory.GetFiles(_workspacePathBuilder.PackagesFolderPath, "*.csproj", options);

			foreach (string csProjPath in csProjPaths) {
				AppendProps(csProjPath);
			}
		}
	}

	#endregion
}

#endregion
