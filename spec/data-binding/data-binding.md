# data-binding

Creatio uses data-binding mechanism to attach data to a package, you can think of as small extract from a database table that is stored in a file, and upon package installation data will be inserted or updated in the database based on the content of that file. This is very useful when you want to have some default data deployed with your package, for example default settings, or reference data.


## Objective

I want to create a new command in clio cli, to create a data binding in a package. 
The command should take the following parameters:
- Package name: the name of the package where the data binding should be created.
- Schema name: the name of the entity schema that the data binding should be based on.
- values: a structure to pass values for data binding

When values are not provided, the command should create a template with empty values, and fill in the descriptor.json file based on the entity schema provided. This will allow users to easily create data bindings based on existing entity schemas, and fill in the values as needed.


Command should be able to add and remove rows from binding.



Data binding consists of several files, you can see a complete example in `/DataBindingPkg/Data/SysSettings_1` folder.

## filter.json
Assume filter.json is always empty, but needs to exist in filesystem.



## descriptor.json

You can see this file in `./packages/DataBindingPkg/Data/SysSettings_1/descriptor.json`, this file describes the entity schema that is used for data binding, as well as some metadata about how data should be inserted or updated in the database.

```json
{
  "Descriptor": {
    "UId": "c653d44c-9c7c-125d-e269-b9257b353ff9",
    "Name": "SysSettings",
    "ModifiedOnUtc": "\/Date(1773403220000)\/",
    "InstallType": 0,
    "Schema": {
      "UId": "27aeadd6-d508-4572-8061-5b55b667c902",
      "Name": "SysSettings"
    },
    "Columns": []
  }
```


This file tells creatio what is contained in the directory where the file is found.
 - "UId": "c653d44c-9c7c-125d-e269-b9257b353ff9", this property is unique to a file.
 - "Name": "SysSettings_1", has to match the containing Directory name, in this case "SysSettings_1"
 - "InstallType": 0, has the following meanings
    - 0 Installation — data is added during the first installation of the package and is updated with the package updates. New records are always added. Existing records are updated only if the “Forced update” checkbox is selected on the “Columns setting” tab. This is the default and most flexible type.
    - 1 Update existing — deprecated type. We recommend using the Installation type instead because this type has similar behavior.",
    - 2 Initial package installation — data is added only during the first installation of the package. Data is not updated or added during subsequent package updates.",
    - 3 Initial data installation — data binding is installed during the first package installation or subsequent updates only if the same binding does not already exist. Use this type to avoid unintended overwrites.",

- shema.UId maps to "uId" in json payload.
- shema.Name maps to "name" in json payload.


### Array of Columns

Every column in the entity schema is represented as an object in the "Columns" array, with the following properties:
```json
{
    "ColumnUId": "ae0e45ca-c495-4fe7-a39d-3ab7278e1617",
    "IsForceUpdate": false,
    "IsKey": true,
    "ColumnName": "Id",
    "DataTypeValueUId": "23018567-a13c-4320-8687-fd6f9e3699bd"
}
```

- ColumnUId maps 1 to 1 from json payload.
- "IsForceUpdate": false, Always use false
- "IsKey": true, maps to "primaryColumnUId" in json payload,
- ColumnName": "Id", maps to "name" in json payload.
- DataTypeValueUId maps to "dataValueType" in json payload, you can


To better understand DataValueType and its restrictions, review this file `C:\bare\Forest\core\trunk\TSBpm\Src\Lib\Terrasoft.Core\DataValueType.cs`
clio already has a type with mapping review DataValueTypeMap class.


## Data Payload
you can see this file in `./packages/DataBindingPkg/Data/SysSettings_1/data.json`, this file contains an array of objects, where each object represents a record that will be inserted or updated in the database upon package installation. Each object has the following properties:

