# Validation

Unit tests cover page inheritance/FK11, direction filtering (including omitted
L12 defaulting to Variable), preservation, all eight icon combinations, repeated
create refusal, layout-name conflicts and unsafe SVG rejection before writes.
NoEnvironment E2E calls the real stdio MCP tool and checks generated artifacts.

Retained PostgreSQL/FSM lab: CustomProcessElementLab, Creatio 10.1.585.0.
Generated UsrRegistration1599ProbePage for the existing registration probe task.
Imported the workspace and compiled its package. Created UsrPage1600Validation
(c1e3116b-af3c-4ac1-94a7-db4ba27c73f3) with two probe elements.

Browser evidence: native page loaded with Input value and Shared value only.
Input retained its Source parameter mapping; changing Shared value from 3 to 9,
saving and reloading preserved 9. The second element retained its mapping to
First calculation.Result. Its numeric source selector offered Result and Shared
value and excluded Input value. Numeric selection filters out Boolean/text outputs.
Both diagram and page-header SVG icons rendered. Probe artifacts remain in the lab.

Full unit suite passed after review fixes (13,696 tests, 25 existing skips).
After review fixes run the focused page fixture, then affected modules/full suite
before committing composition-root changes. Run the real MCP fixture again.
Package installation and SQL Server portability remain tracked by #1602.

