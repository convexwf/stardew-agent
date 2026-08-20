# Stardew Agent 参考项目调研

## 文档信息

| 项目         | 内容 |
| ------------ | ---- |
| **文档标题** | Stardew Agent 参考项目调研 |
| **文档版本** | v0.2 |
| **创建日期** | 2026-08-23 |
| **更新日期** | 2026-08-23 |
| **文档作者** | 项目维护者 |
| **文档类型** | 资料调研报告 |
| **参考资料** | 各参考项目固定提交链接，见[参考实现](#参考实现) |

## 目录

- [调研范围](#调研范围)
- [比较维度](#比较维度)
- [项目分析](#项目分析)
- [实现特征对照](#实现特征对照)
- [资料限制](#资料限制)
- [参考实现](#参考实现)

## 调研范围

本报告记录 StardewMCP、StarDojo、Stardew Valley BotFramework、Farmtronics、SMAPI、StardewMods、SMAPI Dedicated Server Mod、StardewVLA 和 Junimo-Kart-AI 的公开实现特征。项目覆盖真实游戏 Mod、外部控制、游戏内可编程角色、Mod 工程基础、受控环境和视觉小游戏实验等不同层次。

报告只描述项目已有的组件、接口、运行方式和限制，不把多个项目拼接成 Stardew Agent 的设计，也不列出项目复用或采用清单。

## 比较维度

| 维度 | 观察内容 |
| --- | --- |
| 游戏接入 | 是否通过 SMAPI、游戏内 API、屏幕捕获或独立模拟环境接入 |
| 外部通信 | WebSocket、TCP、共享内存、键盘输入或无外部通信 |
| Observation | 状态字段、局部地图、图像或像素输入 |
| Action | 语义动作、Target/Action 对象、离散数组或按键 |
| 执行位置 | 游戏线程、外部进程、策略环境或输入系统 |
| 任务与评测 | 是否有任务初始化、evaluator、训练环境或回放 |
| 工程形态 | Mod、Go/Python 服务、库、独立环境或小游戏脚本 |
| 资料限制 | 代码、文档和当前固定提交中无法确认的内容 |

## 项目分析

### StardewMCP：真实游戏与外部工具调用

[StardewMCP](https://github.com/Hunter-Thompson/stardew-mcp/tree/3ca54bbfc1d446eeb06d822a74c92cd14df82b93) 将系统分为 C# SMAPI Mod 和 Go MCP Server。Mod 通过 WebSocket 暴露游戏状态和动作，Go 服务维护连接、状态缓存、请求响应关联和模型工具。工具包括 `move_to`、`get_surroundings`、`interact`、`use_tool`、`select_item` 和进入门等。

Mod 侧的 [GameStateSerializer](https://github.com/Hunter-Thompson/stardew-mcp/blob/3ca54bbfc1d446eeb06d822a74c92cd14df82b93/mod/StardewMCP/GameStateSerializer.cs) 序列化玩家、时间、地点、附近对象、地图、任务、关系和技能。[CommandExecutor](https://github.com/Hunter-Thompson/stardew-mcp/blob/3ca54bbfc1d446eeb06d822a74c92cd14df82b93/mod/StardewMCP/CommandExecutor.cs) 使用命令队列，并在游戏 tick 中执行移动、工具和交互操作。[Pathfinder](https://github.com/Hunter-Thompson/stardew-mcp/blob/3ca54bbfc1d446eeb06d822a74c92cd14df82b93/mod/StardewMCP/Pathfinder.cs) 使用 tile 图和 A* 搜索。

代码中可以观察到的运行约束包括：WebSocket 地址为 `localhost:8765/game`；状态在连接建立和定时事件中发送；命令和响应包含请求 ID；动作执行和状态序列化分别由不同组件负责。当前固定提交的 README 和源码没有给出完整的长期任务评测框架。

### StarDojo：环境、任务和评测

[StarDojo](https://github.com/StarDojo2025/stardojo/tree/e251401cf1e84ba07cbfa08283a7aba52290e578) 同时包含 `StardojoMod`、Python 环境、任务定义和 Agent Pipeline。Mod 使用回环 TCP 接收命令，文本消息使用 `%` 分隔参数和 `<EOF>` 结束；图像或二进制 Observation 使用 Memory-Mapped File 和 CBOR。

StarDojo 的文档描述了 Observation Space、Action Space、环境状态、任务初始化和 evaluator。任务对象包含存档、初始化命令、难度和评估函数等信息。Agent Pipeline 代码包含模型提供者、技能注册、记忆、规划、任务推断、自我反思和日志等组件。

该项目同时暴露游戏状态和视觉 Observation，支持并行环境、任务输出和视频记录。网络协议、共享内存命名、环境启动参数和任务初始化之间存在项目内部依赖，具体跨平台行为需要结合运行环境确认。

### Stardew Valley BotFramework：Target、Action 和 Brain

[Stardew Valley BotFramework](https://github.com/andyruwruw/stardew-valley-bot-framework/tree/252d28496c545749497d46869d0f0bb7369e2c4d) 将 Bot 行为组织成 `Target`、`Action`、`Brain`、`WorldParser` 和角色控制器。Target 类型包括 Tile、Object 和 Character，并记录查询行为、调用顺序、前置条件、可操作距离和动作。

[Brain.cs](https://github.com/andyruwruw/stardew-valley-bot-framework/blob/252d28496c545749497d46869d0f0bb7369e2c4d/BotFramework/Brain.cs) 根据目标和地点生成动作队列。动作在 `BeforeEach`、`AtLocationStart`、目标位置和 `AfterEach` 等阶段执行。源码重点在游戏内行为调度、目标查询和路线处理，没有看到与外部 LLM 或跨进程 JSON 协议对应的主要组件。

### Farmtronics：游戏内可编程 Bot

[Farmtronics](https://github.com/JoeStrout/Farmtronics/tree/a59fc65bdb263d257d0ecd453202b65c6269f7a5) 在游戏内提供 Home Computer 和可编程 Bot。Bot 具有自己的 Farmer 表现、位置、背包、工具选择和更新循环；MiniScript 通过 `me` 对象访问位置、方向、库存和工具使用能力。

[BotObject.cs](https://github.com/JoeStrout/Farmtronics/blob/a59fc65bdb263d257d0ecd453202b65c6269f7a5/Farmtronics/Bot/BotObject.cs) 和相关 Bot 类还涉及保存、加载、Bot 与箱子的转换及多玩家状态。该项目的控制者在游戏内脚本环境中，不是独立的外部 Agent Runtime。

### SMAPI 与 StardewMods：Mod 工程基础

[SMAPI](https://github.com/Pathoschild/SMAPI/tree/79f9bbbe3edbb7ca3369e7ad0d3dd45131b34fc0) 提供 Mod 加载、事件、API、日志、配置和兼容性基础。[Mod package 文档](https://github.com/Pathoschild/SMAPI/blob/79f9bbbe3edbb7ca3369e7ad0d3dd45131b34fc0/docs/technical/mod-package.md)描述了构建、部署和发布包的结构。

[StardewMods](https://github.com/Pathoschild/StardewMods/tree/76565e83ede4bc8b3c293f1c659032ba9c39c213) 是多个实际 Mod 的集合，包含事件订阅、配置、资源修改、内容 API 和兼容性处理等代码。它不提供统一的 Agent 通信协议或任务评测框架。

### SMAPI Dedicated Server Mod：自动化运行形态

[SMAPI Dedicated Server Mod](https://github.com/ObjectManagerManager/SMAPIDedicatedServerMod/tree/4975be17118646b0bce81d2dffb4174aa160412b) 关注自动启动、无人值守或服务端运行相关的 Mod 处理。它与 StardewMCP、StarDojo 的主要差异在于运行管理和游戏生命周期，不是 Observation/Action 或模型策略接口。

### StardewVLA：独立 2D 环境中的行为学习

[StardewVLA](https://github.com/shengyongniu/stardew-vla/tree/3e14d6c859b2ab4aea631e695fd69049e92f9646) 是独立的 2D FarmWorld，不是通过 SMAPI 控制真实 Stardew Valley。项目说明使用图像和语言指令作为输入，先用脚本专家生成行为克隆数据，再用 PPO 进行策略优化，并通过随机地图和保留测试观察泛化。

### Junimo-Kart-AI：视觉输入与小游戏控制

[Junimo-Kart-AI](https://github.com/Cabbagito/Junimo-Kart-AI/tree/a7d7eea83edcfc247e63ecda5fb354fb7e797188) 通过屏幕捕获、数字识别和键盘输入控制 Junimo Kart 小游戏，并包含 CNN/DQN 类实验代码。它的状态频率、动作空间和奖励条件围绕小游戏设计，不包含完整农场环境的通信或任务系统。

## 实现特征对照

| 项目 | 游戏接入 | 通信/输入 | Observation | Action/控制 | 任务/评测 |
| --- | --- | --- | --- | --- | --- |
| StardewMCP | SMAPI Mod | WebSocket + JSON | 结构化状态、局部地图摘要 | `move_to`、工具、交互等高层命令 | 外部工具调用，源码中未见与 StarDojo 同等的任务 evaluator |
| StarDojo | SMAPI Mod + Python 环境 | TCP 文本 + 共享内存 | 结构化状态和图像 | 离散动作数组转换为命令 | 任务配置、初始化、evaluator、并行环境 |
| BotFramework | 游戏内 Bot | 无本报告范围内的外部通信 | WorldParser 和目标查询 | Target、Action、Brain | 行为调度，不是模型评测环境 |
| Farmtronics | 游戏内 Bot | MiniScript | Bot Farmer 状态 | 脚本 API、移动、工具 | 游戏内程序运行 |
| SMAPI | Mod 加载层 | Mod API 和事件 | 由具体 Mod 决定 | 由具体 Mod 决定 | 不提供 Agent 任务 |
| StardewMods | 多个 SMAPI Mod | 由具体 Mod 决定 | 由具体 Mod 决定 | 由具体 Mod 决定 | 不提供统一评测 |
| Dedicated Server Mod | SMAPI Mod | 服务端/生命周期管理 | 由具体游戏和 Mod 决定 | 由具体游戏和 Mod 决定 | 自动化运行相关 |
| StardewVLA | 独立 2D 环境 | 环境内部调用 | 像素和指令 | 学习策略动作 | 行为克隆、PPO、泛化测试 |
| Junimo-Kart-AI | 屏幕与键盘 | 屏幕捕获 + 键盘 | 图像/数字识别 | 键盘动作 | 小游戏训练与测试 |

## 资料限制

- 固定提交只代表对应时间点的代码，不代表项目当前主分支状态。
- 仓库 README、源码和文档对运行平台、游戏版本、SMAPI 版本及依赖的描述完整程度不同。
- “支持任务评测”只表示代码或文档中出现任务/evaluator 相关组件，不代表评测结果已经在本地复现。
- 参考项目中的类名、工具名和字段名属于各项目内部接口，不能直接视为跨项目标准。
- 许可证、维护状态和依赖版本需要以各项目当前公开页面为准，本报告不作法律或维护状态判断。
- 多实例运行、连接断开、读档、地点切换和动作中断等边界行为，不能仅凭静态代码完整确认。

## 参考实现

- [StardewMCP README](https://github.com/Hunter-Thompson/stardew-mcp/blob/3ca54bbfc1d446eeb06d822a74c92cd14df82b93/README.md)
- [StardewMCP CommandExecutor.cs](https://github.com/Hunter-Thompson/stardew-mcp/blob/3ca54bbfc1d446eeb06d822a74c92cd14df82b93/mod/StardewMCP/CommandExecutor.cs)
- [StardewMCP GameStateSerializer.cs](https://github.com/Hunter-Thompson/stardew-mcp/blob/3ca54bbfc1d446eeb06d822a74c92cd14df82b93/mod/StardewMCP/GameStateSerializer.cs)
- [StarDojo README](https://github.com/StarDojo2025/stardojo/blob/e251401cf1e84ba07cbfa08283a7aba52290e578/README.md)
- [StarDojo Action Space](https://github.com/StarDojo2025/stardojo/blob/e251401cf1e84ba07cbfa08283a7aba52290e578/docs/docs_src/action_space.md)
- [StarDojo Observation Space](https://github.com/StarDojo2025/stardojo/blob/e251401cf1e84ba07cbfa08283a7aba52290e578/docs/docs_src/observation_space.md)
- [BotFramework Usage](https://github.com/andyruwruw/stardew-valley-bot-framework/blob/252d28496c545749497d46869d0f0bb7369e2c4d/documentation/USAGE.md)
- [BotFramework Brain.cs](https://github.com/andyruwruw/stardew-valley-bot-framework/blob/252d28496c545749497d46869d0f0bb7369e2c4d/BotFramework/Brain.cs)
- [Farmtronics README](https://github.com/JoeStrout/Farmtronics/blob/a59fc65bdb263d257d0ecd453202b65c6269f7a5/README.md)
- [StardewVLA README](https://github.com/shengyongniu/stardew-vla/blob/3e14d6c859b2ab4aea631e695fd69049e92f9646/README.md)
