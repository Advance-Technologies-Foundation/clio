# SQL log validation

Unit: initial persistence precedes executor; full normalized long Unicode SQL retained; SELECT row counts including zero; DML affected counts including unknown; failures complete with error and no count; initial-log failure prevents execution; completion-log failure preserves successful response; readers disposed.

Live disposable Creatio: install rebuilt archive, verify native entity and text storage; run SELECT, empty SELECT, DML and invalid SQL; read the pending record from an independent connection during a delayed query; verify full SQL equality and measured duration; reinstall/upgrade the package and verify the entity remains usable. Verify both runtime archive inventories and builds.
