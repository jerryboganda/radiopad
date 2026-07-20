# Pins the allow/ask boundary of pretooluse.ps1. Run with: powershell -NoProfile -File hooks/test-pretooluse.ps1
$ErrorActionPreference = 'Stop'
$hook = Join-Path $PSScriptRoot 'pretooluse.ps1'

$cases = @(
    @{ Command = 'dotnet build'; Expect = 'ask' },
    @{ Command = 'dotnet build --configuration Release'; Expect = 'ask' },
    @{ Command = 'dotnet test'; Expect = 'ask' },
    @{ Command = 'pnpm build'; Expect = 'ask' },
    @{ Command = 'pnpm typecheck'; Expect = 'ask' },
    @{ Command = 'pnpm lint'; Expect = 'ask' },
    @{ Command = 'pnpm --filter @radiopad/frontend build:desktop'; Expect = 'ask' },
    @{ Command = 'npx next build'; Expect = 'ask' },
    @{ Command = 'cargo build --release'; Expect = 'ask' },
    @{ Command = 'cargo test'; Expect = 'ask' },
    @{ Command = 'cargo tauri build'; Expect = 'ask' },
    @{ Command = 'docker build -t radiopad .'; Expect = 'ask' },
    @{ Command = 'docker compose build'; Expect = 'ask' },
    @{ Command = 'gh run watch 12345'; Expect = 'ask' },
    @{ Command = 'dotnet test --filter Retention_Worker_Skips_When_LegalHold'; Expect = 'allow' },
    @{ Command = 'dotnet run --project src/RadioPad.Api'; Expect = 'allow' },
    @{ Command = 'pnpm dev'; Expect = 'allow' },
    @{ Command = 'pnpm install'; Expect = 'allow' },
    @{ Command = 'pnpm release:desktop'; Expect = 'allow' },
    @{ Command = 'pnpm vitest run frontend/lib/companion.test.ts'; Expect = 'allow' },
    @{ Command = 'git push'; Expect = 'allow' },
    @{ Command = 'git commit -m "wip"'; Expect = 'allow' },
    @{ Command = 'gh run list -L 1'; Expect = 'allow' },
    @{ Command = 'gh pr create --fill'; Expect = 'allow' },
    @{ Command = 'git reset --hard'; Expect = 'ask' }
)

$failed = 0
foreach ($case in $cases) {
    $payload = @{ tool_name = 'Bash'; tool_input = @{ command = $case.Command } } | ConvertTo-Json -Compress
    $stdout = $payload | & powershell -NoProfile -NonInteractive -File $hook
    $actual = ($stdout | ConvertFrom-Json).hookSpecificOutput.permissionDecision
    $ok = $actual -eq $case.Expect
    if (-not $ok) { $failed++ }
    $status = if ($ok) { 'PASS' } else { 'FAIL' }
    $suffix = if ($ok) { '' } else { "   -> got '$actual'" }
    Write-Output ("{0}  {1}  {2}{3}" -f $status, $case.Expect.PadRight(5), $case.Command, $suffix)
}

Write-Output ""
if ($failed -gt 0) {
    Write-Output "$failed of $($cases.Count) failing"
    exit 1
}
Write-Output "all $($cases.Count) passing"
exit 0
