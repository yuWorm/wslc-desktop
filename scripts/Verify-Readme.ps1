$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$englishPath = Join-Path $root "README.md"
$chinesePath = Join-Path $root "README.zh-CN.md"

if (-not (Test-Path $englishPath)) {
    throw "README.md must exist."
}

if (-not (Test-Path $chinesePath)) {
    throw "README.zh-CN.md must exist."
}

$english = Get-Content -Raw $englishPath
$chinese = Get-Content -Raw $chinesePath

function Require-Contains {
    param(
        [string]$Text,
        [string]$Pattern,
        [string]$Message
    )

    if ($Text -notmatch $Pattern) {
        throw $Message
    }
}

function Require-NotContains {
    param(
        [string]$Text,
        [string]$Pattern,
        [string]$Message
    )

    if ($Text -match $Pattern) {
        throw $Message
    }
}

foreach ($needle in @(
    "# WSLC Desktop",
    "[Chinese documentation](README.zh-CN.md)",
    "## What It Is",
    "## Requirements",
    "## First Run",
    "## Using the GUI",
    "## Using Docker CLI",
    "## Backend Modes",
    "## Docker Compose with the WSLC Backend",
    "## Troubleshooting"
)) {
    Require-Contains $english ([regex]::Escape($needle)) "English README must contain section '$needle'."
}

foreach ($needle in @(
    "# WSLC Desktop",
    "[English documentation](README.md)",
    "## 软件定位",
    "## 环境要求",
    "## 首次启动",
    "## 使用 GUI",
    "## 使用 Docker CLI",
    "## 后端模式",
    "## 使用 WSLC backend 运行 Docker Compose",
    "## 故障排查"
)) {
    Require-Contains $chinese ([regex]::Escape($needle)) "Chinese README must contain section '$needle'."
}

foreach ($needle in @(
    "wsl --install",
    "wsl --update",
    "wslc.exe",
    "wslcd-desktop",
    "npipe:////./pipe/wslc-desktop-docker",
    "docker context create wslc-desktop",
    "docker --context wslc-desktop ps -a",
    "docker --context wslc-desktop compose up -d",
    "network_mode: default",
    "Docker API backend",
    "WSLC backend",
    "Settings -> CLI tools",
    "docker-compose.exe",
    "cli-plugins"
)) {
    Require-Contains $english ([regex]::Escape($needle)) "English README must document '$needle'."
    Require-Contains $chinese ([regex]::Escape($needle)) "Chinese README must document '$needle'."
}

foreach ($pattern in @(
    "WSLC backend[\s\S]*wslc\.exe",
    "Docker API backend[\s\S]*Docker-compatible",
    "user-defined network",
    "restart policy",
    "stats streaming",
    "interactive TTY",
    "build",
    "secrets",
    "configs",
    "deploy"
)) {
    Require-Contains $english $pattern "English README must document compatibility detail matching '$pattern'."
}

foreach ($pattern in @(
    "WSLC backend[\s\S]*wslc\.exe",
    "Docker API backend[\s\S]*Docker 兼容",
    "user-defined network",
    "restart policy",
    "stats streaming",
    "interactive TTY",
    "build",
    "secrets",
    "configs",
    "deploy"
)) {
    Require-Contains $chinese $pattern "Chinese README must document compatibility detail matching '$pattern'."
}

Require-NotContains $english "### 中文说明" "English README must not contain embedded Chinese explanation sections."
Require-NotContains $chinese "### 中文说明" "Chinese README must be a standalone document, not embedded subsection copy."

"README_VERIFY_OK"
