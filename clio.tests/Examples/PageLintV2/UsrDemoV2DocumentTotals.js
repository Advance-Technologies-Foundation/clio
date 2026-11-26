/**
 * Regression fixture: shared client logic imported by another client module (not by a page) in the
 * Classic-to-Freedom benchmark run of 2026-11-25, reduced and renamed. Expected: catch-log-only x1 (the catch in
 * postDocument that holds only a comment). The catches that return a fallback value are not reported.
 */
define("UsrDemoV2DocumentTotals", ["@creatio-devkit/common"], function(sdk) {
	"use strict";

	async function sumContractColumn(sdkRef, entityName, columnName, activeColumn, contractId) {
		try {
			const model = await sdkRef.Model.create(entityName);
			const rows = await model.load({ attributes: [columnName, activeColumn, "DemoContract"] });
			return (rows || []).filter((r) => r[activeColumn] && r.DemoContract === contractId)
				.reduce((sum, r) => sum + Number(r[columnName] || 0), 0);
		} catch (e) {
			return null;
		}
	}

	async function countActive(sdkRef, contractId) {
		try {
			const model = await sdkRef.Model.create("DemoContractItem");
			const rows = await model.load({ attributes: ["Id", "DemoIsActive", "DemoContract"] });
			return (rows || []).filter((r) => r.DemoIsActive && r.DemoContract === contractId).length;
		} catch (e) {
			return null;
		}
	}

	async function postDocument(sdkRef, request, options) {
		const { $context } = request;
		await $context.set(options.postedAttributeName, true);
		try {
			const log = await sdkRef.Model.create("DemoDocumentHistory");
			await log.insert({ DemoDocument: await $context.Id, DemoAction: "post" });
		} catch (e) {
			/* The log write does not stop the posting. */
		}
	}

	return { sumContractColumn: sumContractColumn, countActive: countActive, postDocument: postDocument };
});
