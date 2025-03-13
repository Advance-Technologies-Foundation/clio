using System.Diagnostics;
using clioAgent.ChainOfResponsibility;
using clioAgent.Handlers.ChainLinks;
using ErrorOr;

namespace clioAgent.Handlers;

[Traceabale]
public sealed class RestoreDbHandler(IChainHandler<RequestContext, ResponseContext> chainHandler) : BaseHandler {

	#region Methods: Protected

	protected override ErrorOr<Success> InternalExecute(Dictionary<string, object> commandObj,
		CancellationToken cancellationToken){
		commandObj.TryGetValue("CreatioBuildZipPath", out object? creatioBuildZipPathObj);
		commandObj.TryGetValue("Name", out object? nameObj);
		
		if ( string.IsNullOrEmpty(creatioBuildZipPathObj?.ToString())) {
			Error error = Error.Failure("RestoreDbHandler.CreatioBuildZipPathIsNull", "CreatioBuildZipPath is null");
			return error;
		}
		string creatioBuildZipPath = creatioBuildZipPathObj.ToString()!;

		if (nameObj == null || string.IsNullOrEmpty(nameObj.ToString())) {
			return Error.Failure("RestoreDbHandler.NameIsNull", "Name is null");
		}
		
		string name = nameObj.ToString()!;
		ActivityContext activityCtx = ActivityContext ?? new ActivityContext();
		RequestContext request = new (creatioBuildZipPath, name, true, activityCtx);
		
		ResponseContext chainResult = chainHandler.HandleAsync(request)
												.ConfigureAwait(false)
												.GetAwaiter().GetResult();
		return chainResult.Result.IsError
			? chainResult.Result
			: Result.Success;
	}

	#endregion
	

}
