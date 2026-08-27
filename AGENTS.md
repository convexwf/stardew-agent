# Stardew Agent 仓库协作约定

## 文档引用

- 仓库内文档必须能够在 GitHub 上独立阅读。
- 外部参考资料使用公开网页链接或项目名称；优先链接到上游仓库、官方文档、论文或具体的 GitHub 文件/提交页面。
- 不要在已提交文档中引用本地工作副本、`temp/`、绝对路径、`file://`、`vscode://` 或其他仓库外路径。
- `temp/` 可以作为本地调研和实验工作区，但不是文档引用目标，也不是 Stardew Agent 的运行时依赖。
- 如果需要记录参考项目的代码位置，使用 GitHub 文件链接，并同时记录分支、标签或提交号，避免引用会漂移的本地路径。

## 调研文档

- 长文档使用中文元信息表；篇幅较大时添加目录。
- 文件名使用描述性名称，不使用数字前缀表达阅读顺序；阅读顺序统一维护在对应目录的 `README.md` 中。
- 调研文档只记录公开资料中的事实、实现差异、可观察限制、资料未覆盖的问题和待验证事实。
- 调研文档不得替 Stardew Agent 做项目决策，不得写入“当前建议”“推荐方案”“实施计划”“MVP 路线”“冻结设计”“验收标准”或其他要求项目采取某种做法的内容。
- 方案对比只描述候选方案的来源、特征、优缺点和适用前提；不追加 Stardew Agent 的选择、排序或最终结论。
- 组件关系、通信流程和时序优先使用 Mermaid；方案对比优先使用表格。
- 实现决策应放在独立的设计或实现文档中，并明确标注其决策依据；不要把调研文档当作设计文档。
- 调研资料必须记录参考资料的版本、分支或提交号；尚未通过编译、运行或实验验证的内容标记为“待验证”。

## 变更范围

- 参考项目只用于阅读、对比和实验，不要直接在 `temp/` 中积累 Stardew Agent 的正式实现。
- 正式代码、测试、配置和文档放在本仓库内，并保持路径可被 GitHub 访问。
- 修改已有文档时保持无关内容稳定；新增文档时先更新目录或索引，再补充正文。
- 默认完成修改后暂存、使用 `git-dated-commit` 创建提交并推送到 `origin`；只有用户明确要求不提交或不推送时例外。

## 构建与验证

### Rust CLI

- Rust CLI 的基础验证命令是 `cargo test --manifest-path cli/Cargo.toml` 和 `cargo build --manifest-path cli/Cargo.toml --release`。
- CLI 未指定 `--bridge-dir` 且未设置 `STARDEW_BRIDGE_DIR` 时，使用 CLI 可执行文件所在目录下的 `bridge/`，不依赖启动时的工作目录；合并发布包让 CLI 与 Mod 文件共用该目录。
- 提交前运行 `git diff --check`，检查空白字符和补丁格式问题。
- `cargo fmt --all -- --check` 仅在本机安装了 `rustfmt` 时运行；如果缺少该组件，应明确记录为环境限制，不要把它误报成代码编译失败。

### SMAPI Mod

- `smapi-mod/StardewAgentMod.csproj` 当前以 `net6.0` 为目标框架，对应 Stardew Valley 1.6/SMAPI 的运行时兼容目标；本机安装的 SDK 版本与项目目标框架是两个概念。
- 标准构建命令为：

  ```bash
  dotnet build smapi-mod/StardewAgentMod.csproj --configuration Release
  ```

- ModBuildConfig 需要能够定位游戏目录或等价的参考程序集目录。如果自动探测不到游戏，使用 `-p:GamePath="<game-or-reference-assemblies-directory>"`；占位路径只在本地命令中替换，不能把本机绝对路径写进仓库文档。
- 不需要启动游戏即可做编译验证，但必须先提供游戏目录或参考程序集。没有游戏安装时，在 `ugame` 工作区的 `temp/` 下克隆官方 [Stardew Valley/SMAPI 参考程序集](https://github.com/StardewModders/mod-reference-assemblies/blob/main/docs/README.md)：

  ```bash
  REF_DIR="../temp/mod-reference-assemblies"
  git clone --depth 1 https://github.com/StardewModders/mod-reference-assemblies.git "$REF_DIR"
  ```

  如果目录已经存在，先执行 `git -C "$REF_DIR" pull --ff-only` 更新它；不要把参考程序集复制到本仓库或提交到 Git。然后使用该目录执行构建：

  ```bash
  dotnet build smapi-mod/StardewAgentMod.csproj \
    --configuration Release \
    -p:GamePath="$REF_DIR"
  ```

- 只执行不带 `GamePath` 的命令不能算完成本地 C# 编译验证；如果输出 `The mod build package can't find your game folder`，说明构建尚未进入 C# 编译阶段。必须补齐 `GamePath` 后重新执行，并确认输出为 `Build succeeded` 且 `0 Error(s)`。

- 根目录的 `Directory.Build.props` 将发布压缩包输出到 `_releases/`，并关闭自动部署；构建产物不会自动安装到本机游戏目录。
- `bin/`、`obj/` 和 `_releases/` 是构建生成物，除非任务明确要求，否则不应提交。

### CI 与验证边界

- `.github/workflows/build-demo.yml` 中，Windows job 验证 CLI，SMAPI job 使用官方构建环境验证 Mod；`main` 分支 push 或手动运行会更新固定的 `latest` 开发版 GitHub Release，PR 只做构建检查。Actions artifact 是 job 间传递和失败排查用的中间产物，不等同于 Release 下载资产。
- Release 对外只提供一个合并的 Windows 压缩包，其中包含 CLI 可执行文件和 SMAPI Mod 文件；job 间的 Actions artifact 仍仅用于传递和排查。
- 参考程序集可以验证 C# 编译和 Mod 包结构，但不能证明 Mod 在真实游戏中的行为正确。涉及事件、地图、角色移动、存档或文件通信的改动，仍需在 Windows + SMAPI + Stardew Valley 中做运行验证。
- 本地构建出现警告时要单独分类。例如 .NET 6 SDK 可能报告分析器编译器版本不匹配的 `CS9057`；只要构建结果明确为成功且无错误，就不能把该警告描述成编译失败，但也不要声称已经完成运行时验证。
- 如果本地环境缺少游戏或参考程序集，先按上面的步骤下载参考程序集；只有本地环境确实无法配置时，才依靠 CI 做编译门禁。每次验证都应分别报告 Rust 编译、C# 编译、打包检查和真实游戏运行验证的结果，不能用其中一项代替其他项，也不能把环境准备失败描述为源码编译结果。
