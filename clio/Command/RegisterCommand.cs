using System;
using Clio.UserEnvironment;
using CommandLine;

namespace Clio.Command
{
	[Verb("register", HelpText = "Register clio in global environment", Hidden = true)]
	internal class RegisterOptions
	{
		[Option('t', "Target", Default = "u", HelpText = "Target environment location. Could be user location or" +
			" machine location. Use 'u' for set user location and 'm' to set machine location.")]
		public string Target { get; set; }

		[Option('p', "Path", HelpText = "Path where clio is stored.")]
		public string Path { get; set; }

	}

	[Verb("unregister", HelpText = "Unregister clio in global environment", Hidden = true)]
	internal class UnregisterOptions
	{
		[Option('t', "Target", Default = "u", HelpText = "Target environment location. Could be user location or" +
			" machine location. Use 'u' for set user location and 'm' to set machine location.")]
		public string Target { get; set; }

		[Option('p', "Path", HelpText = "Path where clio is stored.")]
		public string Path { get; set; }

	}

	class RegisterCommand : Command<RegisterOptions>
	{
		public RegisterCommand() {
		}

		public override int Execute(RegisterOptions options) {
			try {
				var creatioEnv = new CreatioEnvironment();
				string path = string.IsNullOrEmpty(options.Path) ? Environment.CurrentDirectory : options.Path;
				IResult result = options.Target == "m"
					? creatioEnv.MachineRegisterPath(path)
					: creatioEnv.UserRegisterPath(path);
				result.ShowMessagesTo(Console.Out);
				return 0;
			} catch (Exception e) {
				Console.WriteLine(e);
				return 1;
			}
		}
	}

	class UnregisterCommand : Command<UnregisterOptions>
	{
		public override int Execute(UnregisterOptions options) {
			try {
				var creatioEnv = new CreatioEnvironment();
				IResult result = options.Target == "m"
					? creatioEnv.MachineUnregisterPath()
					: creatioEnv.UserUnregisterPath();
				result.ShowMessagesTo(Console.Out);
				return 0;
			} catch (Exception e) {
				Console.WriteLine(e);
				return 1;
			}
		}
	}
}
