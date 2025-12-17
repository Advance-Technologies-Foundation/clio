using System;
using System.IO;
using System.Linq;

namespace Clio.Project;

public class VSProject{
	#region Constructors: Public

	public VSProject(string destPath = null, string @namespace = null) {
		DestPath = destPath;
		Namespace = @namespace;
		if (string.IsNullOrEmpty(Namespace)) {
			string curDir = Environment.CurrentDirectory;
			ProjFile = Directory.GetFiles(curDir, "*.csproj").FirstOrDefault();
			if (File.Exists(ProjFile)) {
				Console.WriteLine($"Detected projFile {ProjFile}");
				string fileText = File.ReadAllText(ProjFile);
				int start = fileText.IndexOf("<RootNamespace>", StringComparison.InvariantCulture);
				int end = fileText.IndexOf("</RootNamespace>", StringComparison.InvariantCulture);
				if (end > start) {
					Namespace = fileText.Substring(start + 15, end - start - 15);
					Console.WriteLine($"Detected namespace {Namespace}");
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
		Console.WriteLine($"Save {name} class");
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
			Console.WriteLine($"Modified proj file {ProjFile}");
		}
	}

	#endregion
}
