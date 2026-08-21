# Stardew Agent Bridge Mod

这是游戏内的 SMAPI 适配层。它读取 CLI 写入的 action request，访问游戏运行时状态，并把 action result 和 snapshot 写回 Bridge 目录。

## 本地运行配置

第一次加载 Mod 时会自动生成 `config.json`。默认 Bridge 目录是当前 Mod 文件夹下的 `bridge/`：

```json
{
  "BridgeDirectory": ""
}
```

也可以把 `BridgeDirectory` 改为绝对路径。CLI 使用同一个目录，例如：

```text
stardew-cli.exe --bridge-dir "D:\\stardew-agent-bridge" status
```

## 构建

```text
dotnet build StardewAgentMod.csproj --configuration Release
```

项目引用 `Pathoschild.Stardew.ModBuildConfig`。本地有游戏目录时，它可以部署 Mod；GitHub Actions 中通过根目录的 `Directory.Build.props` 禁用自动部署，并只上传 `_releases/` 下的 zip。
