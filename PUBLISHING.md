# Building & Publishing StayAwake

StayAwake is a single-file C# app (`app.cs`) using .NET 10 file-based apps with
NativeAOT (`#:property PublishAot=true`, `RuntimeIdentifier=win-x64`).

## Quick build (for development / compile-check)

```powershell
dotnet build app.cs
```

This produces a framework-dependent build under a temp dir and is enough to
verify the code compiles. It does **not** produce the distributable exe.

## Publishing the native exe (NativeAOT)

NativeAOT compiles to a self-contained native executable, which requires the
**MSVC C++ linker**. Two gotchas on this machine:

1. Only **Visual Studio 2019 Build Tools** has the C++ VC tools installed
   (the "Desktop development with C++" workload). VS 18 Professional does
   **not** have them — using it gives `error: Platform linker not found`.
2. The AOT build calls `vswhere.exe`, which is **not on PATH** by default,
   giving `'vswhere.exe' is not recognized`.

So publish from the **VS 2019 Build Tools x64 developer environment**, with the
VS Installer directory (where `vswhere.exe` lives) added to PATH:

```powershell
$vsdev     = "C:\Program Files (x86)\Microsoft Visual Studio\2019\BuildTools\Common7\Tools\VsDevCmd.bat"
$installer = "C:\Program Files (x86)\Microsoft Visual Studio\Installer"

cmd /c "set `"PATH=$installer;%PATH%`" && call `"$vsdev`" -arch=x64 -host_arch=x64 && dotnet publish app.cs -o artifacts/app"
```

Output: `artifacts/app/app.exe` — a self-contained native exe (~1.5 MB).
(`artifacts/` is gitignored; the exe is build output, not committed.)

## Installing as the `stayawake` command

The `stayawake` command resolves to `C:\Users\fu.yu\tools\stayawake.exe`
(`Get-Command stayawake`). To update it after publishing:

```powershell
Copy-Item artifacts\app\app.exe C:\Users\fu.yu\tools\stayawake.exe -Force
```

## Usage

```
stayawake            # keep awake, unlimited, headless
stayawake -m         # also jiggle the mouse every ~30s
stayawake -h 2       # stop after 2 hours
stayawake -s         # fullscreen bouncing-shapes screensaver (Esc/any key exits)
stayawake -s -h 1    # screensaver for 1 hour
```

Keep-awake (`SetThreadExecutionState`) is active in every mode. Press Ctrl+C
to stop headless mode; Esc or any key exits the screensaver.
