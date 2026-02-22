# Morulab Unity Tools

Morulab製Unityツール集。ランチャーシステム + BLM Local Connector（BOOTH Library Manager連携ツール）を含みます。

## VCCへの追加

以下のURLをVCCの「Add Repository」に追加してください：

```
https://moruton1119.github.io/com.morulab.unity-tools/index.json
```

## 主な機能

- **Morulab Launcher**: ツール統合管理ランチャー（多言語対応: EN/JA/KO）
- **BLMデータベース連携**: BLMのSQLiteデータベースから商品情報を読み込み
- **ローカルアセット対応**: `LocalAssets/`フォルダで自作アセットも管理
- **一括インポート**: キューシステムによる複数アセットの一括インポート
- **インポート履歴管理**: タグベースでインストール済みアセットを追跡

---

## アーキテクチャ

### システム全体図

```mermaid
graph TB
    subgraph UI[UI層]
        Window["BLMConnectorWindow\n(エディタウィンドウ)"]
        UXML["BLMConnectorWindow.uxml\n(UIレイアウト)"]
        USS["BLMConnectorWindow.uss\n(スタイル)"]
    end

    subgraph Core[コア層]
        App["BLMConnectorApp\n(アプリケーションロジック)"]
        Models["BLMDataModels\n(データモデル)"]
    end

    subgraph Services[サービス層]
        DB["BLMDatabaseService\n(SQLite読み込み)"]
        Local["LocalAssetService\n(ローカルアセット読み込み)"]
        Importer["BLMAssetImporter\n(アセットインポート)"]
    end

    subgraph Infrastructure[インフラ層]
        Queue["AssetImportQueue\n(インポートキュー)"]
        History["BLMHistory\n(インポート履歴)"]
        Tagger["BLMProductTagger\n(アセットタグ付け)"]
        Env["BLMEnvironmentManager\n(環境設定)"]
    end

    subgraph External[外部データソース]
        BLM_DB[("BLM Database\n(data.db)")]
        BLM_Library["BLM Library Root\n(b{id}/)"]
        LocalAssets["LocalAssets/\n(ローカル開発用)"]
    end

    Window --> App
    UXML --> Window
    USS --> Window
    
    App --> DB
    App --> Local
    App --> Queue
    App --> History
    
    DB --> BLM_DB
    DB --> BLM_Library
    Local --> LocalAssets
    
    App --> Importer
    Queue --> Importer
    
    Tagger --> Queue
    History --> Tagger
    
    Env --> DB
```

### データフロー

```mermaid
flowchart LR
    subgraph Sources[データソース]
        A1[("BLM SQLite\n(data.db)")]
        A2["LocalAssets/\n(フォルダ)"]
    end
    
    subgraph Load[読み込み]
        B1[BLMDatabaseService]
        B2[LocalAssetService]
    end
    
    subgraph Process[処理]
        C1["BoothProduct\n(モデル)"]
        C2[AssetImportQueue]
        C3[BLMAssetImporter]
    end
    
    subgraph Unity[Unity]
        D1["Assets/BLM_Imports/"]
        D2["アセットタグ\n(BLM_Managed)"]
    end
    
    A1 --> B1 --> C1
    A2 --> B2 --> C1
    C1 --> C2 --> C3 --> D1
    C3 --> D2
```

---

## コンポーネント詳細

| ファイル | 役割 |
|---------|------|
| `BLMConnectorWindow.cs` | Unity エディタウィンドウのエントリーポイント |
| `BLMConnectorApp.cs` | UIイベント処理、フィルタリング、グリッド表示ロジック |
| `BLMDatabaseService.cs` | BLMのSQLiteデータベースから商品情報を読み込み |
| `LocalAssetService.cs` | `LocalAssets/`フォルダから自作アセットをスキャン |
| `BLMDataModels.cs` | データモデル定義 (`BoothProduct`, `BoothAsset`, `BoothPackage`) |
| `BLMAssetImporter.cs` | アセットをUnityにインポート（コピー/パッケージインポート） |
| `AssetImportQueue.cs` | インポートキュー管理（中断・再開・永続化対応） |
| `BLMHistory.cs` | インストール済み商品をアセットタグで追跡 |
| `BLMProductTagger.cs` | インポート時に自動で`BLM_PID_xxx`タグを付与 |
| `BLMEnvironmentManager.cs` | SQLite DLL の競合検出・解決・環境設定 |

---

## データモデル

```mermaid
classDiagram
    class BoothProduct {
        +string id
        +string name
        +string shopName
        +string shopUrl
        +string thumbnailPath
        +string thumbnailUrl
        +string rootFolderPath
        +string shopSubdomain
        +string sourceType
        +List~BoothPackage~ packages
        +List~BoothAsset~ assets
    }
    
    class BoothPackage {
        +string fileName
        +string fullPath
        +bool isImported
        +PackageType type
    }
    
    class BoothAsset {
        +string fileName
        +string fullPath
        +AssetType assetType
    }
    
    class PackageType {
        <<enumeration>>
        Unknown
        Main
        Material
        FXT
        Texture
        Optional
    }
    
    class AssetType {
        <<enumeration>>
        UnityPackage
        Texture
        Model
        Audio
        Other
    }
    
    BoothProduct "1" --> "*" BoothPackage
    BoothProduct "1" --> "*" BoothAsset
    BoothPackage --> PackageType
    BoothAsset --> AssetType
```

---

## 使用方法

### 1. ランチャーを開く

Unityメニューから `Morulab > Launcher` を選択。

### 2. BLM Connector を使用

ランチャーから `BLM Connector` を選択するか、`Morulab > BLM Connector (Standalone)` から直接開く。

### 3. アセットのインポート

1. **グリッドから選択**: 商品をクリックして詳細を表示
2. **ダブルクリック**: 全パッケージをキューに追加
3. **個別インポート**: 詳細パネルから特定のアセットのみインポート
4. **キュー処理**: `Process Queue` ボタンで一括インポート実行

### 4. ローカルアセットの追加

1. `Open Local Assets Folder` でフォルダを開く
2. `LocalAssets/` 内にフォルダを作成
3. `.unitypackage` ファイルを配置
4. Refresh で読み込み

### 5. インポート先

| アセットタイプ | インポート先 |
|--------------|------------|
| UnityPackage | 標準インポートダイアログ |
| その他 (画像/モデル/音声) | `Assets/BLM_Imports/{商品名}/` |

---

## 依存関係

- **SQLite**: `sqlite3.dll`, `Mono.Data.Sqlite.dll`
- **Unity Editor**: UI Toolkit (UXML/USS)
