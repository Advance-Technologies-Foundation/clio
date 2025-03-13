using System.Diagnostics;
using System.Runtime.CompilerServices;
using clioAgent.Handlers.ChainLinks;

namespace clioAgent.ChainOfResponsibility;

/// <summary>
///  Base class for chain links that simplifies implementation.
/// </summary>
/// <typeparam name="TRequest">The type of request being processed.</typeparam>
/// <typeparam name="TResponse">The type of response returned after processing.</typeparam>
public abstract class AbstractChainLink<TRequest, TResponse> : IChainLink<TRequest, TResponse> where TRequest : IRequestWithActivity{

	#region Methods: Protected

	
    /// <summary>
    ///  Processes the request.
    /// </summary>
    /// <param name="request">The request to process.</param>
    /// <returns>The response after processing.</returns>
    protected abstract Task<TResponse> ProcessAsync(TRequest request);

    /// <summary>
    ///  Determines whether processing should continue to the next link.
    /// </summary>
    /// <param name="request">The original request.</param>
    /// <param name="response">The response after processing.</param>
    /// <returns>True if processing should continue, false otherwise.</returns>
    protected virtual bool ShouldContinue(TRequest request, TResponse response) => true;

    /// <summary>
    ///  Determines whether this link should process the request.
    /// </summary>
    /// <param name="request">The request to check.</param>
    /// <returns>True if this link should process the request, false otherwise.</returns>
    protected virtual bool ShouldProcess(TRequest request) => true;

	#endregion

	#region Methods: Public

    /// <summary>
    ///  Executes this link's processing logic on the request.
    /// </summary>
    /// <param name="request">The request to process.</param>
    /// <param name="next">The next link in the chain, or null if this is the last link.</param>
    /// <returns>The response after processing.</returns>
    public async Task<TResponse> ExecuteAsync(TRequest request, IChainLink<TRequest, TResponse> next){
		// Check if this link should process the request
		if (!ShouldProcess(request)) {
			// Skip to the next link if it exists
			return next != null
				? await next.ExecuteAsync(request, null)
				: default;
		}
		ActivityContext = request.ActivityContext;
		// Process the request
		TResponse response = await ExecuteWithTraceAsync(ProcessAsync, request);

		// Check if processing should continue
		if (ShouldContinue(request, response) && next != null) {
			// Call the next link
			return await next.ExecuteAsync(request, null);
		}

		// Return the response
		return response;
	}

	public event EventHandler<JobStatusChangedEventArgs>? JobStatusChanged;
	public void OnJobStatusChanged(JobStatusChangedEventArgs e){
		JobStatusChanged?.Invoke(this, e);
	}
	
	public Guid JobId { get; set; }
	protected ActivityContext ActivityContext { get; set; }

	protected virtual string HandlerName => GetType().Name;
	protected virtual Dictionary<string, object> Tags { get; } = new ();
	
	Action<Guid> StartActivity =>(stepId) => OnJobStatusChanged(new JobStatusChangedEventArgs(JobId) {
		CurrentStatus = Status.Started,
		Message = $"{HandlerName} - {Status.Started.ToString()}",
		StepId = stepId
	});
	Action<Guid, Activity?> CompleteActivity =>(stepId, activity) => {
		OnJobStatusChanged(new JobStatusChangedEventArgs(JobId) {
			CurrentStatus = Status.Completed,
			Message = $"{HandlerName} - {Status.Completed.ToString()}",
			StepId = stepId
		});
		activity?.SetStatus(ActivityStatusCode.Ok);
	};
	Action<Guid, Activity?> ErrorActivity =>(stepId, activity) => {
		OnJobStatusChanged(new JobStatusChangedEventArgs(JobId) {
			CurrentStatus = Status.Failed,
			Message = $"{HandlerName} - {Status.Failed.ToString()}",
			StepId = stepId
		});
		activity?.SetStatus(ActivityStatusCode.Error);
	};
	
	private async Task<TResponse> ExecuteWithTraceAsync(Func<TRequest, Task<TResponse>> action, 
		TRequest request){
		Guid stepId = Guid.NewGuid();
		Activity? activity = CreateActivity(Tags, HandlerName);
		activity?.Start();
		try {
			StartActivity(stepId);
			TResponse result = await action.Invoke(request);
			CompleteActivity(stepId, activity);
			return result;
		}
		catch (Exception e) {
			ErrorActivity(stepId, activity);
		}
		finally {
			activity?.Dispose();
		}
		return default(TResponse);
	}
	
	private ActivitySource ActivitySource => new(GetType().Name);
	private Activity? ExecuteActivity => ActivitySource.CreateActivity(GetType().Name, ActivityKind.Internal, ActivityContext);
	private Activity? CreateActivity(Dictionary<string, object>? tags = null, string methodName = ""){
		return tags switch {
					null => ActivitySource.CreateActivity(
						methodName,
						ActivityKind.Internal,
						ExecuteActivity?.Context ?? ActivityContext),
					var _ => ActivitySource.CreateActivity(
						methodName,
						ActivityKind.Internal,
						ExecuteActivity?.Context ?? ActivityContext,
						tags!)
				};
	}

	
	#endregion

}
