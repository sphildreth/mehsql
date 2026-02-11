# MehSQL Command-Line Usage

MehSQL supports opening database files and loading SQL scripts directly from the command line.

## Basic Usage

### Open a Database File

```bash
# Using compiled binary
./mehsql /path/to/database.ddb

# Using dotnet run (for development)
dotnet run --project src/MehSql.App -- /path/to/database.ddb
```

When a database file is provided:
- The application starts with the specified database loaded
- The schema explorer automatically refreshes with the database structure
- All panes (schema tree, query editor, results) are ready to use

### Load a Database and SQL File Together

```bash
# Open database and load SQL script
./mehsql /path/to/database.ddb /path/to/query.sql

# Development mode
dotnet run --project src/MehSql.App -- database.ddb setup.sql
```

When both files are provided:
- Database is loaded first
- SQL file content is loaded into the query editor
- Ready to execute with Ctrl+Enter (Cmd+Enter on macOS)

## File Type Detection

MehSQL automatically detects file types:

- **Database files**: `.ddb` extension or any other file (first non-SQL file)
- **SQL scripts**: `.sql` extension
- **Relative paths**: Resolved relative to current working directory
- **Absolute paths**: Used as-is

## Examples

### Example 1: Open an existing database

```bash
./mehsql /mnt/incoming/decentdb_test/musicbrainz.ddb
```

### Example 2: Create and open a new database

```bash
# Database will be created if it doesn't exist
./mehsql ~/my_project/data/new_database.ddb
```

### Example 3: Open database with initialization script

```bash
./mehsql production.ddb migrations/001_initial.sql
```

### Example 4: Multiple arguments

```bash
# First non-.sql file is treated as database
# .sql files are loaded into the editor (last one wins)
./mehsql mydata.ddb setup.sql query.sql
```

## Error Handling

If a specified file doesn't exist:
- **Database file**: Error is shown in the application UI
- **SQL file**: Warning is logged, application continues
- The application will still start successfully

## Integration with Shell

### Linux/macOS

Create an alias for quick access:

```bash
# Add to ~/.bashrc or ~/.zshrc
alias mehsql='dotnet run --project ~/projects/mehsql/src/MehSql.App --'

# Then use it
mehsql /path/to/database.ddb
```

### Windows

Create a batch file or PowerShell alias:

```powershell
# PowerShell profile
function mehsql { dotnet run --project C:\Projects\mehsql\src\MehSql.App -- $args }

# Then use it
mehsql C:\data\database.ddb
```

## File Associations

After installing MehSQL, you can associate `.ddb` files with the application so that double-clicking a database file automatically opens it in MehSQL.

### Linux

```bash
# Create desktop entry (example)
cat > ~/.local/share/applications/mehsql.desktop << EOF
[Desktop Entry]
Type=Application
Name=MehSQL
Exec=/path/to/mehsql %f
MimeType=application/x-decentdb;
EOF
```

### macOS

```bash
# Set MehSQL as default for .ddb files
# (requires packaged .app bundle)
duti -s com.example.mehsql .ddb all
```

### Windows

```powershell
# Associate .ddb files with MehSQL
# (requires installed .exe)
ftype DecentDB="C:\Program Files\MehSQL\mehsql.exe" "%1"
assoc .ddb=DecentDB
```

## Logging

Command-line argument processing is logged at startup. Check logs for:

```
[Information] Database file argument detected: /path/to/database.ddb
[Information] SQL file argument detected: /path/to/query.sql
[Information] Opening database from command-line: /path/to/database.ddb
[Warning] Command-line argument file not found: /invalid/path.ddb
```

Log location varies by platform:
- **Linux**: `~/.config/mehsql/logs/`
- **macOS**: `~/Library/Application Support/mehsql/logs/`
- **Windows**: `%APPDATA%\mehsql\logs\`
