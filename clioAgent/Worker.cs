using System.Collections.Concurrent;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using clioAgent.Handlers;
using ErrorOr;
using Microsoft.Extensions.Options;

namespace clioAgent;

public class Worker {

	#region Fields: Private

	private readonly ConcurrentQueue<BaseJob<IHandler>> _jobs;
	private readonly ConcurrentDictionary<Guid, JobStatus> _statusBag;
	private readonly ConcurrentBag<JobStatus> _statusSteps;
	private readonly IValidateOptions<BaseJob<IHandler>> _validator;
	private readonly IServiceProvider _services;
	private readonly IList<IHandler> _handlers = [];

	#endregion

	#region Constructors: Public

	public Worker(ConcurrentQueue<BaseJob<IHandler>> jobs,
		ConcurrentDictionary<Guid, JobStatus> statusBag, ConcurrentBag<JobStatus> statusSteps,
		IValidateOptions<BaseJob<IHandler>> validator, IServiceProvider services){
		_jobs = jobs;
		_statusBag = statusBag;
		_statusSteps = statusSteps;
		_validator = validator;
		_services = services;
	}

	#endregion

	#region Methods: Private

	private void OnStatusChanged(object? sender, JobStatusChangedEventArgs e){
		_statusBag[e.JobId].CurrentStatus = e.CurrentStatus;
		_statusBag[e.JobId].Message = e.Error?.FirstOrDefault().Description;
		_statusSteps.Add(new JobStatus {
			JobId = e.JobId,
			CurrentStatus = e.CurrentStatus,
			Message = e.Error is null ? e.Message : e.Error.FirstOrDefault().Description,
			StepId = e.StepId
		});
	}

	#endregion

	#region Methods: Public
	
	[UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "The method requires reflection to validate BaseJob.")]
	[DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(BaseJob<IHandler>))]
	public ErrorOr<BaseJob<IHandler>> AddJobToQueue(ConcurrentQueue<BaseJob<IHandler>> jobs, Dictionary<string, object> commandObj, string handlerName){
		
		//TODO: Can we do better ?
		IHandler? handler = handlerName switch {
			"RestoreDb" => _services.GetRequiredService<RestoreDbHandler>(),
			"CreateSite" => _services.GetRequiredService<DeployIISHandler>(),
			var _ => null
		};
		
		BaseJob<IHandler> job = new() {
			CommandObj = commandObj,
			HandlerName = handlerName, 
			Handler = handler, 
			ActivityContext = Activity.Current?.Context ?? new ActivityContext()
		};
		ValidateOptionsResult validationResult = _validator.Validate(handlerName, job);
		if(validationResult.Failed) {
			List<Error> errors = [];
			errors.AddRange(validationResult.Failures!.Select(failure => Error.Validation("Validation", failure)));
			return errors;
		}
		jobs.Enqueue(job);
		return job;
	}

	public void Run(CancellationToken stoppingToken){
		while (!stoppingToken.IsCancellationRequested) {
			bool isJob = _jobs.TryDequeue(out BaseJob<IHandler>? job);
			if (job is not null) {
				_statusBag.TryAdd(job.Id, new JobStatus {
					CurrentStatus = Status.Pending,
					JobId = job.Id,
				});
			}

			if (isJob && job?.CommandObj is not null) {
				IHandler? handler = _services.GetRequiredService(job.Handler.GetType()) as IHandler;
				if(handler is null) {
					throw new Exception($"Handler {job.Handler.GetType()} not found");
				}
				_handlers.Add(handler);
				handler.Id = job.Id;
				handler.ActivityContext = job.ActivityContext;

				handler.JobStatusChanged += OnStatusChanged;
				ErrorOr<Success> result = handler.Execute(job.CommandObj, stoppingToken);

				if (result.IsError) {
					_statusBag[job.Id].CurrentStatus = Status.Failed;
					_statusBag[job.Id].Message = result.FirstError.Description;
					_statusSteps.Add(new JobStatus {
						JobId = job.Id,
						CurrentStatus = Status.Failed,
						Message = $"{Status.Failed} due to: {result.FirstError.Description}",
					});
				} else {
					_statusBag[job.Id].CurrentStatus = Status.Completed;
				}

				handler.JobStatusChanged -= OnStatusChanged;
				handler.Dispose();
				_handlers.Remove(handler);
			}
			Thread.Sleep(100);
		}
	}

	#endregion

}

public class JobStatus {

	#region Properties: Public

	public Status CurrentStatus { get; set; }

	public DateTime Date { get; } = DateTime.UtcNow;

	public Guid JobId { get; init; }

	public string? Message { get; set; }
	
	public Guid? StepId { get; set; }
	

	#endregion

}
