# WSLC Desktop

[English documentation](README.md)

WSLC Desktop 是一个基于 WinUI 3 的 WSLC 图形化管理工具，用于管理 Windows 系统级容器。它提供容器、镜像、数据卷、端口、Compose 项目、日志、终端、daemon 状态、运行时设置和 Docker 兼容命令行入口。

应用核心是当前用户下运行的 `wslcd-desktop` 后台 daemon。图形界面通过原生 WSLC Desktop API 连接 daemon；Docker CLI、Docker Compose 和兼容工具可以通过 Docker Engine API 兼容的命名管道连接 daemon。

## 软件定位

WSLC Desktop 不是 Docker Desktop。它首先是 WSLC 管理工具，同时提供 Docker API 兼容层。

- 默认后端是 WSLC backend。它使用 `wslc.exe`，因此看到的是终端里 `wslc` 命令同一套容器、镜像和数据卷。
- Docker 兼容入口暴露在 `npipe:////./pipe/wslc-desktop-docker`。
- 应用也支持显式切换到 Docker API backend，用于连接 Docker Desktop 或其他 Docker API 兼容引擎。
- GUI 不会自动切换你的全局 Docker context。是否创建或使用 `wslc-desktop` context 由用户决定。

## 环境要求

必须满足：

- Windows 支持 WSL。
- WSL 版本足够新，并包含 `wslc.exe`。
- WSLC Desktop 和 `wslcd-desktop` 在同一个 Windows 用户下运行。

如果没有安装 WSL，运行：

```powershell
wsl --install
```

如果已安装 WSL 但没有 `wslc.exe`，更新 WSL：

```powershell
wsl --update
```

推荐安装：

- Docker CLI，也就是 `docker.exe`。
- Docker Compose，也就是 `docker-compose.exe`，或 Docker CLI 插件目录里的 `cli-plugins\docker-compose.exe`。

Docker CLI 和 Compose 对 GUI 不是必须项，但如果要使用 `docker` 或 `docker compose` 连接 WSLC Desktop，就需要安装。

## 首次启动

应用启动时会先检查宿主环境，再启动 `wslcd-desktop`。

1. 如果 `wslc.exe` 可用，应用会启动 daemon 并进入 GUI。
2. 如果没有 WSL，应用会弹出必须处理的对话框，并给出 `wsl --install`。这一步会阻塞正常使用。
3. 如果有 WSL 但没有 `wslc.exe`，应用会认为 WSL 版本过旧，并给出 `wsl --update`。这一步也会阻塞正常使用。
4. 如果缺少 Docker CLI，应用只会弹出可关闭提示。你可以跳过并继续使用 GUI。

后续可以在 `Settings -> CLI tools` 安装 Docker CLI 辅助工具。

安装器支持：

- 从 `https://download.docker.com/win/static/stable/x86_64/` 下载最新 Docker CLI zip，并且只安装 `docker.exe`。
- 网络不稳定时，手动选择 Docker CLI zip 安装。
- 从 GitHub 下载最新 Docker Compose。
- 手动选择本地 Compose exe 安装。
- 同时安装 Compose 到 `bin\docker-compose.exe` 和 `bin\cli-plugins\docker-compose.exe`。
- 将工具 `bin` 目录添加到用户 PATH 或系统 PATH。

## 使用 GUI

主要页面包括：

- 容器：创建、启动、停止、重启、删除、检查、查看日志和打开终端。
- 镜像：查看、拉取、删除，并跟踪拉取任务。
- 数据卷：查看、创建和删除命名数据卷。
- 网络：查看容器发布端口并打开端点 URL。
- Compose：打开 Compose 文件，预览服务计划，创建并启动服务。
- 设置：配置运行时 Provider、WSLC 代理和镜像设置、Docker API backend、daemon 控制、CLI 工具安装、开机启动、语言和诊断。

daemon 控制可以在设置页和标题栏运行状态浮层中使用。系统托盘也提供快捷操作。点击窗口关闭按钮会隐藏到托盘；从托盘选择退出会同时退出 UI 并关闭 `wslcd-desktop`。

## 使用 Docker CLI

WSLC Desktop 在下面的地址暴露 Docker Engine API 兼容入口：

```text
npipe:////./pipe/wslc-desktop-docker
```

创建 Docker context：

```powershell
docker context create wslc-desktop --docker "host=npipe:////./pipe/wslc-desktop-docker"
```

按命令使用 context：

```powershell
docker --context wslc-desktop version
docker --context wslc-desktop ps -a
docker --context wslc-desktop images
docker --context wslc-desktop pull nginx:alpine
docker --context wslc-desktop run --name web -p 18080:80 nginx:alpine
docker --context wslc-desktop logs web
docker --context wslc-desktop exec web sh -lc "uname -a"
docker --context wslc-desktop rm -f web
```

如果希望把它设为默认 Docker CLI 目标：

```powershell
docker context use wslc-desktop
```

如果不想改变全局 Docker CLI 状态，推荐始终使用显式 `--context` 写法。

## 后端模式

WSLC Desktop 有两种运行时 Provider 模式。

