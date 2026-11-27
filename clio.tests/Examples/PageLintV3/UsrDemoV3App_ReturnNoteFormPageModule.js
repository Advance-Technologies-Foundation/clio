/**
 * Fixture: a record page's client module from the Classic-to-Freedom benchmark run of 2026-11-26, reduced to the code
 * the quality rules read, as it was saved before the migration gate's repair. Names are replaced.
 * Expected: numeric-comparison-type x1 (markPosted). The catches return a fallback value and are not reported.
 */
define("UsrDemoV3App_ReturnNoteFormPageModule", ["@creatio-devkit/common", "UsrDemoV3App_ContractAmountModule"], function(sdk, contractAmount) {
	"use strict";

	const {showMessage, dataServiceUrl, parameter} = contractAmount;

	const DATA_VALUE_TYPE_GUID = 0;
	const DATA_VALUE_TYPE_BOOLEAN = 12;
	const UPDATE_QUERY_PATH = "DataService/json/SyncReply/UpdateQuery";
	const LOG_ENTITY = "DemoDocumentLog";

	async function sendDataServiceQuery(relativePath, body) {
		try {
			const response = await new sdk.HttpClientService().post(dataServiceUrl(relativePath), body);
			const data = response?.body ?? response;
			return data && data.success !== false ? data : null;
		} catch (e) {
			return null;
		}
	}

	async function markPosted(entitySchemaName, recordId) {
		const result = await sendDataServiceQuery(UPDATE_QUERY_PATH, {
			rootSchemaName: entitySchemaName,
			operationType: 2,
			columnValues: {items: {DemoIsPosted: parameter(DATA_VALUE_TYPE_BOOLEAN, true)}},
			filters: {
				items: {
					Id: {
						filterType: 1,
						comparisonType: 3,
						leftExpression: {expressionType: 0, columnPath: "Id"},
						rightExpression: {expressionType: 2, parameter: parameter(DATA_VALUE_TYPE_GUID, recordId)}
					}
				},
				logicalOperation: 0,
				filterType: 6
			}
		});
		return Boolean(result && result.rowsAffected > 0);
	}

	async function writeLog(entitySchemaName, recordId, message) {
		try {
			const model = await sdk.Model.create(LOG_ENTITY);
			await model.insert({DemoName: message, DemoRecordId: recordId, DemoEntityName: entitySchemaName});
			return true;
		} catch (e) {
			return false;
		}
	}

	async function postDocument(request, entitySchemaName) {
		const recordId = await request.$context.Id;
		if (!(await markPosted(entitySchemaName, recordId))) {
			await showMessage(request, await request.$context.DemoPostFailedMessageText);
			return false;
		}
		await writeLog(entitySchemaName, recordId, "Posted");
		return true;
	}

	return {
		postDocument
	};
});
