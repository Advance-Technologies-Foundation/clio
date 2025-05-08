using System;
using System.Threading;
using System.Threading.Tasks;
using Clio.Command;
using MediatR;

namespace Clio.Requests;

public class RegisterOAuthCredentials : IExternalLink
{

    #region Properties: Public

    public string Content { get; set; }

    #endregion

}

/// <summary>
///     Handles clio links externalLink clio://RegisterOAuthCredentials?protocol=https:&host=129117-crm-bundle.creatio.com
///     &name=vscode&clientId=83B03D807E3DEEAEF6A55D8CB587E191&clientSecret
///     =C6EA75A49446A63F239BEB4C89892A610E638063AC298EEAF6786E309E06970C
/// </summary>
internal class RegisterOAuthCredentialsHandler : BaseExternalLinkHandler, IRequestHandler<RegisterOAuthCredentials>
{

    #region Fields: Private

    private readonly RegAppCommand _regCommand;

    #endregion

    #region Constructors: Public

    public RegisterOAuthCredentialsHandler(RegAppCommand regCommand)
    {
        _regCommand = regCommand;
    }

    #endregion

    #region Methods: Public

    public Task Handle(RegisterOAuthCredentials request, CancellationToken cancellationToken)
    {
        Uri.TryCreate(request.Content, UriKind.Absolute, out _clioUri);

        //TODO: change JS to merge protocol and host into one param
        string baseUrl = $"{ClioParams["protocol"]}//{ClioParams["host"]}";

        //TODO: Pass OAuth20IdentityServerUrl SysSetting instead of guessing it
        string authUrl
            = $"{ClioParams["protocol"]}//{ClioParams["host"].Replace(".creatio.com", "-is.creatio.com/connect/token")}";

        RegAppOptions opt = new()
        {
            IsNetCore = false, //In OAuthClientAppPage check if this.window.location.pathname starts with /0
            ClientId = ClioParams["clientId"],
            ClientSecret = ClioParams["clientSecret"],
            Uri = baseUrl,
            EnvironmentName
                = ClioParams["name"]
                    .Replace(" ",
                        "-"), //Probably needs a unique name across all environments (may be combine baseUrl and name)
            AuthAppUri = authUrl,
            Login = string.Empty,
            Password = string.Empty,
            Maintainer = "Customer" //Should pass in deepLink as arg Maintainer (SysSetting.Maintainer)
        };

        _regCommand.Execute(opt);
        return Unit.Task;
    }

    #endregion

}
