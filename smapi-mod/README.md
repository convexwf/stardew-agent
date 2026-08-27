# Stardew Agent Bridge Mod

这是游戏内的 SMAPI 适配层。它在进入存档后创建一个 `companion-1` AI Companion，读取 CLI 写入的 action request，访问游戏运行时状态，并把 action result 和 snapshot 写回 Bridge 目录。

Companion 由可见 NPC 和隐藏 shadow farmer 组成：NPC 负责画面中的位置和寻路，shadow farmer 持有物品并调用 Farmer、Tool、Item 等游戏机制。它不是原版联机 Farmhand，不需要启动第二个游戏客户端。

当前 Demo 保留逻辑名称 `companion-1`，并通过 SMAPI `AssetRequested` 注入最小角色数据，同时将 `Portraits/companion-1` 临时映射到游戏已有的 Abigail 头像。该头像只是通信和生成验证阶段的占位资源，后续可以替换为 Mod 自带的自定义 portrait，不改变 CLI、状态扫描或动作路由。

## 支持的动作

Mod 当前处理以下 `payload.action`：

```text
ping
move_relative       move_to             cancel
face_direction      use_tool            interact
warp_to             observe              get_inventory
attack              cast_fishing_rod    set_auto_combat
eat_item             say                 bubble
start_mode           follow
```

`start_mode` 当前支持 `chop_trees`、`water_crops`、`harvest_crops`、`plant_crops`、`mine` 和 `fish`。这些模式由 Mod 在游戏 Tick 中持续执行，快照中的 `companion.mode_info` 暴露当前阶段；CLI 只需提交一次模式请求，结束模式使用对应 request ID 调用 `cancel`。

CLI 与协议字段、结果示例和完整链路见 [`doc/demo/cli-file-bridge.md`](../doc/demo/cli-file-bridge.md)。Mod 只接受 `actor_id=companion-1`；主农场主只进入状态快照，不是这些动作的目标。

`warp_to` 是显式传送，可以把 Companion 放到另一个已加载地点；Follow 会在 Mod 内持续跟随主农场主。农场持续模式只处理当前地点可扫描的树木和耕地，`mine` 只处理当前矿井楼层的可破坏石块，`fish` 在当前地点附近水域推进鱼竿状态机。`use_tool`、`attack` 和 `cast_fishing_rod` 是否成功，取决于 shadow farmer 背包和游戏中的目标、地点及其他前置条件。

## 配置

第一次加载 Mod 时会自动生成 `config.json`。默认 Bridge 目录是当前 Mod 文件夹下的 `bridge/`：

```json
{
  "BridgeDirectory": "",
  "LatestWriteIntervalSeconds": 5,
  "SnapshotHistoryIntervalSeconds": 60,
  "SnapshotHistoryLimit": 10,
  "BubbleTemplates": {
    "MissingTool": "我没有{tool}，无法继续{mode}。",
    "MissingSeed": "我没有可用的种子，无法继续播种。",
    "NoTilledSoil": "没有找到可以播种的已开垦土地。",
    "InventoryFull": "我的背包已满，无法继续工作。",
    "PathBlocked": "我在{location}遇到了障碍，正在重新寻找路径。",
    "LowStamina": "我太累了，需要休息。",
    "NoWater": "我的浇水壶没水了，无法继续浇水。",
    "NoFishingWater": "这里没有找到可以钓鱼的水域。",
    "ModeActionFailed": "我无法完成{mode}的当前动作。"
  }
}
```

配置项含义：

- `BridgeDirectory`：CLI 和 Mod 共同访问的 Bridge 目录；为空时使用 Mod 目录下的 `bridge/`；
- `LatestWriteIntervalSeconds`：完整 `snapshots/snapshot-latest.json` 的写出间隔；
- `SnapshotHistoryIntervalSeconds`：历史快照槽位的写出间隔；
- `SnapshotHistoryLimit`：历史槽位数量；
- `BubbleTemplates`：持续模式遇到缺少工具、资源、路径或背包问题时使用的官方 NPC 气泡文本模板；支持 `{tool}`、`{mode}`、`{location}` 和 `{target}` 占位符。

历史文件使用固定槽位轮转。例如限制为 3 时，文件只会是 `snapshot-0.json`、`snapshot-1.json` 和 `snapshot-2.json`，写到末尾后重新覆盖 `snapshot-0.json`。`snapshot-latest.json` 始终保存完整最新状态，并记录最近历史快照的 `snapshot_index`，不是只保存一个指针。

也可以把 `BridgeDirectory` 改为绝对路径。Windows 上，合并发布包中的 CLI 会默认使用自身所在目录下的 `bridge/`，无需额外参数：

```text
path\to\stardew-cli.exe status
```

如果 CLI 与 Mod 分开放置，则使用同一个绝对路径：

```text
stardew-cli.exe --bridge-dir "D:\stardew-agent-bridge" status
```

## 构建

```text
dotnet build StardewAgentMod.csproj --configuration Release
```

项目引用 `Pathoschild.Stardew.ModBuildConfig`。本地有游戏目录时，它可以部署 Mod；GitHub Actions 中通过根目录的 `Directory.Build.props` 禁用自动部署，并只上传 `_releases/` 下的 zip。CI 负责生成构建产物，不替代 Windows + SMAPI 的真实游戏验证。
