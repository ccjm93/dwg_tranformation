# Repository Guidelines

## Project Structure & Module Organization

`LayerExporter.sln` contains a Civil 3D/AutoCAD plug-in and its supporting projects. Put AutoCAD-facing commands, WPF UI, services, and infrastructure in `src/LayerExporter`. Keep reusable, AutoCAD-independent geometry, CRS, and shapefile logic in `src/LayerExporter.Core`. Unit tests live in `tests/LayerExporter.Tests` and should mirror the Core feature they cover. Build automation and loading helpers are under `tools/`; installer sources are in `installer/`. `deploy/LayerExporter.bundle` is the Autodesk autoloader package and contains generated binaries for supported version bands. Treat those binaries as build outputs, not source files.

## Build, Test, and Development Commands

The repository requires the .NET 10 SDK, which can build all targeted frameworks.

```powershell
dotnet restore LayerExporter.sln
dotnet test tests\LayerExporter.Tests\LayerExporter.Tests.csproj
powershell -File tools\build.ps1 -Civil3DVersion 2026
powershell -File tools\build.ps1 -All
```

`dotnet test` runs the AutoCAD-independent xUnit suite. The versioned build command compiles one compatibility band and copies output into `deploy/bin` and the bundle. `-All` builds the 2018-2024 (`net48`), 2025-2026 (`net8.0-windows`), and 2027 (`net10.0-windows`) bands. Load the matching `LayerExporter.dll` with AutoCAD `NETLOAD` for manual validation.

## Coding Style & Naming Conventions

Use four-space indentation and file-scoped namespaces. Nullable reference types and implicit usings are enabled; preserve nullability annotations and avoid suppressions without justification. Follow standard C# naming: `PascalCase` for types, methods, and public members; `camelCase` for locals and parameters; descriptive suffixes such as `Service`, `ViewModel`, and `Tests`. Keep platform dependencies out of `LayerExporter.Core`. Run `dotnet build` before submitting changes; no separate formatter or linter is currently configured.

## Testing Guidelines

Tests use xUnit (`[Fact]`) and follow `TypeOrFeatureTests.cs` plus behavior-focused method names such as `OpenRing_IsClosedAutomatically`. Add regression tests for geometry, CRS, and SHP behavior in the test project. UI and AutoCAD integration changes require a documented manual Civil 3D check because they are not covered by the unit suite.

## Commit & Pull Request Guidelines

Recent history uses concise, imperative, feature-level subjects, sometimes in Korean; keep each commit focused and avoid generic messages such as `update`. Pull requests should describe the user-visible change, supported AutoCAD bands affected, and verification commands. Link relevant issues and include screenshots for WPF dialog or ribbon changes. Do not commit local installation paths, credentials, or unrelated generated binaries.
