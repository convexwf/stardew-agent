# Stardew Agent 调研资料

## 文档信息

| 项目         | 内容 |
| ------------ | ---- |
| **文档标题** | Stardew Agent 调研资料索引 |
| **文档版本** | v0.2 |
| **创建日期** | 2026-08-23 |
| **更新日期** | 2026-08-23 |
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

本目录用于记录 Stardew Agent 正式开发前的实现型调研，重点回答以下问题：

- Agent 如何与 Stardew Valley 建立稳定、可验证的通信链路；
- 游戏状态和可执行动作应该如何建模；
- 现有项目分别解决了哪些问题，哪些设计可以复用；
- LLM、确定性执行器、寻路算法和强化学习应该如何分工；
- 如何把这些结论收敛成一个可以逐步验证的 MVP。

这些文档面向刚加入项目的读者。每篇报告应直接说明当前发现、当前建议、约束和待验证问题，不要求读者了解此前的讨论过程。

## 阅读顺序

文件名不使用数字前缀，阅读顺序在这里统一维护：

1. `communication-architecture.md`：先理解游戏、Mod、外部 Agent 和 LLM 之间的分层通信链路。
2. `existing-projects.md`：再比较参考项目的架构、状态、动作、通信和评测方式。
3. `observation-action-contract.md`：根据前两篇的结论定义自己的 Observation、Action 和结果协议。
4. `action-execution-and-pathfinding.md`：研究动作执行、寻路、动画等待、失败重试和结果校验。
5. `agent-loop-and-evaluation.md`：定义 Observe、Plan、Act、Verify 闭环以及任务评测指标。
6. `llm-and-rl-boundary.md`：确定 LLM、规则代码和强化学习的职责边界。
7. `mvp-roadmap.md`：将调研结论整理成分阶段、可验收的实现路线。

前两篇是架构调研的入口；第三、第四篇会直接影响第一版 Mod 的接口设计；最后三篇用于控制项目范围和后续实验节奏。

## 报告清单

| 文件 | 主要问题 | 预期产出 | 状态 |
| ---- | -------- | -------- | ---- |
| `communication-architecture.md` | 游戏进程和外部 Agent 如何通信？MCP 在哪一层？ | 通信分层、时序图、协议对比和 MVP 推荐方案 | 待编写 |
| `existing-projects.md` | StarDojo、StardewMCP、BotFramework、Farmtronics 等项目分别做了什么？ | 项目对比矩阵、可复用设计、差异和风险 | 待编写 |
| `observation-action-contract.md` | Agent 看见什么、能做什么、如何确认动作结果？ | Observation、Action、Result 的初版数据契约 | 待编写 |
| `action-execution-and-pathfinding.md` | 高层动作如何落到底层游戏操作？ | 执行器、寻路、冷却、重试和校验方案 | 待编写 |
| `agent-loop-and-evaluation.md` | 如何稳定运行和衡量一个 Agent？ | Agent 闭环、任务集、轨迹记录和评测指标 | 待编写 |
| `llm-and-rl-boundary.md` | LLM 和强化学习分别应该解决什么问题？ | 分层职责、训练边界和实验建议 | 待编写 |
| `mvp-roadmap.md` | 调研结果如何转成工程步骤？ | 分阶段目标、验收标准和依赖关系 | 待编写 |

## 调研方法

每篇报告按以下顺序组织内容：

1. **调研问题**：明确本篇要解决的工程问题和非目标。
2. **资料范围**：列出参考项目、分支、提交号、关键文件和官方文档。
3. **事实观察**：只记录可以从代码、配置、运行结果或文档中直接验证的内容。
4. **方案比较**：说明各方案的优点、缺点、适用条件和已知风险。
5. **当前建议**：给出 Stardew Agent 当前采用或暂不采用的方案，并说明依据。
6. **待验证问题**：列出需要通过编译、运行游戏或实验才能确认的内容。
7. **对实现的影响**：明确下一步应该创建哪些接口、测试或实验。

报告中应区分“参考项目已经这样做”和“Stardew Agent 建议这样做”。参考项目的实现可以借鉴，但不默认适合直接复制；尤其需要单独检查游戏版本、SMAPI 版本、运行平台、许可证和维护状态。

涉及流程、组件关系或交互时优先使用 Mermaid。涉及方案选择时使用对比表，并在结尾给出明确推荐，不只罗列资料。

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
- 主报告只保留仍然有效的当前结论；完整的否决方案和过程性讨论放到单独的方案对比或外部 Journal。
- 每篇报告完成后更新本 README 的“报告清单”状态和更新时间。
- 任何结论如果尚未在本地编译、启动游戏或实验中验证，应明确标记为“待验证”。
