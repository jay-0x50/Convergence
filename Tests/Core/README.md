# Core validation

Run from the repository root with the .NET 10 SDK:

```powershell
dotnet run --project Tests/Core/Convergence.Core.Tests.csproj
```

This executable compiles the same engine files used by Unity and requires no NuGet test packages. It checks deck construction, turn rules, summoning, combat, chain responses, failure atomicity, and deterministic randomized play. Failures return a nonzero exit code.

The tests exercise the engine through public actions. Fixture decks use a generous starting hand so each required card is available while leaving cards for later draw phases.
