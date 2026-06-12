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
		return pm;
	}

	private ErrorOr<string> GetProcessSchema(Guid processUId) {

		string currentStep = string.Empty;
		try {
			currentStep = "BuildRoute";
			string route = serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.ProcessSchemaRequest);
			
			currentStep = "CreatePayload";
			ProcessSchemaRequest payload = new (processUId);
			string payloadJson = payload.ToString();
			
			currentStep = "ExecuteRequest";
			return applicationClient.ExecutePostRequest(route, payloadJson, 10_000, 3, 1);
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
			// Data access: exact Name match first; the Caption fallback (what a no-code user types,
			// ENG-91168) is queried only when there is no Name match. Selection/ambiguity logic lives
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