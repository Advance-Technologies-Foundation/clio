using System;
using System.Linq;
using ATF.Repository;
using ATF.Repository.Providers;
using Clio.Common;
using Clio.CreatioModel;
using ErrorOr;

namespace Clio.Command.ProcessModel;

/// <summary>Identifies a process by exactly one of code (Name), UId, or caption.</summary>
/// <param name="Code">Process code (schema Name), e.g. <c>UsrProcess_493d4c9</c>.</param>
/// <param name="UId">Process UId (GUID string).</param>
/// <param name="Caption">Process caption (display name).</param>
public sealed record ProcessIdentity(string Code, string UId, string Caption);

/// <summary>
/// Reads an existing process's full schema from a Creatio environment via the proven
/// <c>ProcessSchemaRequest</c> route, resolving its id from <c>VwProcessLib</c> by code/UId/caption.
/// </summary>
public interface IProcessSchemaReader {
	/// <summary>
	/// Resolves the process by the supplied identity and returns its parsed schema.
	/// </summary>
	/// <param name="identity">The process identity (exactly one of code/uid/caption populated).</param>
	/// <returns>The parsed schema, or an error (not found / unreachable / empty response).</returns>
	ErrorOr<ProcessSchemaResponse> Read(ProcessIdentity identity);
}

/// <inheritdoc cref="IProcessSchemaReader" />
public sealed class ProcessSchemaReader(ILogger logger, IApplicationClient applicationClient,
	IDataProvider dataProvider, IServiceUrlBuilder serviceUrlBuilder) : IProcessSchemaReader {

	/// <inheritdoc />
	public ErrorOr<ProcessSchemaResponse> Read(ProcessIdentity identity) {
		ErrorOr<Guid> processId = ResolveId(identity);
		if (processId.IsError) {
			return processId.Errors;
		}
		ErrorOr<string> jsonResponse = GetProcessSchema(processId.Value);
		if (jsonResponse.IsError) {
			return jsonResponse.Errors;
		}
		if (string.IsNullOrWhiteSpace(jsonResponse.Value)) {
			return Error.Failure("ReadSchema", "empty response from the server");
		}
		return ProcessSchemaResponse.FromJson(jsonResponse.Value, logger);
	}

	private ErrorOr<Guid> ResolveId(ProcessIdentity identity) {
		try {
			IAppDataContext ctx = AppDataContextFactory.GetAppDataContext(dataProvider);
			VwProcessLib row;
			if (!string.IsNullOrWhiteSpace(identity.Code)) {
				row = ctx.Models<VwProcessLib>().FirstOrDefault(p => p.Name == identity.Code);
			} else if (!string.IsNullOrWhiteSpace(identity.UId) && Guid.TryParse(identity.UId, out Guid uid)) {
				row = ctx.Models<VwProcessLib>().FirstOrDefault(p => p.UId == uid);
			} else if (!string.IsNullOrWhiteSpace(identity.Caption)) {
				row = ctx.Models<VwProcessLib>().FirstOrDefault(p => p.Caption == identity.Caption);
			} else {
				return Error.Failure("ResolveId", "no process identity provided (code, uid, or caption)");
			}

			if (row is null) {
				return Error.Failure("ResolveId", $"process not found ({Describe(identity)})");
			}
			if (row.Id == Guid.Empty) {
				return Error.Failure("ResolveId", $"process ({Describe(identity)}) has an empty Id");
			}
			return row.Id;
		} catch (Exception e) {
			return Error.Failure("ResolveId", e.Message);
		}
	}

	private ErrorOr<string> GetProcessSchema(Guid processUId) {
		string currentStep = string.Empty;
		try {
			currentStep = "BuildRoute";
			string route = serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.ProcessSchemaRequest);

			currentStep = "CreatePayload";
			ProcessSchemaRequest payload = new(processUId);
			string payloadJson = payload.ToString();

			currentStep = "ExecuteRequest";
			return applicationClient.ExecutePostRequest(route, payloadJson, 10_000, 3, 1);
		} catch (Exception e) {
			return Error.Failure("GetProcessSchema", $"Error at step: {currentStep}. {e.Message}");
		}
	}

	private static string Describe(ProcessIdentity identity) {
		if (!string.IsNullOrWhiteSpace(identity.Code)) {
			return $"code '{identity.Code}'";
		}
		if (!string.IsNullOrWhiteSpace(identity.UId)) {
			return $"uid '{identity.UId}'";
		}
		return $"caption '{identity.Caption}'";
	}
}
