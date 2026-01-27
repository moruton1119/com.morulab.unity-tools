# BLM Connector (Booth Library Manager)

Manage and import your booth assets directly from your local BOOTH Library Manager (BLM) database.

## Features

- **BLM Sync**: Reads `data.db` from BLM to display your purchased assets list.
- **Thumbnail Grid**: Browse your assets with visual thumbnails.
- **Selective Import**: Choose specific `.unitypackage` files from a product folder.
- **Import Queue**: Safely process multiple packages sequentially to prevent Unity crashes.
- **Installation Tracking**: Automatically detects if a product is already successfully imported.

## How to Use

1. Open this tool from the **Morulab Launcher**.
2. Click on an asset in the grid to see its details.
3. In the detail panel, check the packages you want to import.
4. Click **Add to Queue** and then **Process Queue** to start importing.

## Requirements

Requires SQLite libraries. If missing, the tool will attempt to guide you through installation or use built-in fallbacks.
