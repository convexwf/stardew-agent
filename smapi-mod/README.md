# Stardew Agent Bridge Mod

这是游戏内的 SMAPI 适配层。它在进入存档后创建一个 `companion-1` AI Companion，读取 CLI 写入的 action request，访问游戏运行时状态，并把 action result 和 snapshot 写回 Bridge 目录。

Companion 由可见 NPC 和隐藏 shadow farmer 组成：NPC 负责画面中的位置和寻路，shadow farmer 持有物品并调用 Farmer、Tool、Item 等游戏机制。它不是原版联机 Farmhand，不需要启动第二个游戏客户端。

## 支持的动作

Mod 当前处理以下 `payload.action`：

```text
ping
move_relative       move_to             cancel
face_direction      use_tool            interact
warp_to             observe              get_inventory
attack              cast_fishing_rod    set_auto_combat
eat_item
```

CLI 与协议字段、结果示例和完整链路见 [`doc/demo/cli-file-bridge.md`](../doc/demo/cli-file-bridge.md)。Mod 只接受 `actor_id=companion-1`；主农场主只进入状态快照，不是这些动作的目标。

`warp_to` 是显式传送，可以把 Companion 放到另一个已加载地点；当前 Mod 没有跨地图自主寻路，也没有 follow、farm、mine、fish 等自主调度模式。`use_tool`、`attack` 和 `cast_fishing_rod` 是否成功，取决于 shadow farmer 背包和游戏中的目标、地点及其他前置条件。

## 配置

第一次加载 Mod 时会自动生成 `config.json`。默认 Bridge 目录是当前 Mod 文件夹下的 `bridge/`：

```json
{
  "BridgeDirectory": "",
  "LatestWriteIntervalSeconds": 5,
  "SnapshotHistoryIntervalSeconds": 60,
  "SnapshotHistoryLimit": 10
}
```

配置项含义：

- `BridgeDirectory`：CLI 和 Mod 共同访问的 Bridge 目录；为空时使用 Mod 目录下的 `bridge/`；
- `LatestWriteIntervalSeconds`：完整 `snapshots/snapshot-latest.json` 的写出间隔；
- `SnapshotHistoryIntervalSeconds`：历史快照槽位的写出间隔；
- `SnapshotHistoryLimit`：历史槽位数量。

历史文件使用固定槽位轮转。例如限制为 3 时，文件只会是 `snapshot-0.json`、`snapshot-1.json` 和 `snapshot-2.json`，写到末尾后重新覆盖 `snapshot-0.json`。`snapshot-latest.json` 始终保存完整最新状态，并记录最近历史快照的 `snapshot_index`，不是只保存一个指针。

也可以把 `BridgeDirectory` 改为绝对路径。Windows 上 CLI 使用同一个目录：

```text
stardew-cli.exe --bridge-dir "D:\stardew-agent-bridge" status
```

## 构建

```text
dotnet build StardewAgentMod.csproj --configuration Release
```

项目引用 `Pathoschild.Stardew.ModBuildConfig`。本地有游戏目录时，它可以部署 Mod；GitHub Actions 中通过根目录的 `Directory.Build.props` 禁用自动部署，并只上传 `_releases/` 下的 zip。CI 负责生成构建产物，不替代 Windows + SMAPI 的真实游戏验证。
