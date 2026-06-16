#!/usr/bin/env python3
"""
BLM Database Importer
JSON -> SQLite (data.db)

Usage: python import_blm_db.py
"""

import sqlite3
import json
import os
from pathlib import Path

# Paths
SCRIPT_DIR = Path(__file__).parent
PROJECT_ROOT = SCRIPT_DIR.parent
DOCUMENTATION_DIR = PROJECT_ROOT / "Documentation~"
INPUT_JSON = DOCUMENTATION_DIR / "blm_db_export.json"

# Default BLM database path
APPDATA = os.environ.get("APPDATA", "")
DEFAULT_DB_PATH = Path(APPDATA) / "pm.booth.library-manager" / "data.db"


def encode_to_blob(value, column_type):
    """Encode value to BLOB if needed (UTF-16 LE for TEXT in BLM)"""
    if column_type and "BLOB" in column_type.upper():
        if isinstance(value, str):
            return value.encode("utf-16-le")
    return value


def import_database(json_path=None, db_path=None):
    """Import JSON to SQLite database"""
    if json_path is None:
        json_path = INPUT_JSON
    if db_path is None:
        db_path = DEFAULT_DB_PATH

    json_path = Path(json_path)
    db_path = Path(db_path)

    if not json_path.exists():
        print(f"Error: JSON file not found at {json_path}")
        return False

    print(f"Reading JSON: {json_path}")

    with open(json_path, "r", encoding="utf-8") as f:
        data = json.load(f)

    print(f"Importing to: {db_path}")

    # Ensure directory exists
    db_path.parent.mkdir(parents=True, exist_ok=True)

    # Remove existing database
    if db_path.exists():
        os.remove(db_path)
        print(f"Removed existing database")

    conn = sqlite3.connect(str(db_path))
    cursor = conn.cursor()

    schema = data.get("schema", {})
    tables = data.get("tables", {})

    # Create tables from schema
    for table_name, columns in schema.items():
        col_defs = []
        for col in columns:
            col_def = f"{col['name']} {col['type']}"
            if col["notnull"]:
                col_def += " NOT NULL"
            if col["pk"]:
                col_def += " PRIMARY KEY"
            col_defs.append(col_def)

        create_sql = f"CREATE TABLE {table_name} ({', '.join(col_defs)})"
        cursor.execute(create_sql)
        print(f"Created table: {table_name}")

    # Insert data
    for table_name, rows in tables.items():
        if not rows:
            continue

        columns = schema.get(table_name, [])
        col_names = [col["name"] for col in columns]
        col_types = {col["name"]: col["type"] for col in columns}

        placeholders = ", ".join(["?" for _ in col_names])
        insert_sql = (
            f"INSERT INTO {table_name} ({', '.join(col_names)}) VALUES ({placeholders})"
        )

        for row in rows:
            values = []
            for col_name in col_names:
                value = row.get(col_name)
                col_type = col_types.get(col_name, "")
                # Note: In BLM, item_directory_path is stored as BLOB (UTF-16 LE)
                # We keep it as string in JSON, but you may need to encode it
                values.append(value)

            cursor.execute(insert_sql, values)

        print(f"Inserted {len(rows)} rows into {table_name}")

    conn.commit()
    conn.close()

    print("Import completed!")
    return True


def main():
    import argparse

    parser = argparse.ArgumentParser(description="Import JSON to BLM SQLite database")
    parser.add_argument("--json", "-j", help="Path to JSON file", default=None)
    parser.add_argument("--db", help="Path to data.db", default=None)
    args = parser.parse_args()

    import_database(args.json, args.db)


if __name__ == "__main__":
    main()
