# Component / Composite guidance dedup — tester checklist

> **Context for the tester:** the AI assistant used to carry built-in descriptions of these
> components/composites inside clio. We removed those duplicated descriptions; the AI now pulls the
> details live from the component registry via `get-component-info`. For each row below,
> **ask the AI assistant to do the scenario and confirm the result still works correctly** — the page
> renders, the right component is used, and nothing regressed because the built-in text was removed.

| Component / Composite | Type | What to test (ask the AI to do this — it must still work) |
|---|---|---|
| **Expanded list** | composite | Ask to **add a related/child list to a record page** (a list that shows the records linked to the record currently open). Verify a proper expandable list appears with its header buttons (add / refresh / search) and shows the linked records. The page must open and render — not break. |
| **Attachments** | composite | Ask to **add an "Attachments" block** to a page by name. Verify the AI builds the correct attachments block (doesn't hand-assemble something wrong). |
| **Next steps** | composite | Ask to **add a "Next steps" block** by name. Verify it produces the correct widget. |
| **Approval list** | composite | Ask to **add an "Approval list"** by name. Verify the correct block is created. |
| **Communication options** | composite | Ask to **add "Communication options"** (phone/email block) by name. Verify the correct block is created. |
| **crt.DataGrid** | component | Ask to **add a list/table to a page**, and on a related list ask to **turn on inline "add new record"**. Verify the list shows data and new rows can be added inline. |
| **crt.ExpansionPanel** | component | Ask to **add a collapsible/expandable section** to a page. Verify the page still renders and the section expands/collapses — no blank/broken card. |
| **crt.ImageInput** | component | Ask to **add an image/photo field** to an entity and show it on a page. Verify the image field works (you can upload and see the image) — it should use an "Image link" field, not a plain image column. |
| **crt.Gallery** | component | Ask for a **gallery / "show images as cards" / carousel**. Verify the AI suggests/uses the gallery component instead of a wrong look-alike. |
| **crt.List** | component | Ask for a **simple list**. Verify the AI picks the right list component (doesn't confuse it with a grid/gallery). |
| **crt.Toggle** | mobile component | On a **mobile page**, ask to add a **yes/no (boolean) field**. Verify it adds a toggle/switch (not a desktop checkbox). |
| **crt.BarcodeScanner** | mobile component | On a **mobile page**, ask to add a **barcode/QR scanner**. Verify it's added correctly. |
| **crt.FloatingActionButton** | mobile component | On a **mobile page**, ask for a **floating "+" action button**. Verify it's placed correctly. |
| **crt.Sort** | mobile component | On a **mobile list page**, ask to add a **sort control**. Verify it works. |
| **crt.QuickFilterGroup** | mobile component | On a **mobile list page**, ask to add **quick-filter chips**. Verify they're added correctly. |
| **crt.PasswordInput, crt.DataGrid, crt.HtmlEditor, crt.IFrame, crt.Chat, crt.Dashboards, crt.ColorPicker, crt.TagSelect, crt.MultiSelect, crt.EncryptedInput** | web-only components | On a **mobile page**, ask for one of these (e.g. a password field, a data grid). Verify the AI **says it's not available on mobile and suggests an alternative** — it must NOT silently insert a broken component. |

**Overall smoke check:** for any of the above, the AI should fetch component details correctly and produce a working page; nothing should have gotten "dumber" because the built-in text was removed.
