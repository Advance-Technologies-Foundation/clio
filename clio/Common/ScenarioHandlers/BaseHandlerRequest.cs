using MediatR;
using OneOf;
using System.Collections.Generic;

namespace Clio.Common.ScenarioHandlers {
    public class BaseHandlerRequest : IRequest<OneOf<BaseHandlerResponse, HandlerError>> {
        public Dictionary<string, string> Arguments { get; set; }
    }
}
