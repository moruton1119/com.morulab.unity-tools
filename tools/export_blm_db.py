#!/usr/bin/env python3
"""
BLM Database Exporter
SQLite (data.db) -> JSON

Usage: python export_blm_db.py
"""

import sqlite3
import json
import os
from datetime import datetime
from pathlib import Path

# Paths
SCRIPT_DIR = Path(__file__).parent
PROJECT_ROOT = SCRIPT_DIR.parent
DOCUMENTATION_DIR = PROJECT_ROOT / "Documentation~"
OUTPUT_JSON = DOCUMENTATION_DIR / "blm_db_export.json"

# Default BLM database path
APPDATA = os.environ.get("APPDATA", "")
DEFAULT_DB_PATH = Path(APPDATA) / "pm.booth.library-manager" / "data.db"


def decode_blob(value):
    """Decode BLOB value (UTF-16 LE or UTF-8)"""
    if isinstance(value, bytes):
        try:
            decoded = value.decode("utf-16-le")
            if decoded and len(decoded) > 0:
                return decoded
        except:
            pass
        try:
            return value.decode("utf-8")
        except:
            return value.hex()
    return value


def encode_blob(value):
    """Encode string to BLOB format for import"""
    if isinstance(value, str):
        return value.encode("utf-16-le")
    return value


def export_database(db_path=None, output_path=None):
    """Export SQLite database to JSON"""
    if db_path is None:
        db_path = DEFAULT_DB_PATH
    if output_path is None:
        output_path = OUTPUT_JSON

    db_path = Path(db_path)
    output_path = Path(output_path)

    if not db_path.exists():
        print(f"Error: Database not found at {db_path}")
        return False

    print(f"Reading database: {db_path}")

    result = {
        "metadata": {
            "exported_at": datetime.now().isoformat(),
            "source": str(db_path),
            "tool": "export_blm_db.py",
        },
        "schema": {},
        "tables": {},
    }

    conn = sqlite3.connect(str(db_path))
    conn.row_factory = sqlite3.Row
    cursor = conn.cursor()

    # Get all tables
    cursor.execute("SELECT name FROM sqlite_master WHERE type='table'")
    tables = [row[0] for row in cursor.fetchall()]
    print(f"Found tables: {tables}")

    for table in tables:
        # Get table info for schema
        cursor.execute(f"PRAGMA table_info({table})")
        columns = cursor.fetchall()
        result["schema"][table] = [
            {
                "name": col[1],
                "type": col[2],
                "notnull": bool(col[3]),
                "pk": bool(col[5]),
            }
            for col in columns
        ]

        # Get all rows
        cursor.execute(f"SELECT * FROM {table}")
        rows = cursor.fetchall()

        table_data = []
        for row in rows:
            row_dict = {}
            for i, col in enumerate(columns):
                value = row[i]
                # Decode BLOB values
                if isinstance(value, bytes):
                    value = decode_blob(value)
                row_dict[col[1]] = value
            table_data.append(row_dict)

        result["tables"][table] = table_data
        print(f"  {table}: {len(table_data)} rows")

    conn.close()

    # Ensure output directory exists
    output_path.parent.mkdir(parents=True, exist_ok=True)

    # Write JSON
    with open(output_path, "w", encoding="utf-8") as f:
        json.dump(result, f, ensure_ascii=False, indent=2)

    print(f"Exported to: {output_path}")
    return True


def main():
    import argparse

    parser = argparse.ArgumentParser(description="Export BLM SQLite database to JSON")
    parser.add_argument("--db", help="Path to data.db", default=None)
    parser.add_argument("--output", "-o", help="Output JSON path", default=None)
    args = parser.parse_args()

    export_database(args.db, args.output)


if __name__ == "__main__":
    main()
