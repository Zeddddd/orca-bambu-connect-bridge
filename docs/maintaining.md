# 维护说明

## 文件结构

| 文件 | 用途 |
| --- | --- |
| `src/Program.cs` | 主窗口、托盘操作和本地设置 |
| `src/PathPicker.cs` | 粘贴、浏览和校验完整路径 |
| `src/FolderMonitor.cs` | 监视目录，等待切片写完，再交接文件 |
| `src/SliceFile.cs` | 从切片内识别机型并生成导入链接 |
| `src/ConnectLauncher.cs` | 查找并唤醒 Bambu Connect |
| `tests/` | 窗口、目录和交接测试 |

## 文件交接

1. 监视器只接受顶层新建或改写的 `.gcode.3mf`。写入稳定且能安全打开后，才读取内部机型。
2. 按切片里的拓竹型号名称识别机型。文件缺少名称时，会尝试使用已知的拓竹机型编号。机型标记有冲突时会跳过。
3. 优先使用手动指定的位置。自动模式会查 Windows 登记和常见安装目录。手动位置失效后，窗口会提示重新指定或切回自动模式。
4. 用 Bambu Connect 的导入入口传递切片的完整路径。链接直接交给程序，以保留中文、括号、空格和 `&` 等符号。窗口上的「已交接」表示已发出打开请求，导入结果以 Bambu Connect 为准。[拓竹第三方软件对接说明](https://blog.bambulab.com/updates-and-third-party-integration-with-bambu-connect/)

监视器只读取切片和启动本机的 Bambu Connect。用户选择的路径存于应用旁的 `WatchFolder.txt` 和 `ConnectPath.txt`，请把这两个文件留在本地。

## 图标

仓库自带原创双色图标，源码构建可以直接使用。如果希望在本机生成双品牌图标，可用 Pillow 和本机安装的 OrcaSlicer、Bambu Studio 图标文件。官方图标归各自权利人所有，仓库不分发这些图标。

```powershell
python -m pip install Pillow
python .\tools\make_icon.py --orca-logo 'C:\path\to\OrcaSlicer_154_title.png' --bambu-logo 'C:\path\to\BambuStudio.png' --output .\artifacts\BridgeSplit.ico
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\build.ps1 -IconPath .\artifacts\BridgeSplit.ico
```

OrcaSlicer 标志由 Justin Levine 设计。[OrcaSlicer 项目说明](https://github.com/OrcaSlicer/OrcaSlicer)
