# NeatWin · 叠边与层级 v2

Windows 浮动窗口整理工具与独立习惯记录器。整理的是可使用的窗口关系，不是强迫所有矩形无重叠；你的临时摆放也不是训练标准答案。

## 使用与升级

发布包内包含两个自带 .NET 运行时的 Windows x64 程序：

```text
NeatWin.exe             主程序：主动整理、可选随手辅助、撤销
NeatWin.Recorder.exe    独立记录器：只观察，不移动窗口
```

先从托盘退出两份旧程序，再解压、替换程序文件；无需清空旧数据。两个 EXE 保留在同一目录，主程序的记录器按钮才可直接启动。主窗口标题应显示“叠边与层级 v2”，记录器标题应显示“习惯记录器 v2”。

继续使用 Smart／均衡并主动点击整理即可；不需要开启随手辅助，也不需要刻意摆出完美布局来训练。记录器可以独立运行，关闭面板后留在托盘；退出须用托盘菜单。登录启动默认关闭，主动勾选后才启用。更换程序所在目录后，应在新记录器重新关闭、开启登录启动以更新路径。

## 这一版怎样整理

同一屏幕的局部工作组会同时比较普通并排、适度叠边、成组叠放、只改前后层级与局部微调。两窗保持好用宽度却略放不下时，可以遮住一部分边缘，而不是一律缩窄。明显叠放的窗口可以保留叠放关系和露出入口。仍会保留原状，不为制造变化而胡乱搬动。

相对层级可让参考窗口在输入窗口上方，但不设置永久置顶、不主动抢焦点、不持续抢回层级。只在连续的普通窗口工作组内调整；之后点击窗口，Windows 仍可能改变顺序。无关窗口、对话框、永久置顶窗口或状态变化会阻止不安全的层级操作。

“撤销上次整理”包含几何和相对层级；已被你继续调整的窗口／层级相关组会跳过。界面区分请求与实际结果，并显示本次主要采用的方案。应用拒绝尺寸调整或层级未能应用时，不把 API 调用直接算作全部成功。

Classic、可逆纵向填充和可选视频比例功能保留。Smart 随手辅助仍是另一条小范围路径，不会每次松手重排整张桌面；本轮主要改进主动整理和记录器。

**几何不能识别内容**：中心保护区域和叠边范围只是启发式取值，并不能保证被盖住的边缘没有重要按钮或信息。

## 记录器与本地数据

记录器 v2 记录拖动／缩放前、松手后、短期确认后的窗口几何、层级、前台与邻居对应关系；另外记录前台／状态变化，但不直接拿来源不明的变化训练偏好。旧 v1 数据保留并兼容读取，不会事后虚构旧层级。

本地目录：

```text
%LOCALAPPDATA%\NeatWin\Recorder
```

| 文件 | 内容 |
|---|---|
| `adjustments-*.jsonl` | 手势及操作前后、确认后的背景关系 |
| `workspace-*.jsonl` | 合并后的前台、层级与几何状态变化 |
| `plans-*.jsonl` | 主程序考虑了什么方案、为何拒绝、请求什么以及实际观测结果 |
| `intent-reference.json` | 有界的间距与叠放关系弱参考 |

不保存窗口标题、应用名、进程号、原始窗口句柄、按键、截图或鼠标轨迹；没有自动上传。匿名编号只用于会话内对应关系。几何与时间仍是个人数据，导出后不要直接提交到公开仓库。

支持暂停／继续、打开目录、导出 ZIP 和确认后清空。暂停只暂停记录器自身；主程序仍可在你主动操作整理时写诊断。完全停止新增记录需退出两者。托盘“使用记录器的弱参考”只控制是否读取偏好，不控制记录。

原始日志合计最多 32 文件、32 MB、30 天，运行期间清理；可能因容量上限更早淘汰。参考去重、衰减、有上限，未继续修改不等于满意，程序自己的输出也不当作成功偏好。默认即可生成叠边／叠放方案，不必等待积累样本。

## 构建与检查

Windows 10 2004 或更新版本／Windows 11，.NET 8 SDK。`global.json` 固定在 .NET 8 系列，避免使用构建机上其他编译器的行为。

```powershell
dotnet test tests/NeatWin.Tests/NeatWin.Tests.csproj -c Release
dotnet build src/NeatWin.Recorder/NeatWin.Recorder.csproj -c Release

dotnet publish src/NeatWin/NeatWin.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o artifacts/win-x64
dotnet publish src/NeatWin.Recorder/NeatWin.Recorder.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o artifacts/win-x64
```

Actions 保留单元测试结果、源码快照、自带运行时程序包和启动检查；另有隔离测试窗口的 Windows 集成工作流。开发用离线回放工具直接编译生产核心源码，可在本机分析 JSONL，不上传私有数据：

```powershell
dotnet run --project tools/NeatWin.Replay -c Release -- "adjustments.jsonl" "replay-results.jsonl"
```

旧数据回放有层级和可缩放假设，不是现场复原。编译／自动检查也不能代替所有实际应用的桌面验收。尚不识别窗口内容、不查询所有应用的最小尺寸限制，也不保证所有第三方移动方式发出相同事件。

研究依据、取舍、数据语义和验证边界见 [叠边与意图观察 v2](docs/OCCLUSION_INTENT_V2.md)。[v1 说明](docs/INTENT_LAYOUT_AND_RECORDER.md) 与 [V0 求解器说明](docs/SMART_SOLVER.md) 保留作历史对照，不代表当前产品路径。
