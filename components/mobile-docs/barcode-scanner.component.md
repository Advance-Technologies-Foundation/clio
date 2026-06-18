# How to Add a BarcodeScanner (`crt.BarcodeScanner`) to a Mobile Page

> Audience: Clio AI agent inserting a `crt.BarcodeScanner` into a mobile page schema.
> Renders a camera-based barcode and QR-code scanner field; mobile-only component.

## Metadata
- **Category**: fields
- **Container**: no
- **Parent types**: any layout container (e.g. `crt.GridContainer`, `crt.DetailsGrid`)
- **Typical children**: none

---

## 1. Mental model
`crt.BarcodeScanner` opens the device camera and continuously scans for barcodes or
QR codes. When a code is recognized the `scanned` action fires; bind it to a request
(typically `crt.SetAttributeFromBarcodeRequest`) to write the decoded text into a page
attribute. `scanTimeout` (milliseconds) sets a cooldown between successive scans to
prevent duplicate writes. The `features` object enables optional hardware features
(`flashToggle`, `beepOnScan`). This component is **mobile-only** — do not insert it into
web page schemas.

---

## 2. Clio operation
```jsonc
{
  "operation": "insert",
  "name": "BarcodeScanner",
  "values": {
    "type": "crt.BarcodeScanner",
    "scanned": {
      "request": "crt.SetAttributeFromBarcodeRequest",
      "params": {
        "attribute": "$PDS_Barcode"
      }
    },
    "enabled": true
  },
  "parentName": "DetailsGrid",
  "propertyName": "items",
  "index": 0
}
```

---

## 3. Property reference
Full `inputs`, `outputs`, `default`, `values`, and `designerDefaults` for `crt.BarcodeScanner` are in
`ComponentRegistry.json` under `componentType: "crt.BarcodeScanner"`.

---

## 4. Copy-paste minimal example
```jsonc
{
  "operation": "insert",
  "name": "BarcodeScanner",
  "values": {
    "type": "crt.BarcodeScanner",
    "scanned": {
      "request": "crt.SetAttributeFromBarcodeRequest",
      "params": {
        "attribute": "$PDS_Barcode"
      }
    },
    "enabled": true
  },
  "parentName": "DetailsGrid",
  "propertyName": "items",
  "index": 0
}
```

---

## 5. Common pitfalls
- **Mobile-only** — inserting `crt.BarcodeScanner` into a web (desktop) page schema will cause a runtime error because the browser does not expose the native camera scanning API used by this component.
- **Missing `scanned` action** — without a `scanned` binding the scanner activates and reads codes but the decoded value is discarded; always wire `scanned` to a request that stores the result.
- **`scanTimeout` too low** — a timeout of `0` or a very small value causes the `scanned` action to fire multiple times for the same physical code; set at least `1000` ms for most use cases.
