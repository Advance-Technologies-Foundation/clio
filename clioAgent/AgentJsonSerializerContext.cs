using System.Collections.Concurrent;
using System.Text.Json.Serialization;
using clioAgent.AuthPolicies;
using clioAgent.EndpointDefinitions;
using clioAgent.Handlers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace clioAgent;

[JsonSerializable(typeof(DbServer))]
[JsonSerializable(typeof(Db))]
[JsonSerializable(typeof(CreatioProducts))]
[JsonSerializable(typeof(Logging))]
[JsonSerializable(typeof(Settings))]
[JsonSerializable(typeof(TraceServer))]
[JsonSerializable(typeof(ProblemDetails))]
[JsonSerializable(typeof(CustomProblem))]
[JsonSerializable(typeof(AuthorizationFailureReason))]
[JsonSerializable(typeof(Dictionary<string,object?>))]
[JsonSerializable(typeof(IDictionary<string,object?>))]
[JsonSerializable(typeof(Dictionary<string,object>))]
[JsonSerializable(typeof(IDictionary<string,object>))]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(string[]))]
[JsonSerializable(typeof(object))]
[JsonSerializable(typeof(int))]
[JsonSerializable(typeof(long))]
[JsonSerializable(typeof(List<string>))]
[JsonSerializable(typeof(Guid))]
[JsonSerializable(typeof(IEnumerable<string>))]
[JsonSerializable(typeof(BaseJob<IHandler>))]
[JsonSerializable(typeof(RestoreDbResponse))]
[JsonSerializable(typeof(ConcurrentDictionary<Guid, JobStatus>))]
[JsonSerializable(typeof(ConcurrentQueue<BaseJob<IHandler>>))]
[JsonSerializable(typeof(Status))]
[JsonSerializable(typeof(DateTime))]
[JsonSerializable(typeof(StepStatus))]
[JsonSerializable(typeof(IEnumerable<StepStatus>))]
[JsonSerializable(typeof(ErrorOr.Error))]
public partial class AgentJsonSerializerContext : JsonSerializerContext
{
}


public enum Status {

	Pending,
	Completed,
	Started,
	Failed
}