# maimai NEEDY GIRL OVERDOSE（私有协作仓库）

> **这是私有仓库，只给受邀的组员用，请不要公开、不要转发。**
> 仓库里有从 SEGA 游戏贴图改出来的素材（改色、像素化、拼合）和改动数据，
> 也有 NEEDY GIRL OVERDOSE 的同人美术。见 [NOTICE.md](NOTICE.md)。
> **游戏本体一个文件都没有**：安装器读你自己机器上的游戏文件，把我们的改动写进去。

把 maimai DX 1.70（SDEZ / MAGiCAL）本机版整套换成「maimai NEEDY GIRL OVERDOSE」：
登录到选歌、结算的界面贴图和配色、Windose 风格的选歌底板和标签、背景动效、转场里的糖糖和超天酱、
像素字体、开机画面。模组还支持自定义搭档（Live2D 模型或逐帧精灵对），但本仓库**不附带任何搭档角色素材**：
自己做的搭档放进 `LocalAssets\PartnerLive2D\<名字>\partner.ini`，做法见 skill 里的 architecture / tuning-layouts。

## 需要什么

- 你自己的 **maimai DX SDEZ 1.70** 本机版（`Package\Sinmai.exe`、`Sinmai_Data\`），能正常进游戏。
  其他版本不行：安装器会逐个对象核对原值，对不上就什么都不写。
- **MelonLoader**（加载 `Mods\` 里的模组）和 **AquaMai**（按你现在的方式装，MuMod 或 Mods 里的 AquaMai.dll 都行）。
- **Live2DCubismCore.dll（可选）**：只有 Live2D 模型做的搭档需要它；逐帧精灵搭档和换皮本身都不需要。
  Live2D 的许可不允许我们分发；要用 Live2D 模型搭档的话，去 Live2D 官网下载 *Cubism SDK for Native*，
  把 `Core\dll\windows\x86_64\Live2DCubismCore.dll` 放到 `Sinmai.exe` 旁边。
  没有它时模组照常运行（选歌布局、精灵搭档、开机画面、转场、像素字体都在），只跳过 Live2D 模型搭档，日志里会有一行警告。
- 游戏路径要全英文，不能在 E 盘或 Y 盘（segatools 会重定向这两个盘）。
- 建议在 AquaMai.toml 里打开 `[Tweaks.TimeSaving.CheckSkip]`（开机画面按自检进度显示）。

## 安装（玩家）

1. 在 GitHub Releases 下载 `maimai-ngo-skin-<日期>.zip`，解压到任意位置（比如 `<GAME_ROOT>\` 下面）。
2. 关掉游戏，双击 `install.bat`（或者把你的 `Package` 文件夹拖到 `install.bat` 上）。
3. 安装器做的事：
   - 先检查这个文件夹的 `Sinmai.exe` 没在运行，在运行就拒绝；
   - 把所有会被改的原文件备份到 `Package\NGO_skin_backup\`（带 SHA256）；
   - 往你自己的 `resources.assets` 和几个 StreamingAssets 包（角色卡、导航角色）里写入贴图、精灵网格、颜色字段、
     材质和场景改动，改曲目分类配色 XML，再放入模组、它的素材和 A500 搭档数据（数量见 `patch\manifest.json`）；
   - 再运行一次（比如更新到新版本）会先从备份还原再重新写，可以放心重复运行。
4. 卸载：双击 `uninstall.bat`，从备份放回原文件、删掉新增的文件。`status.bat` 看当前状态。

不会碰：`segatools.ini`（keychip）、`DEVICE\aime.txt`（卡号）、`AquaMai.toml`、存档、日志。
`LocalAssets\PartnerLive2D\settings.ini` 里会改账号的选项（`charaLevel` 等）在发布包里默认关掉。

## 文档里的占位符

文档和 skill 里不写任何人的本机路径，统一用占位符：

| 占位符 | 指的是 |
|---|---|
| `<PKG>` | 你的游戏包文件夹（有 `Sinmai.exe`、`Mods\`、`Sinmai_Data\` 的那个） |
| `<GAME_ROOT>` | `<PKG>` 的上一级（启动器、备份、playlogs 所在） |
| `<PROJECT>` / `<REPO>` | 本仓库 |
| `<WORK>` / `<WORK_ROOT>` | 开发时放任务说明、中间产物的工作文件夹（不在仓库里） |
| `<ART_REPO>` | 美术交接用的另一个仓库（不公开） |
| `<SCRATCH>` / `<RELEASE_DIR>` | 导出脚本用的临时目录 / 发布包输出目录 |
| `<DECOMPILED>` | 自己反编译出来的游戏代码（不随仓库提供） |
| `<TOOLS>` | 本机工具目录（.NET SDK、Cubism SDK 等） |

代码里的默认路径保留原样（脚本靠它们运行），用环境变量或脚本顶部的常量改成你自己的。

## 从源码构建

**模组（PartnerLive2D.dll）**：需要 .NET 8 SDK，以及你自己游戏里的 `Sinmai_Data\Managed` 和 `MelonLoader\net35`（编译引用，不进仓库）。

```powershell
dotnet build PartnerLive2D.csproj -c Release -p:GamePackage="<PKG>"
# 产物：bin\Release\net472\PartnerLive2D.dll，复制到 Package\Mods\
```

`build.ps1` 是原机器上的脚本（固定了 `<TOOLS>\dotnet` 等路径），可以照着改。无头测试在 `tests\Headless`。

**安装器（ngo_patcher.exe）**：Python 3.12 + `UnityPy TypeTreeGeneratorAPI Pillow texture2ddecoder etcpak astc-encoder-py pyinstaller`。

```powershell
powershell -ExecutionPolicy Bypass -File patcher\build_exe.ps1 -Python C:\path\to\python.exe -Out dist
# 不打包也能直接跑：python patcher\ngo_patcher.py install "<PKG>" --patch patch
```

从 git 检出直接安装时，缺一样只在 Release 里的东西：`exact\`（参考机的精确压缩数据；没有它安装器会自己把 PNG
重新压成 BC7，肉眼无差，字节不同）。

**重新导出**（只在原机器上，有工作中的游戏和原版安装包时）：`python tools\ngo_export_public.py`。
它从当前已安装的游戏里取出我们的改动，对比原版逐个对象导出到 `patch\`，再打 Release 包、
在原版文件的副本上装一遍并逐对象核对，最后做隐私扫描并本地提交（不会 push）。

## 目录

| 路径 | 内容 |
|---|---|
| `src\` | PartnerLive2D 模组（C#，MelonLoader + Harmony）。`Ngo*.cs` 是换皮专用的钩子 |
| `src\Framework\` | Live2D Cubism Native Framework 的 C# 移植（Live2D Open Software License） |
| `mcp\` | partnerlive2d MCP 服务（让 AI 代理调整、预览、截图、构建安装、驱动测试），见 `mcp\README.md` |
| `.claude\skills\maimai-ngo\` | 给 Claude Code 的项目 skill：钩子、配置、测试方法、贴图修补、踩过的坑 |
| `tools\` | 换皮工具（UnityPy 改 resources.assets、取色、拼图、测试驱动等）。很多脚本写死了 `<PKG>` |
| `tests\` | 无头测试（`tests\Headless`） |
| `docs\` | 选歌视图、rig3 格式说明；`docs\tasks\` 是各轮任务说明和工作记录 |
| `patch\` | **安装器写入的数据**：`textures\` 改过的贴图 PNG，`targets\*.json` 每个游戏文件要改的对象和字段（旧值/新值），`xml_patch.json`，`files\` 新增文件（模组素材、A500 搭档数据），`blobs\` 大块数据（像素字体） |
| `patcher\` | 安装器源码 `ngo_patcher.py`、打包脚本、发布包里的 bat |
| `launcher\` | 一键启动（`Play-maimai.bat`、`launcher.ps1`：独显、性能模式、P 核、GPU 锁频、监控）和本机版使用说明 |
| `art\dot_redrawn\` | dot 重画的贴图（和原版逐像素相同的已剔除）；`art\ngo_recolor\` 早期改色图 |
| `deploy\` | `build.ps1 -Install` 会一起复制的配置 |

Release 里另有 `maimai-ngo-art-source-<日期>.zip`：Codex / 生图的全部工作稿（原图、反编译、截图类文件已剔除）。

## 参与改动

- **改一张贴图**：直接改 `patch\textures\<文件>\<贴图名>__<PathID>.png`（保持尺寸；要改尺寸看对应 `targets\*.json` 里的精灵数据）。
  安装器发现像素和 `exact` 不一致时会自动重新压缩。
- **改颜色 / 位置 / 网格**：改 `patch\targets\*.json` 里那条的 `new`（`old` 是原版值，安装器拿它核对）。
- **改模组**：改 `src\`，构建后测一遍；模组素材和配置在 `patch\files\LocalAssets\PartnerLive2D\`。
- 提交前：不要放游戏文件、dll、日志、截图、自己的 `segatools.ini` / `aime.txt` / `AquaMai.toml`（`.gitignore` 已拦）；
  单个文件不超过 50 MB。走 fork + PR，PR 里写清楚改了哪个画面、附游戏内截图（注意别截到卡号和玩家名）。
- **给 AI 代理用**：Claude Code 打开本仓库会自动加载 `.claude\skills\maimai-ngo`；MCP 服务按 `mcp\README.md` 注册
  （环境变量 `PARTNERLIVE2D_PACKAGE` 指向你的 Package，`PARTNERLIVE2D_PROJECT` 指向本仓库）。

---

## English (short)

Private collaboration repo for a maimai DX 1.70 (SDEZ) "NEEDY GIRL OVERDOSE" re-skin and its PartnerLive2D MelonLoader mod.
It ships no game files: `patch\` holds our edits as data (changed textures as PNG, every other changed field as JSON
old/new values, XML line edits, added files), and `ngo_patcher` writes them into **your own** SDEZ 1.70 install,
backup-first (`Package\NGO_skin_backup`), re-runnable, with uninstall.

- Requirements: your SDEZ 1.70 + MelonLoader + AquaMai. `Live2DCubismCore.dll` (Cubism SDK for Native, next to `Sinmai.exe`) is optional: only Live2D-model partners need it. No partner character art is included; bring your own partner (Live2D model or sprite pair) via `LocalAssets\PartnerLive2D\<name>\partner.ini`.
- Install: download the release zip, close the game, run `install.bat`. Remove: `uninstall.bat`.
- Build the mod: `dotnet build PartnerLive2D.csproj -c Release -p:GamePackage=<your Package>`.
- Build the patcher: `patcher\build_exe.ps1 -Python <python with UnityPy...>`.
- Agents: project skill in `.claude\skills\maimai-ngo`, MCP server in `mcp\` (see `mcp\README.md`).
- Contribute through forks and PRs; never commit game files, DLLs, logs, screenshots or private configs. See NOTICE.md.
