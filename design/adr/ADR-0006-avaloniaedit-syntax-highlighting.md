# ADR-0006: AvaloniaEdit for SQL Syntax Highlighting

## Status
Accepted

## Context
The SQL query editor was a plain `TextBox` with no syntax highlighting, making it hard to read complex queries. Users need visual cues for SQL keywords, strings, numbers, and comments.

## Decision
Replace the `TextBox` with **AvaloniaEdit 11.4.0** (`TextEditor` control) using **TextMate grammars** for SQL syntax highlighting via the `DarkPlus` theme.

Packages added:
- `Avalonia.AvaloniaEdit` 11.4.0
- `AvaloniaEdit.TextMate` 11.4.0 (brings `TextMateSharp.Grammars` transitively)

## Alternatives Considered
- **Manual regex-based highlighting**: Fragile, incomplete, and would require significant effort to maintain.
- **RoslynPad editor**: Overkill for SQL; primarily designed for C#.
- **Plain TextBox with colored spans**: Avalonia TextBox doesn't support inline formatting.

## Consequences
- Adds AvaloniaEdit + TextMateSharp dependencies (~2 MB).
- SQL editor gains line numbers, syntax highlighting, and better editing UX.
- `TextEditor.Text` is not directly bindable; requires manual two-way sync with ViewModel via `TextChanged` event.
