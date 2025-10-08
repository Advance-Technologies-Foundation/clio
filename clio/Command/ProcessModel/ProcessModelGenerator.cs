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

public class ProcessModelGenerator(
	ILogger logger
	, IApplicationClient applicationClient
	, IDataProvider dataProvider
	, IServiceUrlBuilder serviceUrlBuilder)
	: IProcessModelGenerator{
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
	
	private ErrorOr<ProcessModel> GetProcessIdFromName(string processCode) {
		
		string currentStep = string.Empty;
		try {
			currentStep = "AppDataContext";
			IAppDataContext ctx = AppDataContextFactory.GetAppDataContext(dataProvider);
			
			currentStep = "QueryCreatio";
			VwProcessLib processLibItem = ctx.Models<VwProcessLib>()
				.FirstOrDefault(p => p.Name == processCode);
			
			if (processLibItem is null) {
				return Error.Failure("GetProcessIdFromName", $"Could not find process with name:{processCode}");
			}

			if (processLibItem.Id == Guid.Empty || string.IsNullOrWhiteSpace(processLibItem.Caption) ) {
				string message =  processLibItem.Id == Guid.Empty ? "Empty Id" : 
					string.IsNullOrWhiteSpace(processLibItem.Caption) ? "Empty Caption" : "Unknown error"; 
				return Error.Failure("GetProcessIdFromName", $"Error at step: {currentStep}. Process with name:{processCode} has invalid data. {message}");	
			}
			
			return new ProcessModel(processLibItem.Id, processCode) {
				Name = processLibItem.Caption,
			};
		}
		catch (Exception e) {
			return Error.Failure("GetProcessIdFromName", $"Error at step: {currentStep}. {e.Message}");
		}
	}
	
}