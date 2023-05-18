using CommandLine.Text;
using CommandLine;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Clio.Common;
using Clio.UserEnvironment;
using System.Diagnostics;
using System.IO;

namespace Clio.Command
{

	[Verb("mklink", HelpText = "Create symbolic links.")]
	internal class MkLinkOptions
	{

		[Value(0, Default = "")]
		public string Link
		{
			get; set;
		}

		[Value(1, Default = "")]
		public string Target
		{
			get; set;
		}

	}

	class MkLinkCommand : Command<MkLinkOptions>
	{

		public MkLinkCommand() {
		}

		public override int Execute(MkLinkOptions options) {
			try {
				if (OperationSystem.Current.IsWindows) {
					InternalExecute(options.Link, options.Target);
					Console.WriteLine("Symbolic link created.");
					return 0;
				}
				Console.WriteLine("Clio mklink command is only supported on: 'windows'.");
				return 1;
			} catch (Exception e) {
				Console.WriteLine(e);
				return 1;
			}
		}

		internal static void InternalExecute(string link, string target) {
			Process mklinkProcess = Process.Start(
				new ProcessStartInfo("cmd", $"/c mklink /D \"{link}\" \"{target}\"") {
					CreateNoWindow = true
			});
			mklinkProcess.WaitForExit();
		}
	}
}
