using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Clio.Tests.Command.ProcessModel;

/// <summary>
/// Rebuilds <c>process-builder-write-keys.schema.json</c> from the CrtProcessBuilder <c>[DataContract]</c>
/// sources. Test-side on purpose: the generator runs where the drift check runs, against the archive the test
/// output carries, and clio's runtime needs only the file it produces.
/// </summary>
/// <remarks>
/// SYNTAX only - no semantic model, so no reference assemblies for the platform types the contracts sit
/// beside. That is enough because the contracts are a closed tree: every member is a scalar, a string array, a
/// <c>List&lt;T&gt;</c> or a nested contract, and the one base type (<c>FilterDescriptor : FilterGroupDescriptor</c>)
/// is itself a contract in the same files. A member whose type is not a contract is emitted as <c>{}</c>: the
/// schema constrains KEYS, and a wrong value type is refused loudly by the server's deserializer already.
/// </remarks>
internal static class ProcessBuilderWriteKeySchemaGenerator {

	/// <summary>The create-business-process descriptor: what clio wraps under <c>request</c> for BuildProcess.</summary>
	internal const string CreateDescriptorRoot = "BuildProcessRequest";

	/// <summary>One item of <c>operations[]</c>, for ModifyProcess and ModifyProcessAsNewVersion alike.</summary>
	internal const string ModifyOperationRoot = "ProcessOperationDescriptor";

	private static readonly HashSet<string> ListTypeNames = new(StringComparer.Ordinal) {
		"List", "IList", "ICollection", "IEnumerable", "IReadOnlyList", "IReadOnlyCollection"
	};

	/// <summary>A contract member: its wire name, the contract it holds (or null for a scalar), and whether it is a list.</summary>
	internal sealed record ContractMember(string Name, string ContractType, bool IsList);

	/// <summary>Parses every <c>[DataContract]</c> class in the sources, base members flattened in.</summary>
	internal static IReadOnlyDictionary<string, IReadOnlyList<ContractMember>> ReadContracts(
			IEnumerable<string> sources) {
		List<ClassDeclarationSyntax> classes = sources
			.Select(source => CSharpSyntaxTree.ParseText(source).GetRoot())
			.SelectMany(root => root.DescendantNodes().OfType<ClassDeclarationSyntax>())
			.Where(declaration => HasAttribute(declaration.AttributeLists, "DataContract"))
			.ToList();
		HashSet<string> contractNames = classes.Select(declaration => declaration.Identifier.Text)
			.ToHashSet(StringComparer.Ordinal);
		Dictionary<string, ClassDeclarationSyntax> byName = classes
			.GroupBy(declaration => declaration.Identifier.Text, StringComparer.Ordinal)
			.ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
		return byName.Keys.ToDictionary(name => name,
			name => (IReadOnlyList<ContractMember>)CollectMembers(name, byName, contractNames),
			StringComparer.Ordinal);
	}

	/// <summary>Builds the schema document for every contract reachable from the two write roots.</summary>
	internal static JsonObject Generate(IReadOnlyDictionary<string, IReadOnlyList<ContractMember>> contracts) {
		SortedSet<string> reachable = new(StringComparer.Ordinal);
		Queue<string> pending = new([CreateDescriptorRoot, ModifyOperationRoot]);
		while (pending.Count > 0) {
			string name = pending.Dequeue();
			if (!contracts.ContainsKey(name)) {
				throw new InvalidOperationException(
					$"Write contract '{name}' is referenced but not declared in the contract sources.");
			}
			if (!reachable.Add(name)) {
				continue;
			}
			foreach (ContractMember member in contracts[name].Where(member => member.ContractType is not null)) {
				pending.Enqueue(member.ContractType);
			}
		}
		JsonObject definitions = [];
		foreach (string name in reachable) {
			JsonObject properties = [];
			foreach (ContractMember member in contracts[name].OrderBy(member => member.Name, StringComparer.Ordinal)) {
				properties[member.Name] = MemberSchema(member);
			}
			definitions[name] = new JsonObject {
				["type"] = new JsonArray("object", "null"),
				["additionalProperties"] = false,
				["properties"] = properties
			};
		}
		return new JsonObject {
			["$schema"] = "https://json-schema.org/draft/2020-12/schema",
			["$id"] = "https://github.com/Advance-Technologies-Foundation/clio/process-builder-write-keys.schema.json",
			["title"] = "CrtProcessBuilder write-contract keys",
			["description"] = "GENERATED - do not edit. The keys the CrtProcessBuilder ProcessDesignService accepts, "
				+ "one definition per [DataContract] class reachable from the create descriptor and a modify "
				+ "operation, read from Files/src/cs/Contracts/*.cs in clio/CrtProcessBuilder/CrtProcessBuilder.gz. "
				+ "Keys only: a scalar member is {}. Regenerate with CLIO_REGENERATE_PROCESS_BUILDER_KEY_SCHEMA=1 "
				+ "and the ProcessBuilderWriteKeySchemaTests fixture.",
			["$ref"] = $"#/$defs/{CreateDescriptorRoot}",
			["x-modifyOperation"] = $"#/$defs/{ModifyOperationRoot}",
			["$defs"] = definitions
		};
	}

