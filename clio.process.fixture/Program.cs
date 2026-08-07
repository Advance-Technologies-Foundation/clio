using System.Diagnostics;
using System.Globalization;

const string holdPipesArgument = "--hold-inherited-pipes";
const string spawnDescendantArgument = "--spawn-inherited-handle-descendant";
const string overflowOutputArgument = "--overflow-output-with-inherited-handle-descendant";
const string carriageReturnOutputArgument = "--write-carriage-return-output";
const string invocationMarkerFileName = "invoked.marker";
const string descendantPidFileName = "descendant.pid";

if (args.Length == 1 && string.Equals(args[0], holdPipesArgument, StringComparison.Ordinal)) {
	await Task.Delay(TimeSpan.FromSeconds(30));
	return 0;
}

if (args.Length == 2 && string.Equals(args[0], spawnDescendantArgument, StringComparison.Ordinal)) {
	using Process descendant = StartPipeHoldingDescendant();
	await File.WriteAllTextAsync(args[1], descendant.Id.ToString(CultureInfo.InvariantCulture));
	Console.Out.Write("parent-exited");
	Console.Out.Flush();
	return 0;
}

if (args.Length == 2 && string.Equals(args[0], overflowOutputArgument, StringComparison.Ordinal)) {
	using Process descendant = StartPipeHoldingDescendant();
	await File.WriteAllTextAsync(args[1], descendant.Id.ToString(CultureInfo.InvariantCulture));
	Console.Out.Write(new string('x', 8192));
	Console.Out.Flush();
	return 0;
}

if (args.Length == 1 && string.Equals(args[0], carriageReturnOutputArgument, StringComparison.Ordinal)) {
	Console.Out.Write("first\r");
	Console.Out.Flush();
	await Task.Delay(TimeSpan.FromSeconds(1));
	Console.Out.Write("second");
	Console.Out.Flush();
	return 0;
}

string fixtureDirectory = AppContext.BaseDirectory;
await File.WriteAllTextAsync(Path.Combine(fixtureDirectory, invocationMarkerFileName),
	string.Join(' ', args));
using Process gitDescendant = StartPipeHoldingDescendant();
await File.WriteAllTextAsync(Path.Combine(fixtureDirectory, descendantPidFileName),
	gitDescendant.Id.ToString(CultureInfo.InvariantCulture));
Console.Out.Write("fake-git-parent-exited");
Console.Out.Flush();
return 1;

static Process StartPipeHoldingDescendant() {
	ProcessStartInfo descendantStartInfo = new(Environment.ProcessPath!) {
		UseShellExecute = false,
		CreateNoWindow = true
	};
	descendantStartInfo.ArgumentList.Add(holdPipesArgument);
	return Process.Start(descendantStartInfo)
		?? throw new InvalidOperationException("The inherited-pipe fixture descendant did not start.");
}
