namespace Clio.Mcp.E2E.Support.Mcp;

internal sealed record ClioProcessDescriptor(
	string Command,
	IReadOnlyList<string> Arguments,
	string WorkingDirectory);
