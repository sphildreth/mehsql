# ADR-0005: YAML-based User Settings

## Status
Accepted

## Context
MehSQL needs persistent user settings (recent files, preferences) that survive across application restarts. We need a human-readable, editable configuration format.

## Decision
Use **YamlDotNet 16.3.0** to serialize/deserialize a `UserSettings` class to `~/.config/mehsql/settings.yaml` (XDG on Linux, AppData on Windows/macOS).

YAML was chosen over JSON because:
- More human-readable and editable by hand
- Cleaner syntax for lists (recent files)
- User preference for the format

## Alternatives Considered
- **JSON** (`System.Text.Json`): No extra dependency, but less readable for hand-editing.
- **TOML**: Good readability but no mature .NET library with wide adoption.
- **XML**: Verbose and not user-friendly for hand-editing.

## Consequences
- Adds `YamlDotNet` NuGet dependency to `MehSql.App`.
- Settings file is human-editable.
- Need to handle missing/corrupt settings files gracefully.
