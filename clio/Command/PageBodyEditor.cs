using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Clio.Command;

internal static class PageBodyEditor {

	private static readonly HashSet<string> FormFieldTypes = new(StringComparer.OrdinalIgnoreCase) {
		"crt.Input", "crt.NumberInput", "crt.Checkbox", "crt.DateTimePicker",
		"crt.ComboBox", "crt.RichTextEditor", "crt.PhoneInput", "crt.EmailInput",
		"crt.WebInput", "crt.ColorPicker", "crt.ImageInput", "crt.FileInput",
		"crt.EncryptedInput", "crt.Slider"
	};

	private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(5);
	private static readonly JsonSerializerOptions IndentedOptions = new() { WriteIndented = true };

	public static string AddFormFields(string body, IReadOnlyList<FormFieldSpec> fields) {
		foreach (FormFieldSpec field in fields) {
			if (string.IsNullOrWhiteSpace(field.Path))
				throw new InvalidOperationException(
					$"Field '{field.Name ?? "?"}' missing required path (e.g. 'PDS.UsrName')");
			if (string.IsNullOrWhiteSpace(field.Type))
				throw new InvalidOperationException($"Field '{field.Name ?? "?"}' missing required type");
			if (!FormFieldTypes.Contains(field.Type))
				throw new InvalidOperationException($"Field type '{field.Type}' is not a valid form field type");
		}
		var viewConfigDiff = ParseMarkerJson(body, "SCHEMA_VIEW_CONFIG_DIFF").AsArray();
		string vmMarker = DetectVmMarker(body)
			?? throw new InvalidOperationException("No viewModelConfig marker found in body");
		JsonNode vmData = ParseMarkerJson(body, vmMarker);
		string parent = fields.FirstOrDefault()?.ParentName
			?? DiscoverFormContainer(viewConfigDiff)
			?? "SideAreaProfileContainer";
		(int maxRow, int maxIndex) = FindMaxRowIndex(viewConfigDiff, parent);
		var existingNames = new HashSet<string>(
			viewConfigDiff.OfType<JsonObject>()
				.Select(o => o["name"]?.GetValue<string>())
				.Where(n => n != null)!,
			StringComparer.Ordinal);
		foreach (FormFieldSpec field in fields) {
			string attrKey = DeriveAttrKey(field);
			if (existingNames.Contains(attrKey))
				continue;
			maxRow++;
			maxIndex++;
			viewConfigDiff.Add(BuildFormFieldInsert(field, maxRow, maxIndex, parent));
			existingNames.Add(attrKey);
		}
		body = ReplaceMarkerContent(body, "SCHEMA_VIEW_CONFIG_DIFF", SerializeJson(viewConfigDiff));
		if (vmMarker == "SCHEMA_VIEW_MODEL_CONFIG") {
			var vmObj = vmData is JsonObject obj ? obj : new JsonObject();
			if (!vmObj.ContainsKey("attributes"))
				vmObj["attributes"] = new JsonObject();
			JsonObject attrs = vmObj["attributes"]!.AsObject();
			foreach (FormFieldSpec field in fields) {
				string attrKey = DeriveAttrKey(field);
				if (!attrs.ContainsKey(attrKey))
					attrs[attrKey] = JsonNode.Parse($"{{\"modelConfig\":{{\"path\":\"{field.Path}\"}}}}");
			}
			body = ReplaceMarkerContent(body, vmMarker, SerializeJson(vmObj));
		} else {
			var vmArray = vmData.AsArray();
			JsonObject mergeOp = FindOrCreateMergeOp(vmArray, ["attributes"]);
			if (!mergeOp.ContainsKey("values"))
				mergeOp["values"] = new JsonObject();
			JsonObject values = mergeOp["values"]!.AsObject();
			foreach (FormFieldSpec field in fields) {
				string attrKey = DeriveAttrKey(field);
				if (!values.ContainsKey(attrKey))
					values[attrKey] = JsonNode.Parse($"{{\"modelConfig\":{{\"path\":\"{field.Path}\"}}}}");
			}
			body = ReplaceMarkerContent(body, vmMarker, SerializeJson(vmArray));
		}
		return body;
	}

