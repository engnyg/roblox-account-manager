<#
  打包並發佈新版到 GitHub Releases（自動更新會讀這個 Release）。

  用法：
    1. 把 src/App/RobloxAccountManager.App.csproj 的 <Version> 改成新版本（例如 3.1.0）
    2. 在專案根目錄執行：  pwsh tools/release.ps1            （只打包，不發佈）
                           pwsh tools/release.ps1 -Publish   （打包並建立 GitHub Release）
    3. 需要已登入 gh（gh auth status）

  Release 的 tag 一定是 v<Version>；zip 檔名 RobloxAccountManagerV3-v<Version>.zip（程式只認這個前綴）。
#>
param([switch]$Publish, [string]$Notes = "")
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Set-Location $root

[xml]$proj = Get-Content "src/App/RobloxAccountManager.App.csproj"
$version = ($proj.Project.PropertyGroup | Where-Object { $_.Version } | Select-Object -First 1).Version
if (-not $version) { throw "找不到 <Version>" }
$tag = "v$version"

dotnet test tests/Tests --nologo -v q
if ($LASTEXITCODE -ne 0) { throw "測試沒過，停止發行" }

$out = Join-Path $root "release"
if (Test-Path $out) { Remove-Item $out -Recurse -Force }
dotnet publish src/App -c Release -r win-x64 --self-contained false -o "$out/files" --nologo
if ($LASTEXITCODE -ne 0) { throw "publish 失敗" }

$zip = Join-Path $out "RobloxAccountManagerV3-$tag.zip"
Compress-Archive -Path "$out/files/*" -DestinationPath $zip
$hash = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLower()
"打包完成：$zip"
"SHA-256：$hash"

if (-not $Publish) { "（沒有加 -Publish，不會建立 GitHub Release）"; return }

$repo = "engnyg/roblox-account-manager"
if (-not $Notes) { $Notes = "Roblox Account Manager V3 $tag" }
gh release create $tag $zip --repo $repo --title $tag --notes $Notes
"已發佈：https://github.com/$repo/releases/tag/$tag"
