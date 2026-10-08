# AGENTS.md — 文件批量盖章与水印工具（File Stamp Watermark）

本文件是仓库级行为规范，供在此仓库工作的 AI 智能体与协作者执行。

## 一、线上仓库白名单（铁律，2026-09-27 事故后确立）

本仓库同时推送 GitHub（gggg312/FileStampWatermark）与 Gitee（gaopeng262/file-stamp-watermark）。**线上只允许出现主程序源码与开源合规文件**，任何测试、过程、交付、备份、印章资产一律禁止入库。

- **允许入库**：`WebShell/`、`WebUI/prototype/`、`PDFQFZ/`、`README.md`、`LICENSE`、`LICENSES/`、`LICENSING.md`、`THIRD-PARTY-NOTICES.md`、`.gitignore`、`.gitattributes`、`封面图.png`。（V1.0.0.37：弃用的 `PDFQFZ.WPF/` 项目已移入本地 `备份文件夹\PDFQFZ.WPF_弃用_1.0.0.37\`，不入库；其被 WebShell 编译的 `Services/StampEngine.cs`、`StampBatchWorker.cs`、`StampBatchRequest.cs` 已并入 `WebShell/Services/`）
- **禁止入库**（`.gitignore` 已排除，文件保留在本地工作区，不入 git）：`交付版本/`、`备份文件夹/`、`备份/`、`E2E/`、`PDFQFZ.Tests/`、`工具脚本/`、`输出记录*.md`、`Web交付版本/`、一切 `*.exe`、一切印章/公章图片（如 `WebShell/Assets/公章.png`）、`*debug_page.pdf`、`app_icon_raw.png`、`运行组件/`。
- **公章铁律**：软件正式版默认不携带任何印章；EXE 不内嵌公章（已移除 `EmbeddedResource` 与释放逻辑）。任何人不得重新把公章图片加入源码、资源或提交。

## 二、每次 push 前的强制检查

1. 先运行本地守卫脚本（不入库，位于 `工具脚本\pre-push_guard.ps1`），暂存区/已跟踪文件命中黑名单即报错阻断；
2. `git ls-files` 人工复核：不得出现 公章/印章/交付版本/备份/E2E/输出记录/.exe；
3. 推送前确认线上仓库历史对象库干净——git 历史对象同样会泄露，一旦推过敏感对象，唯一彻底解法是删除线上仓库重建，因此**发现已推送敏感内容必须先重建线上仓库再推**；
4. 新仓库从 1.0.0.34 起，历史已清空重写，禁止用 `git fetch/pull` 把旧远端历史拉回本地。

## 三、版本与交付约定

- 每次代码改动后：单测回归（`dotnet test PDFQFZ.Tests\PDFQFZ.Tests.csproj`，基线 242）→ 编译双档（免费 TRACE / 赞助 TRACE;VIP_PAID）→ 版本号 +1 → 交付 EXE 放 `交付版本\<版本>_免费版与赞助版\`（该目录不入库）。
- EXE 文件名不带"免费版"字样（发布用名）；赞助版加 `_赞助版` 后缀。
- 用户手动修改过的文件作为后续修订母版，不得回退。
