using System.Threading;
using System.Threading.Tasks;
using MediatR;
using OneOf;

namespace Clio.Common.ScenarioHandlers;

public class TestRequest : BaseHandlerRequest
{ }

public class TestResponse : BaseHandlerResponse
{ }

internal class TestRequestHandler : IRequestHandler<TestRequest, OneOf<BaseHandlerResponse, HandlerError>>
{

    #region Methods: Public

    public async Task<OneOf<BaseHandlerResponse, HandlerError>> Handle(TestRequest request,
        CancellationToken cancellationToken)
    {
        return new TestResponse
        {
            Status = BaseHandlerResponse.CompletionStatus.Success
        };
    }

    #endregion

}
