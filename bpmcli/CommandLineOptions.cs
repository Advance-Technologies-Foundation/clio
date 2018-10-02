using CommandLine;
using CommandLine.Text;

namespace bpmcli
{
	internal class BaseOptions
	{
		[Option('u', "uri", Required = false, HelpText = "bpm`online site uri")]
		public string Uri { get; set; }

		[Option('p', "Password", Required = false, HelpText = "User password")]
		public string Password { get; set; }

		[Option('l', "Login", Required = false, HelpText = "User login (administrator permisssion required)")]
		public string Login { get; set; }

		[Option('e', "Environment", Required = false, HelpText = "Environment name")]
		public string Environment { get; set; }
	}

	[Verb("exec", HelpText = "Execute assembly")]
	internal class ExecuteOptions : BaseOptions
	{
		[Option('f', "FilePath", Required = true, HelpText = "Assembly file path")]
		public string FilePath { get; set; }

		[Option('t', "ExecutorType", Required = true, HelpText = "Assembly type name for proceed")]
		public string ExecutorType { get; set; }
	}

	[Verb("restart", HelpText = "Restart application")]
	internal class RestartOptions : BaseOptions
	{
	}

	[Verb("fetch", HelpText = "Download assembly")]
	internal class FetchOptions : BaseOptions
	{
		[Option('n', "Package name", Required = true, HelpText = "Package name")]
		public string PackageName { get; set; }

		[Option('o', "Operation", Required = false, HelpText = "Operation: load - from file system to app, download - from app to file system)")]
		public string Operation { get; set; }
	}

	[Verb("compress", HelpText = "Compression project")]
	internal class CompressionOptions
	{
		[Option('s', "SourcePath", Required = true, HelpText = "Path to source package folder")]
		public string SourcePath { get; set; }
		[Option('d', "DestinationPath", Required = true, HelpText = "Compressed package file path")]
		public string DestinationPath { get; set; }
	}

	[Verb("cfg", HelpText = "Configure environment settings")]
	internal class ConfigureOptions : BaseOptions
	{
		[Option('a', "ActiveEnvironment", Required = false, HelpText = "Set environment by default")]
		public string ActiveEnvironment { get; set; }
	}
	[Verb("remove", HelpText = "Remove environment settings")]
	internal class RemoveOptions : BaseOptions
	{
		[Option('e', "ActiveEnvironment", Required = true, HelpText = "Environment name")]
		public new string Environment { get; set; }
	}
	[Verb("install", HelpText = "Install package")]
	class InstallOptions : BaseOptions
	{
		[Option('f', "FilePath", Required = true, HelpText = "Package file path")]
		public string FilePath { get; set; }
		[Option('r', "ReportPath", Required = false, HelpText = "Log file path")]
		public string ReportPath { get; set; }
	}
}