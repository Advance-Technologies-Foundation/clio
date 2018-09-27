using CommandLine;
using CommandLine.Text;

namespace bpmcli
{
	internal class BaseOptions
	{
		[Option('u', "uri", Required = false)]
		public string Uri { get; set; }

		[Option('p', "Password", Required = false)]
		public string Password { get; set; }

		[Option('l', "Login", Required = false)]
		public string Login { get; set; }

		[Option('e', "Environment", Required = false)]
		public string Environment { get; set; }
	}

	[Verb("exec", HelpText = "Execute assembly.")]
	internal class ExecuteOptions : BaseOptions
	{
		[Option('f', "FilePath", Required = true)]
		public string FilePath { get; set; }

		[Option('t', "ExecutorType", Required = true)]
		public string ExecutorType { get; set; }
	}

	[Verb("restart", HelpText = "Restart application.")]
	internal class RestartOptions : BaseOptions
	{
	}

	[Verb("fetch", HelpText = "Download assembly.")]
	internal class FetchOptions : BaseOptions
	{
		[Option('p', "Package name", Required = true)]
		public string PackageName { get; set; }

		[Option('o', "Operation", Required = false)]
		public string Operation { get; set; }
	}

	[Verb("compress", HelpText = "Compression project")]
	internal class CompressionOptions
	{
		[Option('s', "SourcePath", Required = true)]
		public string SourcePath { get; set; }
		[Option('d', "DestinationPath", Required = true)]
		public string DestinationPath { get; set; }
	}

	[Verb("cfg", HelpText = "Configure environment settings.")]
	internal class ConfigureOptions : BaseOptions
	{
		[Option('a', "ActiveEnvironment", Required = false)]
		public string ActiveEnvironment { get; set; }
	}
	[Verb("remove", HelpText = "Remove environment settings.")]
	internal class RemoveOptions : BaseOptions
	{
		[Option('e', "ActiveEnvironment", Required = true)]
		public new string Environment { get; set; }
	}
	[Verb("install", HelpText = "Install package.")]
	class InstallOptions : BaseOptions
	{
		[Option('f', "FilePath", Required = true)]
		public string FilePath { get; set; }
		[Option('r', "ReportPath", Required = false)]
		public string ReportPath { get; set; }
	}
}