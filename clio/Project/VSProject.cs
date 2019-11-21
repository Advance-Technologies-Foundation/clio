using System;
using System.IO;
using System.Linq;

namespace Clio.Project
{
	public class VSProject
	{

		public string DestPath { get; set; }

		public string Namespace { get; set; }

		public string ProjFile { get; set; }

		public VSProject(string destPath = null, string @namespace = null) {
			DestPath = destPath;
			Namespace = @namespace;
			if (string.IsNullOrEmpty(Namespace)) {
				var curDir = Environment.CurrentDirectory;
				ProjFile = Directory.GetFiles(curDir, "*.csproj").FirstOrDefault();
				if (File.Exists(ProjFile)) {
					Console.WriteLine($"Detected projFile {ProjFile}");
					var fileText = File.ReadAllText(ProjFile);
					int start = fileText.IndexOf("<RootNamespace>");
					int end = fileText.IndexOf("</RootNamespace>");
					if (end > start) {
						Namespace = fileText.Substring(start + 15, end - start - 15);
						Console.WriteLine($"Detected namespace {@Namespace}");
					}
					if (string.IsNullOrEmpty(DestPath)) {
						DestPath = $"{curDir}\\Files\\cs";
					}
				}

			}
		}

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
				var content = File.ReadAllText(ProjFile);
				File.WriteAllText(ProjFile, content.Substring(0, content.Length - 1));
				Console.WriteLine($"Modified proj file {ProjFile}");
			}
		}
	}
}
