using System;
using System.IO;

namespace cliogate.Files.cs
{
	public class CreatioPathBuilder
	{

		public static string RootPath { get; set; } = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Terrasoft.Configuration", "Pkg");
		
		public static string GetPackageFilePath(string packageName) {
				return Path.Combine(RootPath, packageName, "Files");
		}

	}
}