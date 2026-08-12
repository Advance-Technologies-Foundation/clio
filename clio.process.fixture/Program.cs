using System.Diagnostics;
using System.Text.Json;
using Clio.ProcessFixture;

const string holdPipesArgument = "--hold-inherited-pipes";
const string spawnDescendantArgument = "--spawn-inherited-handle-descendant";
const string growDirectoryArgument = "--grow-directory-with-inherited-pipes";
const string spawnGrowingDescendantArgument = "--spawn-growing-inherited-handle-descendant";
const string overflowOutputArgument = "--overflow-output-with-inherited-handle-descendant";
const string carriageReturnOutputArgument = "--write-carriage-return-output";
const string invocationMarkerFileName = "invoked.marker";
const string descendantIdentityFileName = "descendant.identity";

if (args.Length == 1 && string.Equals(args[0], holdPipesArgument, StringComparison.Ordinal)) {
	await Task.Delay(TimeSpan.FromSeconds(30));
	return 0;
}

if (args.Length == 2 && string.Equals(args[0], growDirectoryArgument, StringComparison.Ordinal)) {
	await Task.Delay(TimeSpan.FromMilliseconds(300));
	Directory.CreateDirectory(args[1]);
	await File.WriteAllBytesAsync(Path.Combine(args[1], "late-growth.bin"), new byte[4096]);
	await Task.Delay(TimeSpan.FromSeconds(30));
	return 0;
}

if (args.Length == 2 && string.Equals(args[0], spawnDescendantArgument, StringComparison.Ordinal)) {
	using Process descendant = StartPipeHoldingDescendant();
	await WriteProcessIdentityAsync(args[1], descendant);
	await WriteOutputAsync("parent-exited");
	return 0;
}

if (args.Length == 3 && string.Equals(args[0], spawnGrowingDescendantArgument, StringComparison.Ordinal)) {
	using Process descendant = StartPipeHoldingDescendant(growDirectoryArgument, args[2]);
	await WriteProcessIdentityAsync(args[1], descendant);
	await WriteOutputAsync("parent-exited");
	return 0;
}

if (args.Length == 2 && string.Equals(args[0], overflowOutputArgument, StringComparison.Ordinal)) {
	using Process descendant = StartPipeHoldingDescendant();
	await WriteProcessIdentityAsync(args[1], descendant);
	await WriteOutputAsync(new string('x', 8192));
	return 0;
}

if (args.Length == 1 && string.Equals(args[0], carriageReturnOutputArgument, StringComparison.Ordinal)) {
	await WriteOutputAsync("first\r");
	await Task.Delay(TimeSpan.FromSeconds(1));
	await WriteOutputAsync("second");
	return 0;
}

string fixtureDirectory = AppContext.BaseDirectory;
await File.WriteAllTextAsync(Path.Combine(fixtureDirectory, invocationMarkerFileName),
	string.Join(' ', args));
using Process gitDescendant = StartPipeHoldingDescendant();
await WriteProcessIdentityAsync(Path.Combine(fixtureDirectory, descendantIdentityFileName), gitDescendant);
await WriteOutputAsync("fake-git-parent-exited");
return 1;

static Process StartPipeHoldingDescendant(params string[] arguments) {
	ProcessStartInfo descendantStartInfo = new(GetCurrentExecutablePath()) {
		UseShellExecute = false,
		CreateNoWindow = true
	};
	if (arguments.Length == 0) {
		descendantStartInfo.ArgumentList.Add(holdPipesArgument);
	} else {
		foreach (string argument in arguments) {
			descendantStartInfo.ArgumentList.Add(argument);
		}
	}
	return Process.Start(descendantStartInfo)
		?? throw new InvalidOperationException("The inherited-pipe fixture descendant did not start.");
}

static async Task WriteProcessIdentityAsync(string path, Process process) {
	ProcessIdentity identity = new(process.Id, process.StartTime.ToUniversalTime().Ticks,
		Path.GetFullPath(GetCurrentExecutablePath()));
	await File.WriteAllTextAsync(path, JsonSerializer.Serialize(identity));
}

static async Task WriteOutputAsync(string output) {
	await Console.Out.WriteAsync(output);
	await Console.Out.FlushAsync();
}

static string GetCurrentExecutablePath() => Environment.ProcessPath
	?? throw new InvalidOperationException("The fixture executable path is unavailable.");
