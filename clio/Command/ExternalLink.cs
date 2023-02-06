namespace Clio.Command
{
	using CommandLine;
	using System;
	using System.Collections.Specialized;


	#region Class: AddPackageOptions

	[Verb("externalLink", Aliases = new string[] { "link" }, HelpText = "Handle external deep-links")]
	public class ExternalLinkOptions : EnvironmentOptions
	{

		#region Properties: Public
		[Option('c', "content", Required = false, HelpText = "content")]
		public string Content
		{
			get; set;
		}


		#endregion

	}

	#endregion

	#region Class: AddPackageCommand

	public class ExternalLinkCommand : Command<ExternalLinkOptions>
	{
		private readonly RegAppCommand _regCommand;

		#region Fields: Private



		#endregion

		#region Constructors: Public

		public ExternalLinkCommand(RegAppCommand regCommand)
		{
			_regCommand = regCommand;
		}

		#endregion

		#region Methods: Public

		public override int Execute(ExternalLinkOptions options)
		{

			Uri uri = new Uri(options.Content);
			NameValueCollection clioParams = System.Web.HttpUtility.ParseQueryString(uri.Query);

			Console.WriteLine("clio was called with:");
			for (var i = 0; i < clioParams.Count; i++)
			{
				var key = clioParams.Keys[i];
				var value = clioParams.GetValues(i)?[0];
				Console.WriteLine($"\t{key} - {value}");
			}

			var baseUrl = $"{clioParams["protocol"]}//{clioParams["host"]}";

			//Should pass OAuth20IdentityServerUrl SysSetting for completeness
			var authUrl = $"{clioParams["protocol"]}//{clioParams["host"].Replace(".creatio.com", "-is.creatio.com/connect/token")}";


			var opt = new RegAppOptions
			{
				IsNetCore = false,                                      //Should pass in deepLink as arg
				ClientId = clioParams["clientId"],
				ClientSecret = clioParams["clientSecret"],
				Uri = baseUrl,
				Name = clioParams["name"],
				AuthAppUri = authUrl,
				Login = string.Empty,
				Password = string.Empty,
				Maintainer = "Customer"                                 //Should pass in deepLink as arg Maintainer (SysSetting.Maintainer)
			};

			_regCommand.Execute(opt);


			Console.ReadLine();
			return 0;
		}
		#endregion
	}

	#endregion

}
