# Native sequence enrollment

Parent clio#1574; task clio#1576. Add a CLI/MCP operation for 1–100 explicit contact IDs and one sequence ID. Use the native bulk enrollment service once, preserve its counts and errors, and read back participant statuses through DataService. The operation never activates a sequence or manually creates activities. Draft/inactive sequences retain native behavior.

Unknown write completion must be distinguishable from a known platform result. No automatic transport or authentication replay is allowed. Partial success and failed readback must not invite blindly repeating enrollment. Errors and output are bounded and sanitized.
