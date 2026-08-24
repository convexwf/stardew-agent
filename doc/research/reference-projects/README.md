# 参考项目档案

## 文档信息

| 项目         | 内容 |
| ------------ | ---- |
| **文档标题** | Stardew Agent 参考项目档案索引 |
| **文档版本** | v0.1 |
| **创建日期** | 2026-08-25 |
| **更新日期** | 2026-08-25 |
| **文档作者** | 项目维护者 |
| **文档类型** | 参考项目资料索引 |
| **参考资料** | 各项目档案中的固定提交链接 |

## 目录

- [目标](#目标)
- [阅读顺序](#阅读顺序)
- [项目状态](#项目状态)
- [统一记录范围](#统一记录范围)

## 目标

本目录按项目分别记录参考仓库的公开实现。每篇文档固定一个参考提交，描述项目定位、组件、游戏通信、状态与动作、运行方式、源码入口和资料限制。这里的项目档案用于支撑 `doc/research/` 下的横向主题调研，不替 Stardew Agent 做技术选择。

## 阅读顺序

项目档案按与真实 Stardew Valley 控制链路的接近程度以及抽象层次排列：

1. [StardewValley-MCP](stardewvalley-mcp.md)：Companion、Shadow Farmer、Player Mode 和 JSON 文件 Bridge。
2. [StardewMCP](stardew-mcp.md)：真实游戏 Mod、WebSocket、Go 客户端、状态序列化和寻路。
3. [StarDojo](stardojo.md)：环境封装、Observation/Action、任务和评测。
4. Stardew Valley BotFramework：Target、Action、Brain 和游戏内行为调度。
5. Farmtronics：游戏内可编程 Bot、脚本 API 和 Bot 状态。
6. SMAPI：Mod 加载、事件、API、构建和运行基础。

尚未出现链接的项目档案尚未撰写；完成一篇后再把链接加入本 README。

## 项目状态

| 项目 | 文件 | 固定参考提交 | 状态 |
| ---- | ---- | ------------ | ---- |
| StardewValley-MCP | [stardewvalley-mcp.md](stardewvalley-mcp.md) | `6cb2ffa` | 已完成 |
| StardewMCP | [stardew-mcp.md](stardew-mcp.md) | `3ca54bb` | 已完成 |
| StarDojo | [stardojo.md](stardojo.md) | `e251401c` | 已完成 |
| Stardew Valley BotFramework | `stardew-valley-bot-framework.md` | `252d2849` | 待撰写 |
| Farmtronics | `farmtronics.md` | `a59fc65b` | 待撰写 |
| SMAPI | `smapi.md` | `79f9bbbe` | 待撰写 |

## 统一记录范围

每篇项目档案尽量覆盖以下内容：

- 项目公开声明的目标、运行平台和依赖；
- 固定提交对应的目录、入口类、服务进程和游戏内组件；
- 游戏进程、Mod、外部进程和模型/工具层之间的通信链路；
- Observation、Action、Result 或等价状态表达；
- 控制对象、角色表示、执行线程和生命周期；
- 构建、启动、配置和运行时前置条件；
- 代码中可以确认的特征、限制和未覆盖问题。

“创新点”只在项目自身文档或源码明确表达时作为项目声明记录；否则使用“实现特征”描述可观察差异。档案不写 Stardew Agent 的采用、否决、路线或实施计划。
