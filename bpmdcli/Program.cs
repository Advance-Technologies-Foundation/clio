using System;
using CommandLine;

namespace bpmdcli
{

	[Verb("rebase", HelpText = "Change bpm package project core pathes")]
	internal class RebaseOptions
	{
		[Option('f', "FilePath", Required = true, HelpText = "Path to the project file")]
		public string FilePath { get; set; }

		[Option('t', "ProjectType", Required = false, HelpText = "Type of the bpm project file. Can be 'pkg' or 'sln'",
			Default = "pkg")]
		public string ProjectType { get; set; }
	}

	public class Program
	{

		private static int Rebase(RebaseOptions options) {
			try {
				switch (options.ProjectType) {
					case "sln": {
							throw new NotSupportedException("option sln temporaly not supported");
						}
					case "pkg": {
							BpmPkgProject.LoadFromFile(options.FilePath)
								.RebaseToCoreDebug()
								.SaveChanges();
						}
						break;
					default: {
							throw new NotSupportedException($"You use not supported option type {options.ProjectType}");
						}
				}
				return 0;
			} catch (Exception e) {
				Console.WriteLine(e);
				return 1;
			}
		}

		private static int Main(string[] args) {
			return Parser.Default.ParseArguments<RebaseOptions>(args).MapResult(
				Rebase,
				errs => 1
				);
		}
	}
}
