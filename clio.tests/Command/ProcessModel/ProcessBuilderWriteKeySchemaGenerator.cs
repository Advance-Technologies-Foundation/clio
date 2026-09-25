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
		List<SyntaxNode> roots = sources.Select(source => CSharpSyntaxTree.ParseText(source).GetRoot()).ToList();
		// Every shape below is one the generator cannot model faithfully, and a silent mis-read would pass the
		// drift test - the test compares the generator with its own earlier output. So each one FAILS here,
		// naming the contract, rather than producing a schema that refuses a key the server accepts (a field,
		// a computed name, a partial class, a record) or never looks inside a collection it did not recognise.
		RecordDeclarationSyntax record = roots.SelectMany(root => root.DescendantNodes().OfType<RecordDeclarationSyntax>())
			.FirstOrDefault(declaration => HasAttribute(declaration.AttributeLists, "DataContract"));
		if (record is not null) {
			throw Unsupported(record.Identifier.Text, "is a record; only [DataContract] classes are modelled");
		}
		List<ClassDeclarationSyntax> classes = roots
			.SelectMany(root => root.DescendantNodes().OfType<ClassDeclarationSyntax>())
			.Where(declaration => HasAttribute(declaration.AttributeLists, "DataContract"))
			.ToList();
		string duplicate = classes.GroupBy(declaration => declaration.Identifier.Text, StringComparer.Ordinal)
			.FirstOrDefault(group => group.Count() > 1)?.Key;
		if (duplicate is not null) {
			throw Unsupported(duplicate, "is declared more than once (a partial class, or two namespaces)");
		}
		HashSet<string> contractNames = classes.Select(declaration => declaration.Identifier.Text)
			.ToHashSet(StringComparer.Ordinal);
		Dictionary<string, ClassDeclarationSyntax> byName = classes
			.ToDictionary(declaration => declaration.Identifier.Text, StringComparer.Ordinal);
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
		FieldDeclarationSyntax field = declaration.Members.OfType<FieldDeclarationSyntax>()
			.FirstOrDefault(candidate => HasAttribute(candidate.AttributeLists, "DataMember"));
		if (field is not null) {
			throw Unsupported(name, "carries a [DataMember] FIELD; only properties are modelled");
		}
		foreach (PropertyDeclarationSyntax property in declaration.Members.OfType<PropertyDeclarationSyntax>()) {
			AttributeSyntax dataMember = FindAttribute(property.AttributeLists, "DataMember");
			if (dataMember is null) {
				continue;
			}
			string wireName = WireName(name, property, dataMember);
			(string elementType, bool isList) = Unwrap(name, property.Type, contractNames);
			members.Add(new ContractMember(wireName, contractNames.Contains(elementType) ? elementType : null, isList));
		}
		return members;
	}

	private static (string ElementType, bool IsList) Unwrap(string contract, TypeSyntax type,
			ISet<string> contractNames) => type switch {
		NullableTypeSyntax nullable => Unwrap(contract, nullable.ElementType, contractNames),
		ArrayTypeSyntax array => (TypeName(array.ElementType), true),
		GenericNameSyntax generic when ListTypeNames.Contains(generic.Identifier.Text)
			&& generic.TypeArgumentList.Arguments.Count == 1 => (TypeName(generic.TypeArgumentList.Arguments[0]), true),
		// Any OTHER generic that carries a contract (Collection<T>, HashSet<T>, Dictionary<,>) would be taken
		// for a scalar and its items never walked - a false pass, so it is refused instead.
		GenericNameSyntax generic when generic.TypeArgumentList.Arguments
			.Any(argument => contractNames.Contains(TypeName(argument)))
			=> throw Unsupported(contract, $"holds a contract in '{generic}', a collection the generator does not model"),
		QualifiedNameSyntax qualified => Unwrap(contract, qualified.Right, contractNames),
		_ => (TypeName(type), false)
	};

	// The wire name: the literal Name argument, or the property's own name when there is none. A Name that is
	// not a literal (a const, nameof) cannot be read from syntax alone, so it is refused rather than guessed.
	private static string WireName(string contract, PropertyDeclarationSyntax property, AttributeSyntax dataMember) {
		AttributeArgumentSyntax nameArgument = dataMember.ArgumentList?.Arguments
			.FirstOrDefault(argument => argument.NameEquals?.Name.Identifier.Text == "Name");
		if (nameArgument is null) {
			return property.Identifier.Text;
		}
		return nameArgument.Expression is LiteralExpressionSyntax literal
			? literal.Token.ValueText
			: throw Unsupported(contract, $"names '{property.Identifier.Text}' with a non-literal DataMember Name");
	}

	private static InvalidOperationException Unsupported(string contract, string what) =>
		new($"Contract '{contract}' {what}. Teach ProcessBuilderWriteKeySchemaGenerator the shape before regenerating.");

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
}
