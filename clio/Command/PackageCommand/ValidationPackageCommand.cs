using System;
using System.Collections.Generic;
using System.Text;
using CommandLine;

namespace Clio.Command.PackageCommand
{

	[Verb("validation-pkg", Aliases = new string[] { "validation"}, HelpText = "Validation package")]
	public class ValidationPkgOptions
	{
		[Value(0, MetaName = "Name", Required = false, HelpText = "Name of the package for validation")]
		public string Name {
			get; set;
		}

		[Option('d', "DestinationResult", Required = false, HelpText = "Destination path for result validation")]
		public string DestinationResult {
			get; set;
		}
	}

	class ValidationPackageCommand
	{
		public static int Validate(ValidationPkgOptions opts) {

			return 1;
		}

	}
}
