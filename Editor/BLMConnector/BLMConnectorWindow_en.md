# BLM Connector

Browse and import assets from your BOOTH Library Manager (BLM) library directly in Unity.

---

## Basic Usage

1. Open from **Morulab Launcher** or **Morulab > BLM Connector**
2. Click an asset in the grid to see details
3. Select files and import

---

## Filters

Use the sidebar dropdown to filter displayed assets.

| Option | Content |
|--------|---------|
| All Products | All assets |
| BLM Products | BLM-managed assets only |
| Local Products | LocalAssets folder only |
| 📋 [List Name] | Custom lists created in BLM |

---

## Import Methods

### Method 1: Double-Click (Quick Import)
Double-click a product card to import all packages at once.

### Method 2: Individual Selection
1. Click a product to open the modal
2. Click "Import" on individual files
3. Use "Select All / Deselect" for batch selection

### Method 3: Queue System
1. Add multiple products to queue with "Add to Queue"
2. Run "Process Queue" to import all
3. Toggle "Show Import Dialog" to control dialog display

---

## Import Status

Check import status on product cards in the grid.

| Status | Display | Description |
|--------|---------|-------------|
| Not Imported | Normal | Not yet imported |
| Installed | Dimmed | Previously imported |
| Session Imported | Blue border | Imported this session |

---

## Managing Imported Assets

The following buttons appear in the modal footer for imported products.

| Button | Function |
|--------|----------|
| Show in Project | Focus the imported folder in Project window |
| Delete | Delete the imported folder (with confirmation) |

---

## LocalAssets Feature

Manage non-BLM assets with the same UI.

### How to Use
1. Click "Open LocalAssets Folder" in the sidebar
2. Create a folder for your asset inside LocalAssets
3. The folder name becomes the product name
4. Place `thumbnail.png` for a custom thumbnail

---

## Troubleshooting

| Issue | Solution |
|-------|----------|
| Assets not showing | Click "Refresh DB" |
| Filters not working | Click "Refresh DB" |
| Show in Project not appearing | Import the asset first |
