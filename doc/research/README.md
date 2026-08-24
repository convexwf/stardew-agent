# Stardew Agent 调研资料

## 文档信息

| 项目         | 内容 |
| ------------ | ---- |
| **文档标题** | Stardew Agent 调研资料索引 |
| **文档版本** | v0.5 |
| **创建日期** | 2026-08-23 |
| **更新日期** | 2026-08-25 |
| **文档作者** | 项目维护者 |
| **文档类型** | 技术调研索引 |
| **参考资料** | 参考项目的上游网页链接，见[参考项目](#参考项目) |

## 目录

- [目标](#目标)
- [阅读顺序](#阅读顺序)
- [报告清单](#报告清单)
- [调研方法](#调研方法)
- [参考项目](#参考项目)
- [文档维护约定](#文档维护约定)

## 目标

本目录用于记录 Stardew Agent 正式开发前的资料型调研，重点记录以下内容：

- 现有项目如何与 Stardew Valley 通信；
- 不同项目如何表示游戏状态、动作和结果；
- 动作执行、寻路、任务循环和评测在参考项目中的实现形态；
- LLM、规则代码、搜索算法和强化学习在参考项目中的职责；
- 参考项目的开发阶段、依赖关系和已知限制。

这些文档面向刚加入项目的读者。每篇报告只陈述资料来源、事实观察、横向差异、已知限制和待验证事实，不替 Stardew Agent 选择技术方案，也不规定实现步骤。

## 阅读顺序

文件名不使用数字前缀，阅读顺序在这里统一维护：

1. [`communication-architecture.md`](communication-architecture.md)：查看参考项目中的游戏进程、Mod 和外部进程通信方式。
2. [`reference-projects/README.md`](reference-projects/README.md)：进入按项目拆分的详细档案，先阅读最接近 Companion 控制链路的 StardewValley-MCP。
3. [`existing-projects.md`](existing-projects.md)：查看各参考项目的组件、接口和运行边界总览。
4. [`observation-action-contract.md`](observation-action-contract.md)：比较不同项目中的 Observation、Action 和 Result 表达方式。
5. [`action-execution-and-pathfinding.md`](action-execution-and-pathfinding.md)：查看动作队列、寻路、等待、校验和失败处理的实际实现。
6. [`agent-loop-and-evaluation.md`](agent-loop-and-evaluation.md)：查看任务循环、任务配置、轨迹记录和评测指标的实际实现。
7. [`llm-and-rl-boundary.md`](llm-and-rl-boundary.md)：查看参考项目中 LLM、规则、搜索和 RL 的使用边界与实验条件。
8. [`development-stage-observations.md`](development-stage-observations.md)：查看参考项目公开资料中的开发阶段和依赖，不形成 Stardew Agent 路线。

项目档案索引提供单项目事实背景；其后总览和主题报告分别记录项目矩阵、状态、动作、执行、评测和模型资料；最后一篇只整理参考项目公开的阶段性信息。

## 报告清单

| 文件 | 调研主题 | 内容范围 | 状态 |
| ---- | -------- | -------- | ---- |
| [`communication-architecture.md`](communication-architecture.md) | 游戏通信实现 | StardewMCP、StarDojo 的链路、协议和传输特征 | 已完成 |
| [`reference-projects/README.md`](reference-projects/README.md) | 单项目详细档案 | 按项目记录固定版本、架构、通信、控制对象和资料限制 | 首篇完成，持续补充 |
| [`existing-projects.md`](existing-projects.md) | 参考项目实现 | 组件、运行方式、接口和限制对比 | 已完成 |
| [`observation-action-contract.md`](observation-action-contract.md) | 状态与动作表达 | Observation、Action、Result 的字段和粒度差异 | 已完成 |
| [`action-execution-and-pathfinding.md`](action-execution-and-pathfinding.md) | 动作执行实现 | 执行队列、寻路、等待、校验和失败处理 | 已完成 |
| [`agent-loop-and-evaluation.md`](agent-loop-and-evaluation.md) | 任务与评测实现 | 任务模型、轨迹、指标和复现条件 | 已完成 |
| [`llm-and-rl-boundary.md`](llm-and-rl-boundary.md) | 模型与策略实验 | LLM、规则、搜索和 RL 的实际使用方式 | 已完成 |
| [`development-stage-observations.md`](development-stage-observations.md) | 阶段性信息 | 参考项目公开资料中的开发阶段、依赖和未解决问题 | 已完成 |

## 调研方法

每篇报告按以下顺序组织内容：

1. **调研问题**：明确本篇记录的事实范围。
2. **资料范围**：列出参考项目、分支、提交号、关键文件和官方文档。
3. **事实观察**：只记录可以从代码、配置、运行结果或文档中直接验证的内容。
4. **方案比较**：并列说明不同实现的特征、优点、缺点和适用前提。
5. **资料限制**：记录资料没有覆盖或无法从源码确认的内容。
6. **待验证事实**：列出需要通过编译、运行游戏或实验确认的事实，不写实现安排。

报告中应区分“参考项目已经这样做”和“资料无法确认的内容”。不得写“Stardew Agent 建议这样做”、复用清单、技术选择、实施步骤或验收标准。参考项目的实现不代表 Stardew Agent 的设计。

涉及流程、组件关系或交互时优先使用 Mermaid。涉及方案差异时使用对比表，只记录资料中的差异，不给出推荐。

## 参考项目

参考项目不作为 Stardew Agent 的运行时依赖。文档只引用公开的上游项目页面，不引用本地工作副本、绝对路径或仓库外目录，这样项目发布到 GitHub 后仍然可以正常跳转。

| 项目 | 参考位置 | 调研重点 |
| ---- | -------- | -------- |
| [StardewMCP](https://github.com/Hunter-Thompson/stardew-mcp) | 上游仓库 | SMAPI Mod、WebSocket、JSON 协议、工具封装和 Agent 循环 |
| [StarDojo](https://github.com/StarDojo2025/stardojo) | 上游仓库 | Observation/Action Space、任务定义、环境封装和评测 |
| [Stardew Valley BotFramework](https://github.com/andyruwruw/stardew-valley-bot-framework) | 上游仓库 | Action、Target、Brain、路线规划和寻路 |
| [Farmtronics](https://github.com/JoeStrout/Farmtronics) | 上游仓库 | 游戏内可编程 Bot、角色控制 API 和工具使用 |
| [SMAPI](https://github.com/Pathoschild/SMAPI) | 上游仓库 | Mod 加载、事件、API 和构建方式 |
| [StardewMods](https://github.com/Pathoschild/StardewMods) | 上游仓库 | 成熟 Mod 的工程组织、事件使用和兼容性处理 |
| [SMAPI Dedicated Server Mod](https://github.com/ObjectManagerManager/SMAPIDedicatedServerMod) | 上游仓库 | 自动化运行、无人值守和服务端模式 |
| [StardewVLA](https://github.com/shengyongniu/stardew-vla) | 上游仓库 | 受控 2D 环境、行为克隆和 PPO |
| [Junimo-Kart-AI](https://github.com/Cabbagito/Junimo-Kart-AI) | 上游仓库 | 视觉输入、游戏操作和 DQN 实验 |

当前最重要的两个实现参照是 StardewMCP 和 StarDojo：前者更接近真实游戏控制的工程链路，后者更适合研究任务、观察空间和评测方法。BotFramework 和 Farmtronics 用于补充动作执行与角色控制方面的细节。

## 文档维护约定

- 文件名使用描述性名称，不通过数字前缀表达顺序。
- 阅读顺序、报告状态和新增主题统一维护在本 README 中。
- 外部参考资料必须使用公开网页链接或项目名称；需要定位到源码时使用 GitHub 的文件链接、分支或提交链接。
- 参考项目发生更新时，在报告中记录实际使用的版本、分支或提交号，不只记录仓库地址。
- 不要在已提交文档中写入本地工作目录、绝对路径、`file://` 链接或其他只在本地有效的路径。
- 报告正文只保留有来源的事实、明确标注的分析和资料限制；不写项目当前选择或否决方案。
- 每篇报告完成后更新本 README 的“报告清单”状态和更新时间。
- 任何事实如果尚未通过本地编译、启动游戏或实验验证，应明确标记为“待验证”，不得把它写成实现结论。