	public static string AddListColumns(string body, IReadOnlyList<ListColumnSpec> columns) {
		var viewConfigDiff = ParseMarkerJson(body, "SCHEMA_VIEW_CONFIG_DIFF").AsArray();
		JsonObject datatableOp = viewConfigDiff.OfType<JsonObject>()
			.FirstOrDefault(o => o["name"]?.GetValue<string>() == "DataTable")
			?? throw new InvalidOperationException("DataTable operation not found in SCHEMA_VIEW_CONFIG_DIFF");
		if (!datatableOp.ContainsKey("values"))
			datatableOp["values"] = new JsonObject();
		JsonObject dtValues = datatableOp["values"]!.AsObject();
		if (!dtValues.ContainsKey("columns"))
			dtValues["columns"] = new JsonArray();
		JsonArray existingColumns = dtValues["columns"]!.AsArray();
		var existingCodes = new HashSet<string>(
			existingColumns.OfType<JsonObject>()
				.Select(c => c["code"]?.GetValue<string>())
				.Where(c => c != null)!,
			StringComparer.Ordinal);
		foreach (ListColumnSpec col in columns) {
			if (existingCodes.Contains(col.Code))
				continue;
			var entry = new JsonObject {
				["id"] = col.Id ?? Guid.NewGuid().ToString(),
				["code"] = col.Code,
				["caption"] = col.Caption ?? col.Code,
				["dataValueType"] = col.DataValueType
			};
			if (col.Width.HasValue)
				entry["width"] = col.Width.Value;
			existingColumns.Add(entry);
			existingCodes.Add(col.Code);
		}
		body = ReplaceMarkerContent(body, "SCHEMA_VIEW_CONFIG_DIFF", SerializeJson(viewConfigDiff));
		string vmMarker = DetectVmMarker(body)
			?? throw new InvalidOperationException("No viewModelConfig marker found in body");
		JsonNode vmData = ParseMarkerJson(body, vmMarker);
		if (vmMarker == "SCHEMA_VIEW_MODEL_CONFIG") {
			var vmObj = vmData is JsonObject obj ? obj : new JsonObject();
			if (!vmObj.ContainsKey("attributes"))
				vmObj["attributes"] = new JsonObject();
			JsonObject attrs = vmObj["attributes"]!.AsObject();
			foreach (ListColumnSpec col in columns) {
				if (!attrs.ContainsKey(col.Code)) {
					string entityCol = col.Code.StartsWith("PDS_", StringComparison.Ordinal) ? col.Code[4..] : col.Code;
					attrs[col.Code] = JsonNode.Parse($"{{\"modelConfig\":{{\"path\":\"PDS.{entityCol}\"}}}}");
				}
			}
			body = ReplaceMarkerContent(body, vmMarker, SerializeJson(vmObj));
		} else {
			var vmArray = vmData.AsArray();
			JsonObject mergeOp = FindOrCreateMergeOp(vmArray,
				["attributes", "Items", "viewModelConfig", "attributes"]);
			if (!mergeOp.ContainsKey("values"))
				mergeOp["values"] = new JsonObject();
			JsonObject values = mergeOp["values"]!.AsObject();
			foreach (ListColumnSpec col in columns) {
				if (!values.ContainsKey(col.Code)) {
					string entityCol = col.Code.StartsWith("PDS_", StringComparison.Ordinal) ? col.Code[4..] : col.Code;
					values[col.Code] = JsonNode.Parse($"{{\"modelConfig\":{{\"path\":\"PDS.{entityCol}\"}}}}");
				}
			}
			body = ReplaceMarkerContent(body, vmMarker, SerializeJson(vmArray));
		}
		return body;
	}

	private static bool TryFindMarkerSpan(string body, string marker, out int contentStart, out int contentEnd) {
		string token = $"/**{marker}*/";
		int first = body.IndexOf(token, StringComparison.Ordinal);
		if (first < 0) {
			contentStart = contentEnd = -1;
			return false;
		}
		contentStart = first + token.Length;
		int second = body.IndexOf(token, contentStart, StringComparison.Ordinal);
		if (second < 0) {
			contentStart = contentEnd = -1;
			return false;
		}
		contentEnd = second;
		return true;
	}

	private static string ReplaceMarkerContent(string body, string marker, string newContent) {
		if (!TryFindMarkerSpan(body, marker, out int start, out int end))
			throw new InvalidOperationException($"Marker {marker} not found in body");
		return body[..start] + newContent + body[end..];
	}

	private static string DetectVmMarker(string body) {
		foreach (string marker in new[] { "SCHEMA_VIEW_MODEL_CONFIG_DIFF", "SCHEMA_VIEW_MODEL_CONFIG" }) {
			if (TryFindMarkerSpan(body, marker, out _, out _))
				return marker;
		}
		return null;
	}

	private static JsonNode ParseMarkerJson(string body, string marker) {
		if (!TryFindMarkerSpan(body, marker, out int start, out int end))
			throw new InvalidOperationException($"Marker {marker} not found in body");
		string content = body[start..end].Trim();
		content = Regex.Replace(content, @",(\s*[\]\}])", "$1", RegexOptions.None, RegexTimeout);
		return JsonNode.Parse(content)
			?? throw new InvalidOperationException($"Marker {marker} content parsed as null");
	}

