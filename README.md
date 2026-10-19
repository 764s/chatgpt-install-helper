# ChatGPT 安装助手

面向 Windows 的非官方 ChatGPT 安装与更新工具。直接读取 OpenAI 官方生产渠道清单、下载官方签名的安装包，无需打开 Microsoft Store。

当前源码版本：**1.6.2**。这个版本号属于安装助手；待安装的 ChatGPT 版本在每次操作时实时查询。

## 功能

- **安装 / 重装、更新、仅检查更新**：显示本机版本、官方版本、进度及官方已公布的累计更新内容。
- **先准备，再安装**：下载和校验期间可以继续使用 ChatGPT / Codex；默认准备完成后等待手动应用。
- **复用已有下载**：优先使用助手的有效完整缓存，也可以复用官方应用已下载、且符合实时目标版本的完整 MSIX 包，避免重复下载。旧官方缓存只作灰色提示，不作为安装目标。
- **断点续传与代理**：支持系统代理、HTTP 代理和 TUN，显示下载容量、速度与进度。
- **Codex 侧栏页面**：直接检查、下载、应用更新并重启。可选“准备完成后自动安装并重启”，默认不勾选；底部提供小灰字“打开所在文件夹”。
- **双击即可使用**：自动保存独立副本并释放原 EXE，立即打开更新器；后台仅在检测到 Codex 且插件未安装时添加插件。安装开始和结束使用独立浮层提示，不阻塞原有界面。
- **覆盖重装**：不执行清空应用数据式卸载，不移除 ChatGPT Classic。

## 使用环境

- Windows 10 2004（19041）或更高版本、Windows 11；具体要求仍以最新 ChatGPT 安装包为准。
- x64 / ARM64 安装包自动选择；ARM64 尚未在独立设备上完成实际运行验证。
- 能访问 OpenAI 官方下载源的本地网络或代理。
- Windows 自带的 .NET Framework 4.x、Windows PowerShell 5.1 和应用部署组件。运行不需要 Python、Node、winget 或 .NET SDK。

实际部署需要 Windows 管理员授权，并可能关闭 ChatGPT / Codex。工具不检测任务是否结束；应用更新前请结束任务并保存工作。公司策略禁止应用部署时，仍需由 IT 授权。

## 从源码构建

克隆仓库后，在 Windows PowerShell 中进入仓库目录并运行：

```powershell
git clone https://github.com/764s/chatgpt-install-helper.git
cd chatgpt-install-helper
.\build.ps1 -OutputPath (Join-Path $PWD 'ChatGPTInstaller.exe')
```

构建脚本使用系统内置 `csc.exe` 与 PowerShell 组件，无 NuGet 依赖，不需要 Visual Studio 或额外安装 .NET SDK。脚本执行需符合本机 PowerShell 策略。

生成的 `ChatGPTInstaller.exe` 是单文件联网安装器，侧栏页面与图标已内嵌。双击后自动检查版本；点击“安装 / 重装”或“更新”先准备安装包，准备成功后再点击“立即安装 / 更新”。点击“仅检查更新”只查询版本、缓存状态和发布说明。

本仓库包含源码和文档，不包含 ChatGPT 安装包、离线许可证、个人配置、缓存或运行日志。自行构建的助手 EXE 没有商业代码签名；下载的 ChatGPT MSIX 保持官方签名。

## 添加 Codex 侧栏入口

1. 双击 EXE 即可打开更新器。本机已有 Codex 且插件尚未安装时，在后台自动添加；已安装的插件（包括旧版或停用状态）不会自动重装或启用。
2. 等当前任务结束后，完全退出并重新打开 ChatGPT。
3. 在侧栏“更多”中找到“ChatGPT 更新器”，按需固定。

真正开始安装时显示“正在安装 Codex 插件”的浮层；成功后立即显示“已作为 Codex 插件安装”，说明以后可从 Codex 插件操作，也可保留原 EXE 继续使用。失败会显示原因。提示不占用常规布局、不要求确认，结果 15 秒后收起；关闭进行中的提示不会取消安装，结果仍会再次显示。

原 EXE 将程序保存到 `%LOCALAPPDATA%\ChatGPTInstallHelper\App\{助手版本}`，启动固定副本后退出，释放原文件占用。原下载文件可删除，也可保留继续双击；请勿删除固定副本目录。未检测到 Codex 时可直接使用更新器，不把缺少 Codex 当作更新失败。

Codex 发现和安装状态查询均在后台，已安装时只执行一次只读插件查询；插件内操作完全跳过此流程。ChatGPT 的正常更新和助手内覆盖重装不会删除独立副本或插件来源，也不会隐式重新安装插件。移除插件后，再次直接双击 EXE 会按首次安装条件重新添加；仅在插件内更新 ChatGPT 不会触发添加。

入口位置由客户端管理，不替换官方更新蓝点。插件功能依赖当前 ChatGPT 随附的 Codex CLI 支持插件管理；不支持时仍可使用独立 EXE。需要升级或修复已安装插件时，在“Codex 侧栏入口”页点击“添加 / 修复入口”，再由客户端重新加载插件。

## 版本与缓存规则

每次操作读取官方生产渠道当前公布的版本，按版本专属地址获取完整安装包。清单、包内身份、架构、版本和 Windows 签名都要符合要求；准备后和实际部署前再次核对官方版本，发现目标变化时停止旧包部署。

缓存选择与准备流程由侧栏和完整窗口共用：

| 缓存情况 | 界面与行为 |
| --- | --- |
| 助手或官方已有符合最新版目标的完整包 | 显示“已下载，待校验”，点击“校验并准备”跳过安装包下载 |
| 仅有旧官方完整包 | 正常显示未下载，灰字提示旧缓存；按实时目标下载 |
| 助手有有效部分下载 | 可继续下载，重新核对续传身份 |
| 无可用缓存 | 正常下载 |

只复用当前用户官方应用的 `prepared.msix` 完整缓存。同卷优先建立硬链接，不支持时进行本机复制；不修改官方文件或更新状态，也不合并两端正在进行的下载，不保证官方反向复用助手缓存。缓存位置变化或校验失败时，按正常下载流程处理。

“仅检查更新”不复制、引用或下载完整安装包和许可证，也不执行安装。累计说明仅覆盖官方明确公布、能归属到版本批次的条目；缺失条目或获取失败不代表没有变更。

## 源码与说明

- [源码结构与行为边界](README.txt)
- [完整使用说明与历史验证范围](docs/使用说明.txt)
- `InstallerWorkflow.cs`：准备、安装前复核、部署主流程。
- `ReleaseResolver.cs` / `ReleaseManifest.cs`：实时官方版本发现。
- `PackageCache.cs` / `CacheFiles.cs`：缓存选择、校验和引用。
- `MainForm.cs`：独立窗口与共享操作状态。
- `InstallerStartup.cs` / `StartupNotice.cs`：原 EXE 退出交接、后台检查与插件安装浮层。
- `SidebarMcpServer.cs` / `Sidebar/launcher.html`：侧栏 MCP App 与页面。
- `LocalUpdateServer.cs` / `LocalUpdateClient.cs`：同用户命名管道，连接独立助手进程。

## 官方来源

- [Windows 部署文档](https://learn.chatgpt.com/docs/enterprise/windows-deployment)
- [桌面版本更新说明](https://learn.chatgpt.com/docs/changelog)
- [生产渠道更新清单](https://persistent.oaistatic.com/codex-app-prod/windows-store-update.json)

本项目与 OpenAI 无隶属关系。ChatGPT 安装包和许可证由官方提供；若官方改变产品身份、下载机制或插件接口，需要相应更新本项目。
