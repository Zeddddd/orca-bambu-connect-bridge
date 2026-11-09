# Orca–Bambu Connect Bridge

这是一个 Windows 托盘小工具。它监视你指定的文件夹，等 OrcaSlicer 写完拓竹机型的单盘切片，再唤醒 Bambu Connect 并交给它这份文件。导入和打印由你在 Bambu Connect 中确认。[拓竹第三方软件对接说明](https://blog.bambulab.com/updates-and-third-party-integration-with-bambu-connect/)

[下载 Windows 即用版](https://github.com/Zeddddd/orca-bambu-connect-bridge/releases/latest)

## 使用方法

1. 在 Windows 10 或 11 上安装 Bambu Connect，再打开本项目构建的 `Orca-Bambu-Connect.exe`。
2. 点「选择文件夹…」，粘贴 OrcaSlicer 的导出目录。若窗口没有找到 Bambu Connect，点「指定程序…」粘贴实际 `Bambu Connect.exe` 的完整路径。
3. 在 OrcaSlicer 选择拓竹机型，切片后用「导出单盘切片文件」把 `.gcode.3mf` 存入监视目录。窗口显示已交接后，在 Bambu Connect 确认导入。
4. 点「收起到托盘」或关闭窗口，监视会继续。右键托盘图标选「退出应用」可完全停止。

路径可以用 Ctrl+V 粘贴，也可以点「粘贴」或「浏览…」。从资源管理器复制的双引号路径可直接使用。程序只看所选文件夹的顶层，并保留原切片文件。启动前已经存在的文件会在重新导出或覆盖后处理。

跨网使用时，在 Bambu Connect 登录与打印机绑定的拓竹账号，并让打印机保持云端在线。

## 在本机构建

在仓库目录打开 PowerShell。Windows 需要 .NET Framework 4.8 或更高版本。

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
```

应用和使用说明会打包到 `artifacts/Orca-Bambu-Connect-Tray.zip`。构建不需要下载软件包。应用会把所选目录和手动指定的程序路径保存在自身旁边，建议解压到可写目录。

### 运行测试

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\build.ps1 -Test
```

测试会短暂打开程序窗口。它使用当场生成的切片和本地测试接收程序，检查路径粘贴、含括号的多层目录、机型筛选、文件写入和托盘操作。测试无需登录拓竹账号或连接打印机。

## 遇到问题

| 看到的情况 | 怎么处理 |
| --- | --- |
| 右下角看不到图标 | 展开通知区域的上箭头，再双击双色图标。 |
| 找不到 Bambu Connect | 点「指定程序…」并选择已安装的 `Bambu Connect.exe`。更换位置后可点「自动查找」。 |
| 保存后没有交接 | 检查文件是不是「导出单盘切片文件」生成的 `.gcode.3mf`，并确认保存到了正在监视的目录。 |
| 窗口显示已交接，但 Bambu Connect 没有打开切片 | 检查 Bambu Connect 的导入确认窗口，再试着在 Bambu Connect 里手动导入同一文件。 |

[项目结构、路径处理和图标构建](docs/maintaining.md)
