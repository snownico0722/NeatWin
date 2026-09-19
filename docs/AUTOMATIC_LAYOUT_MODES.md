# 自动整理五档的边界与验证

## 设计

`AutomaticLayoutPolicy` 分开触发条件和排布方式。第三、四档通过同一 `LayoutDispatch` 和主程序 `RunTidy` 调用现有 `IntentLayoutPlanner`，包括观看参数、区域保护、视频后处理、原生执行、核验和撤销。没有复制或弱化 Smart 评分，也没有拟合旧记录中的落点。

第五档使用 `FullTilingPlanner`。它不是 Smart 的激进权重，而是用户明确选择的无重叠布局规则。按工作区分配可调整的普通窗口，将固定窗口和其他不可管理表面从可用区域中扣除。可调整窗口的数量不能满足最小尺寸时跳过该工作区。布局拓扑不因输入焦点变动而重排；现有正确单元匹配零距离。实际应用的私有尺寸限制仍以核验为准。

## 事件与自身归因

`WorkspaceAutoTidyMonitor` 只在第四、五档安装广域 WinEvent hooks。顶层窗口的创建/销毁/显隐/最小化恢复/前台/层级/几何事件只投递一个合并消息，候选计算不在 hook 回调里运行。系统显示和工作区偏好改变也会触发快照核对，子控件与光标事件不作桌面任务变化。第三档只订阅已有手势观察的结束事件；第二档继续原随手辅助。

事件需快照中的实际状态变化才有意义。队列安静450ms、且距上次自动调用至少1000ms才出一个请求；真实移动循环进行中继续等待。关闭或切换档位会清除旧队列、释放不需要的监听。进入第四、五档主动整理一次；恢复已保存模式启动只建基线。

自身行为不能只按进程来源判断：NeatWin 调用其他应用窗口的 SetWindowPos 后，事件仍可能由目标窗口线程产生。调度器记录本次涉及的窗口与目标，结合已有 AutomationGuard 短时标记接受落点取整/限制。短期原生落点稳定后，同一窗口的新目标仍是外部变化。其他窗口在冷却期新出现不会被丢弃。真正的新手动调整会撤销该窗口的自身过滤。

撤销走同样的自身动作归因，并清除积压请求；下一次真正外部变化才再次整理。平铺的撤销不会误记为上一次 Smart 方案的负反馈。偏好和日志中不新增应用标题、内容或进程身份。

## 验证

测试分别覆盖五档路由、旧设置迁移/持久化、关闭队列、手动拖动与程序变化的区别、自身落点/取整/层级回声、冷却期间外部事件、即时手动纠正、撤销回声、DPI/身份状态、同一 Smart 求解结果、平铺边界/固定障碍/不够空间/多屏/幂等与撤销。

Desktop smoke 在 Actions 的隔离桌面上只操作自身创建的子进程窗口，检查真实外部移动、最小化/恢复、自己执行与撤销的过滤、模式切换、实际 WinForms 下拉框；不读取或移动真实用户桌面。测试通过不能视为人因满意度已经验证。

## 原始接口依据

- Microsoft SetWinEventHook（异步投递、线程消息循环、自身进程过滤与重入边界）：https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setwineventhook
- Microsoft Event Constants（顶层位置、前台、显隐、最小化等事件）：https://learn.microsoft.com/en-us/windows/win32/winauto/event-constants
- Microsoft SetWindowPos（非激活和异步定位语义）：https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setwindowpos

450ms、1000ms和格子形状代价为可审查的工程初值，不是假称从论文或个人数据中测得的最优心理参数。
