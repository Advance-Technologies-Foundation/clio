using Clio.Command;
using MediatR;
using System.Threading;
using System.Threading.Tasks;

namespace Clio.Requests
{
	public class RegisterOAuthCredentials : IExtenalLink
	{
		public string Content
		{
			get; set;
		}
	}

	/// <summary>
	/// Handles clio links externalLink clio://RegisterOAuthCredentials?protocol=https:&host=129117-crm-bundle.creatio.com&name=vscode&clientId=83B03D807E3DEEAEF6A55D8CB587E191&clientSecret=C6EA75A49446A63F239BEB4C89892A610E638063AC298EEAF6786E309E06970C
	/// </summary>
	public class RegisterOAuthCredentialsHandler : BaseExternalLinkHandler, IRequestHandler<RegisterOAuthCredentials>
	{
		private readonly RegAppCommand _regCommand;

		public RegisterOAuthCredentialsHandler(RegAppCommand regCommand)
		{
			_regCommand = regCommand;
		}


		public Task<Unit> Handle(RegisterOAuthCredentials request, CancellationToken cancellationToken)
		{

			if (!IsLinkValid(request.Content))
				return Unit.Task;

#if (DEBUG)
			PrintArguments();
#endif

			//TODO: change JS to merge protocol and host into one param
			string baseUrl = $"{clioParams["protocol"]}//{clioParams["host"]}";

			//TODO: Pass OAuth20IdentityServerUrl SysSetting instead of guessing it
			string authUrl = $"{clioParams["protocol"]}//{clioParams["host"].Replace(".creatio.com", "-is.creatio.com/connect/token")}";

			RegAppOptions opt = new RegAppOptions
			{
				IsNetCore = false,                                      //In OAuthClientAppPage check if this.window.location.pathname starts with /0
				ClientId = clioParams["clientId"],
				ClientSecret = clioParams["clientSecret"],
				Uri = baseUrl,
				Name = clioParams["name"].Replace(" ", "-"),                          //Probably needs a unique name across all environments (may be combine baseUrl and name)
				AuthAppUri = authUrl,
				Login = string.Empty,
				Password = string.Empty,
				Maintainer = "Customer"                                 //Should pass in deepLink as arg Maintainer (SysSetting.Maintainer)
			};

			_regCommand.Execute(opt);
			return Unit.Task;
		}
	}
}
