// jscs:disable
/* jshint ignore:start */
/*ignore jslint start*/

define("Realog", ["ServiceHelper", "SearchableTextEdit", "css!RealogCSS"], function() {
	return {
		attributes: {
			"IsAllExceptNoisyLoggers": {
				"dataValueType": Terrasoft.DataValueType.BOOLEAN,
				"type": this.Terrasoft.ViewModelColumnType.VIRTUAL_COLUMN,
				"caption": "All except noisy"
			},
			"Logger": {
				"dataValueType": this.Terrasoft.DataValueType.TEXT,
				"type": this.Terrasoft.ViewModelColumnType.VIRTUAL_COLUMN,
				"caption": "Logger",
				"contentType": this.Terrasoft.ContentType.SEARCHABLE_TEXT,
				"isRequired": true,
				"searchableTextConfig": {
					"listAttribute": "LoggerList",
					"prepareListMethod": "prepareLoggerList",
					"listViewItemRenderMethod": "renderLoggerListItem",
					"itemSelectedMethod": "onLoggerSelected"
				}
			},
			"Level": {
				"dataValueType": Terrasoft.DataValueType.LOOKUP,
				"type": this.Terrasoft.ViewModelColumnType.VIRTUAL_COLUMN,
				"caption": "Level"
			},
			"Query": {
				"dataValueType": this.Terrasoft.DataValueType.TEXT,
				"type": this.Terrasoft.ViewModelColumnType.VIRTUAL_COLUMN,
				"caption": "JS query:"
			},
			"IsTraceEnabled": {
				"dataValueType": this.Terrasoft.DataValueType.BOOLEAN,
				"type": this.Terrasoft.ViewModelColumnType.VIRTUAL_COLUMN,
				"caption": "On/Off",
				"value": true
			},
			"LoggerList": {
				"dataValueType": Terrasoft.DataValueType.COLLECTION
			},
			"LevelList": {
				"dataValueType": Terrasoft.DataValueType.COLLECTION
			},
			"FullLoggerList": {
				"dataValueType": Terrasoft.DataValueType.CUSTOM_OBJECT
			},
			"Status": {
				"dataValueType": this.Terrasoft.DataValueType.TEXT,
				"type": this.Terrasoft.ViewModelColumnType.VIRTUAL_COLUMN,
				"value": "offline"
			}
		},
		properties: {
			oldLogger: "",
			exceptNoisyLoggersValue: "ExceptNoisyLoggers"
		},
		messages: {
			/**
			 * @message InitDataViews
			 * Changes current page header.
			 */
			"InitDataViews": {
				mode: Terrasoft.MessageMode.PTP,
				direction: Terrasoft.MessageDirectionType.PUBLISH
			},
			/**
			 * @message ChangeHeaderCaption
			 * Changes current page header.
			 */
			"ChangeHeaderCaption": {
				mode: Terrasoft.MessageMode.PTP,
				direction: Terrasoft.MessageDirectionType.PUBLISH
			},
			/**
			 * @message NeedHeaderCaption
			 */
			"NeedHeaderCaption": {
				mode: Terrasoft.MessageMode.BROADCAST,
				direction: Terrasoft.MessageDirectionType.SUBSCRIBE
			}
		},
		methods: {

			/**
			 * @inheritdoc Terrasoft.BaseSchemaViewModel#init
			 * @overridden
			 */
			init: function(callback, scope) {
				this.callParent([
					function() {
						this.initHeader();
						Ext.EventManager.on(window, "beforeunload", this.onBeforeUnload.bind(this), this);
						this.set("LevelList", Ext.create("Terrasoft.Collection"));
						this.set("LoggerList", Ext.create("Terrasoft.Collection"));
						this.set("FullLoggerList", []);
						const list = this.get("FullLoggerList");
						this.callService({
							serviceName: "LoggerConfigService",
							methodName: "GetActiveLoggerList"
						}, function (response) {
							if (Terrasoft.isEmptyObject(response)) {
								return true;
							}
							const loggers = response.GetActiveLoggerListResult;
							Terrasoft.each(loggers, function (item) {
								list.push(item);
							}, this);
						});
						this.loadSettings();
						this.subscribeOnColumnChange("IsAllExceptNoisyLoggers", this.onAllExceptNoisyLoggersChanged,
							this);
						Ext.callback(callback, scope || this);
					}, this
				]);
			},

			onAllExceptNoisyLoggersChanged: function() {
				const isAllExceptNoisyLoggers = this.get("IsAllExceptNoisyLoggers");
				if (isAllExceptNoisyLoggers) {
					this.oldLogger = this.get("Logger");
					this.set("Logger", this.exceptNoisyLoggersValue);
				} else {
					if (this.oldLogger) {
						this.set("Logger", this.oldLogger);
					}
				}
			},

			/**
			 * Initializes Header.
			 * @private
			 */
			initHeader: function() {
				this.initPageCaption();
				this.sandbox.subscribe("NeedHeaderCaption", function() {
					this.sandbox.publish("InitDataViews", {
						caption: "Real-time logging"
					});
				}, this);
			},

			/**
			 * Initializes page caption.
			 * @protected
			 */
			initPageCaption: function() {
				this.sandbox.publish("ChangeHeaderCaption", {
					"caption": "Real-time logging",
					"dataViews": Ext.create("Terrasoft.Collection")
				});
			},

			onRender: function() {
				this.callParent(arguments);
				this.main();
			},

			resetConfiguration: function() {
				this.callService({
					serviceName: "LoggerConfigService",
					methodName: "ResetConfiguration"
				}, function() {
					this.set("Status", "offline");
				}.bind(this));
			},

			/**
			 * Handler for before page unload event.
			 * @protected
			 */
			onBeforeUnload: function() {
				this.saveSettings();
				Ext.EventManager.un(window, "beforeunload", this.onBeforeUnload, this);
				this.resetConfiguration();
			},

			saveSettings: function() {
				localStorage.setItem("Logger", this.$Logger);
				localStorage.setItem("Query", this.$Query);
				localStorage.setItem("IsAllExceptNoisyLoggers", this.$IsAllExceptNoisyLoggers);
				const levelConfig = this.$Level;
				if (levelConfig && levelConfig.value) {
					localStorage.setItem("Level", levelConfig.value);
				} else {
					localStorage.setItem("Level", "");
				}
			},

			loadSettings: function() {
				this.set("Logger", localStorage.getItem("Logger") || "");
				this.set("Query", localStorage.getItem("Query") || "");
				this.set("IsAllExceptNoisyLoggers", localStorage.getItem("IsAllExceptNoisyLoggers") === "true");
				const level = localStorage.getItem("Level");
				if (level) {
					this.set("Level", {
						value: level,
						displayValue: level
					});
				}
			},

			onStatusButtonClick: function() {
				const status = this.get("Status");
				if (status === "offline") {
					this.startLogBroadcast();
				} else {
					this.resetConfiguration();
				}
			},

			startLogBroadcast: function() {
				const logger = this.get("Logger");
				if (!logger) {
					this.showInformationDialog("Field 'Logger' is required for subscribing");
					return;
				}
				const levelConfig = this.get("Level");
				const level = (levelConfig) ? levelConfig.value : "";
				const data = {
					"loggerPattern": logger,
					"logLevelStr": level,
					"bufferSize": 1
				};
				this.callService({
					serviceName: "LoggerConfigService",
					methodName: "StartLogBroadcast",
					data: data
				}, function(response, isSuccess) {
					console.log("Done. Response: %O, success: %s", response, isSuccess);
					this.set("Status", "online");
				}, this);
			},

			showHelp: function() {
				const message =
					"All except noisy:\n all loggers except Redis, Messaging, ThreadPool, Quartz, ClientLogger " +
						"(as they are too noisy)\n" +
					"Shortcuts:\n" +
					"'Ctrl + X':  Clear log\n" +
					"'L':         Write log to console\n" +
					"'W':         Save log to file\n" +
					"'H':         Draw horizontal line\n\n" +
					"Query usage:\n" +
					"Filter params $.logger, $.date, $.message\n" +
					"Example: $.message.indexOf('Stack') > -1";
				this.showInformationDialog(message);
			},

			getStatusAttributes: function() {
				return {
					status: this.get("Status")
				}
			},

			onPrepareLogLevelList: function() {
				const levelList = this.get("LevelList");
				levelList.clear();
				const knownLevels = ["Trace", "Debug", "Info", "Warn", "Error", "Fatal", "ALL"];
				const itemList = {};
				knownLevels.forEach(function(knownLevel) {
					itemList[knownLevel] = {
						value: knownLevel,
						displayValue: knownLevel
					};
				});
				levelList.loadAll(itemList);
			},

			prepareLoggerList: function(partialName, loggerList) {
				loggerList.clear();
				if (!partialName) {
					return;
				}
				const loggers = this.get("FullLoggerList");
				const knownLoggers = loggers.filter(function(logger) {
					return logger.toUpperCase().indexOf(partialName.toUpperCase()) > -1;
				});
				if (knownLoggers.length === 0) {
					return;
				}
				const itemList = {};
				const displayGroupText = "Known loggers";
				itemList.suggestionGroup = {
					markerValue: "logger-list-group-header",
					displayValue: displayGroupText,
					isSeparatorItem: true
				};
				knownLoggers.forEach(function(knownLogger) {
					const id = Terrasoft.generateGUID();
					itemList[id] = {
						value: id,
						markerValue: "logger-list-item",
						displayValue: knownLogger
					};
				});
				loggerList.loadAll(itemList);
			},

			/**
			 * Render list item event handler.
			 * @protected
			 * @param {Object} item List element.
			 */
			renderLoggerListItem: function(item) {
				const itemDisplayValue = item.displayValue;
				const itemValue = item.value;
				const primaryTemplate =
					"<span class=\"listview-item-primaryInfo logger-info\" data-value=\"{0}\">{1}</span>";
				const primaryInfoHtml = Ext.String.format(primaryTemplate, itemValue, itemDisplayValue);
				const tpl = [
					"<div class=\"listview-item logger-info\" data-value=\"{0}\">",
					"<div class=\"listview-item-info logger-info\" data-value=\"{0}\">{1}</div>",
					"</div>"
				].join("");
				item.customHtml = this.Ext.String.format(tpl, itemValue, primaryInfoHtml);
			},

			onLoggerSelected: function(item) {
				console.log("Updated logger %s", item.displayValue);
			},

			emulateLogActivity: function() {
				let n = 1;
				setInterval(function() {
					const levels = ["Info", "Warn", "Error"];
					const level = Math.floor(Math.random() * 3);
					const sampleMessageObject = {
						date: new Date(),
						level: levels[level],
						thread: Math.floor(Math.random() * 10000),
						logger: "EmailMiner",
						message: "Something is going on! #" + (n++)
					};
					const jsonMsg = {
						Header: {
							Sender: "TelemetryService"
						},
						Body: JSON.stringify([sampleMessageObject, sampleMessageObject])
					};
					Terrasoft.ServerChannel.fireEvent(Terrasoft.EventName.ON_MESSAGE, this, jsonMsg);
				}, 1000, this);
			},

			zeroPad: function(num, places) {
				const zero = places - num.toString().length + 1;
				return Array(+(zero > 0 && zero)).join("0") + num;
			},

			formatTimeString(dateTime) {
				const timeString = dateTime.toTimeString().replace(/.*(\d{2}:\d{2}:\d{2}).*/, "$1");
				return timeString  + "." + this.zeroPad(dateTime.getMilliseconds(), 3);
			},

			getLogDisplayData: function(logMessage) {
				const levelColorMapping = {
					"TRACE": "#7af0f2",
					"DEBUG": "#adec56",
					"INFO": "#00ec2c",
					"WARN": "#ffd82b",
					"ERROR": "#ec1e24",
					"FATAL": "#ec1e24"
				};
				const level = logMessage.level ? logMessage.level.toUpperCase() : "INFO";
				const color = levelColorMapping[level] || "#ffffff";
				const date = new Date(logMessage.date);
				let message = logMessage.message;
				if (logMessage.stackTrace && (level === "ERROR" || level === "FATAL")) {
					message += "\nStack trace:\n" + logMessage.stackTrace;
				}
				return {
					date: date,
					dateStr: this.formatTimeString(date),
					level: level,
					logger: logMessage.logger,
					message: message,
					log_color: color
				}
			},

			onMessage: function(logMessage, log, query, newId) {
				if (!this.get("IsTraceEnabled")) {
					return;
				}
				log.unshift({
					log_id: newId,
					log_data: this.getLogDisplayData(logMessage)
				});
				this.render(log, query);
			},

			// http://stackoverflow.com/a/30503290/634020
			snapshot: function(obj) {
				if(obj == null || typeof(obj) != 'object') {
					return obj;
				}

				const temp = new obj.constructor();

				for(let key in obj) {
					if (obj.hasOwnProperty(key)) {
						temp[key] = this.snapshot(obj[key]);
					}
				}

				return temp;
			},

			// http://stackoverflow.com/a/25859853/634020
			evalInContext: function(js, context) {
				//# Return the results of the in-line anonymous function we .call with the passed context
				try {
					return { success: function() { with(context) { return eval(js) } }.call(context) };
				}
				catch(e) {
					return { error: e };
				}
			},

			fullFillRenderQuery: function(whereStatement) {
				if (Ext.isEmpty(whereStatement)) {
					 whereStatement = '1';
				}
				const selectTplStart = "$.filter(function($) { return ";
				return selectTplStart + whereStatement + "})";
			},

			saveToFile: function(logArray) {
				let csv = '';
				logArray.forEach(function(data) {
					const item = data.log_data;
					csv += item.date.toLocaleString()+ '.' + item.date.getMilliseconds()
					+ ' ' + item.logger + ' ' + item.level + ' ' + item.thread + ' '
					+ item.message + item.stackTrace + '\r\n';
				});
				const file = new Blob([csv], {type: 'application/csv'});
				const url = URL.createObjectURL(file);
				const a = document.createElement("a");
				a.href = url;
				a.download = 'log.txt';
				document.body.appendChild(a);
				a.click();
				setTimeout(function() {
					document.body.removeChild(a);
					window.URL.revokeObjectURL(url);
				}, 0);
			},

			render: function(log, whereStatement) {
				const query = this.fullFillRenderQuery(whereStatement);
				const main = document.getElementById("MainContainer");
				const error = document.getElementById("ErrorContainer");
				const frag = document.createDocumentFragment();

				const islog = function(e) {
						return e.log_data
					},
					maplog = function(e) {
						return {log_data: e}
					};

				let rerender = false;

				let log_;
				const useFilter = (this.get("Query") !== "");
				let result;
				if (useFilter && query !== "") {
					result = this.evalInContext(query, {
						$: log.map(function(e) {
							return this.snapshot(e);
						}.bind(this)).filter(islog).map(islog)
					});
				} else {
					result = {
						error: ""
					};
				}

				if (result.success) {
					log_ = result.success.map(maplog);
					error.textContent = '';
					rerender = true;
				}
				else {
					log_ = log;
					error.textContent = result.error;
					rerender = error !== '';
				}

				main.innerHTML = '';

				if (rerender) {
					log_.forEach(function(msg) {
						const item = document.createElement("div");
						item.className = 'item';

						if (msg.log_delimiter) {
							item.className = 'delimiter';
						}
						else {
							if (msg.log_data.level) {
								item.className = 'item ' + msg.log_data.level;
							}

							if (msg.log_data.log_color) {
								item.style.color = msg.log_data.log_color;
							}

							if (msg.log_data.logger) {
								var kv = document.createElement("div");
								kv.className = 'kv log-type';
								item.appendChild(kv);

								var v = document.createElement("div");
								v.className = 'v';
								v.textContent = msg.log_data.logger;
								kv.appendChild(v);
							}

							for (let key in msg.log_data) {
								if (key !== 'logger'
										&& key !== 'level'
										&& key !== 'log_color'
										&& key !== 'date'
										&& msg.log_data.hasOwnProperty(key)) {
									var kv = document.createElement("div");
									kv.className = "kv " + key;
									item.appendChild(kv);

									const k = document.createElement("div");
									k.className = 'k';
									k.textContent = key;
									kv.appendChild(k);

									var v = document.createElement("div");
									v.textContent = msg.log_data[key];
									kv.appendChild(v);
								}
							}
						}

						frag.appendChild(item);
					});

					//TODO Rewrite: use container list, don't change html directly
					main.appendChild(frag);
				}
			},

			main: function() {
				let id = 0;
				let log = [];
				let query = "";

				Terrasoft.ServerChannel.on(Terrasoft.EventName.ON_MESSAGE, function(channel, message) {
					if (message.Header.Sender === "TelemetryService") {
						const msg = JSON.parse(message.Body, function(key, value) {
							if (typeof value === 'string') {
								if (value.indexOf("/Date(") === 0) {
									return new Date(parseInt(value.substr(6)));
								} else {
									return value;
								}
							}
							return value;
						});
						const logMessages = msg.logPortion;
						logMessages.forEach(function(logMessage) {
							this.onMessage(logMessage, log, query, id++);
						}, this);
					}
				}, this);

				//TODO Rewrite: use view model events
				window.onkeyup = function(e) {
					const queryElement = document.getElementById("Query-el");
					const key = e.keyCode ? e.keyCode : e.which;

					if (e.target === queryElement) {
						if (queryElement.value !== query) {
							query = queryElement.value;
							this.render(log, query);
						}
						return;
					}
					if (!e.target || e.target.nodeName !== "BODY") {
						return;
					}

					// h
					if (key === 72) {
						if (!log[0] || (log[0] && !log[0].log_delimiter)) {
							log.unshift({log_id: id++, log_delimiter: true });
							this.render(log, query);
						}
					}
					// l
					else if (key === 76) {
						console.log(JSON.stringify(log));
					}
					// w
					else if (key === 87) {
						this.saveToFile(log);
					}
					// ctrl + x
					else if (e.ctrlKey && key === 88) {
						log = [];
						this.render(log, query);
					}
					// e
					else if (key === 69) {
						try {
							log = JSON.parse(localStorage.getItem('log'));
						}
						catch(e) {
							console.log(e);
						}
						this.render(log, query);
					}
				}.bind(this);
			}
		},
		diff: /**SCHEMA_DIFF*/[
			{
				"operation": "insert",
				"name": "RootContainer",
				"values": {
					"id": "RootContainer",
					"itemType": this.Terrasoft.ViewItemType.CONTAINER,
					"items": []
				}
			},
			{
				"operation": "insert",
				"name": "FirstRowContainer",
				"parentName": "RootContainer",
				"propertyName": "items",
				"values": {
					"id": "FirstRowContainer",
					"itemType": this.Terrasoft.ViewItemType.CONTAINER,
					"items": []
				}
			},
			{
				"operation": "insert",
				"name": "IsAllExceptNoisyLoggers",
				"parentName": "FirstRowContainer",
				"propertyName": "items",
				"values": {
					"id": "IsAllExceptNoisyLoggers"
				}
			},
			{
				"operation": "insert",
				"name": "Logger",
				"parentName": "FirstRowContainer",
				"propertyName": "items",
				"values": {
					"id": "Logger",
					"itemType": this.Terrasoft.ViewItemType.TEXT,
					"value": {
						"bindTo": "Logger"
					},
					"enabled": {
						"bindTo": "IsAllExceptNoisyLoggers",
						"bindConfig": {"converter": "invertBooleanValue"}
					}
				}
			},
			{
				"operation": "insert",
				"name": "Level",
				"parentName": "FirstRowContainer",
				"propertyName": "items",
				"values": {
					"bindTo": "Level",
					"contentType": this.Terrasoft.ContentType.ENUM,
					"labelConfig": {
						"caption": "Level",
						"visible": true
					},
					"controlConfig": {
						"prepareList": {"bindTo": "onPrepareLogLevelList"},
						"list": {"bindTo": "LevelList"},
						"classes": ["combo-box-edit-wrap"]
					}
				}
			},
			{
				"operation": "insert",
				"name": "SubscribeButton",
				"parentName": "FirstRowContainer",
				"propertyName": "items",
				"values": {
					"itemType": Terrasoft.ViewItemType.BUTTON,
					"style": Terrasoft.controls.ButtonEnums.style.GREEN,
					"caption": "Subscribe",
					"classes": {"textClass": ["subscribe-button"]},
					"click": {"bindTo": "startLogBroadcast"}
				}
			},
			{
				"operation": "insert",
				"name": "StatusButton",
				"parentName": "FirstRowContainer",
				"propertyName": "items",
				"values": {
					"id": "StatusButton",
					"selectors": {"wrapEl": "#StatusButton"},
					"itemType": Terrasoft.ViewItemType.BUTTON,
					"classes": {"wrapperClass": ["status-button"]},
					"domAttributes": {"bindTo": "getStatusAttributes"},
					"hint": {"bindTo": "Status"},
					"click": {"bindTo": "onStatusButtonClick"}
				}
			},
			{
				"operation": "insert",
				"name": "SecondRowContainer",
				"parentName": "RootContainer",
				"propertyName": "items",
				"values": {
					"id": "SecondRowContainer",
					"itemType": this.Terrasoft.ViewItemType.CONTAINER,
					"items": []
				}
			},
			{
				"operation": "insert",
				"name": "Query",
				"parentName": "SecondRowContainer",
				"propertyName": "items",
				"values": {
					"id": "Query",
					"itemType": this.Terrasoft.ViewItemType.TEXT,
					"value": {
						"bindTo": "Query"
					},
					"hint": "Filter params $.logger, $.date, $.message. Like that $.message.indexOf('Stack') > -1"
				}
			},
			{
				"operation": "insert",
				"name": "HelpButton",
				"parentName": "SecondRowContainer",
				"propertyName": "items",
				"values": {
					"itemType": Terrasoft.ViewItemType.BUTTON,
					"style": Terrasoft.controls.ButtonEnums.style.BLUE,
					"caption": "?",
					"classes": {"textClass": ["help-button"]},
					"click": {"bindTo": "showHelp"}
				}
			},
			{
				"operation": "insert",
				"name": "ErrorContainer",
				"parentName": "RootContainer",
				"propertyName": "items",
				"values": {
					"id": "ErrorContainer",
					"itemType": this.Terrasoft.ViewItemType.CONTAINER,
					"items": []
				}
			},
			{
				"operation": "insert",
				"name": "MainContainer",
				"parentName": "RootContainer",
				"propertyName": "items",
				"values": {
					"id": "MainContainer",
					"itemType": this.Terrasoft.ViewItemType.CONTAINER,
					"items": []
				}
			}
		]/**SCHEMA_DIFF*/
	};
});
