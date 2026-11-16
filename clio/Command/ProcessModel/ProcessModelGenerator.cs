using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ATF.Repository;
using ATF.Repository.Providers;
using Clio.Common;
using Clio.CreatioModel;
using Creatio.Client;
using ErrorOr;
using Terrasoft.Common;

namespace Clio.Command.ProcessModel;

public interface IProcessModelGenerator{
	public ErrorOr<ProcessModel> Generate(GenerateProcessModelCommandOptions options);

	/// <summary>
	/// Reads the flow elements of one process schema by its UId, without resolving a name, in ONE request bounded by
	/// <paramref name="timeoutMs"/>.
	/// </summary>
	/// <param name="schemaUId">The process schema UId, for example the one a sub-process element calls.</param>
	/// <param name="timeoutMs">What the request may spend, in milliseconds; no retry follows a failure.</param>
	/// <returns>
	/// The schema's top-level flow elements, nested ones inside their containers (empty when it has none), or the error
	/// the read hit. Captions are not filled.
	/// </returns>
	public ErrorOr<IReadOnlyList<FlowElement>> ReadFlowElements(Guid schemaUId, int timeoutMs);
}

public class ProcessModelGenerator(ILogger logger, IApplicationClient applicationClient, IDataProvider dataProvider
	, IServiceUrlBuilder serviceUrlBuilder) : IProcessModelGenerator{
	public ErrorOr<ProcessModel> Generate(GenerateProcessModelCommandOptions options) {

		ErrorOr<ProcessModel> pm = GetProcessIdFromName(options.Code);
		if (pm.IsError) {
			return pm;
		}

		ErrorOr<string> jsonResponse = GetProcessSchema(pm.Value.Id);
		if (jsonResponse.IsError) {
			return jsonResponse.Errors;
		}

		if (string.IsNullOrWhiteSpace(jsonResponse.Value)) {
			return Error.Failure("Generate", "Empty response");
		}
		
		
		ErrorOr<ProcessSchemaResponse> schema = ProcessSchemaResponse.FromJson(jsonResponse.Value, logger);
		if (schema.IsError) {
			return schema.Errors;
		}

		
		
		string description = string.Empty;
		bool? isDescription = schema.Value.Schema?.Description?.TryGetValue(options.Culture, out description);
		if (isDescription.HasValue && isDescription.Value && !string.IsNullOrWhiteSpace(description)) {
			pm.Value.Description = description;
		}

		pm.Value.Parameters = schema.Value.Schema?.MetaDataSchema.Parameters;
		pm.Value.FlowElements = schema.Value.Schema?.MetaDataSchema.FlowElements;
		return pm;
	}

	/// <inheritdoc />
	public ErrorOr<IReadOnlyList<FlowElement>> ReadFlowElements(Guid schemaUId, int timeoutMs) {
		ErrorOr<string> jsonResponse = GetProcessSchema(schemaUId, Math.Max(1, timeoutMs), maxAttempts: 1);
		if (jsonResponse.IsError) {
			return jsonResponse.Errors;
		}
		if (string.IsNullOrWhiteSpace(jsonResponse.Value)) {
			return Error.Failure("ReadFlowElements", "Empty response");
		}
		ProcessSchemaResponse schema;
		try {
			// Plain deserialization: the caption fill of FromJson warns for every element without one.
			schema = System.Text.Json.JsonSerializer.Deserialize<ProcessSchemaResponse>(jsonResponse.Value);
		}
		catch (System.Text.Json.JsonException e) {
			return Error.Failure("ReadFlowElements", $"Error deserializing process schema: {e.Message}");
		}
		if (schema?.Schema?.MetaDataSchema is not { } metaData) {
			return Error.Failure("ReadFlowElements", $"Process schema {schemaUId} carried no metadata");
		}
		return metaData.FlowElements ?? [];
	}

	private ErrorOr<string> GetProcessSchema(Guid processUId, int timeoutMs = 10_000, int maxAttempts = 3) {

		string currentStep = string.Empty;
		try {
			currentStep = "BuildRoute";
			string route = serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.ProcessSchemaRequest);
			
			currentStep = "CreatePayload";
			ProcessSchemaRequest payload = new (processUId);
			string payloadJson = payload.ToString();
			
			currentStep = "ExecuteRequest";
			return applicationClient.ExecutePostRequest(route, payloadJson, timeoutMs, maxAttempts, 1);
		}
		catch (Exception e) {
			return Error.Failure("GetProcessSchema", $"Error at step: {currentStep}. {e.Message}");
		}
	}
	
	private ErrorOr<ProcessModel> GetProcessIdFromName(string processNameOrCaption) {

		string currentStep = string.Empty;
		try {
			currentStep = "AppDataContext";
			IAppDataContext ctx = AppDataContextFactory.GetAppDataContext(dataProvider);

			currentStep = "QueryCreatio";
			// Data access: exact Name match first; the Caption fallback (what a no-code user types)
			// is queried only when there is no Name match. Selection/ambiguity logic lives
			// in the unit-testable ProcessLibResolver.
			VwProcessLib byName = ctx.Models<VwProcessLib>()
				.FirstOrDefault(p => p.Name == processNameOrCaption);
			List<VwProcessLib> byCaption = byName is null
				? ctx.Models<VwProcessLib>().Where(p => p.Caption == processNameOrCaption).ToList()
				: [];

			ErrorOr<VwProcessLib> resolved = ProcessLibResolver.Resolve(processNameOrCaption, byName, byCaption);
			if (resolved.IsError) {
				return resolved.Errors;
			}
			VwProcessLib processLibItem = resolved.Value;

			if (processLibItem.Id == Guid.Empty || string.IsNullOrWhiteSpace(processLibItem.Caption) ) {
				string message =  processLibItem.Id == Guid.Empty ? "Empty Id" :
					string.IsNullOrWhiteSpace(processLibItem.Caption) ? "Empty Caption" : "Unknown error";
				return Error.Failure("GetProcessIdFromName", $"Error at step: {currentStep}. Process '{processNameOrCaption}' has invalid data. {message}");
			}

			// Code is always the resolved system Name (process code), even when the caller passed a caption,
			// so callers can put the correct code into the run-process button's processName.
			return new ProcessModel(processLibItem.Id, processLibItem.Name) {
				Name = processLibItem.Caption,
			};
		}
		catch (Exception e) {
			return Error.Failure("GetProcessIdFromName", $"Error at step: {currentStep}. {e.Message}");
		}
	}
	
}