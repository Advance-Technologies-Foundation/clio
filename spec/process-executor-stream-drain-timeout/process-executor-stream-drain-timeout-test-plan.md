# Process executor stream-drain timeout - test plan

## Integration

- Cross-platform `ProcessExecutor` tests use a purpose-built .NET executable whose immediate parent exits after spawning a silent thirty-second descendant that inherits redirected stdout/stderr. Assert prompt `TimedOut` and `Canceled` completion, retained unterminated parent output, and explicit uncertain-descendant semantics. Run natively on Windows and in a Linux .NET SDK container; leave the same path available to macOS CI.
- Exceed the shared output cap without a timeout while the silent descendant holds both pipes; assert the cap cancels both readers and returns promptly.
- Emit `first\r`, pause while the process remains alive, then emit `second`; assert the first real-time callback arrives before exit and the two logical lines stay separate.

## Unit regression

- Run the full `Category=Unit` suite because `clio/Common/ProcessExecutor.cs` is shared infrastructure.
- Retain existing capture, realtime, cancellation, output-limit, and directory-limit coverage.

## MCP end to end

- Start the real `clio mcp-server` process with isolated temporary Clio settings.
- Configure the supported canonical `creatio-curated` Git override.
- Put the purpose-built fake Git executable first on the MCP child's `PATH`, assert its invocation marker, and assert it records the inherited-pipe descendant PID.
- Send the MCP `initialize` request over stdio and bound the observation window around the five-second bootstrap budget.
- Assert the existing non-fatal curated-knowledge warning, the explicit descendant-termination limitation, and successful initialization before the thirty-second descendant could naturally exit.

## Documentation and compatibility

- Verify `mcp-server` help, detailed docs, command index, Wiki anchors, and shipped templates remain accurate.
- Verify no MCP tool/resource/prompt contract changes and no Ring-consumed contract changes.