	private static string SerializeJson(JsonNode node) {
		string raw = node.ToJsonString(IndentedOptions);
		const string prefix = "\t\t";
		string[] lines = raw.Split('\n');
		if (lines.Length <= 1)
			return raw;
		var sb = new StringBuilder(raw.Length + lines.Length * prefix.Length);
		sb.Append(lines[0]);
		for (int i = 1; i < lines.Length; i++)
			sb.Append('\n').Append(prefix).Append(lines[i]);
		return sb.ToString();
	}

	private static string DiscoverFormContainer(JsonArray viewConfigDiff) {
		var counts = new Dictionary<string, int>(StringComparer.Ordinal);
		foreach (JsonObject item in viewConfigDiff.OfType<JsonObject>()) {
			if (item["operation"]?.GetValue<string>() != "insert")
				continue;
			JsonObject values = item["values"] as JsonObject;
			if (values?["layoutConfig"] == null)
				continue;
			string type = values["type"]?.GetValue<string>() ?? string.Empty;
			if (!FormFieldTypes.Contains(type))
				continue;
			string parent = item["parentName"]?.GetValue<string>();
			if (parent != null)
				counts[parent] = counts.GetValueOrDefault(parent) + 1;
		}
		return counts.Count > 0 ? counts.MaxBy(kv => kv.Value).Key : null;
	}

	private static (int maxRow, int maxIndex) FindMaxRowIndex(JsonArray viewConfigDiff, string parentName) {
		int maxRow = 0, maxIndex = -1;
		foreach (JsonObject item in viewConfigDiff.OfType<JsonObject>()) {
			if (item["operation"]?.GetValue<string>() != "insert")
				continue;
			if (item["parentName"]?.GetValue<string>() != parentName)
				continue;
			JsonObject layout = (item["values"] as JsonObject)?["layoutConfig"] as JsonObject;
			int row = layout?["row"]?.GetValue<int>() ?? 0;
			int index = item["index"]?.GetValue<int>() ?? 0;
			if (row > maxRow) maxRow = row;
			if (index > maxIndex) maxIndex = index;
		}
		return (maxRow, maxIndex);
	}

	private static string DeriveAttrKey(FormFieldSpec field) {
		if (!string.IsNullOrWhiteSpace(field.AttrKey))
			return field.AttrKey;
		if (!string.IsNullOrWhiteSpace(field.Path)) {
			int dot = field.Path.IndexOf('.', StringComparison.Ordinal);
			if (dot > 0 && dot < field.Path.Length - 1)
				return field.Path[..dot] + "_" + field.Path[(dot + 1)..];
		}
		return field.Name ?? field.Path ?? string.Empty;
	}

	private static JsonObject BuildFormFieldInsert(FormFieldSpec field, int row, int index, string parentName) {
		string attrKey = DeriveAttrKey(field);
		var values = new JsonObject {
			["layoutConfig"] = new JsonObject {
				["column"] = 1,
				["row"] = row,
				["colSpan"] = 1,
				["rowSpan"] = 1
			},
			["type"] = field.Type,
			["label"] = field.Label ?? $"$Resources.Strings.{attrKey}",
			["labelPosition"] = "auto"
		};
		string bindingProp = field.Type == "crt.ImageInput" ? "value" : "control";
		values[bindingProp] = $"${attrKey}";
		if (field.Type == "crt.DateTimePicker")
			values["pickerType"] = field.PickerType ?? "date";
		if (field.Type == "crt.Input" && field.Multiline == true)
			values["multiline"] = true;
		if (field.Type == "crt.NumberInput" && field.DecimalPrecision.HasValue)
			values["format"] = new JsonObject { ["decimalPrecision"] = field.DecimalPrecision.Value };
		return new JsonObject {
			["operation"] = "insert",
			["name"] = attrKey,
			["values"] = values,
			["parentName"] = field.ParentName ?? parentName,
			["propertyName"] = "items",
			["index"] = index
		};
	}

	private static JsonObject FindOrCreateMergeOp(JsonArray vmArray, string[] pathSegments) {
		foreach (JsonObject item in vmArray.OfType<JsonObject>()) {
			JsonArray pathArr = item["path"] as JsonArray;
			if (pathArr == null) continue;
			bool hasAll = pathSegments.All(seg => pathArr.Any(p => p?.GetValue<string>() == seg));
			if (hasAll)
				return item;
		}
		var pathNode = new JsonArray();
		foreach (string seg in pathSegments)
			pathNode.Add(JsonValue.Create(seg));
		var mergeOp = new JsonObject {
			["operation"] = "merge",
			["path"] = pathNode,
			["values"] = new JsonObject()
		};
		vmArray.Add(mergeOp);
		return mergeOp;
	}
}
