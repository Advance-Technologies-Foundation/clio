/*
 * Creatio custom theme template — theme.css
 * ------------------------------------------------------------------
 * This is a .tpl file. Replace the <%...%> placeholders with your own values:
 *   <%themeCssClass%> — the theme's CSS class (same as theme.json -> cssClassName)
 *
 * HOW TO USE
 * 1. Customize the PALETTES block — this is the main lever. Your palette
 *    edits cascade into the semantic tokens below through var().
 * 2. Optionally change the font families in the TYPOGRAPHY block to rebrand.
 * 3. Keep the SEMANTIC and TYPOGRAPHY blocks as-is unless you need a
 *    specific override — they already encode the correct role mapping.
 *
 * RULES
 * - Assign only the theme layer: palettes, semantic colors, and typography
 *   roles. Do NOT set per-component variables here (e.g. `--crt-data-table-*`).
 * - Do NOT redeclare the platform primitives. They live on `:root` and
 *   inherit into your theme.
 */

.<%themeCssClass%> {
	/* =====================================================================
	 * PALETTES — customize these (500 is each hue's base shade).
	 * The values below are the default Creatio palette; replace with yours.
	 * ===================================================================== */

	--crt-palette-primary-10: #f7f8fb;
	--crt-palette-primary-25: #eff4fb;
	--crt-palette-primary-50: #e3ebfa;
	--crt-palette-primary-100: #cbdbf8;
	--crt-palette-primary-200: #9bbaf2;
	--crt-palette-primary-300: #6c99ea;
	--crt-palette-primary-400: #3d76e1;
	--crt-palette-primary-500: #004fd6;
	--crt-palette-primary-600: #0041b5;
	--crt-palette-primary-700: #003495;
	--crt-palette-primary-800: #002877;
	--crt-palette-primary-900: #001c5a;

	--crt-palette-secondary-10: #f6f7f8;
	--crt-palette-secondary-25: #eef0f2;
	--crt-palette-secondary-50: #e1e5e9;
	--crt-palette-secondary-100: #c8cfd7;
	--crt-palette-secondary-200: #96a4b3;
	--crt-palette-secondary-300: #667b91;
	--crt-palette-secondary-400: #39536f;
	--crt-palette-secondary-500: #0d2e4e;
	--crt-palette-secondary-600: #072949;
	--crt-palette-secondary-700: #032443;
	--crt-palette-secondary-800: #001f3e;
	--crt-palette-secondary-900: #001b37;

	--crt-palette-accent-10: #fdf8f7;
	--crt-palette-accent-25: #fef4f1;
	--crt-palette-accent-50: #ffece7;
	--crt-palette-accent-100: #ffddd5;
	--crt-palette-accent-200: #ffbeaf;
	--crt-palette-accent-300: #ff9c86;
	--crt-palette-accent-400: #ff7659;
	--crt-palette-accent-500: #ff4013;
	--crt-palette-accent-600: #d42d00;
	--crt-palette-accent-700: #a72100;
	--crt-palette-accent-800: #7d1600;
	--crt-palette-accent-900: #550b00;

	--crt-palette-neutral-10: #f9f9f9;
	--crt-palette-neutral-25: #f4f4f4;
	--crt-palette-neutral-50: #ededed;
	--crt-palette-neutral-100: #dfdfdf;
	--crt-palette-neutral-200: #c4c4c4;
	--crt-palette-neutral-300: #a9a9a9;
	--crt-palette-neutral-400: #8e8e8e;
	--crt-palette-neutral-500: #757575;
	--crt-palette-neutral-600: #606060;
	--crt-palette-neutral-700: #4c4c4c;
	--crt-palette-neutral-800: #393939;
	--crt-palette-neutral-900: #272727;

	--crt-palette-error-10: #fcf8f7;
	--crt-palette-error-25: #fbf2f0;
	--crt-palette-error-50: #fbe9e5;
	--crt-palette-error-100: #f9d7cf;
	--crt-palette-error-200: #f4b2a3;
	--crt-palette-error-300: #eb8c77;
	--crt-palette-error-400: #e0634a;
	--crt-palette-error-500: #d2310d;
	--crt-palette-error-600: #b12200;
	--crt-palette-error-700: #8e1900;
	--crt-palette-error-800: #6d1100;
	--crt-palette-error-900: #4d0900;

	--crt-palette-success-10: #f7f9f7;
	--crt-palette-success-25: #f1f6f0;
	--crt-palette-success-50: #e6f0e5;
	--crt-palette-success-100: #d1e4ce;
	--crt-palette-success-200: #a6cda2;
	--crt-palette-success-300: #7cb575;
	--crt-palette-success-400: #4f9d47;
	--crt-palette-success-500: #0b8500;
	--crt-palette-success-600: #076e00;
	--crt-palette-success-700: #045900;
	--crt-palette-success-800: #024400;
	--crt-palette-success-900: #013000;

	/* =====================================================================
	 * SEMANTIC COLORS — usually keep as-is (they reference the palettes).
	 * ===================================================================== */

	/* Background / Base */
	--crt-color-background-base: var(--crt-color-base-light);
	--crt-color-background-canvas: rgb(from var(--crt-color-background-base) r g b / 90%);
	--crt-color-background-interaction-hover: var(--crt-palette-primary-25);
	--crt-color-background-interaction-selected: var(--crt-palette-primary-50);
	--crt-color-background-disabled-subtle: var(--crt-palette-neutral-50);
	--crt-color-background-disabled-soft: var(--crt-palette-neutral-200);

	/* Background / Role colors */
	--crt-color-background-primary: var(--crt-palette-primary-500);
	--crt-color-background-primary-hover: var(--crt-palette-primary-600);
	--crt-color-background-primary-selected: var(--crt-palette-primary-700);
	--crt-color-background-primary-subtle: var(--crt-palette-primary-10);
	--crt-color-background-primary-subtle-hover: var(--crt-palette-primary-25);
	--crt-color-background-primary-subtle-selected: var(--crt-palette-primary-50);
	--crt-color-background-primary-soft: var(--crt-palette-primary-50);
	--crt-color-background-primary-soft-hover: var(--crt-palette-primary-100);
	--crt-color-background-primary-soft-selected: var(--crt-palette-primary-200);

	--crt-color-background-secondary: var(--crt-palette-secondary-500);
	--crt-color-background-secondary-hover: var(--crt-palette-secondary-600);
	--crt-color-background-secondary-selected: var(--crt-palette-secondary-700);
	--crt-color-background-secondary-subtle: var(--crt-palette-secondary-10);
	--crt-color-background-secondary-subtle-hover: var(--crt-palette-secondary-25);
	--crt-color-background-secondary-subtle-selected: var(--crt-palette-secondary-50);
	--crt-color-background-secondary-soft: var(--crt-palette-secondary-50);
	--crt-color-background-secondary-soft-hover: var(--crt-palette-secondary-100);
	--crt-color-background-secondary-soft-selected: var(--crt-palette-secondary-200);

	--crt-color-background-accent: var(--crt-palette-accent-500);
	--crt-color-background-accent-hover: var(--crt-palette-accent-600);
	--crt-color-background-accent-selected: var(--crt-palette-accent-700);
	--crt-color-background-accent-subtle: var(--crt-palette-accent-10);
	--crt-color-background-accent-subtle-hover: var(--crt-palette-accent-25);
	--crt-color-background-accent-subtle-selected: var(--crt-palette-accent-50);
	--crt-color-background-accent-soft: var(--crt-palette-accent-50);
	--crt-color-background-accent-soft-hover: var(--crt-palette-accent-100);
	--crt-color-background-accent-soft-selected: var(--crt-palette-accent-200);

	--crt-color-background-error: var(--crt-palette-error-500);
	--crt-color-background-error-hover: var(--crt-palette-error-600);
	--crt-color-background-error-selected: var(--crt-palette-error-700);
	--crt-color-background-error-subtle: var(--crt-palette-error-10);
	--crt-color-background-error-subtle-hover: var(--crt-palette-error-25);
	--crt-color-background-error-subtle-selected: var(--crt-palette-error-50);
	--crt-color-background-error-soft: var(--crt-palette-error-50);
	--crt-color-background-error-soft-hover: var(--crt-palette-error-100);
	--crt-color-background-error-soft-selected: var(--crt-palette-error-200);

	--crt-color-background-success: var(--crt-palette-success-500);
	--crt-color-background-success-hover: var(--crt-palette-success-600);
	--crt-color-background-success-selected: var(--crt-palette-success-700);
	--crt-color-background-success-subtle: var(--crt-palette-success-10);
	--crt-color-background-success-subtle-hover: var(--crt-palette-success-25);
	--crt-color-background-success-subtle-selected: var(--crt-palette-success-50);
	--crt-color-background-success-soft: var(--crt-palette-success-50);
	--crt-color-background-success-soft-hover: var(--crt-palette-success-100);
	--crt-color-background-success-soft-selected: var(--crt-palette-success-200);

	/* Border / Base */
	--crt-color-border-base: var(--crt-palette-neutral-100);
	--crt-color-border-muted: var(--crt-palette-neutral-50);
	--crt-color-border-selected: var(--crt-palette-primary-500);
	--crt-color-border-disabled: var(--crt-palette-neutral-100);

	/* Border / Role colors */
	--crt-color-border-primary: var(--crt-palette-primary-500);
	--crt-color-border-primary-subtle: var(--crt-palette-primary-100);
	--crt-color-border-primary-soft: var(--crt-palette-primary-200);

	--crt-color-border-secondary: var(--crt-palette-secondary-500);
	--crt-color-border-secondary-subtle: var(--crt-palette-secondary-100);
	--crt-color-border-secondary-soft: var(--crt-palette-secondary-200);

	--crt-color-border-accent: var(--crt-palette-accent-500);
	--crt-color-border-accent-subtle: var(--crt-palette-accent-100);
	--crt-color-border-accent-soft: var(--crt-palette-accent-200);

	--crt-color-border-error: var(--crt-palette-error-500);
	--crt-color-border-error-subtle: var(--crt-palette-error-100);
	--crt-color-border-error-soft: var(--crt-palette-error-200);

	--crt-color-border-success: var(--crt-palette-success-500);
	--crt-color-border-success-subtle: var(--crt-palette-success-100);
	--crt-color-border-success-soft: var(--crt-palette-success-200);

	/* Text / Base */
	--crt-color-text-body: var(--crt-color-base-dark);
	--crt-color-text-heading: var(--crt-palette-secondary-500);
	--crt-color-text-muted: var(--crt-palette-neutral-600);
	--crt-color-text-disabled: var(--crt-palette-neutral-500);
	--crt-color-text-link: var(--crt-palette-primary-500);
	--crt-color-text-link-hover: var(--crt-palette-primary-600);
	--crt-color-text-action: var(--crt-palette-secondary-500);
	--crt-color-text-action-hover: var(--crt-palette-primary-500);

	/* Text / Role colors */
	--crt-color-text-primary: var(--crt-palette-primary-500);
	--crt-color-text-on-primary: var(--crt-color-base-light);
	--crt-color-text-on-primary-subtle: var(--crt-palette-primary-900);
	--crt-color-text-on-primary-soft: var(--crt-palette-primary-900);

	--crt-color-text-secondary: var(--crt-palette-secondary-500);
	--crt-color-text-on-secondary: var(--crt-color-base-light);
	--crt-color-text-on-secondary-subtle: var(--crt-palette-secondary-900);
	--crt-color-text-on-secondary-soft: var(--crt-palette-secondary-900);

	--crt-color-text-accent: var(--crt-palette-accent-500);
	--crt-color-text-on-accent: var(--crt-color-base-light);
	--crt-color-text-on-accent-subtle: var(--crt-palette-accent-900);
	--crt-color-text-on-accent-soft: var(--crt-palette-accent-900);

	--crt-color-text-error: var(--crt-palette-error-500);
	--crt-color-text-on-error: var(--crt-color-base-light);
	--crt-color-text-on-error-subtle: var(--crt-palette-error-900);
	--crt-color-text-on-error-soft: var(--crt-palette-error-900);

	--crt-color-text-success: var(--crt-palette-success-500);
	--crt-color-text-on-success: var(--crt-color-base-light);
	--crt-color-text-on-success-subtle: var(--crt-palette-success-900);
	--crt-color-text-on-success-soft: var(--crt-palette-success-900);

	/* Icon / Base */
	--crt-color-icon-base: var(--crt-palette-secondary-500);
	--crt-color-icon-muted: var(--crt-color-text-muted);
	--crt-color-icon-disabled: var(--crt-color-text-disabled);
	--crt-color-icon-action: var(--crt-color-text-action);
	--crt-color-icon-action-hover: var(--crt-color-text-action-hover);

	/* Icon / Role colors */
	--crt-color-icon-primary: var(--crt-color-text-primary);
	--crt-color-icon-on-primary: var(--crt-color-text-on-primary);
	--crt-color-icon-on-primary-subtle: var(--crt-color-text-on-primary-subtle);
	--crt-color-icon-on-primary-soft: var(--crt-color-text-on-primary-soft);

	--crt-color-icon-secondary: var(--crt-color-text-secondary);
	--crt-color-icon-on-secondary: var(--crt-color-text-on-secondary);
	--crt-color-icon-on-secondary-subtle: var(--crt-color-text-on-secondary-subtle);
	--crt-color-icon-on-secondary-soft: var(--crt-color-text-on-secondary-soft);

	--crt-color-icon-accent: var(--crt-color-text-accent);
	--crt-color-icon-on-accent: var(--crt-color-text-on-accent);
	--crt-color-icon-on-accent-subtle: var(--crt-color-text-on-accent-subtle);
	--crt-color-icon-on-accent-soft: var(--crt-color-text-on-accent-soft);

	--crt-color-icon-error: var(--crt-color-text-error);
	--crt-color-icon-on-error: var(--crt-color-text-on-error);
	--crt-color-icon-on-error-subtle: var(--crt-color-text-on-error-subtle);
	--crt-color-icon-on-error-soft: var(--crt-color-text-on-error-soft);

	--crt-color-icon-success: var(--crt-color-text-success);
	--crt-color-icon-on-success: var(--crt-color-text-on-success);
	--crt-color-icon-on-success-subtle: var(--crt-color-text-on-success-subtle);
	--crt-color-icon-on-success-soft: var(--crt-color-text-on-success-soft);

	/* Effects */
	--crt-color-shadow: rgb(from var(--crt-color-base-dark) r g b / 10%);

	/* =====================================================================
	 * TYPOGRAPHY — change the font families to rebrand. Roles reference the
	 * platform font-size / line-height / weight primitives on :root.
	 * Do not change the font-size or line-height values: the UI is not yet
	 * adapted to altered typography metrics and overriding them breaks the layout.
	 * ===================================================================== */

	--crt-font-family-heading: 'Montserrat', sans-serif;
	--crt-font-family-body: 'Montserrat', sans-serif;

	/* Large */
	--crt-large-1-font-family: var(--crt-font-family-heading);
	--crt-large-1-font-size: var(--crt-font-size-1100);
	--crt-large-1-font-weight: var(--crt-font-weight-medium);
	--crt-large-1-line-height: var(--crt-line-height-1000);
	--crt-large-1-letter-spacing: 0;

	--crt-large-2-font-family: var(--crt-font-family-heading);
	--crt-large-2-font-size: var(--crt-font-size-1000);
	--crt-large-2-font-weight: var(--crt-font-weight-medium);
	--crt-large-2-line-height: var(--crt-line-height-900);
	--crt-large-2-letter-spacing: 0;

	--crt-large-3-font-family: var(--crt-font-family-heading);
	--crt-large-3-font-size: var(--crt-font-size-900);
	--crt-large-3-font-weight: var(--crt-font-weight-medium);
	--crt-large-3-line-height: var(--crt-line-height-800);
	--crt-large-3-letter-spacing: 0;

	--crt-large-4-font-family: var(--crt-font-family-heading);
	--crt-large-4-font-size: var(--crt-font-size-800);
	--crt-large-4-font-weight: var(--crt-font-weight-medium);
	--crt-large-4-line-height: var(--crt-line-height-700);
	--crt-large-4-letter-spacing: 0;

	/* Headlines */
	--crt-headline-1-font-family: var(--crt-font-family-heading);
	--crt-headline-1-font-size: var(--crt-font-size-700);
	--crt-headline-1-font-weight: var(--crt-font-weight-semi-bold);
	--crt-headline-1-line-height: var(--crt-line-height-600);
	--crt-headline-1-letter-spacing: 0;

	--crt-headline-2-font-family: var(--crt-font-family-heading);
	--crt-headline-2-font-size: var(--crt-font-size-600);
	--crt-headline-2-font-weight: var(--crt-font-weight-medium);
	--crt-headline-2-line-height: var(--crt-line-height-500);
	--crt-headline-2-letter-spacing: 0;

	--crt-headline-3-font-family: var(--crt-font-family-heading);
	--crt-headline-3-font-size: var(--crt-font-size-500);
	--crt-headline-3-font-weight: var(--crt-font-weight-medium);
	--crt-headline-3-line-height: var(--crt-line-height-400);
	--crt-headline-3-letter-spacing: 0;

	--crt-headline-4-font-family: var(--crt-font-family-heading);
	--crt-headline-4-font-size: var(--crt-font-size-400);
	--crt-headline-4-font-weight: var(--crt-font-weight-medium);
	--crt-headline-4-line-height: var(--crt-line-height-300);
	--crt-headline-4-letter-spacing: 0;

	--crt-headline-5-font-family: var(--crt-font-family-heading);
	--crt-headline-5-font-size: var(--crt-font-size-300);
	--crt-headline-5-font-weight: var(--crt-font-weight-medium);
	--crt-headline-5-line-height: var(--crt-line-height-200);
	--crt-headline-5-letter-spacing: 0;

	/* Body */
	--crt-body-1-font-family: var(--crt-font-family-body);
	--crt-body-1-font-size: var(--crt-font-size-300);
	--crt-body-1-font-weight: var(--crt-font-weight-medium);
	--crt-body-1-line-height: var(--crt-line-height-300);
	--crt-body-1-letter-spacing: 0;

	--crt-body-2-font-family: var(--crt-font-family-body);
	--crt-body-2-font-size: var(--crt-font-size-200);
	--crt-body-2-font-weight: var(--crt-font-weight-medium);
	--crt-body-2-line-height: var(--crt-line-height-300);
	--crt-body-2-letter-spacing: 0;

	--crt-caption-font-family: var(--crt-font-family-body);
	--crt-caption-font-size: var(--crt-font-size-100);
	--crt-caption-font-weight: var(--crt-font-weight-medium);
	--crt-caption-line-height: var(--crt-line-height-100);
	--crt-caption-letter-spacing: 0;

	/* Functional */
	--crt-button-font-family: var(--crt-font-family-body);
	--crt-button-font-size: var(--crt-font-size-300);
	--crt-button-font-weight: var(--crt-font-weight-medium);
	--crt-button-line-height: var(--crt-line-height-100);
	--crt-button-letter-spacing: 0;

	--crt-button-small-font-family: var(--crt-font-family-body);
	--crt-button-small-font-size: var(--crt-font-size-100);
	--crt-button-small-font-weight: var(--crt-font-weight-medium);
	--crt-button-small-line-height: var(--crt-line-height-100);
	--crt-button-small-letter-spacing: 0;

	--crt-overline-font-family: var(--crt-font-family-body);
	--crt-overline-font-size: var(--crt-font-size-50);
	--crt-overline-font-weight: var(--crt-font-weight-regular);
	--crt-overline-line-height: var(--crt-line-height-50);
	--crt-overline-letter-spacing: 0.05em;
}
