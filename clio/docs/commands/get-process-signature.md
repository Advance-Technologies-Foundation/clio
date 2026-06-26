# get-process-signature

## Command Type

    Development commands

## Name

get-process-signature (alias: gps) - Read the input/output parameter signature of a Creatio business process

## Description

Resolves a business process by its code (schema Name) OR its display caption and
returns its parameter signature: for each parameter the CODE (name), caption, CLR
type, dataValueTypeId, direction, and lookup reference schema. Use it before
authoring a run-process button (crt.RunBusinessProcessRequest): the parameter CODE
— not the caption — must be the key in processParameters / parameterMappings /
recordIdProcessParameterName, otherwise the platform silently drops the value.

## Options

```bash
ProcessCode (pos. 0)         The process code (schema Name) or caption to resolve, as
                             it appears in the process designer

--process-code               The process code (schema Name) or caption to resolve
                             (named form of the positional argument)

--process-name               Hidden backward-compat alias for --process-code

--culture               -x   Culture used to resolve localized parameter captions
(default: en-US)

--Environment           -e   Environment name

--uri                        Application URI

--Login                 -l   User login

--Password              -p   User password

--clientId                   OAuth client ID

--clientSecret               OAuth client secret

--authAppUri                 OAuth authentication app URI
```

## Notes

You may pass the value the user gave — the display caption (for example
"Business process 1") or the schema code (for example UsrProcess_e629820). The
command resolves both and echoes the resolved processCode; put that code into the
button's processName.

When a caption matches more than one process the command returns a failure listing
the candidate codes — re-run with the exact code.

The legacy `--process-name` option remains as a hidden backward-compat alias for
`--process-code`; new usage should prefer `--process-code` (or the positional argument).

## Examples

```bash
Read a process signature by code:
clio get-process-signature UsrProcess_e629820 -e dev

Read a process signature by display caption:
clio get-process-signature "Business process 1" -e dev
```

## Reporting Bugs

    https://github.com/Advance-Technologies-Foundation/clio

- [Clio Command Reference](../../Commands.md#get-process-signature)