SchemaColumnUId: this property maps to the column UId in the entity schema, it is used to identify which column the value should be inserted or updated for.
Value: this is the value that will be inserted or updated in the database for the corresponding column.
It is important to use the correct value, for instance of column is only 50 characters long and I set 51 characters then the behavior is undefined.
To better understand DataValueTypes and its restrictions review this file `C:\bare\Forest\core\trunk\TSBpm\Src\Lib\Terrasoft.Core\DataValueType.cs`



## How to get entity schema for data binding?

1. Execute POST request to the endpoint `DataService/json/SyncReply/RuntimeEntitySchemaRequest` with the body containing the name of the entity you want to create or edit a data binding for. 
You can use the following command:
`clio call-service --method POST -e <ENV_NAME> --service-path "DataService/json/SyncReply/RuntimeEntitySchemaRequest" --body '{"Name": "<Entity_Name>"}'`
this will return a json payload describing the entity schema, including its columns, their data types and UIDs, and other relevant information.

For example calling `clio call-service --method POST -e d2 --service-path "DataService/json/SyncReply/RuntimeEntitySchemaRequest" --body '{"Name": "SysSettings"}'`
returns the following payload:

```json
{
  "schema": {
    "parentUId": "2681062b-df59-4e52-89ed-f9b7dc909ab2",
    "isVirtual": false,
    "isDBView": false,
    "isTrackChangesInDB": false,
    "showInAdvancedMode": true,
    "administratedByOperations": false,
    "administratedByColumns": false,
    "administratedByRecords": false,
    "useMasterRecordRights": false,
    "masterRecordSchemaName": "",
    "columns": {
      "Items": {
        "ae0e45ca-c495-4fe7-a39d-3ab7278e1617": {
          "uId": "ae0e45ca-c495-4fe7-a39d-3ab7278e1617",
          "name": "Id",
          "caption": {
            "en-US": "Id",
            "ru-RU": "Id"
          },
          "description": {},
          "dataValueType": 0,
          "defValue": {
            "valueSourceType": 3,
            "value": "0f30e2b0-6e94-47eb-aa30-6aca36dbe90b",
            "valueSource": "03fac162-6a98-4f29-8d28-dc2f23ab48da",
            "sequenceNumberOfChars": 0
          },
          "isInherited": true,
          "isOverride": false,
          "referenceSchemaUId": null,
          "isRequired": true,
          "isVirtual": false,
          "isValueCloneable": false,
          "isMultilineText": false,
          "isAccentInsensitive": false,
          "useSeconds": false,
          "isMasked": false,
          "isFormatValidated": false,
          "isValueMasked": false,
          "isSimpleLookup": false,
          "isCascade": false,
          "usageType": 1,
          "status": 0,
          "isIndexed": false,
          "isWeakReference": false,
          "isSensitiveData": false
        },
        "e80190a5-03b2-4095-90f7-a193a960adee": {
          "uId": "e80190a5-03b2-4095-90f7-a193a960adee",
          "name": "CreatedOn",
          "caption": {
            "en-US": "Created on",
            "ru-RU": "Дата создания"
          },
          "description": {},
          "dataValueType": 7,
          "defValue": {
            "valueSourceType": 3,
            "value": "2026-03-13T17:15:57.652+01:00",
            "valueSource": "d7c295d3-3146-4ee1-ac49-3a7bd0edc45d",
            "sequenceNumberOfChars": 0
          },
          "isInherited": true,
          "isOverride": false,
          "referenceSchemaUId": null,
          "isRequired": false,
          "isVirtual": false,
          "isValueCloneable": false,
          "isMultilineText": false,
          "isAccentInsensitive": false,
          "useSeconds": false,
          "isMasked": false,
          "isFormatValidated": false,
          "isValueMasked": false,
          "isSimpleLookup": false,
          "isCascade": false,
          "usageType": 1,
          "status": 0,
          "isIndexed": false,
          "isWeakReference": false,
          "isSensitiveData": false
        },
        "ebf6bb93-8aa6-4a01-900d-c6ea67affe21": {
          "uId": "ebf6bb93-8aa6-4a01-900d-c6ea67affe21",
          "name": "CreatedBy",
          "referenceSchemaName": "Contact",
          "caption": {
            "en-US": "Created by",
            "ru-RU": "Создал"
          },
          "description": {},
          "dataValueType": 10,
          "defValue": {
            "valueSourceType": 3,
            "value": "410006e1-ca4e-4502-a9ec-e54d922d2c00",
            "valueSource": "4f367ca9-549b-4a1a-b64e-a40123f52ac0",
            "sequenceNumberOfChars": 0
          },
          "isInherited": true,
          "isOverride": false,
          "referenceSchemaUId": "16be3651-8fe2-4159-8dd0-a803d4683dd3",
          "isRequired": false,
          "isVirtual": false,
          "isValueCloneable": false,
          "isMultilineText": false,
          "isAccentInsensitive": false,
          "useSeconds": false,
          "isMasked": false,
          "isFormatValidated": false,
          "isValueMasked": false,
          "isSimpleLookup": false,
          "isCascade": false,
          "usageType": 1,
          "status": 0,
          "isIndexed": true,
          "isWeakReference": true,
          "isSensitiveData": false
        },
        "9928edec-4272-425a-93bb-48743fee4b04": {
          "uId": "9928edec-4272-425a-93bb-48743fee4b04",
          "name": "ModifiedOn",
          "caption": {
            "en-US": "Modified on",
            "ru-RU": "Дата изменения"
          },
          "description": {},
          "dataValueType": 7,
          "defValue": {
            "valueSourceType": 3,
            "value": "2026-03-13T17:15:57.652+01:00",
            "valueSource": "d7c295d3-3146-4ee1-ac49-3a7bd0edc45d",
            "sequenceNumberOfChars": 0
          },
          "isInherited": true,
          "isOverride": false,
          "referenceSchemaUId": null,
          "isRequired": false,
          "isVirtual": false,
          "isValueCloneable": false,
          "isMultilineText": false,
          "isAccentInsensitive": false,
          "useSeconds": false,
          "isMasked": false,
          "isFormatValidated": false,
          "isValueMasked": false,
          "isSimpleLookup": false,
          "isCascade": false,
          "usageType": 1,
          "status": 0,
          "isIndexed": true,
          "isWeakReference": false,
          "isSensitiveData": false
        },
        "3015559e-cbc6-406a-88af-07f7930be832": {
          "uId": "3015559e-cbc6-406a-88af-07f7930be832",
          "name": "ModifiedBy",
          "referenceSchemaName": "Contact",
          "caption": {
            "en-US": "Modified by",
            "ru-RU": "Изменил"
          },
          "description": {},
          "dataValueType": 10,
          "defValue": {
            "valueSourceType": 3,
            "value": "410006e1-ca4e-4502-a9ec-e54d922d2c00",
            "valueSource": "4f367ca9-549b-4a1a-b64e-a40123f52ac0",
            "sequenceNumberOfChars": 0
          },
          "isInherited": true,
          "isOverride": false,
          "referenceSchemaUId": "16be3651-8fe2-4159-8dd0-a803d4683dd3",
          "isRequired": false,
          "isVirtual": false,
          "isValueCloneable": false,
          "isMultilineText": false,
          "isAccentInsensitive": false,
          "useSeconds": false,
          "isMasked": false,
          "isFormatValidated": false,
          "isValueMasked": false,
          "isSimpleLookup": false,
          "isCascade": false,
          "usageType": 1,
          "status": 0,
          "isIndexed": true,
          "isWeakReference": true,
          "isSensitiveData": false
        },
        "736c30a7-c0ec-4fa9-b034-2552b319b633": {
          "uId": "736c30a7-c0ec-4fa9-b034-2552b319b633",
          "name": "Name",
          "caption": {
            "en-US": "Name",
            "ru-RU": "Название"
          },
          "description": {},
          "dataValueType": 28,
          "defValue": {
            "valueSourceType": 0,
            "valueSource": "",
            "sequenceNumberOfChars": 0
          },
          "isInherited": true,
          "isOverride": true,
          "referenceSchemaUId": null,
          "isRequired": true,
          "isVirtual": false,
          "isValueCloneable": true,
          "isMultilineText": false,
          "isAccentInsensitive": false,
          "useSeconds": false,
          "isMasked": false,
          "isFormatValidated": false,
          "isValueMasked": false,
          "isSimpleLookup": false,
          "isCascade": false,
          "usageType": 0,
          "status": 0,
          "isIndexed": false,
          "isWeakReference": false,
          "isSensitiveData": false
        },
        "9e53fd7c-dde4-4502-a64c-b9e34148108b": {
          "uId": "9e53fd7c-dde4-4502-a64c-b9e34148108b",
          "name": "Description",
          "caption": {
            "en-US": "Description",
            "ru-RU": "Описание"
          },
          "description": {},
          "dataValueType": 29,
          "defValue": {
            "valueSourceType": 0,
            "valueSource": "",
            "sequenceNumberOfChars": 0
          },
          "isInherited": true,
          "isOverride": true,
          "referenceSchemaUId": null,
          "isRequired": false,
          "isVirtual": false,
          "isValueCloneable": true,
          "isMultilineText": false,
          "isAccentInsensitive": false,
          "useSeconds": false,
          "isMasked": false,
          "isFormatValidated": false,
          "isValueMasked": false,
          "isSimpleLookup": false,
          "isCascade": false,
          "usageType": 0,
          "status": 0,
          "isIndexed": false,
          "isWeakReference": false,
          "isSensitiveData": false
        },
        "13aad544-ec30-4e76-a373-f0cff3202e24": {
          "uId": "13aad544-ec30-4e76-a373-f0cff3202e24",
          "name": "Code",
          "caption": {
            "en-US": "Code",
            "ru-RU": "Код"
          },
          "description": {},
          "dataValueType": 27,
          "defValue": {
            "valueSourceType": 0,
            "valueSource": "",
            "sequenceNumberOfChars": 0
          },
          "isInherited": true,
          "isOverride": true,
          "referenceSchemaUId": null,
          "isRequired": true,
          "isVirtual": false,
          "isValueCloneable": true,
          "isMultilineText": false,
          "isAccentInsensitive": false,
          "useSeconds": false,
          "isMasked": false,
          "isFormatValidated": false,
          "isValueMasked": false,
          "isSimpleLookup": false,
          "isCascade": false,
          "usageType": 0,
          "status": 0,
          "isIndexed": false,
          "isWeakReference": false,
          "isSensitiveData": false
        },
        "f7960a8a-1fd4-41d2-997a-fd78ea60075f": {
          "uId": "f7960a8a-1fd4-41d2-997a-fd78ea60075f",
          "name": "ValueTypeName",
          "caption": {
            "en-US": "Type",
            "ru-RU": "Тип"
          },
          "description": {},
          "dataValueType": 28,
          "defValue": {
            "valueSourceType": 0,
            "valueSource": "",
            "sequenceNumberOfChars": 0
          },
          "isInherited": false,
          "isOverride": false,
          "referenceSchemaUId": null,
          "isRequired": false,
          "isVirtual": false,
          "isValueCloneable": true,
          "isMultilineText": false,
          "isAccentInsensitive": false,
          "useSeconds": false,
          "isMasked": false,
          "isFormatValidated": false,
          "isValueMasked": false,
          "isSimpleLookup": false,
          "isCascade": false,
          "usageType": 0,
          "status": 0,
          "isIndexed": false,
          "isWeakReference": false,
          "isSensitiveData": false
        },
        "3fd9f15d-37f4-4f91-a6cf-f222d54e342d": {
          "uId": "3fd9f15d-37f4-4f91-a6cf-f222d54e342d",
          "name": "SysFolder",
          "referenceSchemaName": "SysSettingsFolder",
          "caption": {
            "en-US": "Folder",
            "ru-RU": "Группа"
          },
          "description": {},
          "dataValueType": 10,
          "defValue": {
            "valueSourceType": 0,
            "valueSource": "",
            "sequenceNumberOfChars": 0
          },
          "isInherited": false,
          "isOverride": false,
          "referenceSchemaUId": "81996156-45e6-40de-931e-6ddc6f75cd7e",
          "isRequired": false,
          "isVirtual": false,
          "isValueCloneable": true,
          "isMultilineText": false,
          "isAccentInsensitive": false,
          "useSeconds": false,
          "isMasked": false,
          "isFormatValidated": false,
          "isValueMasked": false,
          "isSimpleLookup": false,
          "isCascade": false,
          "usageType": 2,
          "status": 0,
          "isIndexed": true,
          "isWeakReference": false,
          "isSensitiveData": false
        },
        "764cd95a-59b3-4060-b17f-2797d5c76aaa": {
          "uId": "764cd95a-59b3-4060-b17f-2797d5c76aaa",
          "name": "IsPersonal",
          "caption": {
            "en-US": "Personal",
            "ru-RU": "Персональная"
          },
          "description": {},
          "dataValueType": 12,
          "defValue": {
            "valueSourceType": 0,
            "valueSource": "",
            "sequenceNumberOfChars": 0
          },
          "isInherited": false,
          "isOverride": false,
          "referenceSchemaUId": null,
          "isRequired": false,
          "isVirtual": false,
          "isValueCloneable": true,
          "isMultilineText": false,
          "isAccentInsensitive": false,
          "useSeconds": false,
          "isMasked": false,
          "isFormatValidated": false,
          "isValueMasked": false,
          "isSimpleLookup": false,
          "isCascade": false,
          "usageType": 0,
          "status": 0,
          "isIndexed": false,
          "isWeakReference": false,
          "isSensitiveData": false
        },
        "eb971f1a-dd41-4668-99aa-1f2a6b61a1b9": {
          "uId": "eb971f1a-dd41-4668-99aa-1f2a6b61a1b9",
          "name": "IsCacheable",
          "caption": {
            "en-US": "Cached",
            "ru-RU": "Кешируется"
          },
          "description": {},
          "dataValueType": 12,
          "defValue": {
            "valueSourceType": 1,
            "value": true,
            "valueSource": "True",
            "sequenceNumberOfChars": 0
          },
          "isInherited": false,
          "isOverride": false,
          "referenceSchemaUId": null,
          "isRequired": false,
          "isVirtual": false,
          "isValueCloneable": true,
          "isMultilineText": false,
          "isAccentInsensitive": false,
          "useSeconds": false,
          "isMasked": false,
          "isFormatValidated": false,
          "isValueMasked": false,
          "isSimpleLookup": false,
          "isCascade": false,
          "usageType": 0,
          "status": 0,
          "isIndexed": false,
          "isWeakReference": false,
          "isSensitiveData": false
        },
        "3fabd836-6a53-4d8d-9069-6df88d9dae1e": {
          "uId": "3fabd836-6a53-4d8d-9069-6df88d9dae1e",
          "name": "ProcessListeners",
          "caption": {
            "en-US": "Active processes",
            "ru-RU": "Активные процессы"
          },
          "description": {},
          "dataValueType": 4,
          "defValue": {
            "valueSourceType": 0,
            "valueSource": "",
            "sequenceNumberOfChars": 0
          },
          "isInherited": true,
          "isOverride": false,
          "referenceSchemaUId": null,
          "isRequired": false,
          "isVirtual": false,
          "isValueCloneable": true,
          "isMultilineText": false,
          "isAccentInsensitive": false,
          "useSeconds": false,
          "isMasked": false,
          "isFormatValidated": false,
          "isValueMasked": false,
          "isSimpleLookup": false,
          "isCascade": false,
          "usageType": 2,
          "status": 0,
          "isIndexed": false,
          "isWeakReference": false,
          "isSensitiveData": false
        },
        "b2280617-35f9-4006-8634-c557a2e121c2": {
          "uId": "b2280617-35f9-4006-8634-c557a2e121c2",
          "name": "ReferenceSchemaUId",
          "caption": {
            "en-US": "Identifier of system setting lookup",
            "ru-RU": "Идентификатор справочника системной настройки"
          },
          "description": {},
          "dataValueType": 0,
          "defValue": {
            "valueSourceType": 0,
            "valueSource": "",
            "sequenceNumberOfChars": 0
          },
          "isInherited": false,
          "isOverride": false,
          "referenceSchemaUId": null,
          "isRequired": false,
          "isVirtual": false,
          "isValueCloneable": true,
          "isMultilineText": false,
          "isAccentInsensitive": false,
          "useSeconds": false,
          "isMasked": false,
          "isFormatValidated": false,
          "isValueMasked": false,
          "isSimpleLookup": false,
          "isCascade": false,
          "usageType": 0,
          "status": 0,
          "isIndexed": true,
          "isWeakReference": false,
          "isSensitiveData": false
        },
        "64fadca1-ab7c-4471-9d25-68599acc729b": {
          "uId": "64fadca1-ab7c-4471-9d25-68599acc729b",
          "name": "IsSSPAvailable",
          "caption": {
            "en-US": "Allow for portal users",
            "ru-RU": "Разрешить для пользователей портала"
          },
          "description": {},
          "dataValueType": 12,
          "defValue": {
            "valueSourceType": 0,
            "valueSource": "",
            "sequenceNumberOfChars": 0
          },
          "isInherited": false,
          "isOverride": false,
          "referenceSchemaUId": null,
          "isRequired": false,
          "isVirtual": false,
          "isValueCloneable": true,
          "isMultilineText": false,
          "isAccentInsensitive": false,
          "useSeconds": false,
          "isMasked": false,
          "isFormatValidated": false,
          "isValueMasked": false,
          "isSimpleLookup": false,
          "isCascade": false,
          "usageType": 0,
          "status": 0,
          "isIndexed": false,
          "isWeakReference": false,
          "isSensitiveData": false
        }
      }
    },
    "primaryColumnUId": "ae0e45ca-c495-4fe7-a39d-3ab7278e1617",
    "primaryDisplayColumnUId": "736c30a7-c0ec-4fa9-b034-2552b319b633",
    "primaryColorColumnUId": null,
    "hierarchyColumnUId": null,
    "createdOnColumnUId": "e80190a5-03b2-4095-90f7-a193a960adee",
    "createdByColumnUId": "ebf6bb93-8aa6-4a01-900d-c6ea67affe21",
    "modifiedOnColumnUId": "9928edec-4272-425a-93bb-48743fee4b04",
    "modifiedByColumnUId": "3015559e-cbc6-406a-88af-07f7930be832",
    "uId": "27aeadd6-d508-4572-8061-5b55b667c902",
    "realUId": "27aeadd6-d508-4572-8061-5b55b667c902",
    "name": "SysSettings",
    "caption": {
      "en-US": "System setting",
      "ru-RU": "Системная настройка"
    },
    "description": {},
    "extendParent": false,
    "packageUId": null
  },
  "success": true
}
```



## Localization
`./packages/DataBindingPkg/Data/SysSettings_1/Localization/en-US.json` this file contains localization for the data binding, it is used to provide localized values for the columns that are of type LocalizableString. For instance if i wanted to provide russian localization for the Name column in SysSettings entity, I would add "Name": "Тэстовая настройка, это описание" to the data.ru-RU.json file.



## How to read this payload?

When you see "primaryColumnUId" then it maps to "IsKey": true, otherwise assume the column is not a key ("IsKey": false). There can only be one key per entity.
"primaryColumnUId": "ae0e45ca-c495-4fe7-a39d-3ab7278e1617",


- ColumnUId maps 1 to 1 from json payload.
- "IsForceUpdate": false, Always use false
- ColumnName": "IsPersonal", maps to "name" in json payload.