| 范围 | WSLC backend | Docker API backend |
| --- | --- | --- |
| 默认 | 是 | 否，需要显式启用 |
| 运行时来源 | `wslc.exe` | Docker 兼容 API host |
| 常见 host | 本机 WSLC 控制面 | Docker Desktop、其他命名管道引擎，或显式允许的 TCP host |
| 资源范围 | 与 `wslc` CLI 看到的资源一致 | 来自配置的 Docker 兼容引擎 |
| 容器生命周期 | 支持 | 取决于外部引擎能力 |
| 镜像 | 查看、拉取、删除 | 查看、拉取、删除 |
| 数据卷 | 命名卷和 bind mount | 取决于 Docker API provider |
| 网络 | 发布端口视图；不支持 Docker-style user-defined network CRUD | 取决于外部引擎，可能支持更多 |
| 日志 | buffered logs；follow 取决于 provider 能力 | 取决于外部引擎 |
| 统计 | 支持 one-shot stats；stats streaming 受 provider 能力限制 | 取决于外部引擎 |
| Exec | 支持非交互 exec；interactive TTY 受 provider 能力限制 | 取决于外部引擎 |
| build/push/search | 当前产品路径不支持 build 和 push；registry search 明确不支持 | 取决于 provider，但 GUI 当前聚焦运行时管理 |

如果要管理 Windows WSLC 资源，使用 WSLC backend。如果希望 GUI 指向其他 Docker 兼容运行时，才使用 Docker API backend。

## 使用 WSLC backend 运行 Docker Compose

Compose 通过 Docker API 兼容层工作。推荐流程：

```powershell
docker --context wslc-desktop compose config
docker --context wslc-desktop compose up -d
docker --context wslc-desktop compose ps
docker --context wslc-desktop compose down
```

推荐的服务写法：

```yaml
services:
  web:
    image: nginx:alpine
    network_mode: default
    ports:
      - "18080:80"
    environment:
      NGINX_ENTRYPOINT_QUIET_LOGS: "1"
    volumes:
      - web-data:/usr/share/nginx/html

volumes:
  web-data:
```

常见支持字段包括 `image`、`command`、`environment`、`ports`、`volumes`、`labels`、`depends_on` 和 `network_mode: default`。

WSLC backend 下需要注意：

- 尽量给服务添加 `network_mode: default`。WSLC backend 不支持 Docker-style user-defined network creation，因此依赖 Compose 隐式项目网络的文件可能失败。
- `restart` / restart policy 受限，因为 WSLC provider 当前没有完整 Docker restart-policy 能力。
- WSLC provider 支持 buffered logs；实时 follow 取决于 provider 能力。
- 统计信息推荐使用 `docker stats --no-stream`。默认 stats streaming 在 provider 不支持时会返回明确的 unsupported 响应。
- 支持非交互 `docker exec`。interactive TTY 受 provider 能力限制。
- `build`、用户自定义 `networks`、`secrets`、`configs`、`deploy`、image push 和 registry search 不属于当前 WSLC backend 支持路径。

## 故障排查

### 缺少 `wslc.exe`

运行：

```powershell
wsl --update
```

如果 WSL 本身不存在，运行：

```powershell
wsl --install
```

命令完成后重启 WSLC Desktop。

### 找不到 Docker CLI

打开 `Settings -> CLI tools`。

可以选择：

- 在线安装 Docker CLI。
- 从本地 zip 安装 Docker CLI。
- 在线安装 Compose。
- 选择本地 Compose exe。
- 将工具 `bin` 目录添加到用户 PATH 或系统 PATH。

更新 PATH 后请打开新的终端。

### Docker context 无法连接

先在 GUI 标题栏状态浮层或设置页确认 `wslcd-desktop` 正在运行。必要时重新创建 context：

```powershell
docker context rm wslc-desktop
docker context create wslc-desktop --docker "host=npipe:////./pipe/wslc-desktop-docker"
docker --context wslc-desktop ps -a
```

### Compose 创建网络时报错

给每个 service 使用 `network_mode: default`，或移除 Compose 文件里的用户自定义 `networks`。WSLC backend 会对 Docker network create/delete 操作返回明确的 unsupported 错误。

### 修改运行时设置后没有生效

从设置页或标题栏运行状态浮层重启 `wslcd-desktop`。代理、镜像代理、runtime provider 和 Docker API backend 设置会在 daemon 启动时生效。

### 需要日志或诊断

在设置页导出诊断。反馈问题时请附带 daemon 健康状态、provider 状态、最近操作以及相关 Docker/Compose 命令输出。

## 相关文档

- WSLC 文档：https://github.com/MicrosoftDocs/wsl/blob/main/WSL/wsl-container.md
- Docker Windows 静态二进制：https://download.docker.com/win/static/stable/x86_64/
- Docker Compose releases：https://github.com/docker/compose/releases
- 本地 Compose 兼容说明：`docs/COMPOSE_COMPATIBILITY.md`
- 本地 Docker API 矩阵：`docs/DOCKER_API_COMPATIBILITY_MATRIX.md`
- 本地 runtime provider 矩阵：`docs/RUNTIME_PROVIDER_MATRIX.md`
