# NoAdZip 엔진·흐름 자동 시험 (창 없이 /silent 로 실행하므로 사용자 조작을 빼앗지 않는다)
# 사용: pwsh tests\engine-tests.ps1 -Work <빈 시험 폴더>
param([Parameter(Mandatory = $true)][string]$Work)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
# 시험 대상 빌드 폴더: 환경변수 NOADZIP_BIN (기본 bin). 사용자가 쓰는 bin 을 잠그지 않도록 보통 bin_dev 를 쓴다.
$binName = if ($env:NOADZIP_BIN) { $env:NOADZIP_BIN } else { 'bin' }
$exe = Join-Path $root "$binName\몽집.exe"
$sz = Join-Path $root "$binName\7z.exe"
if (Test-Path $Work) { Remove-Item $Work -Recurse -Force }
New-Item -ItemType Directory -Force $Work | Out-Null
$dataDir = Join-Path $Work '_data'
New-Item -ItemType Directory -Force $dataDir | Out-Null
$env:NOADZIP_DATA = $dataDir
Remove-Item Env:NOADZIP_PASSWORD -ErrorAction SilentlyContinue
Remove-Item Env:NOADZIP_EXCLUDE -ErrorAction SilentlyContinue

$script:pass = 0
$script:fail = 0
function Check([string]$name, [bool]$cond, [string]$detail = '') {
    if ($cond) { $script:pass++; Write-Host ("  PASS  " + $name) }
    else { $script:fail++; Write-Host ("  FAIL  " + $name + "  " + $detail) -ForegroundColor Red }
}

