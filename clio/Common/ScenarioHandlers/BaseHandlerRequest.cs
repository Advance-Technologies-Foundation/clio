using System.Collections.Generic;
using MediatR;
using OneOf;

namespace Clio.Common.ScenarioHandlers;

public class BaseHandlerRequest : IRequest<OneOf<BaseHandlerResponse, HandlerError>>
{
    public Dictionary<string, string> Arguments { get; set; }
}
