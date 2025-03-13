using System.IO.Abstractions;
using clioAgent.ChainOfResponsibility;
using clioAgent.Providers;
using ErrorOr;

namespace clioAgent.Handlers.ChainLinks;

[Traceabale]
public class ResoreMsLink : AbstractChainLink<RequestContext, ResponseContext> {

	protected override bool ShouldContinue(RequestContext request, ResponseContext response) => 
		!response.Result.IsError;
	protected override bool ShouldProcess(RequestContext request){
		return false;
		if(request.UnzippedDirectory is null || !request.UnzippedDirectory.Exists) {
			return false;
		}
		
		IFileInfo[] backupFiles = request.UnzippedDirectory
										.GetFiles("*.*", SearchOption.AllDirectories)
										.Where(f => 
											f.Name.EndsWith(WorkingDirectoryProvider.MsSqlBackupExtension, StringComparison.OrdinalIgnoreCase) ||
											f.Name.EndsWith(WorkingDirectoryProvider.PgBackupExtension, StringComparison.OrdinalIgnoreCase))
										.ToArray();
		
		bool isMs = backupFiles.Any(f => f.Name.EndsWith(WorkingDirectoryProvider.MsSqlBackupExtension, StringComparison.OrdinalIgnoreCase));
		return isMs;
	}

	protected override Task<ResponseContext> ProcessAsync(RequestContext request) => 
		CreateMsDatabase(request.ZipFile!)
			.Match<Task<ResponseContext>>(
				onValue: success => Task.FromResult(new ResponseContext {
					Result = Result.Success,
				}),
				onError: err => Task.FromResult(new ResponseContext {
					Result = err
				}));
	
	private ErrorOr<Success> CreateMsDatabase(IFileSystemInfo zipFile){
		throw new NotImplementedException("CreateMsDatabase not implemented");
	}
}
