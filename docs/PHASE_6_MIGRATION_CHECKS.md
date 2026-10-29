# Phase 6 migration checks

The standalone `tests/VPB6.MigrationChecks` project verifies the deterministic keyboard mapping boundary:

- 61 mapped pitches from C2 through C7.
- One binding per pitch in the default map.
- Legacy uppercase entries are interpreted as shifted bindings.

Run it from the repository root with:

```powershell
dotnet run --project tests\VPB6.MigrationChecks\VPB6.MigrationChecks.csproj
```
