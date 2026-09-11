using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
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
	#region Constants: Private

	//Both separators are listed regardless of platform: a backslash is a legal file-name character on
	//Unix, so accepting it there would let "..\\Outside" survive into a name Windows later reads as a path.
	private static readonly char[] NameSeparators = ['/', '\\', ':'];

	//Windows and macOS resolve paths case-insensitively; a case-sensitive prefix check would reject a
	//legitimate destination there. Linux is case-sensitive and must compare exactly.
	private static readonly StringComparison PathComparison =
		RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

	#endregion

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

			}
		}
		
		if (string.IsNullOrEmpty(DestPath)) {
			//Also needed when the namespace was supplied explicitly, otherwise AddFile
			//would compose a path from a null DestPath
			DestPath = Path.Combine(Environment.CurrentDirectory, "Files", "cs");
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
		string targetPath = ResolveTargetPath(name);
		_logger?.WriteInfo($"Save {name} class");
		if (!string.IsNullOrEmpty(Namespace)) {
			body = body.Replace("<Namespace>", Namespace);
		}

		File.WriteAllText(targetPath, body);
	}

	/// <summary>
	/// Turns an item name into the absolute file it may be written to, refusing anything that is not one
	/// plain file name.
	/// </summary>
	/// <remarks>
	/// <see cref="Path.Combine(string,string)"/> DISCARDS the first argument when the second is rooted, so
	/// an absolute name silently wrote outside <see cref="DestPath"/> instead of failing. add-item also
	/// feeds this method dictionary keys taken from a Creatio response, which is not clio's own data, so
	/// the name is validated first and the composed path is then re-checked against the destination:
	/// either check alone leaves a gap on one of the supported platforms.
	/// </remarks>
	private string ResolveTargetPath(string name) {
		if (string.IsNullOrWhiteSpace(name)) {
			throw new ArgumentException("Item name is required and cannot be empty.", nameof(name));
		}
		if (name.IndexOfAny(NameSeparators) >= 0 || name == "." || name == ".."
			|| name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) {
			throw new ArgumentException(
				$"Item name '{name}' must be a single file name without a directory, a drive or '..'.",
				nameof(name));
		}
		string destinationRoot = Path.GetFullPath(DestPath);
		string targetPath = Path.GetFullPath(Path.Combine(destinationRoot, $"{name}.cs"));
		string prefix = destinationRoot.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
			? destinationRoot
			: destinationRoot + Path.DirectorySeparatorChar;
		if (!targetPath.StartsWith(prefix, PathComparison)) {
			throw new ArgumentException(
				$"Item name '{name}' resolves outside the destination directory '{destinationRoot}'.",
				nameof(name));
		}
		return targetPath;
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
