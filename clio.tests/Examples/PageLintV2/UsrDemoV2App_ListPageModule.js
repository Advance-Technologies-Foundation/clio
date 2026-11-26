/**
 * Regression fixture: the list page's client module from the Classic-to-Freedom benchmark run of
 * 2026-11-25, reduced and renamed. Expected: catch-log-only x1 (closeContracts).
 */
define("UsrDemoV2App_ListPageModule", ["@creatio-devkit/common"], function(sdk) {
	"use strict";

	const CLOSED_STATUS = "00000000-0000-4000-8000-0000000000d3";

	/** Sets Status = Closed on each contract; one update per record, a failed one does not stop the rest. */
	async function closeContracts(ids) {
		const model = await sdk.Model.create("DemoContract");
		for (const id of ids) {
			try {
				await model.update({ DemoStatus: CLOSED_STATUS }, [{ type: "primaryColumnValue", value: id }]);
			} catch (e) {
				console.error("Contract " + id + " was not closed.", e);
			}
		}
	}

	return { closeContracts: closeContracts };
});
