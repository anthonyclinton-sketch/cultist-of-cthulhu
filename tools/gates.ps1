<#
    Gate runner and launcher (docs/11 §2).

        ./tools/gates.ps1                       run every gate
        ./tools/gates.ps1 -Arena                play the M1 combat slice
        ./tools/gates.ps1 -Arena -MeteredDodge  play Build B (the M1 control arm)
        ./tools/gates.ps1 -Play                 the bullet stress arena
        ./tools/gates.ps1 -Lab                  the Pattern Lab
        ./tools/gates.ps1 -ShowSeed 7           render one generated floor as ASCII
        ./tools/gates.ps1 -Seed cthulhu         fix the seed (any text, hashed)
        ./tools/gates.ps1 -SkipBuild            skip the C# build

    Every gate exits non-zero on failure, so this script is CI-consumable as-is.
#>
[CmdletBinding()]
param(
    [string]$Seed = "cthulhu",
    [switch]$Play,
    [switch]$Arena,
    [switch]$Lab,
    [string]$ShowSeed,
    [switch]$MeteredDodge,
    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot

# Godot is not assumed to be on PATH. Override with $env:GODOT.
if ($env:GODOT -and (Test-Path $env:GODOT)) {
    $godot = $env:GODOT
} else {
    $candidates = @(
        "$env:USERPROFILE\Downloads\Godot_v4.7-stable_mono_win64\Godot_v4.7-stable_mono_win64\Godot_v4.7-stable_mono_win64_console.exe",
        "$env:LOCALAPPDATA\Programs\Godot\Godot_v4.7-stable_mono_win64_console.exe"
    )
    $godot = $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1
    if (-not $godot) {
        throw "Godot not found. Set `$env:GODOT to the Godot 4.7 mono console executable."
    }
}

Write-Host "godot   $godot"
Write-Host "seed    $Seed`n"

if (-not $SkipBuild) {
    # NOTE: Godot loads the Debug assembly from .godot/mono/temp/bin/Debug when running
    # from the CLI. Building -c Release produces a binary Godot will NOT load, and the
    # gates then silently measure stale code. Do not "optimise" this to Release.
    & dotnet build "$root/CultistOfCthulhu.csproj" -v q --nologo
    if ($LASTEXITCODE -ne 0) { throw "C# build failed." }
}

if ($Play)  { & $godot --path $root res://scenes/debug/StressTest.tscn  --seed $Seed; exit $LASTEXITCODE }
if ($Arena) {
    # -MeteredDodge runs Build B, the M1 control arm (docs/11 §M1 test design).
    $extra = if ($MeteredDodge) { @("--metered-dodge") } else { @() }
    & $godot --path $root res://scenes/debug/CombatArena.tscn --seed $Seed @extra
    exit $LASTEXITCODE
}
if ($Lab)   { & $godot --path $root res://scenes/debug/PatternLab.tscn  --seed $Seed; exit $LASTEXITCODE }

if ($ShowSeed) {
    # The Generation Visualiser (docs/06 §10). Note the arg form is --show-seed=N with an
    # equals sign — GenerationTest parses it as a single token, so passing it as two
    # arguments silently falls through to the full 10k sweep instead.
    & $godot --headless --path $root res://scenes/debug/GenerationTest.tscn "--show-seed=$ShowSeed"
    exit $LASTEXITCODE
}

$failed = @()

Write-Host "`n### CONTENT VALIDATION ###"
& $godot --headless --path $root res://scenes/debug/ContentValidator.tscn
if ($LASTEXITCODE -ne 0) { $failed += "content validation" }

Write-Host "`n### ASCENSION INVARIANTS ###"
& $godot --headless --path $root res://scenes/debug/AscensionTest.tscn
if ($LASTEXITCODE -ne 0) { $failed += "ascension invariants" }

Write-Host "`n### BANISH ###"
& $godot --headless --path $root res://scenes/debug/BanishTest.tscn
if ($LASTEXITCODE -ne 0) { $failed += "banish" }

Write-Host "`n### FLOOR GENERATION (10k seeds) ###"
& $godot --headless --path $root res://scenes/debug/GenerationTest.tscn
if ($LASTEXITCODE -ne 0) { $failed += "floor generation" }

# Advisory, not a gate. These are tuning targets — drifting out of them should start a
# conversation, not break the build.
Write-Host "`n### ECONOMY SIMULATION (advisory) ###"
& $godot --headless --path $root res://scenes/debug/EconomySim.tscn

Write-Host "`n### M0 GATE 1 — BULLET PERFORMANCE ###"
& $godot --headless --path $root res://scenes/debug/Benchmark.tscn --seed $Seed
if ($LASTEXITCODE -ne 0) { $failed += "bullet performance" }

Write-Host "`n### M0 GATE 2 — DETERMINISM ###"
& $godot --headless --path $root res://scenes/debug/DeterminismTest.tscn --seed $Seed
if ($LASTEXITCODE -ne 0) { $failed += "determinism" }

Write-Host ""
if ($failed.Count -gt 0) {
    Write-Host "M0 GATES FAILED: $($failed -join ', ')" -ForegroundColor Red
    exit 1
}
Write-Host "ALL M0 GATES PASS" -ForegroundColor Green
exit 0
