# autoupdate

## Command Type

    Application management

## Name

autoupdate - Enable or disable automatic updates on startup

## Synopsis

```bash
autoupdate [--enable | --disable]
```

## Description

Controls whether clio automatically checks for and installs newer versions
in the background each time it starts.

When enabled (the default), clio queries NuGet at most once every 8 hours.
If a newer version is found, it launches `dotnet tool update clio -g` as a
background process and immediately continues with the requested command.
The updated binary becomes active on the next invocation.

When disabled, clio shows a one-line notice that a newer version is
available and suggests running `clio update` manually.

Running `autoupdate` without arguments displays the current setting.

## Options

```bash
--enable    Enable automatic updates on startup (default behavior)

--disable   Disable automatic updates on startup
```

## Examples

```bash
# Show current autoupdate setting
autoupdate

# Disable automatic updates
autoupdate --disable

# Re-enable automatic updates
autoupdate --enable
```

## Behavior

- With no flags: prints whether auto-update is currently enabled or disabled
- --enable: sets Autoupdate = true in appsettings.json and confirms
- --disable: sets Autoupdate = false in appsettings.json and confirms
- The update check is cached for 8 hours to avoid hitting NuGet on every run
- Background update never blocks or delays the current command
- A network timeout of 3 seconds is applied to the version check

## Exit Codes

    0   Setting applied successfully (or status displayed)

## Reporting Bugs

    https://github.com/Advance-Technologies-Foundation/clio

- [Clio Command Reference](../../Commands.md#autoupdate)