function App([string[]]$a) {
    $q = ($a | ForEach-Object { '"' + $_ + '"' }) -join ' '
    $o = Join-Path $Work '_out.txt'
    $e = Join-Path $Work '_err.txt'
    $p = Start-Process -FilePath $exe -ArgumentList ($q + ' /silent') -Wait -PassThru -WindowStyle Hidden `
        -RedirectStandardOutput $o -RedirectStandardError $e
    $script:lastOut = (Get-Content $o -Raw -ErrorAction SilentlyContinue)
    return $p.ExitCode
}

function Fresh([string]$n) {
    $d = Join-Path $Work $n
    New-Item -ItemType Directory -Force $d | Out-Null
    return $d
}

Write-Host '== 시험 자료 준비'
& python -I (Join-Path $PSScriptRoot 'mkzips.py') $Work
$src = Fresh '원본 폴더\테스트 폴더'
New-Item -ItemType Directory -Force "$src\sub\deep" | Out-Null
Set-Content "$src\한글파일.txt" '안녕하세요' -Encoding utf8
Set-Content "$src\sub\a.txt" 'AAA' -Encoding utf8
Set-Content "$src\sub\deep\b.log" 'log' -Encoding utf8
Set-Content "$src\temp.tmp" 'tmp' -Encoding utf8
$srcParent = Split-Path $src -Parent

Write-Host '== 1. ZIP 압축 / 검사 / 풀기'
$rc = App @('/czip', $src)
Check 'ZIP 압축 종료코드 0' ($rc -eq 0) "rc=$rc"
$zip = Join-Path $srcParent '테스트 폴더.zip'
Check 'ZIP 파일 생성' (Test-Path $zip)
Check 'ZIP 검사 통과' ((App @('/test', $zip)) -eq 0)
$o1 = Fresh 'out1'
Copy-Item $zip $o1
$rc = App @('/xfolder', (Join-Path $o1 '테스트 폴더.zip'))
Check '이름 폴더 풀기 종료코드 0' ($rc -eq 0) "rc=$rc"
Check '이중 폴더 방지 (최상위 폴더 1개면 그대로)' (Test-Path "$o1\테스트 폴더\sub\a.txt") ((Get-ChildItem $o1 -Recurse | ForEach-Object FullName) -join '; ')
Check '이중 폴더가 생기지 않음' (-not (Test-Path "$o1\테스트 폴더\테스트 폴더"))
Check '한글 파일명·내용 보존' ((Get-Content "$o1\테스트 폴더\한글파일.txt" -Raw -Encoding utf8).Trim() -eq '안녕하세요')

Write-Host '== 2. 최상위가 여러 개일 때 이름 폴더 생성'
$o2 = Fresh 'out2'
Copy-Item "$Work\two_tops.zip" $o2
Check '풀기 성공' ((App @('/xfolder', "$o2\two_tops.zip")) -eq 0)
Check 'two_tops 폴더 생성' (Test-Path "$o2\two_tops\a.txt")
$o2b = Fresh 'out2b'
Copy-Item "$Work\two_tops.zip" $o2b
Check '/xhere 는 폴더를 만들지 않음' (((App @('/xhere', "$o2b\two_tops.zip")) -eq 0) -and (Test-Path "$o2b\a.txt") -and -not (Test-Path "$o2b\two_tops"))
$o2c = Fresh 'out2c'
Copy-Item "$Work\one_top.zip" $o2c
Check '단일 최상위 폴더 압축 풀기' (((App @('/xfolder', "$o2c\one_top.zip")) -eq 0) -and (Test-Path "$o2c\only\inner\y.txt") -and -not (Test-Path "$o2c\one_top"))

Write-Host '== 3. 지정 폴더 풀기 (/xto)'
$o3 = Join-Path $Work 'out3\새 폴더'
$rc = App @('/xto', $o3, $zip)
Check '/xto 가 없는 폴더도 만들어 풂' (($rc -eq 0) -and (Test-Path "$o3\테스트 폴더\sub\deep\b.log")) "rc=$rc"

Write-Host '== 4. 7z / TAR 계열 왕복'
foreach ($f in @('7z', 'tar', 'tar.gz', 'tar.bz2', 'tar.xz')) {
    $s = Fresh ("fmt_$f")
    Copy-Item $src $s -Recurse
    $rc = App @("/c:$f", "$s\테스트 폴더")
    $arc = Join-Path $s ("테스트 폴더." + $f)
    Check "$f 압축" (($rc -eq 0) -and (Test-Path $arc)) "rc=$rc"
    Check "$f 임시 파일 정리" (-not (Get-ChildItem $s -Force -Filter '.nozip*') -and -not (Get-ChildItem $s -Force -Filter '*.nozip~'))
    $d = Fresh ("fmt_${f}_out")
    Copy-Item $arc $d
    $rc = App @('/xfolder', (Join-Path $d ("테스트 폴더." + $f)))
    $found = Get-ChildItem $d -Recurse -Filter 'a.txt' -ErrorAction SilentlyContinue
    Check "$f 풀기" (($rc -eq 0) -and $found -ne $null) "rc=$rc"
}

Write-Host '== 5. 단일 파일 GZ'
$g = Fresh 'gz'
Set-Content "$g\note.txt" 'gz test' -Encoding utf8
Check 'gz 압축' (((App @('/c:gz', "$g\note.txt")) -eq 0) -and (Test-Path "$g\note.txt.gz"))
Remove-Item "$g\note.txt"
Check 'gz 풀기' (((App @('/xhere', "$g\note.txt.gz")) -eq 0) -and (Test-Path "$g\note.txt"))

Write-Host '== 6. 암호 (AES-256)'
$pwd1 = Fresh 'pw'
Copy-Item $src $pwd1 -Recurse
$env:NOADZIP_PASSWORD = 'pass123!'
Check '암호 ZIP 압축' ((App @('/czip', "$pwd1\테스트 폴더")) -eq 0)
$pzip = "$pwd1\테스트 폴더.zip"
$listing = & $sz l -slt -sccUTF-8 $pzip | Out-String
Check '항목이 암호화됨(Encrypted = +)' ($listing -match 'Encrypted = \+')
Check 'AES 로 암호화됨' ($listing -match 'AES')
Remove-Item Env:NOADZIP_PASSWORD
$pd = Fresh 'pw_out_bad'
Copy-Item $pzip $pd
Check '암호 없이 풀면 실패' ((App @('/xhere', "$pd\테스트 폴더.zip")) -ne 0)
$env:NOADZIP_PASSWORD = 'wrong'
Check '틀린 암호면 실패' ((App @('/xhere', "$pd\테스트 폴더.zip")) -ne 0)
$env:NOADZIP_PASSWORD = 'pass123!'
$pd2 = Fresh 'pw_out_ok'
Copy-Item $pzip $pd2
Check '맞는 암호면 성공' ((App @('/xfolder', "$pd2\테스트 폴더.zip")) -eq 0)
Check '암호 풀기 내용 일치' ((Get-Content "$pd2\테스트 폴더\sub\a.txt" -Raw -Encoding utf8).Trim() -eq 'AAA')
$env:NOADZIP_PASSWORD = 'pass123!'
Check '암호 ZIP 검사' ((App @('/test', $pzip)) -eq 0)
Remove-Item Env:NOADZIP_PASSWORD
$p7 = Fresh 'pw7'
Copy-Item $src $p7 -Recurse
$env:NOADZIP_PASSWORD = '7zpass'
Check '암호 7z 압축·검사' (((App @('/c7z', "$p7\테스트 폴더")) -eq 0) -and ((App @('/test', "$p7\테스트 폴더.7z")) -eq 0))
Remove-Item Env:NOADZIP_PASSWORD

Write-Host '== 7. 제외 패턴'
$ex = Fresh 'excl'
Copy-Item $src $ex -Recurse
$env:NOADZIP_EXCLUDE = '*.tmp;*.log'
Check '제외 패턴 압축' ((App @('/czip', "$ex\테스트 폴더")) -eq 0)
Remove-Item Env:NOADZIP_EXCLUDE
$l = & $sz l -slt -sccUTF-8 "$ex\테스트 폴더.zip" | Out-String
Check '*.tmp 제외됨' ($l -notmatch 'temp\.tmp')
Check '*.log 제외됨' ($l -notmatch 'b\.log')
Check '다른 파일은 포함' (($l -match 'a\.txt') -and ($l -match '한글파일\.txt'))

Write-Host '== 8. 선택 항목만 풀기'
$sel = Fresh 'sel'
Copy-Item $zip $sel
Check '폴더 지정 풀기' (((App @('/xhere', "/only:테스트 폴더\sub", "$sel\테스트 폴더.zip")) -eq 0) -and (Test-Path "$sel\테스트 폴더\sub\deep\b.log"))
Check '지정하지 않은 항목은 안 풀림' (-not (Test-Path "$sel\테스트 폴더\한글파일.txt"))
$sel2 = Fresh 'sel2'
Copy-Item $zip $sel2
Check '파일 하나만 풀기' (((App @('/xhere', "/only:테스트 폴더\sub\a.txt", "$sel2\테스트 폴더.zip")) -eq 0) -and (Test-Path "$sel2\테스트 폴더\sub\a.txt") -and -not (Test-Path "$sel2\테스트 폴더\sub\deep"))

Write-Host '== 9. 보안: Zip Slip / 절대 경로'
$sl = Fresh 'slip\inner'
Copy-Item "$Work\slip.zip" $sl
$rc = App @('/xfolder', "$sl\slip.zip")
Check 'Zip Slip 압축파일 풀기는 완료' ($rc -eq 0) "rc=$rc out=$script:lastOut"
$slipRoot = Join-Path $Work 'slip'
$evil = @(Get-ChildItem $slipRoot -Recurse -Filter 'evil*.txt' -ErrorAction SilentlyContinue) + @(Get-ChildItem $Work -Filter 'evil*.txt' -ErrorAction SilentlyContinue)
Check '폴더 밖으로 탈출한 파일이 없음' ($evil.Count -eq 0) (($evil | ForEach-Object FullName) -join '; ')
Check '정상 항목은 풀림' ((Get-ChildItem $slipRoot -Recurse -Filter 'good.txt').Count -eq 1)
$ab = Fresh 'abs'
Copy-Item "$Work\abs.zip" $ab
$rc = App @('/xhere', "$ab\abs.zip")
Check '절대 경로 항목이 드라이브 루트에 풀리지 않음' (-not (Test-Path 'C:\tmp_abs_evil.txt') -and -not (Test-Path 'J:\tmp_abs_evil.txt') -and -not (Test-Path "\tmp_abs_evil.txt"))

Write-Host '== 10. 보안: 압축 폭탄 경고'
[System.IO.File]::WriteAllText("$dataDir\settings.ini", "BombRatio=50`nBombMinBytes=1000000`n")
$b = Fresh 'bomb'
Copy-Item "$Work\bomb.zip" $b
$rc = App @('/xfolder', "$b\bomb.zip")
Check '의심 압축파일은 무인 모드에서 거부' (($rc -ne 0) -and -not (Test-Path "$b\zeros.bin")) "rc=$rc"
$rc = App @('/xhere', "$b\bomb.zip", '/force')
Check '/force 면 풀림' (($rc -eq 0) -and (Test-Path "$b\zeros.bin"))
Remove-Item "$dataDir\settings.ini"

Write-Host '== 11. 파일명 인코딩 (CP949, UTF-8 플래그 없음)'
$kr = Fresh 'cp949'
Copy-Item "$Work\cp949.zip" $kr
$rc = App @('/xhere', "$kr\cp949.zip")
Check 'CP949 한글 파일명 복원' (($rc -eq 0) -and (Test-Path "$kr\한글.txt")) ((Get-ChildItem $kr | ForEach-Object Name) -join ', ')

Write-Host '== 12. 같은 이름 파일 처리 (무인 모드는 이름 바꿔 풀기)'
$ow = Fresh 'overwrite'
Copy-Item "$Work\two_tops.zip" $ow
Set-Content "$ow\a.txt" 'ORIGINAL' -NoNewline
$rc = App @('/xhere', "$ow\two_tops.zip")
Check '기존 파일 내용 유지' ((Get-Content "$ow\a.txt" -Raw) -eq 'ORIGINAL')
Check '새 파일이 다른 이름으로 풀림' ((Get-ChildItem $ow -Filter 'a*.txt').Count -eq 2)
[System.IO.File]::WriteAllText("$dataDir\settings.ini", "Overwrite=all`n")
$rc = App @('/xhere', "$ow\two_tops.zip")
Check '덮어쓰기 설정이면 덮어씀' ((Get-Content "$ow\a.txt" -Raw) -eq 'A')
Remove-Item "$dataDir\settings.ini"

Write-Host '== 13. 손상·오류 처리'
Check '손상된 ZIP 검사는 실패' ((App @('/test', "$Work\broken.zip")) -ne 0)
Check '없는 파일 풀기는 실패(비정상 종료 아님)' ((App @('/xhere', "$Work\nope.zip")) -ne 0)
Set-Content "$Work\notarchive.zip" 'this is not an archive' -Encoding utf8
Check '압축파일이 아닌 파일은 거부' ((App @('/test', "$Work\notarchive.zip")) -ne 0)

Write-Host '== 14. 긴 경로 (260자 초과)'
$lp = Join-Path $Work 'longpath'
$seg = 'a' * 50
$deep = $lp
1..7 | ForEach-Object { $deep = Join-Path $deep $seg }
[System.IO.Directory]::CreateDirectory('\\?\' + $deep) | Out-Null
[System.IO.File]::WriteAllText('\\?\' + (Join-Path $deep 'long.txt'), 'long')
Check '긴 경로 시험 자료 생성' ($deep.Length -gt 260)
$rc = App @('/czip', (Join-Path $lp $seg))
Check '긴 경로 압축' (($rc -eq 0) -and (Test-Path "$lp\$seg.zip")) "rc=$rc"
$lo = Fresh 'longpath_out'
Copy-Item "$lp\$seg.zip" $lo
$rc = App @('/xhere', "$lo\$seg.zip")
Check '긴 경로 풀기' (($rc -eq 0) -and ([System.IO.File]::Exists('\\?\' + (Join-Path $lo ($seg + '\' + (($seg + '\') * 6) + 'long.txt'))))) "rc=$rc"

Write-Host '== 16. 분할 압축 / 분할 파일 풀기 (.002 를 열어도 .001 로 연결)'
$sp = Fresh 'split'
$rnd = New-Object byte[] (3MB)
(New-Object System.Random 42).NextBytes($rnd)
[IO.File]::WriteAllBytes("$sp\big.bin", $rnd)
$env:NOADZIP_SPLIT_MB = '1'
Check 'ZIP 분할 압축' ((App @('/czip', "$sp\big.bin")) -eq 0)
Check '분할 조각 3개 이상 생성' ((Get-ChildItem $sp -Filter 'big.zip.0*').Count -ge 3) ((Get-ChildItem $sp | ForEach-Object Name) -join ', ')
Check '임시 조각이 남지 않음' ((Get-ChildItem $sp -Force -Filter '*.nozip~*').Count -eq 0)
Remove-Item Env:NOADZIP_SPLIT_MB
$spo = Fresh 'split_out'
Get-ChildItem $sp -Filter 'big.zip.0*' | Copy-Item -Destination $spo
Check '두 번째 조각을 지정해도 전체 풀기' (((App @('/xhere', "$spo\big.zip.002")) -eq 0) -and ((Get-FileHash "$spo\big.bin").Hash -eq (Get-FileHash "$sp\big.bin").Hash))
$sp7 = Fresh 'split7'
Copy-Item "$sp\big.bin" $sp7
$env:NOADZIP_SPLIT_MB = '1'
Check '7z 분할 압축' ((App @('/c7z', "$sp7\big.bin")) -eq 0)
Remove-Item Env:NOADZIP_SPLIT_MB
Remove-Item "$sp7\big.bin"
Check '7z 분할 풀기' (((App @('/xhere', "$sp7\big.7z.001")) -eq 0) -and ((Get-FileHash "$sp7\big.bin").Hash -eq (Get-FileHash "$sp\big.bin").Hash))

Write-Host '== 17. 7z 파일명 암호화 / 솔리드 끄기'
$he = Fresh 'hdrenc'
Copy-Item $src $he -Recurse
$env:NOADZIP_PASSWORD = 'hdrpw'
$env:NOADZIP_HDRENC = '1'
Check '파일명 암호화 7z 압축' ((App @('/c7z', "$he\테스트 폴더")) -eq 0)
Remove-Item Env:NOADZIP_HDRENC
Remove-Item Env:NOADZIP_PASSWORD
$noPw = & $sz l -slt "$he\테스트 폴더.7z" 2>&1 | Out-String
Check '암호 없이는 목록도 못 봄' (($noPw -notmatch 'a\.txt') -and ($noPw -match 'rror|assword'))
Check '암호 없이 검사하면 실패' ((App @('/test', "$he\테스트 폴더.7z")) -ne 0)
$env:NOADZIP_PASSWORD = 'hdrpw'
Check '암호로 검사하면 통과' ((App @('/test', "$he\테스트 폴더.7z")) -eq 0)
$heo = Fresh 'hdrenc_out'
Copy-Item "$he\테스트 폴더.7z" $heo
Check '암호로 풀기' (((App @('/xfolder', "$heo\테스트 폴더.7z")) -eq 0) -and (Test-Path "$heo\테스트 폴더\sub\a.txt"))
Remove-Item Env:NOADZIP_PASSWORD
$ns = Fresh 'nosolid'
Copy-Item $src $ns -Recurse
$env:NOADZIP_SOLID = '0'
Check '솔리드 끄고 압축' ((App @('/c7z', "$ns\테스트 폴더")) -eq 0)
Remove-Item Env:NOADZIP_SOLID
Check '솔리드가 꺼져 있음' ((& $sz l -slt "$ns\테스트 폴더.7z" | Out-String) -match 'Solid = -')
$sd = Fresh 'solid'
Copy-Item $src $sd -Recurse
Check '기본(솔리드 켬) 압축' ((App @('/c7z', "$sd\테스트 폴더")) -eq 0)
Check '솔리드가 켜져 있음' ((& $sz l -slt "$sd\테스트 폴더.7z" | Out-String) -match 'Solid = \+')

Write-Host '== 18. MOTW(인터넷에서 받은 표시) 전파'
$mo = Fresh 'motw'
Copy-Item $zip "$mo\web.zip"
Set-Content -Path "$mo\web.zip" -Stream Zone.Identifier -Value "[ZoneTransfer]`r`nZoneId=3"
Check '원본에 Zone.Identifier 가 있음' ([bool]((Get-Content "$mo\web.zip" -Stream Zone.Identifier -ErrorAction SilentlyContinue) -match 'ZoneId=3'))
Check '풀기 성공' ((App @('/xhere', "$mo\web.zip")) -eq 0)
Check '풀린 파일에 Zone.Identifier 전파' ([bool]((Get-Content "$mo\테스트 폴더\한글파일.txt" -Stream Zone.Identifier -ErrorAction SilentlyContinue) -match 'ZoneId=3'))
$mo2 = Fresh 'motw_none'
Copy-Item $zip "$mo2\local.zip"
Check '표시 없는 압축파일은 표시 없이 풀림' (((App @('/xhere', "$mo2\local.zip")) -eq 0) -and -not (Get-Content "$mo2\테스트 폴더\한글파일.txt" -Stream Zone.Identifier -ErrorAction SilentlyContinue))

Write-Host '== 19. CAB 풀기 (구형 포맷)'
$cb = Fresh 'cab'
Set-Content "$cb\hello.txt" 'cab content' -Encoding utf8
Push-Location $cb
& makecab /D CompressionType=LZX hello.txt hello.cab | Out-Null
Pop-Location
Remove-Item "$cb\hello.txt"
Check 'CAB 파일 생성' (Test-Path "$cb\hello.cab")
Check 'CAB 풀기' (((App @('/xhere', "$cb\hello.cab")) -eq 0) -and ((Get-Content "$cb\hello.txt" -Raw -Encoding utf8).Trim() -eq 'cab content'))

Write-Host '== 20. 탐색기 우클릭 메뉴 등록/해제 (시험용 레지스트리 위치만 사용)'
$env:NOADZIP_REGTEST = 'Software\NoAdZipTest'
Remove-Item 'HKCU:\Software\NoAdZipTest' -Recurse -Force -ErrorAction SilentlyContinue
Check '메뉴 등록 종료코드 0' ((App @('/regmenu')) -eq 0)
Check '압축 메뉴 키(*)' (Test-Path -LiteralPath 'HKCU:\Software\NoAdZipTest\Classes\*\shell\NoAdZip')
Check '폴더 메뉴 키' (Test-Path 'HKCU:\Software\NoAdZipTest\Classes\Directory\shell\NoAdZip')
Check '압축파일 풀기 메뉴 키(.zip)' (Test-Path 'HKCU:\Software\NoAdZipTest\Classes\SystemFileAssociations\.zip\shell\NoAdZip.Extract')
$sub = (Get-ItemProperty -LiteralPath 'HKCU:\Software\NoAdZipTest\Classes\*\shell\NoAdZip' -ErrorAction SilentlyContinue).SubCommands
Check '하위 메뉴 목록' ($sub -match 'NoAdZip.Compress;NoAdZip.Zip;NoAdZip.7z') "$sub"
$cmd = (Get-ItemProperty 'HKCU:\Software\NoAdZipTest\CommandStore\shell\NoAdZip.XHere\command' -ErrorAction SilentlyContinue).'(default)'
Check '풀기 명령줄 형식' (($cmd -match '몽집.exe" /xhere "%1"')) "$cmd"
Check '메뉴 아이콘 지정' ((Get-ItemProperty 'HKCU:\Software\NoAdZipTest\CommandStore\shell\NoAdZip.XHere' -ErrorAction SilentlyContinue).Icon -match '몽집.exe",0')
Check '메뉴 해제 종료코드 0' ((App @('/unregmenu')) -eq 0)
Check '해제 후 압축 메뉴 키 없음' (-not (Test-Path -LiteralPath 'HKCU:\Software\NoAdZipTest\Classes\*\shell\NoAdZip'))
Check '해제 후 명령 저장소 비어 있음' (-not (Test-Path 'HKCU:\Software\NoAdZipTest\CommandStore\shell\NoAdZip.XHere'))
Remove-Item 'HKCU:\Software\NoAdZipTest' -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item Env:NOADZIP_REGTEST

Write-Host '== 21. 다중 선택 대기열: 프로세스 3개가 하나의 압축으로 합쳐짐'
$qd = Fresh 'queue\묶음'
1..3 | ForEach-Object { Set-Content "$qd\f$_.txt" "file $_" -Encoding utf8 }
$env:NOADZIP_QUEUE = '1'
$procs = 1..3 | ForEach-Object {
    Start-Process -FilePath $exe -ArgumentList "/czip `"$qd\f$_.txt`" /silent" -PassThru -WindowStyle Hidden
}
$procs | Wait-Process -Timeout 30
Remove-Item Env:NOADZIP_QUEUE
$qz = @(Get-ChildItem $qd -Filter '*.zip')
Check '압축파일이 정확히 1개 생성' ($qz.Count -eq 1) (($qz | ForEach-Object Name) -join ', ')
if ($qz.Count -ge 1) {
    $ql = & $sz l -slt -sccUTF-8 $qz[0].FullName | Out-String
    Check '세 파일이 모두 들어 있음' (($ql -match 'f1\.txt') -and ($ql -match 'f2\.txt') -and ($ql -match 'f3\.txt'))
}
Check '모든 프로세스가 정상 종료' (($procs | Where-Object { $_.ExitCode -ne 0 }).Count -eq 0)

Write-Host '== 22. 포터블 모드 (portable.txt 가 있으면 설정·로그를 프로그램 폴더에 저장)'
$pt = Fresh 'portable'
Copy-Item (Split-Path $exe -Parent) "$pt\app" -Recurse
Set-Content "$pt\app\portable.txt" 'portable' -Encoding utf8
$ps = Fresh 'portable_src'
Set-Content "$ps\x.txt" 'x' -Encoding utf8
$saved = $env:NOADZIP_DATA
Remove-Item Env:NOADZIP_DATA
$p = Start-Process -FilePath "$pt\app\몽집.exe" -ArgumentList "/czip `"$ps\x.txt`" /silent" -Wait -PassThru -WindowStyle Hidden
$env:NOADZIP_DATA = $saved
Check '포터블 압축 성공' (($p.ExitCode -eq 0) -and (Test-Path "$ps\x.zip"))
Check '로그가 프로그램 폴더 data 에 생김' ((Get-ChildItem "$pt\app\data\logs" -Filter '*.log' -ErrorAction SilentlyContinue).Count -ge 1)

Write-Host '== 15. 임시 파일 누수 확인'
$left = @(Get-ChildItem (Join-Path $env:TEMP 'NoAdZip') -Filter 'list_*.txt' -ErrorAction SilentlyContinue)
Check '목록 임시 파일이 남지 않음' ($left.Count -eq 0) ($left.Name -join ', ')
# 사용자가 따로 쓰는 NoAdZip 은 세지 않고, 이 시험이 띄운 빌드 폴더·시험 폴더의 프로세스만 확인한다.
$binDir = Split-Path $exe -Parent
$procs = @(Get-Process -Name '7z', '몽집' -ErrorAction SilentlyContinue | Where-Object { $_.Path -and (($_.Path -like "$binDir\*") -or ($_.Path -like "$Work\*")) })
Check '남은 하위 프로세스 없음' ($procs.Count -eq 0) (($procs | ForEach-Object { $_.Name + ':' + $_.Id }) -join ', ')

Write-Host ''
Write-Host ("결과: PASS {0} / FAIL {1}" -f $script:pass, $script:fail)
if ($script:fail -gt 0) { exit 1 }
