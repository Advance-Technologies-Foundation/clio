# Restart Command

The `restart` command is used to restart a web application. 
This command ensures that the application is stopped and started again to apply changes or resolve issues.

## Aliases
- `restart`

## SYNOPSIS
```bash
restart [options]
```

## Options
- `--timeout <seconds>` : Specifies the timeout duration for the restart operation.

## Example
```bash
# Restart the web application with default settings
restart

# Restart the web application with a timeout of 30 seconds
restart --timeout 30
```
