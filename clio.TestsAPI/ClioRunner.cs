using System.Diagnostics;
using System.Reflection;
using NUnit.Framework;

namespace clio.ApiTest;


public interface ICLioRunner
{

	public string RunClioCommand(string commandName, string clioArgs, string workingDirectory = "", Dictionary<string,string> envVariables = null);

}

public class ClioRunner : ICLioRunner
{

	#region Fields: Private

	private readonly AppSettings _appSettings;

	#endregion

	#region Constructors: Public

	public ClioRunner(AppSettings appSettings){
		_appSettings = appSettings;
	}

	#endregion

	#region Methods: Public

	public string RunClioCommand(string commandName, string clioArgs, string workingDirectory = "", Dictionary<string,string> envVariables = null){
		string mainLocation = Assembly.GetExecutingAssembly().Location;
		string mainLocationDirPath = Path.GetDirectoryName(mainLocation);
		string clioDevPath = Path.Combine(mainLocationDirPath, "..", "..", "..", "..", "clio", "bin", "Debug", "net6.0",
			"clio.exe");

		string url = _appSettings.URL;
		string username = _appSettings.LOGIN;
		string password = _appSettings.PASSWORD;
		bool isNetCore = _appSettings.IS_NETCORE;
		string envArgs = $"-u {url} -l {username} -p {password} -i {isNetCore}";

		ProcessStartInfo psi = new() {
			FileName = clioDevPath,
			Arguments = $"{commandName} {clioArgs} {envArgs}",
			RedirectStandardOutput = true,
			RedirectStandardError = true, 
		};
		
		if(!string.IsNullOrWhiteSpace(workingDirectory)) {
			psi.WorkingDirectory = workingDirectory;
		}
		
		if(envVariables != null) {
			foreach (KeyValuePair<string,string>kvp in envVariables) {
				psi.EnvironmentVariables.Add(kvp.Key, kvp.Value);
			}
		}
		
		
		
		Process? process = Process.Start(psi);
		if (process is null) {
			Assert.Fail("Could not start clio-dev process");
		}
		process!.WaitForExit();
		return process.StandardOutput.ReadToEnd();
	}

	#endregion

}