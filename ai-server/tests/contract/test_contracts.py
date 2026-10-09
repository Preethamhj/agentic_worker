import glob
import json
import os
from typing import Any

import jsonschema
import pytest
from referencing import Registry, Resource

SCHEMAS_DIR = os.path.abspath(
    os.path.join(os.path.dirname(__file__), "../../../contracts/schemas")
)
EXAMPLES_DIR = os.path.abspath(
    os.path.join(os.path.dirname(__file__), "../../../contracts/examples")
)


def build_schema_registry() -> tuple[dict[str, dict[str, Any]], Registry]:
    schemas: dict[str, dict[str, Any]] = {}
    resources = []
    schemas_dir_uri = SCHEMAS_DIR.replace("\\", "/")

    for filepath in glob.glob(os.path.join(SCHEMAS_DIR, "*.schema.json")):
        schema_name = os.path.basename(filepath).replace(".schema.json", "")
        with open(filepath, "r", encoding="utf-8") as f:
            content = json.load(f)
        schemas[schema_name] = content

        resource = Resource.from_contents(content)
        filename = os.path.basename(filepath)
        resources.append((filename, resource))
        resources.append((f"file:///{schemas_dir_uri}/{filename}", resource))
        if "$id" in content:
            resources.append((content["$id"], resource))

    registry = Registry().with_resources(resources)
    return schemas, registry


SCHEMAS, REGISTRY = build_schema_registry()


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

    with open(fixture_path, "r", encoding="utf-8") as f:
        data = json.load(f)

    validator = jsonschema.Draft202012Validator(schema=schema, registry=REGISTRY)

    if expected_valid:
        validator.validate(instance=data)
    else:
        with pytest.raises((jsonschema.ValidationError, jsonschema.SchemaError)):
            validator.validate(instance=data)
