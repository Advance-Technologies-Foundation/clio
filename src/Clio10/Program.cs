using Clio10.Cli;
using Clio10.Mcp;
using Microsoft.Extensions.DependencyInjection;

// The product selects an interaction surface, not a business operation.
var services = new ServiceCollection();
services.AddClioCli().AddClioMcp();
using var provider = services.BuildServiceProvider();
using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancellation.Cancel(); };
try {
    if (args is ["mcp", ..])
        return await provider.GetRequiredService<IMcpAdapter>().RunAsync(args[1..], cancellation.Token);
    if (args is [] or ["--help"])
        Console.WriteLine("Clio 10 surfaces: CLI (default); mcp (stdio server). Use clio mcp --help for server options.");
    return await provider.GetRequiredService<ICliAdapter>().RunAsync(args, cancellation.Token);
}
catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { return 130; }
