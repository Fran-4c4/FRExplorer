# Contributing

1. Create a focused branch and keep FRExplorer independent from consuming applications.
2. Add or update tests for public behavior and cancellation changes.
3. Run `dotnet build FRExplorer.slnx -c Release` and `dotnet test FRExplorer.slnx -c Release`.
4. Update `docs/api.md` when changing a public property, event, default or cancellation rule.

Shell enumeration must remain off the UI thread. Avoid dependencies on undocumented Explorer internals or bundled third-party Shell controls.

