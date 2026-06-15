using System;
using System.Collections.Generic;
using System.Linq;
using EventType = Clio.Command.ProcessModel.ManagerMap.EventType;

namespace Clio.Command.ProcessModel;

/// <inheritdoc cref="IProcessGraphExtractor" />
public sealed class ProcessGraphExtractor : IProcessGraphExtractor {

	private static readonly HashSet<EventType> FlowTypes = [
		EventType.SequenceFlow, EventType.ConditionalFlow, EventType.DefFlow
	];

	/// <inheritdoc />
	public ProcessDescription Extract(ProcessSchemaResponse schema, string culture) {
		ArgumentNullException.ThrowIfNull(schema);
		culture ??= "en-US";

		Schema schemaInfo = schema.Schema;
		MetaDataSchema metaData = schemaInfo?.MetaDataSchema;
		List<FlowElement> flowElements = metaData?.FlowElements ?? [];

		List<ProcessDescriptionElement> elements = flowElements
			.Where(fe => !FlowTypes.Contains(fe.EventType))
			.Select(fe => new ProcessDescriptionElement(
				Id: fe.UId.ToString(),
				DataId: fe.EventType.ToString(),
				Type: ManagerMap.ResolveRole(fe.EventType).ToString(),
				Label: Localize(fe.Captions, culture) ?? fe.Name,
				Parameters: MapElementParameters(fe.Parameters)))
			.ToList();

		List<ProcessDescriptionFlow> flows = flowElements
			.Where(fe => FlowTypes.Contains(fe.EventType))
			.Select(fe => new ProcessDescriptionFlow(
				Source: fe.SourceRefUId?.ToString(),
				Target: fe.TargetRefUId?.ToString(),
				Kind: FlowKind(fe.EventType)))
			.ToList();

		List<ProcessDescriptionParameter> parameters = (metaData?.Parameters ?? [])
			.Select(p => new ProcessDescriptionParameter(
				Name: p.Name,
				Type: p.DataValueTypeResolved?.Name ?? "object",
				Direction: p.Direction.ToString(),
				Caption: Localize(p.Captions, culture)))
			.ToList();

		string caption = Localize(schemaInfo?.Caption, culture) ?? schemaInfo?.Name;
		return new ProcessDescription(schemaInfo?.Name, caption, (schemaInfo?.UId ?? Guid.Empty).ToString(),
			elements, flows, parameters);
	}

	private static IReadOnlyList<ProcessDescriptionParameter> MapElementParameters(List<FlowElementParameter> parameters) =>
		(parameters ?? [])
			.Select(p => new ProcessDescriptionParameter(
				Name: p.Name,
				Type: p.DataValueTypeResolved?.Name ?? "object",
				Direction: p.Direction.ToString(),
				Caption: null))
			.ToList();

	private static string FlowKind(EventType eventType) => eventType switch {
		EventType.ConditionalFlow => "conditional",
		EventType.DefFlow => "default",
		_ => "sequence"
	};

	private static string Localize(IReadOnlyDictionary<string, string> captions, string culture) {
		if (captions is null || captions.Count == 0) {
			return null;
		}
		if (captions.TryGetValue(culture, out string value) && !string.IsNullOrWhiteSpace(value)) {
			return value;
		}
		return captions.Values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
	}
}
