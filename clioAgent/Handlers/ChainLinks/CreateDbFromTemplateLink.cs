using System.Diagnostics;
using System.IO.Abstractions;
using System.Text;
using clioAgent.ChainOfResponsibility;
using clioAgent.DbOperations;
using clioAgent.Providers;
using ErrorOr;

namespace clioAgent.Handlers.ChainLinks;

[Traceabale]
public class CreateDbFromTemplateLink(IPostgres postgres) : AbstractChainLink<RequestContext, ResponseContext> {

	private IInitializedPostgres _pg;
	protected override bool ShouldContinue(RequestContext request, ResponseContext response) => 
		!response.Result.IsError;
	protected override bool ShouldProcess(RequestContext request){
		if(!postgres.IsInitialized) {
			var p = postgres.Init();
			if(p.IsError) {
				return false;
			}
			_pg = p.Value;
		}
		_pg = postgres as IInitializedPostgres;
		if(request.UnzippedDirectory is null || !request.UnzippedDirectory.Exists) {
			return false;
		}
		
		return !string.IsNullOrWhiteSpace(request.DbName) && !string.IsNullOrWhiteSpace(request.TemplateName);
	}

	protected override async Task<ResponseContext> ProcessAsync(RequestContext request) => 
		(await CreateDbFromTemplateAsync(request.TemplateName, request.DbName, request.Force))
			.Match<ResponseContext>(
				onValue: success => new ResponseContext {
					Result = Result.Success,
				},
				onError: err => new ResponseContext {
					Result = err
				});

	private async Task<ErrorOr<Success>> CreateDbFromTemplateAsync(string templateDbName, string dbName, bool force){
		bool dbCreated = await _pg.CreateDbFromTemplateAsync(templateDbName, dbName, force);
		return dbCreated 
			? Result.Success 
			: Error.Failure("RestoreDb.CreateDbFromTemplateAsync", "Failed to create database");
	}


}
