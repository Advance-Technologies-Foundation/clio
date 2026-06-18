# How to Add a Campaign Viewer (`crt.CampaignViewer`) to a Freedom UI Page

> Audience: code agent inserting `crt.CampaignViewer` into a Creatio Freedom UI page schema.
> Renders the campaign visual designer in read/edit mode for a given campaign record; typically
> placed on a campaign form page to show the campaign flow diagram.

## Metadata

- **Category**: display
- **Container**: no
- **Parent types**: `crt.FlexContainer`, `crt.GridContainer`
- **Typical children**: none

---

## 1. Mental model — the 1 place you must edit

| # | Section | What you add |
|---|---|---|
| 1 | `viewConfigDiff` | An `insert` op with `type: "crt.CampaignViewer"`, `campaignId`, and `fitContent`. **Always present.** |

`crt.CampaignViewer` is **view-only** — no datasource registration is required. Pass `campaignId`
as a `$Attribute` binding (typically the page's primary record ID).

### 1.1 Naming convention

```
CampaignViewer_<id>         // view element name
```

---

## 2. Step-by-step recipe

### 2.1 Insert the view element (`viewConfigDiff` entry)

```jsonc
{
  "operation": "insert",
  "name": "CampaignViewer",
  "parentName": "CampaignViewerFlexContainer",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.CampaignViewer",
    "campaignId": "$Id",
    "fitContent": true,
    "visible": "$ShowCampaignViewer"
  }
}
```

---

## 3. Property reference

Full `inputs`, `outputs`, `default`, `values`, `deprecated`, and `designerDefaults` for
`crt.CampaignViewer` are in `ComponentRegistry.json` under `componentType: "crt.CampaignViewer"`.
Only two inputs: `campaignId` and `fitContent`.

---

## 5. Copy-paste minimal example

```jsonc
// viewConfigDiff entry — from CrtMarketingCampaignsApp Campaigns_FormPage
{
  "operation": "insert",
  "name": "CampaignViewer",
  "parentName": "CampaignViewerFlexContainer",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.CampaignViewer",
    "fitContent": true,
    "campaignId": "$Id",
    "title": "#ResourceString(CampaignViewer_fo92rqr_title)#",
    "visible": "$ShowOldCampaignPlaceholder"
  }
}
```

---

## 6. Driving from page state

```jsonc
// viewModelConfigDiff — campaign ID from the primary datasource
{
  "operation": "merge",
  "path": ["attributes"],
  "values": {
    "Id": { "modelConfig": { "path": "PDS.Id" } }
  }
}

// viewConfigDiff.values
"campaignId": "$Id"
```

---

## 7. Common pitfalls

1. **`campaignId` must be a valid GUID string** — binding to `$Id` (the primary datasource record ID) is the canonical approach; an empty or null value results in a blank viewer.
2. **`fitContent: true` applies `fit-content` CSS to the host** — if the campaign viewer is inside a fixed-height container, set `fitContent: false` so it fills the container instead.
3. **Feature-gated visibility** — the designer toolbar for this component requires the `EnableCampaignViewerDesignerItem` feature to be enabled; the viewer itself works independently of that setting.
4. **No outputs** — `crt.CampaignViewer` fires no events; interaction is self-contained inside the campaign designer loaded by the component.

---

## 8. Quick checklist

- [ ] `insert` op in `viewConfigDiff` with `type: "crt.CampaignViewer"`, unique `name`, valid `parentName`, `propertyName: "items"`, valid `index`.
- [ ] `campaignId` bound to the record ID attribute (e.g. `"$Id"`).
- [ ] `fitContent` set (`true` for height-shrinking behavior, `false` to fill a fixed-height container).
- [ ] `visible` bound to an attribute if the viewer should be conditionally shown.
