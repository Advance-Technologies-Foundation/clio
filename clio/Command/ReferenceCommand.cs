using CommandLine;
using System;
using System.Collections.Generic;
using System.Text;

namespace Clio.Command
{
	[Verb("ref-to", HelpText = "Change creatio package project core paths", Hidden = true)]
	public class ReferenceOptions
	{
		[Option('r', "ReferencePattern", Required = false, HelpText = "Pattern for reference path",
			Default = null)]
		public string RefPattern { get; set; }

		[Option('p', "Path", Required = false, HelpText = "Path to the project file",
			Default = null)]
		public string Path { get; set; }

		[Value(0, MetaName = "ReferenceType", Required = false, HelpText = "Indicates what the project will refer to." +
			" Can be 'bin' or 'src'", Default = "src")]
		public string ReferenceType { get; set; }

	}

	public class ReferenceCommand : Command<ReferenceOptions>
	{
		public override int Execute(ReferenceOptions options) {
			throw new NotImplementedException();
		}
	}
}
