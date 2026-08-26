# CLI 工具系统通信 Demo 技术实现方案

## 文档信息

| 项目 | 内容 |
| --- | --- |
| **文档标题** | CLI 工具系统通信 Demo 技术实现方案 |
| **文档版本** | v0.12 |
| **创建日期** | 2026-08-23 |
| **更新日期** | 2026-08-26 |
| **文档类型** | 技术实现方案 |
| **参考资料** | [SMAPI Mod 结构](https://wiki.stardewvalley.net/Modding:Modder_Guide/APIs/Mod_structure)、[SMAPI Mod package](https://github.com/Pathoschild/SMAPI/blob/develop/docs/technical/mod-package.md)、[StardewMCP](https://github.com/Hunter-Thompson/stardew-mcp/tree/3ca54bbfc1d446eeb06d822a74c92cd14df82b93)、[StardewValley-MCP](https://github.com/amarisaster/StardewValley-MCP) |

## 目录

- [文档定位](#文档定位)
- [当前实现范围](#当前实现范围)
- [数据交互链路](#数据交互链路)
- [动作生命周期与取消](#动作生命周期与取消)
- [组件与目录](#组件与目录)
- [Bridge 目录与文件语义](#bridge-目录与文件语义)
- [协议定义](#协议定义)
- [CLI 命令](#cli-命令)
- [SMAPI Mod 行为](#smapi-mod-行为)
- [Fake Mod 与 Mac 验证](#fake-mod-与-mac-验证)
- [错误、超时与恢复](#错误超时与恢复)
- [GitHub Actions 产物](#github-actions-产物)
- [Windows 真机验证](#windows-真机验证)
- [与参考项目的对应关系](#与参考项目的对应关系)
- [当前限制](#当前限制)
- [实现检查项](#实现检查项)

## 文档定位

本文描述当前仓库中 CLI、Bridge 文件和 SMAPI Mod 的实际实现。它是 Demo 的实现说明，不是对外部项目的客观调研报告；调研事实分别记录在 `doc/research/` 中。

本 Demo 不使用 MCP Server，也不把 CLI 解释成游戏内快捷键。CLI 是一个独立的外部工具进程，调用方可以是人、脚本或其他 Agent。游戏内只有 SMAPI Mod 能直接访问 Stardew Valley 运行时，CLI 只能通过 Bridge 文件与 Mod 间接交互。

当前实现固定创建一个 AI Companion：

```text
actor_id = companion-1
mode     = direct
```

Companion 由两个对象组成：

- 可见 `CompanionNpc`：负责在游戏画面中显示和移动；
- 隐藏 `BotFarmer`：持有物品，并作为工具、战斗、钓鱼和交互等游戏 API 的调用主体。

主农场主不是 CLI 的动作目标。主农场主信息会进入状态快照，作为环境信息提供给外部调用方。

### Companion 资源占位

Companion 的逻辑名称固定为 `companion-1`，这个名称同时用于 CLI 的 `actor_id`、状态扫描和动作路由，不能为了修复游戏资源加载而改成原版 NPC 名称。

当前 Demo 尚未提供自定义头像资源。由于 Stardew Valley 会根据 NPC 名称尝试加载 `Portraits/companion-1`，Mod 在 SMAPI 的 `AssetRequested` 事件中临时将该资源映射到游戏已有的 `Portraits/Abigail`，并向 `Data/Characters` 注入最小的 Companion 角色数据，确保游戏将其作为有效 NPC 处理。生成时会优先选择玩家相邻且可通行的 tile；绘制时使用自定义 NPC 绘制逻辑。这些都是为了先验证 Companion 的生成和通信链路，不表示 Companion 的最终人物形象，也不改变其逻辑身份。

后续设计自己的头像时，只需将该映射的返回资源替换为 Mod 自带的 portrait 文件，并保留 `Portraits/companion-1` 这个资源键；如果替换行走动画，也应以同样方式注册 `Characters/companion-1`，不改动 CLI 和 Bridge 协议。

## 当前实现范围

当前 Demo 已实现三类能力：

1. **状态读取**：读取最新完整快照、世界、主农场主、Companion、环形历史快照，并请求即时观察和背包；
2. **Companion 直控**：移动、转向、使用工具、交互、显式传送、攻击、钓鱼、自动战斗开关、吃物品、发送聊天消息、显示头顶气泡和取消动作；统一取消机制仍待实现；
3. **链路诊断**：查看请求/结果、检查 Bridge 目录、清理历史请求文件和持续观察状态变化。

动作集合如下：

| 类别 | 动作 | 是否改变游戏状态 |
| --- | --- | --- |
| 连通性 | `ping` | 否；验证 CLI → Mod → CLI |
| 移动 | `move_relative`、`move_to` | 是；改变 Companion 的游戏内位置或移动任务；移动过程必须可取消 |
| 姿态 | `face_direction` | 是；改变 Companion 朝向 |
| 工具与交互 | `use_tool`、`interact` | 是；由游戏 API 决定是否成功 |
| 地图 | `warp_to` | 是；显式把 Companion 移到指定地点和 tile |
| 战斗 | `attack`、`set_auto_combat` | 是；攻击一次或开启/关闭实时自动攻击 |
| 钓鱼 | `cast_fishing_rod` | 是；启动鱼竿状态机 |
| 物品 | `get_inventory`、`eat_item` | 背包读取；吃物品会改变体力、生命和堆叠数量 |
| 说话 | `say` | 是；在游戏聊天框中显示 Companion 消息；消息提交前可取消 |
| 气泡 | `bubble` | 是；在 Companion 头顶显示限定时长的文字气泡，显示期间可取消 |
| 观察 | `observe` | 否；读取 Companion 周围 tile、对象、NPC 和怪物 |
| 控制 | `cancel` | 取消任意尚未完成的 action；不回滚已经完成的游戏副作用 |

`use_tool`、`attack` 和 `cast_fishing_rod` 的命令入口已经实现，但实际是否成功取决于 Companion shadow farmer 当前是否拥有相应工具、目标是否存在以及游戏运行时前置条件。失败会通过结构化 `error` 返回，不会伪造成功。

## 数据交互链路

游戏数据不直接从游戏进程流入 CLI。真实状态只存在于 Stardew Valley 运行时；SMAPI Mod 负责读取和改变它，并将有限字段投影为 JSON。CLI 只读写 Bridge 目录。

```mermaid
flowchart LR
    subgraph CLI[CLI 进程]
        C1[命令参数]
        C2[协议编码/解码]
        C3[JSON 输出]
    end

    subgraph BRIDGE[共享 Bridge 目录]
        P[actions/pending/*.json]
        Q[actions/processing/*.json]
        A[actions/archive/*.json]
        R[results/*.json]
        S[snapshots/snapshot-latest.json]
        H[snapshots/snapshot-{index}.json]
        E[errors/*.json]
    end

    subgraph GAME[SMAPI Mod 与游戏进程]
        T[UpdateTicked]
        G[Stardew Valley 运行时]
        V[状态投影]
        X[Companion 动作执行器]
    end

    C1 --> C2
    C2 -->|原子写入| P
    P -->|文件移动领取| Q
    Q --> T
    T -->|解析、校验| X
    X -->|读取/改变| G
    X -->|action.result| R
    Q -->|成功处理| A
    Q -->|解析失败| E
    T --> V
    G --> V
    V -->|完整最新状态| S
    V -->|按历史间隔轮转| H
    R --> C2
    S --> C2
    H --> C2
    C2 --> C3
```

以 `move_to` 为例，一次完整写操作的实际链路是：

1. CLI 将 `actor_id`、目标 tile 和 `action=move_to` 编码为请求文件；
2. Mod 在 `UpdateTicked` 中把请求从 `pending` 移到 `processing`；
3. Mod 校验 actor，并让可见 Companion NPC 使用游戏的 `PathFindController` 寻路；
4. Companion 到达目标、被阻挡、超时或被取消时，Mod 记录前后 tile 和状态；
5. Mod 写入与 `request_id` 同名的结果文件，再将请求移到 `archive`；
6. CLI 读取结果。之后的 `snapshot-latest.json` 会在下一次状态写出时反映 Companion 的当前位置。

因此，写入 JSON 请求本身不等于修改游戏。只有 Mod 领取请求、调用游戏 API 并生成成功结果后，才算动作执行完成。

## 动作生命周期与取消

所有 `action.request` 都必须携带 `request_id`，并通过同一个 `cancel` 动作支持取消。取消是一个独立的控制请求，不在原动作 payload 中增加布尔字段；这样 CLI、脚本和其他外部调用方都可以在拿到目标 request ID 后，从另一个进程发出取消请求。

“支持取消”表示 Mod 在动作尚未完成时能够阻止它继续执行，并为目标请求写出 `cancelled` 终态；它不表示能够撤销已经完成的游戏副作用。例如，已经显示到聊天框的 `say` 消息不能被删除，已经完成的 `eat_item` 不能通过取消恢复物品。对一次性动作，取消窗口位于调用游戏 API 之前；对长动作，取消窗口覆盖整个状态机生命周期。

动作处理必须遵守以下生命周期：

```mermaid
stateDiagram-v2
    [*] --> Pending: CLI 写入 action.request
    Pending --> Cancelled: cancel 在领取前到达
    Pending --> Running: Mod 领取请求
    Running --> CancelRequested: cancel 被接受
    CancelRequested --> Cancelled: 动作在安全点停止
    Running --> Succeeded: 动作完成
    Running --> Failed: 前置条件/游戏 API 失败
    Running --> Blocked: 移动无法到达目标
    Succeeded --> [*]
    Failed --> [*]
    Blocked --> [*]
    Cancelled --> [*]
```

Mod 不使用阻塞等待、线程强杀或 `Thread.Sleep` 实现取消。`UpdateTicked` 每次先处理取消控制请求，再推进当前任务；长动作在每个游戏 Tick 的安全点检查取消标记。取消只能在主线程中调用游戏 API，从而避免跨线程访问 Stardew Valley 对象。

取消请求的处理结果与目标动作结果分开：

1. `cancel` 自身返回“取消请求已接受、目标已完成或目标不存在”等处理结果；
2. 目标请求仍然使用原来的 `request_id` 写出最终 `action.result`；
3. 目标进入 `cancelled` 前，CLI 不能仅凭 `cancel` 的返回就断言动作已经停止，应继续读取目标结果或使用 `result show` 查询；
4. 如果目标已经完成，取消不会回滚副作用，`cancel` 返回目标已完成；如果目标仍在 `pending`，Mod 直接写入目标的 `cancelled` 结果并归档请求；如果目标在执行中，Mod 设置取消标记并由对应动作的 Tick 逻辑完成清理。

取消请求在 Mod 的读取链路中具有优先级：每个 `UpdateTicked` 先扫描并领取 `cancel`，再推进活动任务，最后领取普通动作。普通动作不会自动取消当前任务；当单个 `companion-1` 已经执行一个长动作时，新的普通动作仍返回 `busy`，只有明确的 `cancel` 才能中断它。

### 各类动作的取消边界

| 动作类别 | 执行方式 | 取消检查点 | 已完成后的行为 |
| --- | --- | --- | --- |
| `move_relative`、`move_to` | 路径控制器逐 Tick 移动 | 每个 Tick；调用路径控制器停止并记录当前位置 | 不回退已经走过的路径，目标结果为 `cancelled` |
| `cast_fishing_rod` | 鱼竿状态机逐 Tick 更新 | 抛竿、等待咬钩、收线的每个安全点；清理鱼竿状态 | 已经完成的捕获不回滚 |
| `set_auto_combat` | 自动战斗循环逐 Tick 检查 | 每次自动攻击前；关闭自动战斗并结束任务 | 已造成的伤害不回滚；使用中的模式被关闭 |
| `bubble` | 带截止时间的显示状态 | 每个 Tick；清除当前气泡 | 已消失的气泡不产生额外效果 |
| `face_direction`、`use_tool`、`interact`、`warp_to`、`attack`、`eat_item`、`say` | 单 Tick 内调用一次游戏 API | 调用 API 前 | 已提交的游戏副作用不回滚 |
| `ping`、`observe`、`get_inventory` | 单 Tick 内读取状态 | 读取开始前 | 已返回的只读结果无需撤销 |

一次性动作也必须经过统一任务登记和取消检查，不能因为通常很快就省略 `request_id` 或取消分支。取消请求到达时，如果游戏 API 调用已经完成，系统返回目标已完成，而不是伪造一个取消结果。

### CLI 等待与取消

同步 CLI 等待结果时，CLI 进程本身会阻塞等待，但不会阻塞游戏主线程。为了让长距离 `move_to` 可以在等待期间被中断，CLI 需要提供分离生命周期：先提交并立即打印 `request_id`，再由另一个命令等待结果或发送 `cancel <request-id>`。推荐的接口形状是：

```text
stardew-cli move-to --x 120 --y 80 --detach
stardew-cli wait <request-id>
stardew-cli cancel <request-id>
```

`--detach` 只改变 CLI 的等待行为，不改变 Bridge 协议；请求仍然由 Mod 按相同的 `pending` → `processing` → `results` / `archive` 链路执行。CLI 超时也不等于取消，调用方必须显式发出 `cancel`。

`action.result` 与 `snapshot` 的职责不同：

| 数据 | 含义 | 典型用途 |
| --- | --- | --- |
| `action.result` | 某个 request 的即时执行结果 | 判断这一次移动是否到达、工具是否成功、错误原因是什么 |
| `snapshot` | 某个时刻的状态投影 | 查看完整环境、轮询状态、恢复后读取最新状态 |
| `observe` 结果 | Companion 周围的即时局部观察 | 为移动和交互调用提供局部 tile 信息 |

## 组件与目录

### 组件

| 组件 | 运行位置 | 职责 |
| --- | --- | --- |
| `stardew-cli` | 游戏外；发布产物为 Windows CLI | 解析命令、写请求、等待结果、读取状态和诊断 Bridge |
| `fake-mod` | 游戏外；Mac 可运行 | 模拟 Mod 的文件领取、结果写回和状态快照，用于协议测试 |
| `StardewAgentMod` | Stardew Valley 进程内 | 访问 SMAPI/Game API，创建 Companion，执行动作，生成状态 |
| Bridge 目录 | CLI 与 Mod 都能访问的共享目录 | 传递 JSON 请求、结果和快照 |

### 仓库目录

```text
stardew-agent/
├── .github/workflows/build-demo.yml
├── Directory.Build.props
├── cli/
│   ├── Cargo.toml
│   ├── src/
│   │   ├── main.rs       # CLI 命令和 JSON 输出
│   │   ├── protocol.rs   # Rust 协议 DTO
│   │   ├── bridge.rs     # 文件桥接、快照、诊断
│   │   └── fake.rs       # Fake Mod
│   └── tests/bridge_tests.rs
├── smapi-mod/
│   ├── StardewAgentMod.csproj
│   ├── ModEntry.cs       # SMAPI 事件、请求分发、快照写出
│   ├── CompanionController.cs
│   ├── CompanionObservationScanner.cs
│   ├── Protocol.cs       # C# 协议 DTO
│   ├── BridgeFileStore.cs
│   └── ModConfig.cs
└── doc/demo/cli-file-bridge.md
```

Rust 和 C# 各自维护协议 DTO。字段名通过 JSON 属性保持一致；跨语言协议变化时，两侧必须同时更新。

## Bridge 目录与文件语义

Bridge 目录由 Mod 配置的 `BridgeDirectory` 指定。CLI 优先使用 `--bridge-dir`，其次使用 `STARDEW_BRIDGE_DIR`；两者都未提供时，默认使用 CLI 可执行文件所在目录下的 `bridge/`，与启动 CLI 时的工作目录无关。

合并发布包中 CLI 与 Mod 文件位于同一目录时，CLI 可执行文件旁边的 `bridge/` 与 Mod 默认的 `bridge/` 就是同一个目录。即使从其他工作目录启动 CLI，仍会使用这个位置；只有把 CLI 和 Mod 分开放置时，才需要显式指定 `--bridge-dir` 或设置 `STARDEW_BRIDGE_DIR`。

```text
bridge/
├── actions/
│   ├── pending/
│   ├── processing/
│   └── archive/
├── results/
├── snapshots/
└── errors/
```

| 路径 | 写入者 | 读取者 | 语义 |
| --- | --- | --- | --- |
| `actions/pending/{request_id}.json` | CLI | Mod | 等待处理的 `action.request` |
| `actions/processing/{request_id}.json` | Mod | Mod/诊断 | 已由 Mod 领取、尚未归档的请求 |
| `actions/archive/{request_id}.json` | Mod | CLI/诊断 | 已处理请求的留档 |
| `results/{request_id}.json` | Mod | CLI | 与请求 ID 对应的 `action.result` |
| `snapshots/snapshot-latest.json` | Mod | CLI | Mod 最近一次写出的完整状态，反复替换 |
| `snapshots/snapshot-{index}.json` | Mod | CLI/诊断 | 固定索引的历史快照槽位 |
| `errors/{request_id}[-{unique}].json` | Mod | CLI/诊断 | 请求解析或处理流程异常时的原始请求；协议版本错误可能保留原文件名 |

### 原子写入和领取

CLI 和 Mod 都先写入同目录临时文件，写完后再替换正式 `.json` 文件。Mod 通过把文件从 `pending` 移到 `processing` 来领取请求；同一文件不能被两个 Mod tick 重复领取。

请求 ID 由 CLI 生成 UUID。结果文件使用相同 ID，便于等待、诊断和关联；请求完成后，原始请求进入 `archive`，结果不会因为请求归档而丢失。

### `latest` 和历史快照轮转

`snapshot-latest.json` 保存完整最新状态，不是只记录索引的指针。它的 `payload` 至少包含：

- `latest_write_sequence`：Mod 状态写出的单调序号；
- `snapshot_sequence`：历史快照写出的单调序号；
- `snapshot_index`：最近历史快照占用的固定槽位；
- `game`、`player`、`companion`：当前状态投影。

两个写出周期独立配置：

- `LatestWriteIntervalSeconds`：多久重新写一次 `snapshot-latest.json`；
- `SnapshotHistoryIntervalSeconds`：多久写一次历史槽位；
- `SnapshotHistoryLimit`：历史槽位总数。

历史槽位是环形的，不是“不断创建新文件后删除旧文件”：

```text
SnapshotHistoryLimit = 3

snapshot-0.json → snapshot-1.json → snapshot-2.json
       ↑                                  ↓
       └──────────── 下一轮覆盖 ───────────┘
```

实际顺序是 `0, 1, 2, 0, 1, 2 ...`。每个历史文件保留自己的 `snapshot_sequence`，`snapshot-latest.json` 同时保留当前槽位索引。CLI 读取历史时按 `snapshot_sequence` 排序，而不是按文件名推断新旧。

历史写出后，Mod 会立即用同一份状态重新发布 `snapshot-latest.json`，所以 latest 能够同步反映最新的 `snapshot_index`。latest 写出本身不创建新的历史槽位。

## 协议定义

当前协议版本为 `0.2`。所有消息使用统一 Envelope：

```json
{
  "schema_version": "0.2",
  "message_type": "action.request",
  "request_id": "uuid",
  "created_at_ms": 1776900000000,
  "payload": {}
}
```

### Action Request

`payload.action` 区分动作，`actor_id` 当前必须是 `companion-1`。请求字段如下：

| `action` | 额外字段 |
| --- | --- |
| `ping` | 无 |
| `move_relative` | `direction`、`ticks` |
| `move_to` | `x`、`y` |
| `face_direction` | `direction` |
| `use_tool` | `tool`、`x`、`y` |
| `interact` | `x`、`y` |
| `warp_to` | `location`、`x`、`y` |
| `observe` | `radius` |
| `get_inventory` | 无 |
| `attack` | 无 |
| `cast_fishing_rod` | 无 |
| `set_auto_combat` | `enabled` |
| `eat_item` | 可选 `slot` |
| `say` | `text` |
| `bubble` | `text`、`duration_ms` |
| `cancel` | `target_request_id` |

示例：

```json
{
  "schema_version": "0.2",
  "message_type": "action.request",
  "request_id": "0c6c7d24-8a6d-4b99-9b40-3c1f10c7068f",
  "created_at_ms": 1776900000000,
  "payload": {
    "action": "move_to",
    "actor_id": "companion-1",
    "x": 72,
    "y": 18
  }
}
```

### Action Result

普通动作的结果使用 `message_type=action.result`。常见状态为：

| 状态 | 含义 |
| --- | --- |
| `succeeded` | Mod 执行动作并满足成功条件 |
| `blocked` | 移动任务结束但未到达目标 |
| `cancelled` | 目标动作在完成前被取消 |
| `failed` | 参数、世界状态或游戏 API 前置条件不满足 |
| `expired` | 预留给上层调用方标记等待超时；当前 CLI 超时不会写入这个状态，也不会自动撤销已发出的请求 |

结果包含 `action`、`actor_id`，失败时包含结构化错误：

```json
{
  "schema_version": "0.2",
  "message_type": "action.result",
  "request_id": "0c6c7d24-8a6d-4b99-9b40-3c1f10c7068f",
  "created_at_ms": 1776900000123,
  "payload": {
    "status": "blocked",
    "action": "move_to",
    "actor_id": "companion-1",
    "ticks": 0,
    "target_tile": {"x": 72, "y": 18},
    "before_tile": {"x": 65, "y": 15},
    "after_tile": {"x": 65, "y": 15},
    "moved": false,
    "world_ready": true,
    "error": {
      "code": "movement_timeout",
      "message": "the companion did not reach the target tile"
    }
  }
}
```

移动结果还包括 `before_tile`、`after_tile`、`target_tile`、`moved` 和 `world_ready`。观察结果的 `data.observation` 包含地点、中心 tile、半径、特殊 tile、怪物和 NPC；背包结果的 `data.inventory` 为结构化物品数组。

取消结果至少包含 `target_request_id`、`target_action`、`target_status` 和 `cancelled`。`cancelled=true` 表示目标已经进入取消流程或已经写出取消终态；调用方仍应读取目标 request 的结果，以确认动作是否已经完成清理。目标动作的取消结果保留原动作字段，例如移动仍需返回取消发生时的 `before_tile`、`after_tile` 和 `target_tile`。

### Snapshot

快照的 `payload` 是只读状态投影，主要字段如下：

```json
{
  "latest_write_sequence": 42,
  "snapshot_sequence": 7,
  "snapshot_index": 2,
  "mod_version": "0.1.0",
  "game_tick": 12345,
  "world_ready": true,
  "game": {
    "year": 1,
    "season": "spring",
    "day": 1,
    "time": 900,
    "location": "Farm",
    "weather": "sunny"
  },
  "player": {
    "name": "Player",
    "location": "Farm",
    "tile": {"x": 64, "y": 15},
    "facing_direction": "down",
    "health": 100,
    "max_health": 100,
    "stamina": 270,
    "max_stamina": 270,
    "money": 500
  },
  "companion": {
    "id": "companion-1",
    "type": "ai_companion",
    "display_name": "Companion",
    "location": "Farm",
    "tile": {"x": 65, "y": 15},
    "facing_direction": "down",
    "mode": "direct",
    "status": "idle",
    "world_ready": true,
    "busy": false,
    "auto_combat": false,
    "inventory": [],
    "capabilities": ["move_to", "observe", "interact"]
  }
}
```

快照不是游戏存档的完整序列化，也不是对主农场主输入的模拟；它只承诺当前协议 DTO 中的字段。

## CLI 命令

所有命令都支持：

```text
stardew-cli [--bridge-dir <bridge-directory>] <command>
```

也可以设置 `STARDEW_BRIDGE_DIR`，省略全局参数。没有参数或环境变量时，CLI 使用自身可执行文件所在目录下的 `bridge/`。输出统一为 JSON；动作结果为失败状态时，CLI 同时打印结果并以非零退出码结束。

### 状态、观察和诊断

| 命令 | 作用 |
| --- | --- |
| `status` | 打印完整 `snapshot-latest.json` |
| `world` | 只打印快照中的 `game` |
| `player` | 只打印快照中的 `player` |
| `companion --actor-id companion-1` | 只打印指定 Companion 状态 |
| `inventory [--actor-id ...]` | 发请求读取实时背包 |
| `observe [--actor-id ...] [--radius 8]` | 发请求读取局部环境 |
| `snapshot list` | 按序号列出历史槽位 |
| `snapshot read <index>` | 读取一个固定历史槽位 |
| `request show <request-id>` | 查找请求及其所在目录 |
| `result show <request-id>` | 读取已写入的结果 |
| `doctor` | 检查目录、latest、历史数量和临时文件 |
| `cleanup --dry-run` | 预览结果、归档和错误文件清理范围 |
| `cleanup` | 删除超过保留时长的结果、归档和错误 JSON |
| `watch` | 按 `latest_write_sequence` 变化持续打印快照 |

`cleanup` 不会触碰 `snapshots/`，也不会删除 `pending` 或 `processing` 中的请求。执行前可以先使用 `--dry-run`。

### Companion 动作

| CLI 命令 | 请求动作 | 示例 |
| --- | --- | --- |
| `ping` | `ping` | `ping` |
| `move <direction> --ticks <n>` | `move_relative` | `move right --ticks 15` |
| `move-to --x <x> --y <y>` | `move_to` | `move-to --x 72 --y 18` |
| `face <direction>` | `face_direction` | `face left` |
| `use-tool <tool> --x <x> --y <y>` | `use_tool` | `use-tool hoe --x 72 --y 18` |
| `interact --x <x> --y <y>` | `interact` | `interact --x 72 --y 18` |
| `warp --location <name> --x <x> --y <y>` | `warp_to` | `warp --location Mine --x 6 --y 6` |
| `attack` | `attack` | `attack` |
| `cast-fishing-rod` | `cast_fishing_rod` | `cast-fishing-rod` |
| `set-auto-combat --enabled <bool>` | `set_auto_combat` | `set-auto-combat --enabled true` |
| `eat-item [--slot <n>]` | `eat_item` | `eat-item --slot 4` |
| `say <text>`（别名 `chat`） | `say` | `say "我已经开始工作了"` |
| `bubble <text> [--duration-ms <ms>]` | `bubble` | `bubble "我在这里" --duration-ms 3000` |
| `cancel <request-id>` | `cancel`；可指向任意尚未完成的 action | `cancel 0c6c7d24-...` |

写命令当前固定控制 `companion-1`；读命令可以通过 `--actor-id` 指定协议中的 actor 字段，但 Mod Demo 目前只接受这个唯一 ID。

`say` 当前使用 Stardew Valley 的聊天框 API，显示一条带有 Companion 颜色的聊天消息。`bubble` 使用 Companion NPC 的世界绘制层，在角色头顶显示带文字换行和尾部指示的临时气泡；它不会打开 NPC DialogueBox，也不产生语音输出。

`move_relative` 的 `ticks` 由 CLI 限制为 `1..=30`。`observe` 的 `radius` 由 CLI 和 Mod 共同限制为 `1..=16`。限制是为了避免单个请求无限占用游戏 tick 或产生过大的观察结果。

为支持长距离 `move_to` 的中断，CLI 实现需要增加分离等待方式。同步模式保持现有命令形状；分离模式只提交请求并立即输出 request ID，`wait` 负责后续等待，`cancel` 负责从另一个 CLI 进程发送取消请求。以下接口属于本方案的待实现部分：

```text
stardew-cli move-to --x 120 --y 80 --detach
stardew-cli wait <request-id>
stardew-cli cancel <request-id>
```

所有动作命令都使用相同的 `request_id`、`wait` 和 `cancel` 机制，而不是为 `move_to` 单独设计一套取消协议。同步 CLI 的超时只停止等待，不会隐式取消动作。

## SMAPI Mod 行为

### 生命周期事件

Mod 订阅以下事件：

| 事件 | 行为 |
| --- | --- |
| `UpdateTicked` | 确保 Companion 存在、进入存档后首次写出 latest、优先领取取消请求、推进活动动作、领取普通 pending 请求 |
| `OneSecondUpdateTicked` | 按配置写 latest 和历史快照 |
| `DayStarted` | 重置 shadow farmer 的睡眠状态和基础资源 |
| `DayEnding` | 向 shadow farmer 发出睡眠就绪信号 |
| `ReturnedToTitle` | 取消所有正在执行的动作并清理可见 Companion |
| `AssetRequested` | 注入 Companion 角色数据，并将当前 Demo 的 `Portraits/companion-1` 临时映射到 `Portraits/Abigail` |

### 请求处理

请求处理顺序是：优先扫描并领取 `cancel` → 推进已有活动动作 → 扫描普通 `pending` → 原子移动到 `processing` → 读取 Envelope → 读取 `payload.action` → 校验 actor 和参数 → 调用 CompanionController 或创建动作状态机 → 写结果 → 归档请求。

`cancel` 是所有 action 共用的取消入口。Mod 为每个已领取的请求保留最小运行状态，并在每个 `UpdateTicked` 执行取消检查：

- `move_relative` 和 `move_to` 由路径控制器逐 Tick 推进，收到取消后停止控制器，保留当前位置并为原请求写入 `cancelled`；
- `cast_fishing_rod` 保存鱼竿状态机对应的 request ID，收到取消后退出钓鱼状态并清理相关状态；
- `set_auto_combat` 的启用状态绑定到活动任务，收到取消后关闭自动战斗循环；
- `bubble` 在显示期间保留 request ID，收到取消后清除气泡；
- 一次性动作在调用游戏 API 前检查取消；如果 API 已返回，则动作视为已完成，取消只能返回目标已完成，不能回滚游戏副作用；
- 只读动作同样登记 request ID，读取开始前可以取消，读取完成后取消不再产生效果。

`UpdateTicked` 不等待长动作结束。它每次只执行有限的状态推进和取消检查，保证 Mod 可以继续读取新的 cancel 请求。动作处理器不得在游戏主线程中运行阻塞循环、等待文件结果或休眠。

### 状态读取

快照状态包括：

- 世界：年份、季节、日期、时间、当前地点和天气；
- 主农场主：名字、地点、tile、朝向、生命、体力和金钱；
- Companion：地点、tile、朝向、生命、体力、背包数量、direct 模式、忙碌状态、当前动作、自动战斗开关和能力列表。

`observe` 的局部扫描从 Companion 所在 `GameLocation` 读取：

- 不可通行或有内容的 tile；
- 水、耕地、作物、作物成熟状态和浇水状态；
- 石头、杂草、树枝、梯子、箱子和机器等对象类型；
- 半径内怪物和普通 NPC。

## Fake Mod 与 Mac 验证

`fake-mod` 不加载 SMAPI 和游戏程序集，只实现同一 Bridge 协议的文件侧行为：领取请求、生成 `action.result`、按独立周期写出 latest 与环形历史快照，并维护一个简化的 Companion 状态。`--latest-interval-ms` 和 `--snapshot-history-interval-ms` 分别控制两个周期；旧参数 `--snapshot-interval-ms` 是 latest 周期的兼容别名。

Mac 上可以运行：

```text
cargo test --manifest-path cli/Cargo.toml
cargo run --manifest-path cli/Cargo.toml --bin fake-mod -- --help
cargo run --manifest-path cli/Cargo.toml --bin fake-mod -- \
  --bridge-dir <bridge-directory> \
  --snapshot-history-limit 10
```

Fake Mod 可以验证：

- CLI 是否能写入请求文件；
- 请求是否能被领取、归档并产生同 ID 结果；
- 每类动作的 JSON 字段和结果形状；
- `latest` 是否为完整状态；
- 历史槽位是否按 `SnapshotHistoryLimit` 轮转；
- `doctor`、请求查询和结果查询是否能读取 Bridge；
- 没有游戏时，CLI 是否能对失败状态返回非零退出码。

Fake Mod 不能验证 SMAPI API、游戏碰撞、实际寻路、工具动画、作物收获、怪物战斗或鱼竿状态机。上述内容必须在 Windows 游戏环境中验证。

## 错误、超时与恢复

### 错误分类

| 错误 | 发生位置 | 结果 |
| --- | --- | --- |
| `invalid_request` | Mod 无法解析 Envelope 或请求文件 | 请求移到 `errors/`，结果写为失败 |
| `unsupported_schema` | 请求协议版本与 Mod 当前版本不一致 | 结果写为失败，请求移到 `errors/` |
| `unsupported_action` | Mod 没有对应分发分支 | 结果写为失败，请求归档 |
| `unsupported_actor` | actor 不是 `companion-1` | 结果写为失败，请求归档 |
| `world_not_ready` | 未进入存档或 Companion 未创建 | 结果写为失败 |
| `busy` | Companion 仍有活动任务 | 新的普通动作失败；取消请求仍然优先处理 |
| `blocked` | 路径结束但没有抵达目标 | 移动结果带前后 tile |
| `movement_timeout` | 移动超出 tick 限制 | 结果失败/阻塞并释放任务 |
| `cancelled_before_start` | 目标仍在 `pending`，尚未调用游戏 API | 目标结果为 `cancelled` 并归档 |
| `cancelled_by_request` | 活动动作在安全点响应取消 | 目标结果为 `cancelled` 并完成动作清理 |
| `request_not_found` | Bridge 中不存在目标 request | `cancel` 自身失败 |
| `request_completed` | 目标已经写出终态结果 | `cancel` 自身返回未取消，不回滚副作用 |
| `unknown_tool`、`no_attack_target` 等 | 游戏动作前置条件不满足 | 结构化动作失败 |

### CLI 超时

CLI 等待 `results/{request_id}.json`，超时只表示 CLI 停止等待，不会撤销已经写入 `pending` 的请求。此时可以使用：

```text
stardew-cli request show <request-id>
stardew-cli result show <request-id>
```

判断请求是仍在 `pending`、正在 `processing`、已归档还是进入 `errors`。对任何尚未进入终态的 action，都可以使用 `cancel <request-id>`；取消请求本身成功不代表目标结果已经写完，应继续查询目标 request 的结果。

### 崩溃恢复

- CLI 写请求和 Mod 写结果/快照使用临时文件后再替换正式文件，减少半截 JSON 被读取的机会；
- Mod 启动时清理超出当前 `SnapshotHistoryLimit` 的历史槽位；
- CLI 读取 latest 失败或缺失时，回退到 `snapshot_sequence` 最大的合法历史槽位；
- `doctor` 会报告目录是否存在、latest 是否可用、历史快照数量和临时文件数量；
- 结果、归档和错误文件由 `cleanup` 按文件修改时间清理，快照不受影响。

## GitHub Actions 产物

工作流文件是 `.github/workflows/build-demo.yml`，负责构建 CLI、构建 SMAPI Mod，并更新一个固定的 `latest` 开发版 GitHub Release。Actions artifact 仍然作为 job 间传递和失败排查用的中间产物。

Release 中提供一个稳定命名的 Windows 下载资产，CLI 和 SMAPI Mod 位于同一个压缩包中：

```text
latest/
└── stardew-agent-windows.zip
    ├── manifest.json
    ├── StardewAgentMod.dll
    ├── stardew-cli.exe
    └── fake-mod.exe
```

将压缩包内容放入同一个目录后，可以把该目录作为 SMAPI Mod 目录。CLI 默认使用自身旁边的 `bridge/`，与 Mod 默认的 `bridge/` 保持一致，不要求从该目录启动命令行。

构建 job 的中间 artifact 结构为：

```text
stardew-agent-mod-<run-number>/
└── StardewAgentMod <version>.zip

stardew-agent-cli-windows-<run-number>/
└── release/
    ├── stardew-cli.exe
    └── fake-mod.exe
```

CI 构建 Mod 时使用仓库根目录的 `Directory.Build.props`：

```xml
<Project>
  <PropertyGroup>
    <ModZipPath>$(MSBuildThisFileDirectory)_releases</ModZipPath>
    <EnableModDeploy>false</EnableModDeploy>
  </PropertyGroup>
</Project>
```

这只影响构建机上的 Mod 自动部署，不会影响 zip 产物生成。推送到 `main` 或手动运行工作流会更新同一个 `latest` Release，不需要用户手工创建或推送版本 tag；workflow 内部使用固定 tag 作为 Release 的标识。Pull Request 只运行构建和测试，不会修改 Release。

Mod 项目目标框架为 `net6.0`。Mac 可以安装 .NET SDK 编译 C# 工程，但真实 SMAPI/Game API 验证仍然需要 Windows 游戏环境；Windows CLI 只发布 Windows 运行产物，Mac 主要运行 Rust 测试和 Fake Mod。

## Windows 真机验证

每次完整验证需要：

1. 用 SMAPI 启动 Stardew Valley 并进入存档；
2. 将合并发布包中的全部文件放入游戏的 `Mods/StardewAgentMod` 目录；
3. 直接运行其中的 `stardew-cli.exe`，CLI 会使用自身旁边的 `bridge/`；
4. 如果 CLI 与 Mod 分开放置，在 Mod 配置中设置相同的 `BridgeDirectory`，或给 CLI 指定 `--bridge-dir`；
5. 依次验证 `status`、`ping`、`observe`、`inventory`、`move-to`、`use-tool` 和 `interact`；
6. 对传送、战斗、钓鱼和自动战斗分别在满足游戏前置条件的场景验证；
7. 用 `snapshot list`、`doctor` 和 `request/result show` 检查文件轮转与请求生命周期。

可以在 Windows 真机验证后，把 Bridge 目录中的 JSON 复制到 Mac，用 CLI 做离线解析、查询和快照轮转测试；这些离线检查不能替代下一次真实游戏验证。

当前实现没有注册游戏内快捷键。用户或外部 Agent 发出动作的入口是 CLI 命令及其生成的 `action.request`；这与参考项目通过 MCP 工具调用直控动作的方式对应，但本 Demo 的外部接口是 CLI。

## 与参考项目的对应关系

参考项目 [StardewValley-MCP](https://github.com/amarisaster/StardewValley-MCP) 的 Player mode 直控动作与本 Demo 的对应关系如下：

| 参考项目能力 | 本 Demo |
| --- | --- |
| `get_surroundings` | `observe` |
| `get_inventory` | `inventory` |
| `get_companion_state` | `companion` + `status` |
| `move_to` | `move-to` |
| `warp_companion` | `warp` |
| `face_direction` | `face` |
| `use_tool` | `use-tool` |
| `interact` | `interact` |
| `attack` | `attack` |
| `cast_fishing_rod` | `cast-fishing-rod` |
| `set_auto_combat` | `set-auto-combat` |
| `eat_item` | `eat-item` |

对应关系只表示动作语义，不表示两边协议字段、伴侣数量、资源配置或自主模式完全相同。当前 Demo 有意只创建一个 `companion-1`，只实现 direct 控制；没有实现参考项目的 follow、farm、mine、fish 等自主模式，也没有把 Companion 注册为原版联机 Farmhand。

参考项目和本 Demo 都采用“可见 NPC + shadow farmer”的职责分离：NPC 负责可见位置和寻路，shadow farmer 负责调用 Farmer/Tool/Item 等游戏机制。由于 shadow farmer 不是原版网络玩家，本 Demo 的 `warp` 是 Mod 内部的显式对象移动，不会创建第二个本地游戏客户端，也不会把它描述成真实联机角色。

## 当前限制

- 只支持一个 actor：`companion-1`；
- 直控入口是 CLI，不是 MCP，不是游戏内按键；
- 没有自主跟随、农场、采矿或钓鱼调度器；
- `warp` 可以把 Companion 显式移动到另一个已加载地点，但没有跨地图自主寻路；
- Companion 是否拥有鱼竿、武器以及动作是否可执行，取决于 shadow farmer 的实际背包和游戏前置条件；
- `say` 只写入游戏聊天框；`bubble` 只显示临时头顶气泡，不会打开 NPC DialogueBox；
- 当前代码的 `cancel` 仍主要覆盖移动；统一覆盖钓鱼、自动战斗、气泡和一次性 action 的取消语义尚待实现；
- 当前 CLI 仍以同步等待为主，分离提交、`wait` 和跨进程取消接口尚待实现；
- 状态是有限投影，不是完整存档或完整游戏对象图；
- 当前 Bridge 适用于同一台机器上的低频 CLI/Mod 通信，不承诺跨机器、多 CLI 并发写入或高频实时控制；
- Fake Mod 只覆盖协议和文件链路，不能替代 Windows 游戏验证；
- CI 负责构建和上传产物，不负责启动游戏、安装 SMAPI 或完成真机验收。

## 实现检查项

以下检查项对应当前代码和文档范围：

- [x] CLI 可写入 `action.request` 并等待按 ID 返回的结果；
- [x] Mod 可领取、校验、处理、归档请求；
- [x] 请求失败时返回结构化错误；
- [x] `latest` 始终保存完整最新状态；
- [x] 历史快照按固定槽位环形覆盖，数量由配置控制；
- [x] CLI 支持状态、观察、背包、快照、请求、结果和 Bridge 诊断；
- [x] CLI 支持向游戏聊天框发送 Companion 消息；
- [x] CLI 支持在 Companion 头顶显示限定时长的文字气泡；
- [x] CLI 支持参考项目 Player mode 的核心直控动作；
- [x] Fake Mod 在 Mac 上覆盖全部协议动作入口；
- [x] Rust 集成测试覆盖 ping、移动、快照轮转和动作协议；
- [x] `move_to` 使用逐 Tick 路径控制，不在游戏主线程同步等待；
- [ ] 所有 action 登记统一任务状态，并在完成前支持 `cancel`；
- [ ] `move_relative`、`move_to`、钓鱼、自动战斗和气泡的活动状态可被取消并完成清理；
- [ ] 一次性和只读 action 具备统一的取消前检查及已完成结果语义；
- [ ] CLI 支持分离提交、按 request ID 等待和跨进程取消；
- [ ] Fake Mod 覆盖 pending、processing、运行中任务和已完成任务的取消竞态；
- [ ] Windows + SMAPI 真实游戏中完成全部动作的阶段验证；
- [ ] 在真实游戏中验证不同地点、工具、作物、箱子、怪物和鱼竿状态机的版本兼容性。
