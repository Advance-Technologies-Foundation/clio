using System;
using Clio.Common;
using CommandLine;

namespace Clio.Command
{
	[Verb("set-feature", Aliases = new[] { "feature" }, HelpText = "Set feature state")]
	internal class FeatureOptions : EnvironmentOptions
	{

		[Value(0, MetaName = "Code", Required = true, HelpText = "Feature code")]
		public string Code { get; set; }

		[Value(1, MetaName = "State", Required = true, HelpText = "Feature state")]
		public int State { get; set; }

		[Value(2, MetaName = "onlyCurrentUser", Required = false, Default = false, HelpText = "Only current user")]
		public bool OnlyCurrentUser { get; set; }

	}

	internal class FeatureCommand : RemoteCommand<FeatureOptions>
	{

		protected override string ServicePath => @"/rest/FeatureStateService/SetFeatureState";

		public FeatureCommand(IApplicationClient applicationClient, EnvironmentSettings settings)
			: base(applicationClient, settings) {
		}

		protected override string GetRequestData(FeatureOptions options) {
			return "{" + $"\"code\":\"{options.Code}\",\"state\":\"{options.State}\",\"onlyCurrentUser\":{options.OnlyCurrentUser.ToString().ToLower()}" + "}";
		}

	}
}
