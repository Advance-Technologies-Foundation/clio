using System;
using System.Threading;
using System.Threading.Tasks;
using Clio.Command;
using MediatR;

namespace Clio.Requests;

public class Restart : IExternalLink
{

    #region Properties: Public

    public string Content { get; set; }

    #endregion

}

/// <summary>
///     Restarts environment
/// </summary>
/// <remarks>
///     Handles requests received via
///     clio://Restart/?environmentName=bundle8055
/// </remarks>
internal class RestartHandler : BaseExternalLinkHandler, IRequestHandler<Restart>
{

    #region Fields: Private

    private readonly RestartCommand _restartCommand;

    #endregion

    #region Constructors: Public

    public RestartHandler(RestartCommand restartCommand)
    {
        _restartCommand = restartCommand;
    }

    #endregion

    #region Methods: Public

    public Task Handle(Restart request, CancellationToken cancellationToken)
    {
        Uri.TryCreate(request.Content, UriKind.Absolute, out _clioUri);
        RestartOptions opt = new()
        {
            Environment = ClioParams["environmentName"]
        };
        _restartCommand.Execute(opt);
        return Unit.Task;
    }

    #endregion

}
