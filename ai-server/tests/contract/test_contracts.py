import glob
import json
import os
from typing import Any

import jsonschema
import pytest

SCHEMAS_DIR = os.path.abspath(
    os.path.join(os.path.dirname(__file__), "../../../contracts/schemas")
)
EXAMPLES_DIR = os.path.abspath(
    os.path.join(os.path.dirname(__file__), "../../../contracts/examples")
)


def load_schemas() -> dict[str, dict[str, Any]]:
    schemas: dict[str, dict[str, Any]] = {}
    for filepath in glob.glob(os.path.join(SCHEMAS_DIR, "*.schema.json")):
        schema_name = os.path.basename(filepath).replace(".schema.json", "")
        with open(filepath, "r", encoding="utf-8") as f:
            schemas[schema_name] = json.load(f)
    return schemas


SCHEMAS = load_schemas()


def get_fixture_files() -> list[str]:
    return glob.glob(os.path.join(EXAMPLES_DIR, "*.json"))


@pytest.mark.parametrize("fixture_path", get_fixture_files())
def test_contract_fixture(fixture_path: str) -> None:
    filename = os.path.basename(fixture_path)
    parts = filename.split(".")
    schema_name = parts[0]
    expected_valid = any(p.startswith("valid") for p in parts)

    assert schema_name in SCHEMAS, f"Schema {schema_name} not found for fixture {filename}"
    schema = SCHEMAS[schema_name]

    base_uri_path = SCHEMAS_DIR.replace("\\", "/")
    resolver = jsonschema.RefResolver(
        base_uri=f"file:///{base_uri_path}/", referrer=schema
    )

    with open(fixture_path, "r", encoding="utf-8") as f:
        data = json.load(f)

    if expected_valid:
        jsonschema.validate(instance=data, schema=schema, resolver=resolver)
    else:
        with pytest.raises((jsonschema.ValidationError, jsonschema.SchemaError)):
            jsonschema.validate(instance=data, schema=schema, resolver=resolver)
