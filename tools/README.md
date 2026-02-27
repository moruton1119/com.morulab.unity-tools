# BLM Database Tools

BLM (BOOTH Library Manager) のSQLiteデータベースをJSON形式でエクスポート/インポートするツール集。

---

## ファイル構成

```
tools/
├── export_blm_db.py    # SQLite → JSON エクスポート
└── import_blm_db.py    # JSON → SQLite インポート

Documentation~/
└── blm_db_export.json  # エクスポートされたデータ
```

---

## 前提条件

- Python 3.8以上
- 標準ライブラリのみ使用（追加インストール不要）

---

## 使い方

### エクスポート（SQLite → JSON）

```powershell
# デフォルトパスで実行
python export_blm_db.py

# パスを指定して実行
python export_blm_db.py --db "C:\path\to\data.db" --output "output.json"
```

**出力先:** `Documentation~/blm_db_export.json`

**オプション:**
| オプション | 説明 | デフォルト |
|-----------|------|-----------|
| `--db` | 入力するdata.dbのパス | `%APPDATA%/pm.booth.library-manager/data.db` |
| `--output`, `-o` | 出力するJSONファイルのパス | `Documentation~/blm_db_export.json` |

---

### インポート（JSON → SQLite）

```powershell
# デフォルトパスで実行
python import_blm_db.py

# パスを指定して実行
python import_blm_db.py --json "input.json" --db "C:\path\to\data.db"
```

**注意:** 既存のデータベースは削除されてから再作成されます。

**オプション:**
| オプション | 説明 | デフォルト |
|-----------|------|-----------|
| `--json`, `-j` | 入力するJSONファイルのパス | `Documentation~/blm_db_export.json` |
| `--db` | 出力するdata.dbのパス | `%APPDATA%/pm.booth.library-manager/data.db` |

---

## JSON構造

```json
{
  "metadata": {
    "exported_at": "2026-02-22T17:56:15",
    "source": "C:\\Users\\...\\data.db",
    "tool": "export_blm_db.py"
  },
  "schema": {
    "booth_items": [
      {"name": "id", "type": "TEXT", "notnull": true, "pk": true},
      {"name": "name", "type": "TEXT", "notnull": false, "pk": false},
      ...
    ]
  },
  "tables": {
    "booth_items": [
      {"id": "123456", "name": "商品名", ...},
      ...
    ],
    "shops": [...],
    "preferences": [...],
    ...
  }
}
```

---

## テーブル一覧

| テーブル | 説明 |
|---------|------|
| `schema_version` | スキーマバージョン |
| `booth_items` | 商品情報 |
| `shops` | ショップ情報 |
| `preferences` | 環境設定（ライブラリパス等） |
| `booth_tags` | タグ定義 |
| `booth_item_tag_relations` | 商品-タグ関連付け |
| `booth_item_variations` | 商品バリエーション |
| `booth_item_update_history` | 更新履歴 |
| `registered_items` | 登録済みアイテム |
| `lists` | リスト |
| `list_items` | リスト内アイテム |
| `notifications` | 通知 |

---

## 主なカラム

### booth_items
| カラム | 型 | 説明 |
|-------|-----|------|
| id | TEXT | 商品ID |
| name | TEXT | 商品名 |
| shop_subdomain | TEXT | ショップのサブドメイン |
| thumbnail_url | TEXT | サムネイルURL |

### shops
| カラム | 型 | 説明 |
|-------|-----|------|
| subdomain | TEXT | ショップのサブドメイン |
| name | TEXT | ショップ名 |

### preferences
| カラム | 型 | 説明 |
|-------|-----|------|
| item_directory_path | BLOB (UTF-16 LE) | ライブラリのルートパス |

---

## 注意事項

1. **BLOBデータ**: `preferences.item_directory_path` は UTF-16 LE でエンコードされています。JSON内ではデコードされた文字列として表示されます。

2. **インポート時の注意**: `import_blm_db.py` は既存のデータベースを削除してから再作成します。重要なデータは事前にバックアップしてください。

3. **BLM起動中**: BLM起動中にインポートを行うと、データベースがロックされている可能性があります。BLMを終了してから実行してください。

---

## ワークフロー例

### 1. データのバックアップ
```powershell
cd tools
python export_blm_db.py
```

### 2. JSONを編集
`Documentation~/blm_db_export.json` をテキストエディタで編集

### 3. 変更を反映
```powershell
python import_blm_db.py
```

### 4. BLMで確認
BLMを起動して変更が反映されているか確認
