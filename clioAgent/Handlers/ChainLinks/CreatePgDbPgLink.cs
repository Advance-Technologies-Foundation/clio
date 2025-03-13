using clioAgent.ChainOfResponsibility;
using clioAgent.DbOperations;
using clioAgent.Providers;
using ErrorOr;

namespace clioAgent.Handlers.ChainLinks;

[Traceabale]
public class CreatePgDbPgLink(IPostgres postgres) : AbstractChainLink<RequestContext, ResponseContext> {

	private IInitializedPostgres? _pg;
	protected override bool ShouldProcess(RequestContext request){
		if(!postgres.IsInitialized) {
			ErrorOr<IInitializedPostgres> isInit = postgres.Init();
			if(isInit.IsError) {
				return false;
			}
			
			_pg = isInit.Value;
		}
		_pg = postgres as IInitializedPostgres;
		if(request.UnzippedDirectory is null || !request.UnzippedDirectory.Exists) {
			return false;
		}
		return request.UnzippedDirectory
					.GetFiles($"*{WorkingDirectoryProvider.PgBackupExtension}", SearchOption.AllDirectories)
					.Length != 0;
	}

	protected override bool ShouldContinue(RequestContext request, ResponseContext response) => 
		!response.Result.IsError;

	protected override async Task<ResponseContext> ProcessAsync(RequestContext request) => 
		(await CreatePgDatabaseAsync(Path.GetFileNameWithoutExtension(request.ZipFile!.Name)))
			.Match<ResponseContext>(
				
				onValue: success => {
					request.TemplateName = success;
					return new ResponseContext {
						Result = Result.Success,
						IsDbCreated = true,
						TemplateName = success
					};
				},
				onError: err => new ResponseContext {
					Result = err
				});
	private async Task<ErrorOr<string>> CreatePgDatabaseAsync(string zipFileName){
		if(_pg is null) {
			return Error.Failure("RestoreDb.DbSettingsNotFound", "Db settings not found");
		}
		var templateName = await _pg.FindTemplateDbByCommentAsync(zipFileName);
		if(string.IsNullOrWhiteSpace(templateName)) {
			templateName = Guid.CreateVersion7().ToString();
			await _pg.CreateDbAsync(templateName);
			await _pg.SetCommentOnDbAsync(templateName, zipFileName);
		}
		return templateName;
	}
}
