namespace Clio.Command
{
	using CommandLine;
	using System;
	using System.Collections.Specialized;


	#region Class: ExternalLinkOptions

	[Verb("externalLink", Aliases = new string[] { "link" }, HelpText = "Handle external deep-links")]
	public class ExternalLinkOptions : EnvironmentOptions
	{
		#region Properties: Public

		// Default to make sure we dont throw prematurely
		[Value(0, Default = "")]
		public string Content
		{
			get; set;
		}

		#endregion
	}

	#endregion

	#region Class: ExternalLinkCommand

	public class ExternalLinkCommand : Command<ExternalLinkOptions>
	{

		#region Fields: Private

		private readonly RegAppCommand _regCommand;
		private Uri _clioUri;

		#endregion

		#region Constructors: Public

		public ExternalLinkCommand(RegAppCommand regCommand)
		{
			_regCommand = regCommand;
		}

		#endregion

		#region Methods: Public

		/// <summary>
		/// to test execute in command line
		/// clio-dev externalLink --content clio://?protocol=https:&host=129117-crm-bundle.creatio.com&name=vscode&clientId=83B03D807E3DEEAEF6A55D8CB587E191&clientSecret=C6EA75A49446A63F239BEB4C89892A610E638063AC298EEAF6786E309E06970C
		/// Make sur  to call clio register before testing, see reg/clio_context_menu_win.reg Lines 20-24 (protocol registration)
		/// </summary>
		/// <param name="options"></param>
		/// <returns></returns>
		public override int Execute(ExternalLinkOptions options)
		{

			if (!IsLinkValid(options.Content))
				return 0;

			NameValueCollection clioParams = System.Web.HttpUtility.ParseQueryString(_clioUri.Query);

#if DEBUG
			Console.WriteLine("clio was called with:");
			for (var i = 0; i < clioParams.Count; i++)
			{
				var key = clioParams.Keys[i];
				var value = clioParams.GetValues(i)?[0];
				Console.WriteLine($"\t{key} - {value}");
			}
#endif

			//TODO: change JS to merge protocol and host into one param
			var baseUrl = $"{clioParams["protocol"]}//{clioParams["host"]}";

			//TODO: Pass OAuth20IdentityServerUrl SysSetting instead of guessing it
			var authUrl = $"{clioParams["protocol"]}//{clioParams["host"].Replace(".creatio.com", "-is.creatio.com/connect/token")}";

			RegAppOptions opt = new RegAppOptions
			{
				IsNetCore = false,                                      //In OAuthClientAppPage check if this.window.location.pathname starts with /0
				ClientId = clioParams["clientId"],
				ClientSecret = clioParams["clientSecret"],
				Uri = baseUrl,
				Name = clioParams["name"],                          //Probably needs a unique name across all environments (may be combine baseUrl and name)
				AuthAppUri = authUrl,
				Login = string.Empty,
				Password = string.Empty,
				Maintainer = "Customer"                                 //Should pass in deepLink as arg Maintainer (SysSetting.Maintainer)
			};

			_regCommand.Execute(opt);

#if DEBUG
			Console.ReadLine();
#endif

			return 0;
		}
		#endregion

		#region Methods: Private

		/// <summary>
		/// Check if Uri is valid
		/// </summary>
		/// <param name="content">URI to validate</param>
		/// <returns>true when Uri parses correctly, otherwise false</returns>
		private bool IsLinkValid(string content)
		{
			if (Uri.TryCreate(content, UriKind.Absolute, out _clioUri))
			{
				if (_clioUri.Scheme != "clio")
				{
					Console.Error.WriteLine("ERROR (UriScheme) - Not a clio URI");
					return false;
				}
			}
			else
			{
				Console.Error.WriteLine("ERROR (Uri) - Clio URI cannot be empty");
				return false;
			}
			return true;
		}
		#endregion
	}

	#endregion
}
