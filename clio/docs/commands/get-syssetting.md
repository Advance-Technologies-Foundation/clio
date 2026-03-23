# get-syssetting

Read a system setting value from an environment.

## Synopsis

```bash
clio get-syssetting <SETTING_CODE> [OPTIONS]
```

## Description

`get-syssetting` is served by the same implementation family as `set-syssetting` and is used
to retrieve the current value of a system setting from a Creatio environment.

## Examples

```bash
clio get-syssetting FeatureToggle -e dev
```

## See also

- [Commands.md](../../Commands.md#get-syssetting)