	/// <summary>The canonical text of a generated document: indented, LF line ends, trailing newline.</summary>
	internal static string Serialize(JsonObject schema) =>
		schema.ToJsonString(new JsonSerializerOptions {
			WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
		}).Replace("\r\n", "\n") + "\n";

	private static JsonObject MemberSchema(ContractMember member) {
		if (member.ContractType is null) {
			return [];
		}
		JsonObject reference = new() { ["$ref"] = $"#/$defs/{member.ContractType}" };
		return member.IsList
			? new JsonObject { ["type"] = new JsonArray("array", "null"), ["items"] = reference }
			: reference;
	}

	private static List<ContractMember> CollectMembers(string name,
			IReadOnlyDictionary<string, ClassDeclarationSyntax> byName, ISet<string> contractNames) {
		ClassDeclarationSyntax declaration = byName[name];
		List<ContractMember> members = [];
		string baseContract = declaration.BaseList?.Types
			.Select(baseType => TypeName(baseType.Type))
			.FirstOrDefault(contractNames.Contains);
		if (baseContract is not null) {
			members.AddRange(CollectMembers(baseContract, byName, contractNames));
		}
		foreach (PropertyDeclarationSyntax property in declaration.Members.OfType<PropertyDeclarationSyntax>()) {
			AttributeSyntax dataMember = FindAttribute(property.AttributeLists, "DataMember");
			if (dataMember is null) {
				continue;
			}
			string wireName = NamedArgument(dataMember, "Name") ?? property.Identifier.Text;
			(string elementType, bool isList) = Unwrap(property.Type);
			members.Add(new ContractMember(wireName, contractNames.Contains(elementType) ? elementType : null, isList));
		}
		return members;
	}

	private static (string ElementType, bool IsList) Unwrap(TypeSyntax type) => type switch {
		NullableTypeSyntax nullable => Unwrap(nullable.ElementType),
		ArrayTypeSyntax array => (TypeName(array.ElementType), true),
		GenericNameSyntax generic when ListTypeNames.Contains(generic.Identifier.Text)
			&& generic.TypeArgumentList.Arguments.Count == 1 => (TypeName(generic.TypeArgumentList.Arguments[0]), true),
		QualifiedNameSyntax qualified => Unwrap(qualified.Right),
		_ => (TypeName(type), false)
	};

	private static string TypeName(TypeSyntax type) => type switch {
		QualifiedNameSyntax qualified => TypeName(qualified.Right),
		SimpleNameSyntax simple => simple.Identifier.Text,
		NullableTypeSyntax nullable => TypeName(nullable.ElementType),
		_ => type.ToString()
	};

	private static bool HasAttribute(SyntaxList<AttributeListSyntax> lists, string name) =>
		FindAttribute(lists, name) is not null;

	private static AttributeSyntax FindAttribute(SyntaxList<AttributeListSyntax> lists, string name) =>
		lists.SelectMany(list => list.Attributes)
			.FirstOrDefault(attribute => TypeName(attribute.Name) is { } attributeName
				&& (attributeName == name || attributeName == name + "Attribute"));

	private static string NamedArgument(AttributeSyntax attribute, string argumentName) =>
		attribute.ArgumentList?.Arguments
			.Where(argument => argument.NameEquals?.Name.Identifier.Text == argumentName)
			.Select(argument => argument.Expression)
			.OfType<LiteralExpressionSyntax>()
			.Select(literal => literal.Token.ValueText)
			.FirstOrDefault();
}
