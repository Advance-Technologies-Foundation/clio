using System;
using Clio.Feature;
using CommandLine;

namespace Clio.Command.FeatureCommand
{
	[Verb("set-feature", Aliases = new[] { "feature" }, HelpText = "Set feature state")]
	internal class FeatureOptions: EnvironmentOptions
	{
		[Value(0, MetaName = "Code", Required = true, HelpText = "Feature code")]
		public string Code { get; set; }

		[Value(1, MetaName = "State", Required = true, HelpText = "Feature state")]
		public int State { get; set; }

	}

	class FeatureCommand: BaseRemoteCommand
	{
		public static int SetFeatureState(FeatureOptions options) {
			try {
				Configure(options);
				var fm = new FeatureModerator(CreatioClient);
				switch (options.State) {
					case 0:
						fm.SwitchFeatureOff(options.Code);
						break;
					case 1:
						fm.SwitchFeatureOn(options.Code);
						break;
					default:
						throw new NotSupportedException($"You use not supported feature state type {options.State}");
				}
			} catch (Exception exception) {
				Console.WriteLine(exception.Message);
				return 1;
			}
			return 0;
		}
	}
}
