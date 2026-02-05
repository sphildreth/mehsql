# MehSQL – Solution Scaffold

## Create Solution (example)
```bash
dotnet new sln -n MehSQL
dotnet new classlib -n MehSql.Core
dotnet new avalonia.app -n MehSql.App
dotnet sln add MehSql.Core MehSql.App
dotnet add MehSql.App reference MehSql.Core
```

## Target Framework
Set in each `.csproj`:
```xml
<TargetFramework>net10.0</TargetFramework>
```

## Directory Layout
```
/src
  /MehSql.Core
  /MehSql.App
/design
  /adr
/tests
/fixtures
```
