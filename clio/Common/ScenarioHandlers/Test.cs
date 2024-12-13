// using MediatR;
// using OneOf;
// using System.Threading;
// using System.Threading.Tasks;
//
// namespace Clio.Common.ScenarioHandlers {
//
//
//     public class TestRequest : BaseHandlerRequest {
//     }
//
//
//
//     public class TestResponse : BaseHandlerResponse {
//     }
//
//     internal class TestRequestHandler : IRequestHandler<TestRequest, OneOf<BaseHandlerResponse, HandlerError>> {
//
//
//         public Task<OneOf<BaseHandlerResponse, HandlerError>> Handle(TestRequest request, CancellationToken cancellationToken) {
//
//            
//             return Task.FromResult<OneOf<BaseHandlerResponse, HandlerError>>(new TestResponse() {
//                 Status = BaseHandlerResponse.CompletionStatus.Success
//             });
//
//         }
//     }
// }
