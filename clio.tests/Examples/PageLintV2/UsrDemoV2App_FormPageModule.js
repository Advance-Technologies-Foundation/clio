/**
 * Regression fixture: the shape of a record page's client module from the Classic-to-Freedom
 * benchmark run of 2026-11-25, reduced to the code the quality rules read. Names and record ids are replaced.
 * Expected: catch-log-only x3 (refreshItemsSummary, deactivateSelectedItems, assignNextContractNumber),
 * numeric-comparison-type x1 (setDueSoonFilter). The catches that keep a fallback value are not reported.
 */
define("UsrDemoV2App_FormPageModule", ["@creatio-devkit/common", "UsrDemoV2DocumentTotals"],
		function(sdk, documentTotals) {
	"use strict";

	/** Status record ids the page compares against. */
	const CONTRACT_STATUS = Object.freeze({
		Draft: "00000000-0000-4000-8000-0000000000d1",
		Closed: "00000000-0000-4000-8000-0000000000d3"
	});
	const LAST_NUMBER_SETTING = "DemoContractLastNumber";
	const ITEMS_DATA_SOURCE = "DemoContractItemDetailListDS";
	const DUE_SOON_DAYS = 30;

	function isEditableStatus(status) {
		return Boolean(status) && status.value !== CONTRACT_STATUS.Closed;
	}

	/** Fails closed: when Status cannot be read the fields stay read-only. */
	async function refreshCanEditFields(request) {
		const { $context } = request;
		let canEdit = false;
		let failed = false;
		try {
			canEdit = isEditableStatus(await $context.PDS_DemoStatus);
		} catch (e) {
			failed = true;
		}
		await $context.set("DemoCanEdit", canEdit && !failed);
	}

	async function refreshItemsSummary($context) {
		const contractId = await $context.Id;
		if (!contractId) {
			return;
		}
		try {
			const [total, activeCount] = await Promise.all([
				documentTotals.sumContractColumn(sdk, "DemoContractItem", "DemoTotal", "DemoIsActive", contractId),
				documentTotals.countActive(sdk, contractId)
			]);
			await $context.set("DemoItemsSummary", total + " / " + activeCount);
		} catch (e) {
			console.error("UsrDemoV2App_FormPageModule: the items summary could not be read.", e);
		}
	}

	/** Sets the Items list's "Due in 30 days" filter. */
	async function setDueSoonFilter($context, isOn) {
		const items = {};
		if (isOn) {
			const limit = new Date();
			limit.setDate(limit.getDate() + DUE_SOON_DAYS);
			items.DemoDueSoonFilter = {
				filterType: 1,
				comparisonType: 6,
				isEnabled: true,
				trimDateTimeParameterToDate: false,
				dataValueType: 7,
				leftExpression: { expressionType: 0, columnPath: "DemoDeliveryDate" },
				rightExpression: { expressionType: 2, parameter: { dataValueType: 7, value: JSON.stringify(limit.toISOString()) } }
			};
		}
		await $context.set("DemoItemsDueSoonFilter", {
			items: items, logicalOperation: 0, isEnabled: true, filterType: 6, rootSchemaName: "DemoContractItem"
		});
	}

	async function deactivateSelectedItems(request, ids) {
		const model = await sdk.Model.create("DemoContractItem");
		for (const id of ids) {
			try {
				await model.update({ DemoIsActive: false }, [{ type: "primaryColumnValue", value: id }]);
			} catch (e) {
				console.error("Contract item " + id + " was not deactivated.", e);
			}
		}
		await sdk.HandlerChainService.instance.process({
			type: "crt.LoadDataRequest",
			dataSourceName: ITEMS_DATA_SOURCE,
			config: { loadType: "reload", useLastLoadParameters: true },
			$context: request.$context,
			scopes: [...request.scopes]
		});
	}

	/** Display name of a status record; the given fallback when it cannot be read. */
	async function readStatusCaption(statusId, fallback) {
		try {
			const model = await sdk.Model.create("DemoContractStatus");
			const rows = await model.load({ attributes: ["Id", "Name"] });
			return (rows && rows.length && rows[0].Name) || fallback;
		} catch (e) {
			return fallback;
		}
	}

	async function assignNextContractNumber($context) {
		const settings = new sdk.SysSettingsService();
		try {
			const setting = await settings.getByCode(LAST_NUMBER_SETTING);
			const next = (Number.parseInt(setting && setting.value, 10) || 0) + 1;
			await settings.update({ code: LAST_NUMBER_SETTING, value: next });
			await $context.set("PDS_DemoNumber", String(next).padStart(5, "0"));
		} catch (e) {
			console.error("UsrDemoV2App_FormPageModule: the next contract number could not be assigned.", e);
		}
	}

	async function refreshStartApprovalEnabled($context) {
		let enabled = false;
		try {
			enabled = !(await $context.PDS_DemoApproved);
		} catch (e) {
			enabled = false;
		}
		await $context.set("DemoStartApprovalButtonDisabled", !enabled);
	}

	return {
		refreshCanEditFields: refreshCanEditFields,
		refreshItemsSummary: refreshItemsSummary,
		setDueSoonFilter: setDueSoonFilter,
		deactivateSelectedItems: deactivateSelectedItems,
		readStatusCaption: readStatusCaption,
		assignNextContractNumber: assignNextContractNumber,
		refreshStartApprovalEnabled: refreshStartApprovalEnabled
	};
});
