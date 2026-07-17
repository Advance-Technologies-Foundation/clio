using System.Collections.Generic;
using CommandLine;

namespace Clio.Command
{
	/// <summary>
	/// Options for <see cref="Clio.Command.PackageCommand.DistributeLicenseCommand"/>.
	/// </summary>
	[Verb("distribute-license", Aliases = new[] { "grant-license" },
		HelpText = "Add or remove users from a Creatio license package")]
	public class DistributeLicenseOptions : RemoteCommandOptions
	{
		[Option("package-id", Required = true,
			HelpText = "License package Id (Guid). Find it via the Supervisor > License section in Creatio")]
		public string PackageId { get; set; }

		[Option("add-user", Required = false, Separator = ',',
			HelpText = "User Id (Guid) to add to the license package. Repeat or separate with ',' for multiple users")]
		public IEnumerable<string> AddUser { get; set; }

		[Option("remove-user", Required = false, Separator = ',',
			HelpText = "User Id (Guid) to remove from the license package. Repeat or separate with ',' for multiple users")]
		public IEnumerable<string> RemoveUser { get; set; }
	}
}
