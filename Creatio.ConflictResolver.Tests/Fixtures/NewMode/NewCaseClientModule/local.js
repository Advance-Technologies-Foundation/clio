define("ParallelNew_FormPage", /**SCHEMA_DEPS*/[]/**SCHEMA_DEPS*/, function/**SCHEMA_ARGS*/()/**SCHEMA_ARGS*/ {
	return {
		viewConfigDiff: /**SCHEMA_VIEW_CONFIG_DIFF*/[
			{
				"operation": "merge",
				"name": "Feed",
				"values": {
					"dataSourceName": "PDS",
					"entitySchemaName": "ParalellTest"
				}
			},
			{
				"operation": "merge",
				"name": "AttachmentList",
				"values": {
					"columns": [
						{
							"id": "6f6e93a8-9086-4549-910f-0be147778b65",
							"code": "AttachmentListDS_Name",
							"caption": "#ResourceString(AttachmentListDS_Name)#",
							"dataValueType": 28,
							"width": 200
						}
					]
				}
			},
			{
				"operation": "insert",
				"name": "TextColumnLocal",
				"values": {
					"layoutConfig": {
						"column": 1,
						"row": 1,
						"colSpan": 1,
						"rowSpan": 1
					},
					"type": "crt.EmailInput",
					"label": "$Resources.Strings.TextColumnLocal",
					"control": "$TextColumnLocal",
					"labelPosition": "auto"
				},
				"parentName": "SideAreaProfileContainer",
				"propertyName": "items",
				"index": 0
			},
			{
				"operation": "insert",
				"name": "Input_lci0tuw",
				"values": {
					"layoutConfig": {
						"column": 1,
						"colSpan": 1,
						"row": 1,
						"rowSpan": 1
					},
					"type": "crt.Input",
					"label": "$Resources.Strings.PDS_ColumnText2_x6daf4q",
					"control": "$PDS_ColumnText2_x6daf4q",
					"placeholder": "",
					"tooltip": "",
					"multiline": false,
					"labelPosition": "auto"
				},
				"parentName": "GeneralInfoTabContainer",
				"propertyName": "items",
				"index": 0
			},
			{
				"operation": "insert",
				"name": "RichTextEditor_onzo0p9",
				"values": {
					"layoutConfig": {
						"column": 2,
						"colSpan": 1,
						"row": 1,
						"rowSpan": 1
					},
					"type": "crt.RichTextEditor",
					"label": "$Resources.Strings.PDS_Column10RightRich_8ql7p8v",
					"control": "$PDS_Column10RightRich_8ql7p8v",
					"labelPosition": "auto",
					"placeholder": "",
					"tooltip": "",
					"needHandleSave": true,
					"filesStorage": {
						"masterRecordColumnValue": "$Id",
						"entitySchemaName": "SysFile",
						"recordColumnName": "RecordId"
					}
				},
				"parentName": "GeneralInfoTabContainer",
				"propertyName": "items",
				"index": 1
			},
			{
				"operation": "insert",
				"name": "NumberInput_rd3se2c",
				"values": {
					"layoutConfig": {
						"column": 1,
						"colSpan": 1,
						"row": 2,
						"rowSpan": 1
					},
					"type": "crt.NumberInput",
					"label": "$Resources.Strings.PDS_ColumnNumber9_ks9ngmr",
					"control": "$PDS_ColumnNumber9_ks9ngmr",
					"placeholder": "",
					"labelPosition": "auto",
					"tooltip": ""
				},
				"parentName": "GeneralInfoTabContainer",
				"propertyName": "items",
				"index": 2
			},
			{
				"operation": "insert",
				"name": "ListWidget_jzcis8m",
				"values": {
					"type": "crt.ListWidget",
					"widgetConfig": {
						"theme": "without-fill",
						"layout": {
							"color": "dark-blue"
						}
					},
					"title": "#ResourceString(ListWidget_jzcis8m_title)#",
					"features": {
						"rows": {
							"numeration": true,
							"selection": {
								"enable": true,
								"multiple": false
							}
						},
						"editable": false
					},
					"items": "$ListWidget_jzcis8m",
					"primaryColumnName": "ListWidget_jzcis8mDS_Id",
					"columns": [
						{
							"id": "8fd2a36b-26b6-295c-2a4f-7bd4381a94da",
							"code": "ListWidget_jzcis8mDS_TextColumnLocal",
							"caption": "#ResourceString(ListWidget_jzcis8mDS_TextColumnLocal)#",
							"dataValueType": 28
						}
					]
				},
				"parentName": "GeneralInfoTab",
				"propertyName": "items",
				"index": 1
			}
		]/**SCHEMA_VIEW_CONFIG_DIFF*/,
		viewModelConfigDiff: /**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/[
			{
				"operation": "merge",
				"path": [
					"attributes"
				],
				"values": {
					"TextColumnLocal": {
						"modelConfig": {
							"path": "PDS.TextColumnLocal"
						}
					},
					"PDS_ColumnText2_x6daf4q": {
						"modelConfig": {
							"path": "PDS.ColumnText2Remote"
						}
					},
					"ListWidget_jzcis8m": {
						"isCollection": true,
						"modelConfig": {
							"path": "ListWidget_jzcis8mDS"
						},
						"viewModelConfig": {
							"attributes": {
								"ListWidget_jzcis8mDS_TextColumnLocal": {
									"modelConfig": {
										"path": "ListWidget_jzcis8mDS.TextColumnLocal"
									}
								},
								"ListWidget_jzcis8mDS_Id": {
									"modelConfig": {
										"path": "ListWidget_jzcis8mDS.Id"
									}
								}
							}
						}
					},
					"PDS_ColumnNumber9_ks9ngmr": {
						"modelConfig": {
							"path": "PDS.ColumnNumber9Remote"
						}
					},
					"PDS_Column10RightRich_8ql7p8v": {
						"modelConfig": {
							"path": "PDS.Column10RightRich"
						}
					}
				}
			},
			{
				"operation": "merge",
				"path": [
					"attributes",
					"Id",
					"modelConfig"
				],
				"values": {
					"path": "PDS.Id"
				}
			}
		]/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/,
		modelConfigDiff: /**SCHEMA_MODEL_CONFIG_DIFF*/[
			{
				"operation": "merge",
				"path": [],
				"values": {
					"primaryDataSourceName": "PDS"
				}
			},
			{
				"operation": "merge",
				"path": [
					"dataSources"
				],
				"values": {
					"PDS": {
						"type": "crt.EntityDataSource",
						"config": {
							"entitySchemaName": "ParalellTest"
						},
						"scope": "page"
					},
					"ListWidget_jzcis8mDS": {
						"type": "crt.EntityDataSource",
						"scope": "viewElement",
						"config": {
							"entitySchemaName": "ParalellTest",
							"attributes": {
								"TextColumnLocal": {
									"path": "TextColumnLocal"
								}
							}
						}
					}
				}
			}
		]/**SCHEMA_MODEL_CONFIG_DIFF*/,
		handlers: /**SCHEMA_HANDLERS*/[]/**SCHEMA_HANDLERS*/,
		converters: /**SCHEMA_CONVERTERS*/{}/**SCHEMA_CONVERTERS*/,
		validators: /**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/
	};
});