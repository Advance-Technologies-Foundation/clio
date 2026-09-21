# Sales engagement MCP requirements

Story: clio#1574. First increment: clio#1575.

Provide compact effective schema and lookup discovery, DataService-first data operations, native enrollment, portable package definitions and executable guidance. Reuse Creatio lifecycle behavior. Preserve explicit activation and enrollment. Do not export participant execution state or credentials. See the seven native sub-issues for scope and acceptance.

Discovery returns effective fields for Sequence, SequenceStep, SequenceParticipant, SequenceRuleset, DeliverySchedule and DeliveryScheduleSlot; bounded lookup/configuration choices; optional existing definition inspection; explicit incomplete/absent/unreadable outcomes. It makes no writes and does not claim that metadata presence proves permission to activate or send email.
