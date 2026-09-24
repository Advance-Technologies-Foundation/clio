# SPEC: Commented page JSON sections (#1641)

Problem: documented Freedom UI page sections fail strict JSON parsing during validate-page and validated update-page.
User: source-first Creatio developer.
Capabilities: CAP-01 accept line and block comments without changing the saved source; CAP-02 retain semantic checks and reject malformed JSON; CAP-03 preserve trailing-comma support using the JSON parser.
Constraints: no MCP signature changes, no JavaScript execution, no custom comment stripper, no new runtime lifecycle commands.
Non-goals: JSON5, permissive mobile JSON changes, redesigning the operator or Clio attachment.
Success: regression tests, unchanged update-page save payload, and real Windows MCP validation of fixtures synchronized to the disposable Omen FSM runtime, followed by verified cleanup. Required paired schema markers remain mandatory.

