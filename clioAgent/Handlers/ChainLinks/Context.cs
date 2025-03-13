using System.Diagnostics;
using System.IO.Abstractions;
using ErrorOr;

namespace clioAgent.Handlers.ChainLinks;


public interface IRequestWithActivity {

	ActivityContext ActivityContext { get; set; }

}

public class RequestContext : SharedItems, IRequestWithActivity {

	public RequestContext(string creatioBuildZipPath, string dbName, bool force, ActivityContext activityContext){
		ActivityContext = activityContext;
		CreatioBuildZipPath = creatioBuildZipPath;
		DbName = dbName;
		Force = force;
	}

	public ActivityContext ActivityContext { get; set;}

	public string CreatioBuildZipPath { get; }
	public string DbName { get; }
}

public class ResponseContext : SharedItems{
	public ErrorOr<Success> Result { get; set; }
}

public abstract class SharedItems {

	public string TemplateName { get; set; } = string.Empty;
	public IFileSystemInfo? ZipFile { get; set; }
	public IDirectoryInfo? UnzippedDirectory { get; set; }
	public bool IsDbCreated { get; set; }
	public DbSettings? DbSettings { get; set; }
	public bool Force { get; set; }
}

public sealed record DbSettings(string Server, int Port, string Password, string UserId);
