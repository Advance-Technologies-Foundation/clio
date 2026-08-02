# build-theme

## Command Type

Theming commands

## Name

build-theme - build the artifacts of a Creatio theme from brand colours and fonts

## Description

Generates a Creatio theme's artifacts from a small set of brand inputs and the matching
version template.

From a required primary colour and CSS class name the command derives the full colour palette
(primary, secondary, accent, success, error), finalizes the colour tokens, and applies the
requested fonts. With `--output` it writes the theme files into a directory; otherwise it
prints the theme.css to stdout.

The target version comes from `--version`, or from `--environment-name` (whose Creatio version
it reads); the two are mutually exclusive, and the newest supported version is used when
neither is given.

## Synopsis

```bash
clio build-theme --primary <colour> [options]
```

## Options

```bash
--primary COLOUR
Required. Brand primary colour in any CSS form: #rrggbb, #rgb, rgb(), hsl(), or a named colour.

--css-class-name NAME
Optional. CSS class applied when the theme is active (^[A-Za-z][A-Za-z0-9_-]*$, max 100). Derived from
--caption (lowercased and hyphenated) when omitted; at least one of --caption or --css-class-name is required.

--secondary COLOUR
Optional. Secondary colour; derived from the primary when omitted.

--accent COLOUR
Optional. Accent colour; chosen from the primary when omitted (with a warning when no
generated candidate meets the accessibility gates).

--success COLOUR
Optional. Success colour; the platform default when omitted.

--error COLOUR
Optional. Error colour; the platform default when omitted.

--heading-font FAMILY
Optional. Heading font family; Montserrat (no @import) when omitted. The name is trimmed and its
internal whitespace runs are collapsed to single spaces first; the normalized name must then match
`^[A-Za-z0-9][A-Za-z0-9 -]*$` and be at most 100 characters, otherwise the build fails with
`INVALID_FONT_FAMILY`. So `" Open   Sans "` builds as `Open Sans`, while `Inter'; }` is rejected.

--body-font FAMILY
Optional. Body font family; Montserrat (no @import) when omitted. Same name rules as
--heading-font.

--font-weights LIST
Optional. Comma-separated font weights to load (e.g. 400,500,600); ignored with a warning when no
--heading-font/--body-font is set; defaults to 400,500,600.

--id ID
Optional. Theme id written to theme.json (directory output); an auto-generated UUID when omitted.

--caption TEXT
Optional. Theme caption written to theme.json (directory output); derived from --css-class-name
when omitted.

--version VERSION
Optional. Creatio version the theme targets (e.g. 10.0); the newest supported version is used
when omitted. Mutually exclusive with --environment-name.

--environment-name NAME
Optional. Registered environment whose Creatio version the theme targets. Mutually exclusive
with --version.

--output DIR
Optional. Output directory; writes theme.css + theme.json. Prints theme.css to stdout when omitted.
```

## Example

Print a theme to stdout:

```bash
clio build-theme --primary "#004fd6" --css-class-name MyTheme
```

Write theme.css + theme.json into a package themes folder:

```bash
clio build-theme --primary "#004fd6" --css-class-name MyTheme --output Files/themes/MyTheme
```

Pick the template by a registered environment's Creatio version:

```bash
clio build-theme --primary "#004fd6" --css-class-name MyTheme --environment-name dev
```

Custom fonts:

```bash
clio build-theme --primary "#004fd6" --css-class-name MyTheme --heading-font "Inter" --body-font "Inter" --font-weights 400,600
```

## Notes

`build-theme` never mutates an environment. A too-old `--version` (older than the lowest
supported version) and an unregistered `--environment-name` are reported as errors.

Every requested font family is checked against the Google Fonts catalogue over the network
(a short bounded probe per family). A family the catalogue does not publish gets no
`@import` — it renders only on machines where the font is already installed, elsewhere the
text falls back to the template's generic face — and the command warns about it. Family
names are case-sensitive: `Roboto` is found, `roboto` is reported missing. When the
catalogue cannot be reached the `@import` is kept, so a Google font keeps working offline,
and the warning says the family could not be verified; re-run the command once connectivity
is back if the family is actually a locally installed font.

## Reporting Bugs

    https://github.com/Advance-Technologies-Foundation/clio

- [Clio Command Reference](../../Commands.md#build-theme)
