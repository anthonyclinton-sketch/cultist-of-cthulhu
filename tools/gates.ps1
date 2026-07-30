<#
    Gate runner and launcher (docs/11 §2).

        ./tools/gates.ps1                       run every gate
        ./tools/gates.ps1 -Floor                PLAY a run: floor 1, boss, summary
        ./tools/gates.ps1 -Floor -Floors 3      play a three-floor run
        ./tools/gates.ps1 -Floor -Autorun       WATCH the run loop play itself
        ./tools/gates.ps1 -Floor -Corruption 3  start Corrupted (3 = Awakened, 10 = Yellow Sign)
        ./tools/gates.ps1 -Floor -FloodDemo     flood every room, to SEE the Tide (docs/07 §3)
        ./tools/gates.ps1 -Floor -StartFloor 2  BEGIN on floor 2, without killing boss 1 first
        ./tools/gates.ps1 -Arena                play the M1 combat slice (fixed arena)
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
    [switch]$Floor,
    [switch]$Arena,
    [switch]$Lab,
    [string]$ShowSeed,
    [switch]$MeteredDodge,
    [switch]$Autorun,
    [int]$Floors = 1,
    [int]$StartFloor = 1,
    [double]$Corruption = 0,
    [switch]$FloodDemo,
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

if ($Floor) {
    $extra = @()
    if ($MeteredDodge) { $extra += "--metered-dodge" }
    if ($Floors -gt 1) { $extra += "--floors=$Floors" }
    # -Autorun plays the run itself, windowed, so the loop can be WATCHED rather than
    # only asserted. It is the same harness the gate runs headlessly.
    if ($Autorun) { $extra += "--autorun" }
    # Start already Corrupted. Reaching Corruption 3 by Banishing takes twelve Banishes at
    # 45 Sanity each, so this is the only practical way to look at the thresholds.
    # InvariantCulture: on a comma-decimal locale "3.5" would be formatted as "3,5" and the
    # game's float.TryParse would silently reject it.
    if ($Corruption -gt 0) {
        $extra += "--corruption=" + $Corruption.ToString([System.Globalization.CultureInfo]::InvariantCulture)
    }
    # The Undercroft authors no water, so the Tide is invisible on floor 1 by design. This
    # synthesises a channel into every room so the mechanic can be watched and walked into
    # before a Wharf template exists — same purpose as -Corruption above.
    if ($FloodDemo) { $extra += "--flood-demo" }
    # Begin partway down. Floor 1 is the only floor with a boss on it, so reaching floor 2
    # otherwise means winning a whole floor first — which makes testing floor-2 content cost
    # five minutes of floor 1 every time. The run still ENDS on the deepest floor asked for,
    # so -StartFloor 2 alone is a one-floor run that happens to be floor 2.
    if ($StartFloor -gt 1) { $extra += "--start-floor=$StartFloor" }
    & $godot --path $root res://scenes/debug/FloorRunner.tscn --seed $Seed @extra
    exit $LASTEXITCODE
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

# A run, played start to finish. The only gate that reaches a boss, completes a floor,
# carries a build down a stair and ends a run — none of which any other gate can even get
# near, because they all leave the player standing in the entrance.
Write-Host "`n### AUTORUN — A COMPLETE RUN ###"
& $godot --headless --path $root res://scenes/debug/FloorRunner.tscn --seed $Seed --autorun --quit-after 40000
if ($LASTEXITCODE -ne 0) { $failed += "autorun" }

Write-Host "`n### AUTORUN — THREE FLOORS (descent carries the run) ###"
& $godot --headless --path $root res://scenes/debug/FloorRunner.tscn --seed $Seed --autorun --floors=3 --quit-after 60000
if ($LASTEXITCODE -ne 0) { $failed += "autorun (3 floors)" }

Write-Host "`n### ENCOUNTERS — DREAD BUDGET AND WAVES ###"
& $godot --headless --path $root res://scenes/debug/EncounterTest.tscn
if ($LASTEXITCODE -ne 0) { $failed += "encounters" }

Write-Host "`n### BLINK STEP FRAME DATA ###"
& $godot --headless --path $root res://scenes/debug/BlinkTest.tscn
if ($LASTEXITCODE -ne 0) { $failed += "blink frame data" }

Write-Host "`n### CORRUPTION ###"
& $godot --headless --path $root res://scenes/debug/CorruptionTest.tscn
if ($LASTEXITCODE -ne 0) { $failed += "corruption" }

Write-Host "`n### THE TIDE ###"
& $godot --headless --path $root res://scenes/debug/TideTest.tscn
if ($LASTEXITCODE -ne 0) { $failed += "tide" }

Write-Host "`n### BOSS 1 ###"
& $godot --headless --path $root res://scenes/debug/BossTest.tscn
if ($LASTEXITCODE -ne 0) { $failed += "boss" }

Write-Host "`n### WALL COLLISION ###"
& $godot --headless --path $root res://scenes/debug/WallCollisionTest.tscn
if ($LASTEXITCODE -ne 0) { $failed += "wall collision" }

Write-Host "`n### FLOOR GENERATION (10k seeds) ###"
& $godot --headless --path $root res://scenes/debug/GenerationTest.tscn
if ($LASTEXITCODE -ne 0) { $failed += "floor generation" }

Write-Host "`n### PLAYABLE FLOOR SMOKE ###"
foreach ($s in @("1", "7", "cthulhu")) {
    & $godot --headless --path $root res://scenes/debug/FloorRunner.tscn --seed $s --quit-after 600 | Out-Null
    if ($LASTEXITCODE -ne 0) { $failed += "floor smoke (seed $s)"; break }
}
if ($failed -notcontains "floor smoke") { Write-Host " boots and runs on 3 seeds: PASS" }

# ENGINE WARNING BUDGET.
#
# A per-frame engine warning is not cosmetic: Godot generates a full managed stack walk,
# formats it and flushes it to disk for each one. At display rate that stalls the process
# badly enough to be reported as the game freezing, which is exactly what happened with
# MultiMesh physics interpolation.
#
# It went unnoticed for a milestone because every gate piped output through
# `Select-Object -Last N`, so the thousands of warnings scrolled past unseen. Counting them
# is the fix; a budget of zero is achievable and anything above it is a real defect.
Write-Host "`n### ENGINE WARNING BUDGET ###"
$warnOutput = & $godot --headless --path $root res://scenes/debug/FloorRunner.tscn --seed 7 --quit-after 600 2>&1
$warnCount = ($warnOutput | Select-String -Pattern "^WARNING:").Count
Write-Host " engine warnings over 600 frames: $warnCount"
if ($warnCount -gt 0) {
    $warnOutput | Select-String -Pattern "^WARNING:" | Select-Object -First 3 | ForEach-Object { Write-Host "   $_" }
    $failed += "engine warnings ($warnCount)"
} else { Write-Host " no engine warnings: PASS" }

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
