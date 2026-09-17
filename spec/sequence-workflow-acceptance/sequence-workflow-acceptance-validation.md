# Sequence workflow validation

Validated on Creatio 10.1.752.0 / .NET 8 / PostgreSQL with Sales Engagement.

- Actual MCP workflow passed on .NET 8 and .NET 10: draft definition, three ordered steps, native enrollment, task and call completion, next manual email, exact content and assignment readback. Invalid synthetic resume was rejected without creating a duplicate activity.
- Fresh-context acceptance using the candidate guide and live contracts passed authoring, exact content readback, standard designer reopen, native UI activation, native enrollment and exactly one first task. It used 18 MCP calls, zero failures and zero retries. Response text reserialized by that harness totaled 155,754 bytes (189,699 including initialization/discovery); this is not a token count.
- Same-workload warm-session comparison: three updates of one synthetic Contact took 3 single-item calls / 154 ms / 2,202 response-text UTF-8 bytes versus 1 batch call / 95 ms / 1,474 bytes. The record was reset and independently verified between phases, outside measurement; final readback confirmed the same expected value. One sample establishes call reduction only, not throughput or general latency improvement.
- The complete repeatable workflow used 25 calls, 53,822 response-text UTF-8 bytes and no write retries. Different harness byte accounting must not be compared as token or performance savings.
- Package portability is covered by the separate sequence-package-portability validation and actual MCP rich-text binding test.

No email was sent. Automatic delivery, mailbox providers, macro expansion, exhaustive timezone/DST cases and cross-user assignment remain outside this proof.

ClioRing compatibility reviewed: `dotnet test clio-ring/ClioRing.Tests/ClioRing.Tests.csproj -c Release` passed 157 tests; `dotnet publish clio-ring/ClioRing.Desktop/ClioRing.Desktop.csproj -c Release -r win-x64 --self-contained true -p:PublishAot=true` succeeded. Native IPC proof discovered 219 tools and passed read-only call, restart recovery and bounded shutdown.

Guidance publication: clio-knowledge PR #181 released library 1.15.20; `update-knowledge --source creatio-curated` resolved that version. Actual MCP get-guidance returned sequences and routing at 1.15.20 / sequence 1015020000, including the Sales Engagement route. The curated guidance-name fixture is regenerated from that catalog.
