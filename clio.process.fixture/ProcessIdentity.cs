namespace Clio.ProcessFixture;

internal sealed record ProcessIdentity(int ProcessId, long StartUtcTicks, string ExecutablePath);
