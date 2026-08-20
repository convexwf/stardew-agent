# Stardew Agent Observation、Action 与 Result 形态调研

## 文档信息

| 项目         | 内容 |
| ------------ | ---- |
| **文档标题** | Stardew Agent Observation、Action 与 Result 形态调研 |
| **文档版本** | v0.2 |
| **创建日期** | 2026-08-23 |
| **更新日期** | 2026-08-23 |
| **文档作者** | 项目维护者 |
| **文档类型** | 资料调研报告 |
| **参考资料** | [StardewMCP GameStateSerializer](https://github.com/Hunter-Thompson/stardew-mcp/blob/3ca54bbfc1d446eeb06d822a74c92cd14df82b93/mod/StardewMCP/GameStateSerializer.cs)、[StarDojo Observation Space](https://github.com/StarDojo2025/stardojo/blob/e251401cf1e84ba07cbfa08283a7aba52290e578/docs/docs_src/observation_space.md)、[StarDojo Action Space](https://github.com/StarDojo2025/stardojo/blob/e251401cf1e84ba07cbfa08283a7aba52290e578/docs/docs_src/action_space.md) |

## 目录

- [调研范围](#调研范围)
- [Observation 的形态](#observation-的形态)
- [Action 的形态](#action-的形态)
- [Result 与状态反馈](#result-与状态反馈)
- [动作粒度差异](#动作粒度差异)
- [字段和版本特征](#字段和版本特征)
- [资料限制](#资料限制)

## 调研范围

本报告比较 StardewMCP 和 StarDojo 中跨组件传递的 Observation、Action 和 Result。这里的“契约”指参考项目实际使用的字段、参数和状态表达，不指 Stardew Agent 的接口设计。代码示例来自公开项目中的概念，经过缩写或整理后仅用于说明字段形态。

## Observation 的形态

### StardewMCP

[GameStateSerializer.cs](https://github.com/Hunter-Thompson/stardew-mcp/blob/3ca54bbfc1d446eeb06d822a74c92cd14df82b93/mod/StardewMCP/GameStateSerializer.cs) 将状态组织为 JSON，内容包括玩家、时间、世界、周围环境、地图、任务、关系和技能。周围环境部分包含附近对象和 61×61 ASCII 地图。状态由 Mod 序列化后通过 WebSocket 发送给外部客户端。

从该实现可以观察到以下字段类别：

| 类别 | 源码中出现的内容 |
| --- | --- |
| 玩家 | 位置、朝向、移动状态、工具或物品相关信息 |
| 时间 | 年、季节、日期、游戏内时间、天气等 |
| 地点 | 当前地点、地图和地点上下文 |
| 周边 | 附近地块、对象、NPC、作物或其他实体 |
| 进度 | 任务、技能、关系和资源信息 |
| 调试/动作 | 当前动作、移动目标或执行状态相关信息 |

具体字段会随固定提交中的序列化代码变化，JSON 没有被文档描述为跨项目标准。

### StarDojo

[Observation Space 文档](https://github.com/StarDojo2025/stardojo/blob/e251401cf1e84ba07cbfa08283a7aba52290e578/docs/docs_src/observation_space.md) 将 Observation 描述为环境状态、玩家、NPC、地点和地块、农场、进度、菜单以及图像等部分。StarDojo 的 Mod 通过 TCP 和共享内存向 Python 环境提供不同类型的数据。

文档中可观察到的字段类别如下：

| 类别 | 资料中出现的内容 |
| --- | --- |
| 玩家 | 位置、状态、当前工具或动作相关信息 |
| NPC | 角色位置和关系等环境信息 |
| 地点/地块 | 当前地点、tile 或周围可交互元素 |
| 游戏状态 | 时间、菜单、暂停或环境状态 |
| 农场/进度 | 农场对象、库存、资源和进展 |
| 图像 | 环境或屏幕图像 Observation |

StarDojo 的 Observation 既有结构化字段，也有图像数据；与 StardewMCP 的 JSON 状态序列化在数据通道和编码方式上不同。

### 两个项目的字段类别对照

| 类别 | StardewMCP | StarDojo | 资料中观察到的差异 |
| --- | --- | --- | --- |
| 玩家和位置 | JSON 状态字段 | Observation Space 字段 | 两者都记录玩家上下文，命名和层级不同 |
| 地图/周边 | 61×61 ASCII 和附近对象 | 地点、tile、对象及可能的图像 | StarDojo 同时包含图像通道 |
| 菜单 | 状态序列化内容 | Observation Space 内容 | 具体菜单字段和更新时机不同 |
| 任务/进度 | 任务、技能、关系 | 进度和任务环境字段 | StarDojo 任务对象与环境 evaluator 绑定更紧 |
| 传输编码 | WebSocket JSON | TCP 文本、共享内存、CBOR | 结构化文本和二进制数据分属不同通道 |

## Action 的形态

### StardewMCP

StardewMCP 的 [WebSocketServer](https://github.com/Hunter-Thompson/stardew-mcp/blob/3ca54bbfc1d446eeb06d822a74c92cd14df82b93/mod/StardewMCP/WebSocketServer.cs) 处理包含 `id`、`type`、`action` 和 `params` 的命令消息。外部工具使用的动作名称包括 `move_to`、`use_tool`、`interact`、`select_item` 以及进入门等。

这些动作描述的是目标或意图，按键、游戏 tick、动画等待和路径细节由 Mod 侧的 [CommandExecutor](https://github.com/Hunter-Thompson/stardew-mcp/blob/3ca54bbfc1d446eeb06d822a74c92cd14df82b93/mod/StardewMCP/CommandExecutor.cs) 处理。动作参数和成功条件由该项目代码解释，不是独立的通用 Action Schema。

### StarDojo

[Action Space 文档](https://github.com/StarDojo2025/stardojo/blob/e251401cf1e84ba07cbfa08283a7aba52290e578/docs/docs_src/action_space.md) 描述离散动作空间和动作到游戏命令的转换。Python 侧的 `convert_discrete_into_commands` 将固定长度的动作数组转换为移动、转向、功能键和其他命令；Mod 侧再通过 TCP 接收文本命令。

StarDojo 的动作表达更接近 Gym 环境的固定形状数组，而 StardewMCP 的工具动作更接近带名称和参数的高层命令。

### Action 形态对照

| 项目 | Action 外形 | 参数方式 | 执行位置 |
| --- | --- | --- | --- |
| StardewMCP | JSON 命令对象 | `action` 名称加 `params` | Mod 的命令执行器和游戏事件 |
| StarDojo | 固定长度离散数组，再转为字符串命令 | 数组位置和离散值 | Python 转换层、Mod TCP 处理层 |
| BotFramework | C# `Target`、`Action` 和行为队列 | 对象字段、目标类型、调用阶段 | 游戏内 Brain 和角色控制器 |
| Farmtronics | MiniScript API 调用 | `me` 对象及脚本参数 | 游戏内 Bot 更新循环 |
| Junimo-Kart-AI | 键盘输入 | 按键和时间/帧 | 屏幕捕获与输入系统 |

## Result 与状态反馈

### StardewMCP

StardewMCP 的响应消息包含请求 ID、消息类型、成功标志、消息文本和数据字段。`CommandExecutor` 处理异步移动、重复工具和蓄力工具操作；代码通过状态和回调把执行结果回传给 WebSocket 层。

因此，消息返回和游戏状态变化不是同一个时间点：外部客户端可能先收到命令响应，再收到后续状态广播。具体动作的成功判断依赖执行器代码和状态序列化结果。

### StarDojo

StarDojo 的 TCP 文本结果使用 `<EOF>` 分隔消息结束。动作调用、Observation 请求和环境初始化命令使用项目内部的字符串约定。任务 evaluator 读取环境 Observation，并根据任务定义计算结果；通信结果文本与任务成功判断是不同层次的反馈。

### 反馈层次对照

| 反馈层次 | StardewMCP | StarDojo |
| --- | --- | --- |
| 传输反馈 | WebSocket JSON 响应 | TCP 文本结果和 `<EOF>` |
| 动作反馈 | 命令执行器回调、成功字段和消息文本 | 命令返回和环境状态 |
| 状态反馈 | 定时或连接建立时的状态 JSON | Observation 请求、共享内存图像和结构化环境状态 |
| 任务反馈 | 外部工具或调用者处理 | 任务 evaluator 计算 |

## 动作粒度差异

| 粒度 | 参考项目中的例子 | 可观察优点 | 可观察限制 |
| --- | --- | --- | --- |
| 语义动作 | StardewMCP 的 `move_to`、`use_tool` | 参数表达目标，调用方不需要管理每个按键 | 执行器需要实现路径、前置条件和异步等待 |
| 目标对象 | BotFramework 的 Tile、Object、Character Target | 目标类型和行为阶段显式存在 | 对象模型与 C# 游戏内部类型联系紧密 |
| 固定离散数组 | StarDojo Action Space | 形状固定，便于 Gym/RL 环境处理 | 数组位置和离散值需要额外解释 |
| 脚本 API | Farmtronics 的 `me` | 游戏内程序可以组合多个操作 | 脚本运行环境与外部进程模型不同 |
| 原始输入 | Junimo-Kart-AI 键盘控制 | 与玩家输入路径接近 | 状态和动作受屏幕、帧率、输入延迟影响 |

## 字段和版本特征

从参考资料中可观察到以下版本化相关问题：

- StardewMCP 的 JSON 字段由 C# 序列化器和 Go 客户端共同解释；
- StarDojo 的动作数组长度、TCP 命令字符串和 CBOR/共享内存格式由 Mod 与 Python 环境共同解释；
- BotFramework、Farmtronics 和 Junimo-Kart-AI 的接口与各自运行时对象或输入方式绑定；
- 参考项目的提交号可以定位源码版本，但资料中没有一个跨项目通用的 `schema_version` 或 Action 版本协议；
- 名称、数组位置、C# 类型和显示文本在不同项目中承担了不同的标识作用。

## 资料限制

- 当前资料没有提供一个独立于 StardewMCP 或 StarDojo 的统一 Observation/Action/Result 标准。
- 静态源码无法完整说明动作在所有动画、菜单、地点切换和存档生命周期中的实际结果。
- 字段是否稳定、是否适合跨版本持久化，需要结合具体游戏版本和运行实验确认。
- 参考项目的动作名称和状态字段不能直接推断 Stardew Agent 的接口设计。
- 视觉 Observation、结构化 Observation 和原始按键输入的性能与延迟差异，需要运行时测量，当前报告不作排序。
