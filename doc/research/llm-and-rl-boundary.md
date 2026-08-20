# Stardew Agent 中 LLM、规则与强化学习的使用形态调研

## 文档信息

| 项目         | 内容 |
| ------------ | ---- |
| **文档标题** | Stardew Agent 中 LLM、规则与强化学习的使用形态调研 |
| **文档版本** | v0.2 |
| **创建日期** | 2026-08-23 |
| **更新日期** | 2026-08-23 |
| **文档作者** | 项目维护者 |
| **文档类型** | 资料调研报告 |
| **参考资料** | [StardewVLA](https://github.com/shengyongniu/stardew-vla/tree/3e14d6c859b2ab4aea631e695fd69049e92f9646)、[Junimo-Kart-AI](https://github.com/Cabbagito/Junimo-Kart-AI/tree/a7d7eea83edcfc247e63ecda5fb354fb7e797188)、[StarDojo](https://github.com/StarDojo2025/stardojo/tree/e251401cf1e84ba07cbfa08283a7aba52290e578)、[StardewMCP](https://github.com/Hunter-Thompson/stardew-mcp/tree/3ca54bbfc1d446eeb06d822a74c92cd14df82b93) |

## 目录

- [调研范围](#调研范围)
- [参考项目中的策略组件](#参考项目中的策略组件)
- [不同问题的输入与输出](#不同问题的输入与输出)
- [StardewVLA 的训练实验](#stardewvla-的训练实验)
- [Junimo-Kart-AI 的视觉控制实验](#junimo-kart-ai-的视觉控制实验)
- [LLM、规则和 RL 的资料对照](#llm规则和-rl-的资料对照)
- [资料限制](#资料限制)

## 调研范围

本报告记录公开参考项目中 LLM、工具调用、规则代码、搜索算法、行为克隆和强化学习的实际位置及实验条件。报告比较不同策略的输入、输出和环境边界，不从这些资料推出 Stardew Agent 的模型选择或训练路线。

## 参考项目中的策略组件

### StardewMCP

StardewMCP 包含 C# SMAPI Mod、Go MCP Server、游戏状态序列化、命令执行器和外部工具。Go 服务将 `move_to`、`use_tool`、`interact` 等能力包装成工具调用接口；Mod 侧负责状态读取和动作执行。固定提交的公开代码可以确认工具层和游戏通信层，但不能仅凭这些文件确认某个 LLM 的训练方法或长期规划效果。

### StarDojo

StarDojo 的 Agent Pipeline 文档和代码包含模型提供者、技能注册、记忆、规划、任务推断、自我反思、日志和任务环境。其 Mod、Python 环境、Observation Space、Action Space 和 evaluator 共同构成模型运行环境。资料显示多种 Agent 组件存在，但没有形成一个与所有任务共享的单一策略接口。

### StardewVLA

[StardewVLA](https://github.com/shengyongniu/stardew-vla/tree/3e14d6c859b2ab4aea631e695fd69049e92f9646) 是独立的 2D FarmWorld。项目描述以图像和语言指令作为输入，使用脚本专家产生行为克隆数据，再使用 PPO 优化策略，并使用程序化地图和保留测试观察泛化。

该环境不是通过 SMAPI 控制真实 Stardew Valley 的 Mod，因此其状态、动作、奖励、重置和游戏时间语义与真实游戏通信链路不同。

### Junimo-Kart-AI

[Junimo-Kart-AI](https://github.com/Cabbagito/Junimo-Kart-AI/tree/a7d7eea83edcfc247e63ecda5fb354fb7e797188) 聚焦 Junimo Kart 小游戏，通过屏幕捕获、数字识别和键盘输入完成控制实验。代码包含 CNN/DQN 类模型或训练实验。它的状态是图像或数字识别结果，动作是键盘输入，奖励和终止条件围绕小游戏分数与失败状态。

## 不同问题的输入与输出

| 问题/组件 | 输入 | 输出 | 资料中的实现位置 |
| --- | --- | --- | --- |
| 游戏状态读取 | 游戏对象、GameLoop 事件 | JSON 或结构化 Observation | StardewMCP、StarDojo Mod |
| 工具调用 | 外部指令、状态缓存、模型请求 | 带名称和参数的命令 | StardewMCP Go MCP Server |
| 任务推断/规划 | 指令、Observation、任务配置、记忆 | 技能或动作序列 | StarDojo Agent Pipeline |
| 规则执行 | Action、目标、游戏状态 | 游戏 API、方向或工具输入 | StardewMCP、BotFramework |
| 路径搜索 | 起点、目标、可通行 tile | 路径或邻接动作 | StardewMCP Pathfinder、BotFramework |
| 行为克隆 | 专家轨迹、图像/指令 | 策略动作 | StardewVLA |
| PPO 训练 | 环境 Observation、奖励、轨迹 | 策略参数 | StardewVLA |
| 视觉小游戏控制 | 图像帧、历史输入 | 键盘动作 | Junimo-Kart-AI |

这些组件的输入输出类型、反馈频率和重置方式不同。表格中的“规则执行”表示代码中的确定性处理，不表示任何项目的统一架构。

## StardewVLA 的训练实验

### 环境

StardewVLA 使用独立的 2D FarmWorld，项目资料中可以看到农场地图、角色、作物和任务相关环境。该环境允许生成或变化地图，并把图像和语言指令输入策略。

### 训练阶段

项目说明了两个阶段：

1. 使用脚本专家产生行为轨迹，进行行为克隆；
2. 在环境中使用 PPO 对策略进行优化。

实验还包含程序化地图和保留测试，用于观察策略在未见布局上的表现。资料没有把这些训练结果等同于真实 Stardew Valley 的运行结果。

### 与真实游戏控制的差异

| 方面 | StardewVLA | 真实游戏 Mod 通信资料 |
| --- | --- | --- |
| 环境 | 项目自带 2D 环境 | Stardew Valley 游戏进程 |
| 状态 | 环境生成的图像、实体和任务状态 | SMAPI 读取的游戏对象或图像 |
| 动作 | 训练环境动作空间 | WebSocket/TCP 命令、工具 API 或输入 |
| 奖励 | 训练环境定义 | 参考通信项目中未见统一 RL 奖励 |
| 重置 | 环境代码控制 | 存档、游戏生命周期和 Mod 状态 |
| 随机性 | 程序化地图和环境随机性 | 游戏时间、NPC、存档和运行时事件 |

## Junimo-Kart-AI 的视觉控制实验

Junimo-Kart-AI 通过屏幕获取游戏画面，再用识别模型或图像处理得到控制所需状态，并向游戏窗口发送键盘输入。训练问题受到屏幕捕获帧率、输入延迟、窗口状态和小游戏奖励的影响。

该项目没有展示完整农场世界的对象状态、任务初始化、地点切换和 SMAPI API 调用。它代表一种视觉输入和原始输入控制链路，与 StardewMCP 的结构化状态和语义命令链路不同。

## LLM、规则和 RL 的资料对照

| 方法 | 参考项目中的职责 | 可观察优点 | 可观察限制 |
| --- | --- | --- | --- |
| LLM/模型工具 | StarDojo Agent Pipeline、StardewMCP 外部工具层 | 能处理文本指令、工具选择、任务推断或记忆 | 资料没有给出跨任务稳定性、成本和错误率的统一数据 |
| 规则代码 | Mod 生命周期、参数处理、前置条件、游戏事件和 Bot 行为 | 逻辑可从源码追踪，直接连接游戏状态 | 依赖具体游戏对象、地图和版本，开放式目标处理较少 |
| 搜索算法 | StardewMCP Pathfinder、BotFramework 路径和目标查询 | 输出路径或目标关系，可检查搜索约束 | 依赖地图和可通行判断，动态状态处理需要额外代码 |
| 行为克隆 | StardewVLA 脚本专家轨迹 | 能从示范轨迹初始化策略 | 轨迹覆盖范围和专家质量影响策略，依赖受控环境 |
| 强化学习 | StardewVLA PPO、Junimo-Kart-AI 的训练实验 | 可根据奖励优化策略 | 奖励、重置、样本成本和训练/真实差异需要单独处理 |
| 原始视觉控制 | Junimo-Kart-AI | 不依赖结构化游戏 API | 受图像、帧率、输入延迟和识别误差影响 |

表格仅并列资料中出现的职责和限制，不产生技术排序。

## 资料限制

- StardewMCP 的公开固定提交可以确认外部工具和 Mod 通信，但不能由静态代码确认 LLM Planner 的实际评测结果。
- StarDojo 的 Agent Pipeline 包含多个模型相关组件，但不同任务、配置和模型提供者的行为需要实验数据才能比较。
- StardewVLA 的训练结果来自独立环境，不能直接解释真实游戏中的 SMAPI、存档和网络时序。
- Junimo-Kart-AI 的小游戏输入、奖励和终止条件不能直接代表完整 Stardew Valley 任务。
- 参考资料没有提供统一的 LLM、规则、搜索和 RL 基准任务、数据集、随机种子和报告格式。
- 训练成本、推理延迟、模型错误率、动作安全性和长期任务表现需要在具体环境中实测。
- 本报告不从资料差异推导模型职责边界、训练顺序或 MVP 范围。
