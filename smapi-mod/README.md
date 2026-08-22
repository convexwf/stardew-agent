# Stardew Agent Bridge Mod

这是游戏内的 SMAPI 适配层。它在主农场主旁边创建一个单独的 AI Companion，读取 CLI 写入的 action request，访问游戏运行时状态，并把 action result 和 snapshot 写回 Bridge 目录。

当前 Demo 的 Companion ID 固定为 `companion-1`。它由可见 NPC 和一个不加入原版多人玩家集合的 shadow farmer 组成；CLI 的 `move` 命令控制 Companion，而不是控制主农场主。Companion 只保证在当前主农场主所在地图内移动。

## 本地运行配置

第一次加载 Mod 时会自动生成 `config.json`。默认 Bridge 目录是当前 Mod 文件夹下的 `bridge/`：

```json
{
  "BridgeDirectory": "",
  "LatestWriteIntervalSeconds": 5,
  "SnapshotHistoryIntervalSeconds": 60,
  "SnapshotHistoryLimit": 10
}
```

`LatestWriteIntervalSeconds` 控制完整的 `snapshots/snapshot-latest.json` 写入间隔；历史槽位写入后也会立即重新发布 latest，以同步最新的 `snapshot_index`；`SnapshotHistoryIntervalSeconds` 控制历史快照写入间隔；`SnapshotHistoryLimit` 控制固定历史槽位数量。比如限制为 10 时，历史文件为 `snapshot-0.json` 到 `snapshot-9.json`，写满后从 `snapshot-0.json` 开始覆盖，不删除并新建无限增长的文件。

也可以把 `BridgeDirectory` 改为绝对路径。CLI 使用同一个目录，例如：

```text
stardew-cli.exe --bridge-dir "D:\\stardew-agent-bridge" status
```

## 构建

```text
dotnet build StardewAgentMod.csproj --configuration Release
```

项目引用 `Pathoschild.Stardew.ModBuildConfig`。本地有游戏目录时，它可以部署 Mod；GitHub Actions 中通过根目录的 `Directory.Build.props` 禁用自动部署，并只上传 `_releases/` 下的 zip。
