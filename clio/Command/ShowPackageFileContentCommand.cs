using Clio.Common;
using CommandLine;
using DocumentFormat.OpenXml.Wordprocessing;
using System;
using System.Net.Http;
using System.Runtime.Intrinsics.Arm;
using System.Security.Policy;

namespace Clio.Command
{
	[Verb("show-package-file-content", Aliases = new string[] { "show-files", "files" }, HelpText = "Show package file context")]
	public class ShowPackageFileContentOptions : EnvironmentNameOptions
	{
		[Option("package", Required = true, HelpText = "Package name")]
		public string PackageName { get; internal set; }

		[Option("file", Required = false, HelpText = "file path")]
		public string FilePath { get; internal set; }
	}

	internal class ShowPackageFileContentCommand : RemoteCommand<ShowPackageFileContentOptions>
	{
		public override HttpMethod HttpMethod => HttpMethod.Get;

		private object _packageName;
		private string _filePath;

		protected override string ServicePath {
			get {
				return IsReadFile ? 
					$"/rest/CreatioApiGateway/GetPackageFileContent?packageName={_packageName}&filePath={Uri.EscapeDataString(_filePath)}" 
					: $"/rest/CreatioApiGateway/GetPackageFilesDirectoryContent?packageName={_packageName}";
			}
		}

		public bool IsReadFile {
			get {
				return !string.IsNullOrEmpty(_filePath);
			}
		}

		public ShowPackageFileContentCommand(IApplicationClient applicationClient, EnvironmentSettings environmentSettings) : base(applicationClient, environmentSettings) {
		}

		public override int Execute(ShowPackageFileContentOptions options) {
			_packageName = options.PackageName;
			_filePath = options.FilePath?.Trim('\\','/');
			return base.Execute(options);
		}

		protected override void ProceedResponse(string response, ShowPackageFileContentOptions options) {
			base.ProceedResponse(response, options);
			if (IsReadFile) {
				PrintFileContent(response);
			} else {
				PrintFolderContent(response);
			}
		}

		private static void PrintFolderContent(string response) {
			Console.WriteLine();
			string trimmedResponse = response.Trim('[', ']');
			var files = trimmedResponse.Split(new char[] { ',' });
			foreach (var item in files) {
				var prettyFilePath = item.Trim('"').Replace("\\\\", "\\").Replace("//", "/").Trim('\\');
				Console.WriteLine(prettyFilePath);
			}
			Console.WriteLine();
		}

		private static void PrintFileContent(string response) {
			Console.WriteLine();
			string prettyFormat = response.Replace("\\r", "\r").
				Replace("\\t", "\t").Replace("\\n", "\n").Trim('"').Replace("\\\"", "\"").Replace("\\/", "/");
			Console.WriteLine(prettyFormat);
			Console.WriteLine();
		}
	}


}