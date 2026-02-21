using System;
using System.IO;
using System.Linq;
using Clio.Common;

namespace Clio.Project;

public interface IVsProject
{
	void AddFile(string name, string body);
	void Reload();
}

public interface IVsProjectFactory
{
	IVsProject Create(string destPath = null, string @namespace = null);
}

public class VsProjectFactory : IVsProjectFactory
{
	private readonly ILogger _logger;

	public VsProjectFactory(ILogger logger) {
		_logger = logger;
	}

	public IVsProject Create(string destPath = null, string @namespace = null) {
		return new VSProject(destPath, @namespace, _logger);
	}
}

public class VSProject : IVsProject{
	#region Constructors: Public

	private readonly ILogger _logger;

	public VSProject(string destPath = null, string @namespace = null, ILogger logger = null) {
		DestPath = destPath;
		Namespace = @namespace;
		_logger = logger;
		if (string.IsNullOrEmpty(Namespace)) {
			string curDir = Environment.CurrentDirectory;
			ProjFile = Directory.GetFiles(curDir, "*.csproj").FirstOrDefault();
			if (File.Exists(ProjFile)) {
				_logger?.WriteInfo($"Detected projFile {ProjFile}");
				string fileText = File.ReadAllText(ProjFile);
				int start = fileText.IndexOf("<RootNamespace>", StringComparison.InvariantCulture);
				int end = fileText.IndexOf("</RootNamespace>", StringComparison.InvariantCulture);
				if (end > start) {
					Namespace = fileText.Substring(start + 15, end - start - 15);
					_logger?.WriteInfo($"Detected namespace {Namespace}");
				}

				if (string.IsNullOrEmpty(DestPath)) {
					DestPath = $"{curDir}\\Files\\cs";
				}
			}
		}
	}

	#endregion

	#region Properties: Public

	public string DestPath { get; set; }

	public string Namespace { get; set; }

	public string ProjFile { get; set; }

	#endregion

	#region Methods: Public

	public void AddFile(string name, string body) {
		_logger?.WriteInfo($"Save {name} class");
		if (!string.IsNullOrEmpty(Namespace)) {
			body = body.Replace("<Namespace>", Namespace);
		}

		File.WriteAllText($"{DestPath}\\{name}.cs", body);
	}

	public void Reload() {
		if (File.Exists(ProjFile)) {
			File.AppendAllText(ProjFile, " ");
			string content = File.ReadAllText(ProjFile);
			File.WriteAllText(ProjFile, content.Substring(0, content.Length - 1));
			_logger?.WriteInfo($"Modified proj file {ProjFile}");
		}
	}

	#endregion
}
