## Task: read, update and delete business rules MCP tools

## Description:

### New tools:

- `read-entity-business-rules` - reads all business rules for specified entity
- `read-page-business-rules` - reads all business rules for specified page
- `update-entity-business-rules` - updates business rules for specified entity (matches business rules to update by name)
- `update-page-business-rules` - updates business rules for specified page (matches business rules to update by name)
- `delete-entity-business-rules` - delete business rules by specified names for specified entity
- `delete-page-business-rules` - delete business rules by specified names for specified page

### Rule contract

Reuse rule contract that already exists for `create-entity-business-rules` and `create-page-business-rules`.

The main change compared to existing contract is `uId` of conditions, expressions and actions.
They're needed because business rules may be replaced in new package (e.g. adding new actions to existing business rule but preserving condition) and using uIds for every block allows to system to store short diff (which contain only blocks that really were changed instead of full new rules).

Also read/update contract should have rule `name` to identify rule. `uId` for rule is not required because `name` is already unique identifier (just read existing `uId` of rule that matches by `name`)

Also read/update contract should have `enabled` property.

Existing contract of backend:
```json
{
  "typeName": "Terrasoft.Core.BusinessRules.BusinessRules",
  "rules": [
    {
      "typeName": "Terrasoft.Core.BusinessRules.BusinessRule",
      "uId": "e73c78a2-e852-4d34-a4c4-f50cbe6578d6",
      "cases": [
        {
          "typeName": "Terrasoft.Core.BusinessRules.Models.BusinessRuleCase",
          "uId": "059d3c37-b741-407a-a6ff-973bd5b4e7b1",
          "condition": {
            "typeName": "Terrasoft.Core.BusinessRules.Models.Conditions.BusinessRuleGroupCondition",
            "uId": "c2729f74-5c3b-4e88-8f47-d603d0b76a56",
            "logicalOperation": 1,
            "conditions": [
              {
                "typeName": "Terrasoft.Core.BusinessRules.Models.Conditions.BusinessRuleCondition",
                "uId": "b8f788e2-3530-41cc-8a0c-6a256f179fa2",
                "leftExpression": {
                  "typeName": "Terrasoft.Core.BusinessRules.Models.Expressions.BusinessRuleAttributeExpression",
                  "uId": "e277d35c-1f25-4fce-b2db-1bf86390bfeb",
                  "type": "AttributeValue",
                  "path": "Name"
                },
                "rightExpression": {
                  "typeName": "Terrasoft.Core.BusinessRules.Models.Expressions.BusinessRuleValueExpression",
                  "uId": "81b8b8ea-36fa-447a-85d5-bf7e13649628",
                  "type": "Const",
                  "value": "Readonly"
                },
                "comparisonType": 2
              }
            ]
          },
          "actions": [
            {
              "typeName": "Terrasoft.Core.BusinessRules.Models.Actions.BusinessRuleActionReadonlyElement",
              "uId": "bee4758c-f371-497b-9624-127b5cf8f2b4",
              "enabled": false,
              "items": "Name"
            }
          ]
        }
      ],
      "triggers": [
        {
          "typeName": "Terrasoft.Core.BusinessRules.Models.Trigger",
          "uId": "c334b501-8df3-4763-84bd-9ce115aaa919",
          "name": "Name"
        }
      ],
      "name": "BusinessRule_1c48625",
      "enabled": true
    }
  ]
}
```

Existing rule contract for create tools

```json
{
  "caption": "Readonly Name",
  "condition": {
    "logicalOperation": "AND",
    "conditions": [
      {
        "leftExpression": { "type": "AttributeValue", "path": "Name" },
        "comparisonType": "equal",
        "rightExpression": { "type": "Const", "value": "Readonly" }
      }
    ]
  },
  "actions": [
    {
      "type": "make-read-only",
      "items": ["Name"]
    }
  ]
}
```

New contract for update tool (input) and for read tool (result):

```json
{
  "caption": "Readonly Name",
  "condition": {
    "logicalOperation": "AND",
    "conditions": [
      {
        "uId": "b8f788e2-3530-41cc-8a0c-6a256f179fa2",
        "leftExpression": {
          "uId": "e277d35c-1f25-4fce-b2db-1bf86390bfeb",
          "type": "AttributeValue",
          "path": "Name"
        },
        "comparisonType": "equal",
        "rightExpression": {
          "uId": "81b8b8ea-36fa-447a-85d5-bf7e13649628",
          "type": "Const",
          "value": "Readonly"
        }
      }
    ]
  },
  "actions": [
    {
      "uId": "c334b501-8df3-4763-84bd-9ce115aaa919",
      "type": "make-read-only",
      "items": ["Name"]
    }
  ],
  "name": "BusinessRule_1c48625",
  "enabled": true
}
```

I think it's ok to add these fields `name`, `uId` and `enabled` in existing contracts but make them optional on contract level, do not require for create tool, but require for update.

### Context
Check existing specification in `./spec/business-rules/`.
Creatio C# core repository: C:\Projects\core
Creatio UI repo: C:\Projects\creatio-ui
Creatio packages repo: C:\Projects\PackageStore

## Refinement
Review task and description and check if everything is clear and sound. If no ask user clarifying questions.

## Testing
Create testing-checklist.md and add there combinations of different rules to cover maximum scenarios.
Here what comes on my mind what should be checked but I suppose it's not the full list and you can come up with additional cases:
- all new tools
- entity & page business rules
- different condition types
- different data value types:
    - text
    - boolean
    - number
    - GUID
    - lookup
- different action types:
    - make readonly
    - make editable
    - make optional
    - make required
    - apply filter
    - apply static filter (this one may be difficult)
    - set value
        - const
        - attribute
        - formula (this one also may be difficult)

Check all cases in a separate clean claude code instance using /test-in-clean-claude skill on local Creatio site: localhost:8080 (Supervisor/Supervisor)

If something does not work make changes in clio, rebuild and test again. Iterate until everything work.
If you're stuck or some changes are too expensive notify user.

## Review
When everything works you can commit changes in the branch and then run /pr-review and /doc-review skills, but in separate subagents to not be biased.