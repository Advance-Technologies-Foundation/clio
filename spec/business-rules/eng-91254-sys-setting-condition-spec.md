# ENG-91254 — System setting as a business-rule condition operand

## Summary

Adds a new business-rule condition operand source, **`SysSetting`**, so a coding agent can configure
page and entity business-rule conditions that compare against a Creatio **system setting** value
(e.g. hide a control when a Boolean setting is enabled). Modeled on the existing `SysValue`
(system-variable) operand, with one difference: a system setting's type is open-ended, so it is
resolved from the target environment rather than a fixed catalog.

Jira: <https://creatio.atlassian.net/browse/ENG-91254>

## Scope

- Page **and** entity business rules (`create/update/read-page-business-rules` and the entity twins).
- A `SysSetting` operand may appear on **either** side of a condition, paired with any other operand
  (`AttributeValue`, `Const`, `SysValue`, or another `SysSetting`).
- Right-operand constants of any type are supported; a `Const` inherits its data value type from the
  operand it is compared against (a Boolean setting compares against a JSON `true`/`false`).
- **Out of scope** (separate ticket): comparing a system setting with a page data-source field.

## Contract

Friendly operand (MCP/CLI input):

```json
{ "type": "SysSetting", "sysSettingName": "DisableEquipmentDelivery" }
```

Persisted platform metadata (produced by the converter, resolved type shown):

```json
{
  "typeName": "Terrasoft.Core.BusinessRules.Models.Expressions.BusinessRuleSysSettingExpression",
  "type": "SysSetting",
  "dataValueTypeName": "Boolean",
  "referenceSchemaName": "",
  "sysSettingName": "DisableEquipmentDelivery"
}
```

`sysSettingName` is the setting **code**. There are no operand-pairing restrictions: comparison
compatibility is purely type-based (`ValidateComparison`), so a future data-source-field operand will
pair with `SysSetting` automatically.

## Type resolution

`SysSettingConditionOperandResolver` (over `ISysSettingsManager`) resolves each referenced setting by
code from the environment and maps its value type to a business-rule data value type:

- `Text / ShortText / MediumText / LongText / MaxSizeText` → kept (canonical `CreatioDataValueType` names).
- `Boolean / Integer / Float / Money / Date / Time / DateTime` → 1:1.
- alias `Decimal` → `Float`, alias `Currency` → `Money`.
- `Lookup` → `Lookup` + `referenceSchemaName` (reverse-resolved from `ReferenceSchemaUId`).
- `Binary`, `SecureText` → rejected (non-comparable / secret-leak).
- unknown code / unresolved reference schema → rejected with a clear error.

The resolver runs once per rule in the service, producing a `sysSettingName → descriptor` map that is
threaded into the pure validator and `SimpleToFull` converter alongside the attribute map, keeping
those components free of environment I/O.

## Touched files

- Model/constants: `BusinessRuleModels.cs`, `BusinessRuleConstants.cs`, `BusinessRuleMetadataDtos.cs`.
- Resolver: `SysSettingOperandDescriptor.cs`, `SysSettingConditionOperandResolver.cs`;
  `ISysSettingsManager.GetSysSettingTypeByCode`.
- Converters: `Converters/SimpleToFullBusinessRuleConverter.cs`, `Converters/FullToSimpleBusinessRuleConverter.cs`.
- Validator: `BusinessRuleValidator.cs`, `PageBusinessRuleValidator.cs`.
- Services: `PageBusinessRuleService.cs`, `EntityBusinessRuleService.cs`; DI in `BindingsModule.cs`.
- MCP: `ToolContractGetTool.cs`, `Resources/BusinessRulesGuidanceResource.cs`.

## Comparison compatibility (cross-subtype)

Two typed operands are compatible when they share the same data value type **or the same text/numeric
family**. Most system settings carry the bare `Text` type while entity/page string columns are a text
subtype (`ShortText`, `MediumText`, …), and `Integer`/`Float`/`Money` are all numeric — so `ValidateComparison`
treats those cross-subtype pairs as compatible (a `Text` setting **equal** a `ShortText` attribute is valid).
`DateTime` subtypes (`Date`/`Time`/`DateTime`) and `Lookup` are kept **exact** (Lookup also matches on
reference schema), since mixing those is not a meaningful comparison. The platform's business-rule engine
accepts the relaxed pairs — verified on a live environment by persisting a `Text`-setting `equal`
`ShortText`-attribute page rule (`succeeded: 1`, read-back confirmed).

## Verification

- Unit: `SimpleToFullBusinessRuleConverterTests`, `FullToSimpleBusinessRuleConverterTests`,
  `BusinessRuleValidatorTests`, `SysSettingConditionOperandResolverTests`.
- E2E: `PageBusinessRuleToolE2ETests`, `EntityBusinessRuleToolE2ETests` (sys-setting create → read-back).
- Real environment (`/test-clio-in-clean-claude`): the Jira use cases — hide `Shipping address` when
  `DisableEquipmentDelivery` is enabled; make a field read-only when a setting equals a page attribute.
