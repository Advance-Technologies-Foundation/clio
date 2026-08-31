define("ParallelDev_FormPage", /**SCHEMA_DEPS*/[]/**SCHEMA_DEPS*/, function/**SCHEMA_ARGS*/()/**SCHEMA_ARGS*/ {
	return {
		viewConfigDiff: /**SCHEMA_VIEW_CONFIG_DIFF*/[
			{
				"operation": "merge",
				"name": "CardContentWrapper",
				"values": {
					"padding": {
						"left": "small",
						"right": "small",
						"top": "none",
						"bottom": "none"
					},
					"visible": true,
					"color": "transparent",
					"borderRadius": "none",
					"alignItems": "stretch"
				}
			},
			{
				"operation": "merge",
				"name": "Tabs",
				"values": {
					"styleType": "default",
					"mode": "tab",
					"bodyBackgroundColor": "primary-contrast-500",
					"selectedTabTitleColor": "auto",
					"tabTitleColor": "auto",
					"underlineSelectedTabColor": "auto",
					"headerBackgroundColor": "auto",
					"allowToggleClose": true
				}
			},
			{
				"operation": "merge",
				"name": "GeneralInfoTab",
				"values": {
					"iconPosition": "only-text"
				}
			},
			{
				"operation": "remove",
				"name": "GeneralInfoTabContainer"
			},
			{
				"operation": "merge",
				"name": "Feed",
				"values": {
					"dataSourceName": "PDS",
					"entitySchemaName": "ParallelDev"
				}
			},
			{
				"operation": "merge",
				"name": "AttachmentList",
				"values": {
					"columns": [
						{
							"id": "9c9266b6-dcf1-433c-aeb2-9b2175320e5f",
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
				"name": "Name",
				"values": {
					"layoutConfig": {
						"column": 1,
						"row": 1,
						"colSpan": 1,
						"rowSpan": 1
					},
					"type": "crt.Input",
					"label": "$Resources.Strings.Name",
					"control": "$Name",
					"labelPosition": "auto"
				},
				"parentName": "SideAreaProfileContainer",
				"propertyName": "items",
				"index": 0
			},
			{
				"operation": "insert",
				"name": "RichTextEditor_urdshgy",
				"values": {
					"type": "crt.RichTextEditor",
					"label": "$Resources.Strings.PDS_ColumnRichTextR_7vz0jhz",
					"control": "$PDS_ColumnRichTextR_7vz0jhz",
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
				"parentName": "GeneralInfoTab",
				"propertyName": "items",
				"index": 0
			},
			{
				"operation": "insert",
				"name": "TabPanel_l1grk1z",
				"values": {
					"type": "crt.TabPanel",
					"items": [],
					"mode": "tab",
					"styleType": "default",
					"bodyBackgroundColor": "primary-contrast-500",
					"tabTitleColor": "auto",
					"selectedTabTitleColor": "auto",
					"headerBackgroundColor": "auto",
					"underlineSelectedTabColor": "auto",
					"fitContent": true,
					"allowToggleClose": true
				},
				"parentName": "GeneralInfoTab",
				"propertyName": "items",
				"index": 1
			},
			{
				"operation": "insert",
				"name": "TabContainer_kd6mkum",
				"values": {
					"type": "crt.TabContainer",
					"items": [],
					"caption": "#ResourceString(TabContainer_kd6mkum_caption)#"
				},
				"parentName": "TabPanel_l1grk1z",
				"propertyName": "items",
				"index": 0
			},
			{
				"operation": "insert",
				"name": "GridContainer_exiggk7",
				"values": {
					"type": "crt.GridContainer",
					"items": [],
					"rows": "minmax(32px, max-content)",
					"columns": [
						"minmax(32px, 1fr)",
						"minmax(32px, 1fr)"
					],
					"gap": {
						"columnGap": "large",
						"rowGap": 0
					}
				},
				"parentName": "TabContainer_kd6mkum",
				"propertyName": "items",
				"index": 0
			},
			{
				"operation": "insert",
				"name": "EmailInput_rk57rhk",
				"values": {
					"layoutConfig": {
						"column": 1,
						"colSpan": 1,
						"row": 1,
						"rowSpan": 1
					},
					"type": "crt.EmailInput",
					"label": "$Resources.Strings.PDS_Column25_vayf73r",
					"control": "$PDS_Column25_vayf73r",
					"labelPosition": "auto",
					"placeholder": "",
					"tooltip": "",
					"needHandleSave": false
				},
				"parentName": "GridContainer_exiggk7",
				"propertyName": "items",
				"index": 0
			},
			{
				"operation": "insert",
				"name": "ColorPicker_2jugyi2",
				"values": {
					"layoutConfig": {
						"column": 2,
						"colSpan": 1,
						"row": 1,
						"rowSpan": 1
					},
					"type": "crt.ColorPicker",
					"label": "$Resources.Strings.PDS_Columncoluor1_a3msb59",
					"labelPosition": "auto",
					"control": "$PDS_Columncoluor1_a3msb59",
					"pickerMode": "extended"
				},
				"parentName": "GridContainer_exiggk7",
				"propertyName": "items",
				"index": 1
			},
			{
				"operation": "insert",
				"name": "TabContainer_5l508m3",
				"values": {
					"type": "crt.TabContainer",
					"items": [],
					"caption": "#ResourceString(TabContainer_5l508m3_caption)#"
				},
				"parentName": "TabPanel_l1grk1z",
				"propertyName": "items",
				"index": 1
			},
			{
				"operation": "insert",
				"name": "GridContainer_ysk1i2k",
				"values": {
					"type": "crt.GridContainer",
					"items": [],
					"rows": "minmax(32px, max-content)",
					"columns": [
						"minmax(32px, 1fr)",
						"minmax(32px, 1fr)"
					],
					"gap": {
						"columnGap": "large",
						"rowGap": 0
					}
				},
				"parentName": "TabContainer_5l508m3",
				"propertyName": "items",
				"index": 0
			},
			{
				"operation": "insert",
				"name": "PhoneInput_h19vxjf",
				"values": {
					"type": "crt.PhoneInput",
					"label": "$Resources.Strings.PDS_ColumnLocalphone_fbv16db",
					"control": "$PDS_ColumnLocalphone_fbv16db",
					"labelPosition": "auto",
					"placeholder": "",
					"tooltip": "",
					"needHandleSave": false,
					"layoutConfig": {
						"column": 1,
						"colSpan": 1,
						"row": 1,
						"rowSpan": 1
					}
				},
				"parentName": "GridContainer_ysk1i2k",
				"propertyName": "items",
				"index": 0
			},
			{
				"operation": "insert",
				"name": "TabContainer_0z71xnq",
				"values": {
					"type": "crt.TabContainer",
					"items": [],
					"caption": "#ResourceString(TabContainer_0z71xnq_caption)#",
					"iconPosition": "only-text",
					"visible": true
				},
				"parentName": "Tabs",
				"propertyName": "items",
				"index": 1
			},
			{
				"operation": "insert",
				"name": "GridContainer_m82x0tq",
				"values": {
					"type": "crt.GridContainer",
					"items": [],
					"rows": "minmax(32px, max-content)",
					"columns": [
						"minmax(32px, 1fr)",
						"minmax(32px, 1fr)"
					],
					"gap": {
						"columnGap": "large",
						"rowGap": 0
					}
				},
				"parentName": "TabContainer_0z71xnq",
				"propertyName": "items",
				"index": 0
			},
			{
				"operation": "insert",
				"name": "DateTimePicker_2t6hhq4",
				"values": {
					"layoutConfig": {
						"column": 1,
						"colSpan": 1,
						"row": 1,
						"rowSpan": 1
					},
					"type": "crt.DateTimePicker",
					"label": "$Resources.Strings.PDS_ColumnDateTime2_jrr2zs9",
					"placeholder": "",
					"readonly": false,
					"labelPosition": "auto",
					"tooltip": "",
					"control": "$PDS_ColumnDateTime2_jrr2zs9",
					"pickerType": "datetime"
				},
				"parentName": "GridContainer_m82x0tq",
				"propertyName": "items",
				"index": 0
			},
			{
				"operation": "insert",
				"name": "Dashboards_w3sizur",
				"values": {
					"type": "crt.Dashboards",
					"_designOptions": {
						"dependencies": [],
						"filters": []
					},
					"layoutConfig": {
						"column": 2,
						"colSpan": 1,
						"row": 1,
						"rowSpan": 1
					}
				},
				"parentName": "GridContainer_m82x0tq",
				"propertyName": "items",
				"index": 1
			},
			{
				"operation": "insert",
				"name": "Checkbox_kk5gdut",
				"values": {
					"layoutConfig": {
						"column": 2,
						"colSpan": 1,
						"row": 2,
						"rowSpan": 1
					},
					"type": "crt.Checkbox",
					"value": true,
					"disabled": false,
					"inversed": false,
					"label": "$Resources.Strings.PDS_ColumnCheckbox22_cytntse",
					"ariaLabel": "",
					"labelPosition": "auto",
					"tooltip": "",
					"control": "$PDS_ColumnCheckbox22_cytntse"
				},
				"parentName": "GridContainer_m82x0tq",
				"propertyName": "items",
				"index": 2
			},
			{
				"operation": "insert",
				"name": "PhoneInput_cmeo0d8",
				"values": {
					"type": "crt.PhoneInput",
					"label": "$Resources.Strings.PDS_Columnphonenumber2_wsblr2j",
					"control": "$PDS_Columnphonenumber2_wsblr2j",
					"labelPosition": "auto",
					"placeholder": "",
					"tooltip": "",
					"needHandleSave": false
				},
				"parentName": "TabContainer_0z71xnq",
				"propertyName": "items",
				"index": 1
			},
			{
				"operation": "insert",
				"name": "TabContainer_xhi61dk",
				"values": {
					"type": "crt.TabContainer",
					"items": [],
					"caption": "#ResourceString(TabContainer_xhi61dk_caption)#",
					"iconPosition": "only-text",
					"visible": true
				},
				"parentName": "Tabs",
				"propertyName": "items",
				"index": 2
			},
			{
				"operation": "insert",
				"name": "GridContainer_hfi5a8y",
				"values": {
					"type": "crt.GridContainer",
					"items": [],
					"rows": "minmax(32px, max-content)",
					"columns": [
						"minmax(32px, 1fr)",
						"minmax(32px, 1fr)"
					],
					"gap": {
						"columnGap": "large",
						"rowGap": 0
					}
				},
				"parentName": "TabContainer_xhi61dk",
				"propertyName": "items",
				"index": 0
			},
			{
				"operation": "insert",
				"name": "EmailInput_xjym6dx",
				"values": {
					"layoutConfig": {
						"column": 1,
						"colSpan": 1,
						"row": 1,
						"rowSpan": 1
					},
					"type": "crt.EmailInput",
					"label": "$Resources.Strings.PDS_Columnemail11_l530hhi",
					"control": "$PDS_Columnemail11_l530hhi",
					"labelPosition": "auto",
					"placeholder": "",
					"tooltip": "",
					"needHandleSave": false
				},
				"parentName": "GridContainer_hfi5a8y",
				"propertyName": "items",
				"index": 0
			},
			{
				"operation": "insert",
				"name": "Label_j55o3f2",
				"values": {
					"layoutConfig": {
						"column": 2,
						"colSpan": 1,
						"row": 1,
						"rowSpan": 1
					},
					"type": "crt.Label",
					"caption": "#MacrosTemplateString(#ResourceString(Label_j55o3f2_caption)#)#",
					"labelType": "headline-1",
					"labelThickness": "default",
					"labelEllipsis": false,
					"labelColor": "auto",
					"labelBackgroundColor": "transparent",
					"labelTextAlign": "start",
					"headingLevel": "label",
					"visible": true
				},
				"parentName": "GridContainer_hfi5a8y",
				"propertyName": "items",
				"index": 1
			},
			{
				"operation": "insert",
				"name": "IndicatorWidget_891zt1i",
				"values": {
					"layoutConfig": {
						"column": 1,
						"colSpan": 1,
						"row": 2,
						"rowSpan": 1
					},
					"type": "crt.IndicatorWidget",
					"config": {
						"title": "#ResourceString(IndicatorWidget_891zt1i_title)#",
						"theme": "without-fill",
						"layout": {
							"color": "green"
						},
						"text": {
							"template": "{0}",
							"metricMacros": "{0}",
							"fontSizeMode": "medium"
						},
						"data": {
							"formatting": {
								"type": "number"
							},
							"providing": {
								"attribute": "IndicatorWidget_891zt1i_Data",
								"schemaName": "ParallelDev",
								"filters": null,
								"aggregation": {
									"column": {
										"orderDirection": 0,
										"orderPosition": -1,
										"isVisible": true,
										"expression": {
											"expressionType": 1,
											"functionArgument": {
												"expressionType": 0,
												"columnPath": "Id"
											},
											"functionType": 2,
											"aggregationType": 1,
											"aggregationEvalType": 2
										}
									}
								},
								"dependencies": [
									{
										"attributePath": "Id",
										"relationPath": "PDS.Id"
									}
								]
							}
						}
					}
				},
				"parentName": "GridContainer_hfi5a8y",
				"propertyName": "items",
				"index": 2
			},
			{
				"operation": "insert",
				"name": "Input_q37h22p",
				"values": {
					"layoutConfig": {
						"column": 2,
						"colSpan": 1,
						"row": 2,
						"rowSpan": 1
					},
					"type": "crt.Input",
					"label": "$Resources.Strings.PDS_ColumnText2local_3qomywc",
					"control": "$PDS_ColumnText2local_3qomywc",
					"placeholder": "",
					"tooltip": "",
					"readonly": false,
					"multiline": false,
					"labelPosition": "auto"
				},
				"parentName": "GridContainer_hfi5a8y",
				"propertyName": "items",
				"index": 3
			}
		]/**SCHEMA_VIEW_CONFIG_DIFF*/,
		viewModelConfigDiff: /**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/[
			{
				"operation": "merge",
				"path": [
					"attributes"
				],
				"values": {
					"Name": {
						"modelConfig": {
							"path": "PDS.Name"
						}
					},
					"PDS_Columnemail11_l530hhi": {
						"modelConfig": {
							"path": "PDS.Columnemail11"
						}
					},
					"PDS_ColumnWebLink2_57g5qka": {
						"modelConfig": {
							"path": "PDS.ColumnWebLink22"
						}
					},
					"PDS_ColumnAutonumber2_hqrguh2": {
						"modelConfig": {
							"path": "PDS.ColumnAutonumber2"
						}
					},
					"PDS_ColumnDateTime2_jrr2zs9": {
						"modelConfig": {
							"path": "PDS.ColumnDateTime2"
						}
					},
					"PDS_Columnphonenumber2_wsblr2j": {
						"modelConfig": {
							"path": "PDS.Columnphonenumber2"
						}
					},
					"PDS_ColumnRichTextR_7vz0jhz": {
						"modelConfig": {
							"path": "PDS.ColumnRichTextR"
						}
					},
					"PDS_ColumnLocalphone_fbv16db": {
						"modelConfig": {
							"path": "PDS.ColumnLocalphone"
						}
					},
					"PDS_ColumnCheckbox22_cytntse": {
						"modelConfig": {
							"path": "PDS.ColumnCheckbox22"
						}
					},
					"PDS_Columncoluor1_a3msb59": {
						"modelConfig": {
							"path": "PDS.Columncoluor1"
						}
					},
					"PDS_Column25_vayf73r": {
						"modelConfig": {
							"path": "PDS.Column25"
						}
					},
					"PDS_ColumnText2local_3qomywc": {
						"modelConfig": {
							"path": "PDS.ColumnText2local"
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
					"primaryDataSourceName": "PDS",
					"loadingConfig": {}
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
							"entitySchemaName": "ParallelDev"
						},
						"scope": "page"
					}
				}
			}
		]/**SCHEMA_MODEL_CONFIG_DIFF*/,
		handlers: /**SCHEMA_HANDLERS*/[]/**SCHEMA_HANDLERS*/,
		converters: /**SCHEMA_CONVERTERS*/{}/**SCHEMA_CONVERTERS*/,
		validators: /**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/
	};
});