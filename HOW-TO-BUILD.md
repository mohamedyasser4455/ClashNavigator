# Clash Navigator — building from source

Graduation project — KAITECH BIM Application Development, Round 11
Mohamed Yasser · [linkedin.com/in/mohamed-y-khalil](https://www.linkedin.com/in/mohamed-y-khalil)

---

## What you need

| | |
|---|---|
| Visual Studio 2022 | with **.NET desktop development** workload (this brings the .NET 8 SDK and the .NET Framework 4.8 targeting pack) |
| Inno Setup 6 | only if you want to rebuild the installer — <https://jrsoftware.org/isdl.php> |
| Internet | the first build restores the Revit API reference packages from NuGet |

Revit itself does **not** have to be installed to build. The Revit API assemblies come from the
`Nice3point.Revit.Api.*` reference packages, which is what makes it possible to build for Revit
2022 → 2026 on a machine that only has one version of Revit.

---

## Why there are five build configurations

Revit changed its runtime at version 2025:

| Revit | Runtime | Configuration | Output |
|---|---|---|---|
| 2022 | .NET Framework 4.8 | `R22` | `bin\R22\ClashNavigator.dll` |
| 2023 | .NET Framework 4.8 | `R23` | `bin\R23\ClashNavigator.dll` |
| 2024 | .NET Framework 4.8 | `R24` | `bin\R24\ClashNavigator.dll` |
| 2025 | .NET 8 | `R25` | `bin\R25\ClashNavigator.dll` |
| 2026 | .NET 8 | `R26` | `bin\R26\ClashNavigator.dll` |

One DLL therefore cannot cover the whole range. The same source is compiled five times; each
build defines its own symbol (`REVIT2022` … `REVIT2026`, plus `R2023_OR_GREATER` and
`R2024_OR_GREATER`) and every place where the API actually changed is handled in one file,
`Core/RevitCompat.cs`.

The two real API differences:

* `ElementId.IntegerValue` (int) became `ElementId.Value` (long) in Revit 2024.
* Selecting an element **inside a link** needs `Selection.SetReferences`, which arrived in
  Revit 2023. On Revit 2022 the section box and zoom still work; only the highlight is skipped.

---

## Build

### From Visual Studio

1. Open `ClashNavigator.sln`.
2. Pick the configuration you want in the toolbar dropdown (`R24` for Revit 2024).
3. Build → Build Solution (**F6**).

A class library cannot be started with F5 — that is expected. To debug inside Revit:
Project Properties → **Debug** → Start external program →
`C:\Program Files\Autodesk\Revit 2024\Revit.exe`

While building `R24`, a post-build step copies the DLL and the manifest into
`%AppData%\Autodesk\Revit\Addins\2024\`, so there is no manual copying during development.

### From the command line

```
dotnet build "ClashNavigator\ClashNavigator.csproj" -c R24
```

---

## Build the installer

Run `ClashNavigator\Installer\BuildSetup.bat`.

It builds all five configurations and then calls Inno Setup. The result is:

```
ClashNavigator\Installer\Output\KT-BD-R11-GP-Mohamed Yasser-ClashNavigator-Setup.exe
```

The installer detects which Revit versions are present and, for each one, writes:

```
C:\ProgramData\Autodesk\Revit\Addins\<version>\ClashNavigator.addin
C:\ProgramData\Autodesk\Revit\Addins\<version>\ClashNavigator\ClashNavigator.dll
```

> Before testing the installer, delete the development copy from
> `%AppData%\Autodesk\Revit\Addins\2024\` — otherwise Revit loads the add-in twice and the
> ribbon button appears twice.

---

## Project layout

```
ClashNavigator/
├── App/          Command (opens the window) + RibbonApplication (builds the ribbon)
├── Models/       Plain data read from the report - no Revit types, so it can be tested
├── Core/         Parser, element resolver, geometry, navigation, version compatibility
├── Services/     File dialogs, behind an interface so the view model stays UI-free
├── ViewModels/   MVVM - the whole behaviour of the window
├── Views/        The WPF window (XAML) with almost no code-behind
├── Resources/    Ribbon icons, compiled into the DLL as embedded resources
└── Installer/    Inno Setup script, manifest and build batch file
```

The dependency direction is one-way: `Views → ViewModels → Core → Models`.
`Models` knows nothing about Revit, and `ViewModels` never calls the Revit API directly — it
hands jobs to `Core/RevitTask`, which gets them back into a valid Revit API context.
