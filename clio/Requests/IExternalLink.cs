using MediatR;

namespace Clio.Requests;

internal interface IExternalLink : IRequest
{

    #region Properties: Public

    public string Content { get; set; }

    #endregion

}
