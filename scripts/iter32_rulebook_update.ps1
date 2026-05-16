$files = Get-ChildItem C:\Users\Administrator\Desktop\Radiopad.com\rulebooks\*.yaml
"count=$($files.Count)" | Write-Host
foreach ($f in $files) {
  try {
    $c = Get-Content $f.FullName -Raw
    if ($c -notmatch 'approved_followups') {
      $list = "  approved_followups:`n    - Recommend clinical correlation.`n    - Recommend follow-up imaging as clinically indicated.`n    - Recommend specialty consult if clinically warranted.`n"
      $regex = [regex]'(?ms)(\n  avoid_terms:\r?\n(?:    - [^\r\n]*\r?\n)+)'
      if ($regex.IsMatch($c)) {
        $c = $regex.Replace($c, "`$1$list", 1)
      } else {
        Write-Host "  NO MATCH avoid_terms in $($f.Name)"
        continue
      }
    }
    if ($c -notmatch 'unauthorized_followup') {
      $rule = "  - id: unauthorized_followup`n    severity: warning`n    description: Recommendation phrases must come from style.approved_followups.`n"
      $regex2 = [regex]'(?ms)(\nrules:\r?\n(?:[\s\S]*?))(\nprompt_blocks:)'
      if ($regex2.IsMatch($c)) {
        $c = $regex2.Replace($c, "`$1$rule`$2", 1)
      } else {
        Write-Host "  NO MATCH rules->prompt_blocks in $($f.Name)"
      }
    }
    Set-Content -Path $f.FullName -Value $c -NoNewline
    Write-Host "  ok $($f.Name)"
  } catch {
    Write-Host "  ERR $($f.Name) :: $_"
  }
}
"final=$((Get-ChildItem C:\Users\Administrator\Desktop\Radiopad.com\rulebooks\*.yaml | Select-String -Pattern 'approved_followups' -List).Count)" | Write-Host
