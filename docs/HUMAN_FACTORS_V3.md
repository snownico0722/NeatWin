# 人因任务排布 v3：研究、用户意图、实现与验证

本轮目标不是重放用户留下的坐标，而是替用户承担继续整理的成本。“用户画像”在这里指本人明确表达的任务偏好，不是人格、注意力障碍、眼动或认知能力诊断。

## 1. 纠正因果解释

手工结果同时受任务收益、操作成本、既有窗口状态和系统约束影响。停止调整可能只是收益不值得再手工找边框，并不证明尺寸理想。接近屏幕高度的窗口缺少上下移动空间，因此横移多也不能换算成纵移厌恶系数。

记录器数据是动作采样，不是整个工作日的时间分布。没有修改不是满意，短期 stable 不是目标标签，程序自身输出也不是偏好数据。相同叠放可以表示暂存，也可以表示共同观看时的空间妥协。旧记录缺少内容和层级时，不给每条日志补造“电视剧/游戏”等语义标签。

计算理性理论为行为受约束的解释提供依据 [R1]，但没有给出 NeatWin 应使用的缩放比例或成本系数。v3 删除旧的全局宽度损失惩罚，不导入上一轮从动作分位数生成的 2.73/1.63 倍移动系数。手工操作成本不能转化为软件已经代劳后仍要保留的惩罚；布局改变造成的重新定位和重排版成本则仍需考虑。

## 2. 本人的任务画像

| 明确表达的用途 | 保留的意图 | 可以改进的细节 | 不应做的事 |
| --- | --- | --- | --- |
| 现在只处理一个任务，另一个等待 | 主要任务合适的尺度、暂存入口和上下文 | 层次、入口、粗摆错位 | 为占满屏幕把所有任务摊开 |
| 一边看节目一边打游戏 | 两边持续可见；观看价值不等于输入次数 | 贴边、叠边、宽高、小幅外侧出屏 | 把无输入播放器当无用背景；机械各半 |
| 粗摆完成但懒得继续缩放 | 任务关系，而非误差坐标 | 主动比较尺寸和不同布局 | 把未缩放频率当禁止缩放 |
| A 在左，B/C 在右侧同列 | 右侧组内关系与切换便利 | 以列或组为单位重排 | 把 B/C 拆成无关的第三列 |
| 主动整理与随手辅助 | 前者寻找明显更好的安排；后者完成当前手势 | 不同介入规模 | 必须先打开随手辅助才有主动整理 |

默认允许小幅水平出屏来自本人的明确授权，不是人口层面的“屏幕边缘都不重要”。托盘“人因排布偏好”可关闭该功能或自动缩放。

## 3. 研究证据与工程边界

| 来源 | 可支持的方向 | 实现落点 | 不可外推的结论 |
| --- | --- | --- | --- |
| R1 计算理性 | 交互行为适应认知、设备和任务约束 | 分开手工成本与自动结果的适应成本 | 不是已拟合的认知模型 |
| R2 混合主动交互 | 自动化应有价值，考虑不确定性，允许调用、终止和共同修正 | 主动候选比较、撤销、近期记忆和诊断 | LookOut 日程系统的系数不直接转用 |
| R3 窗口管理实践 | 使用方式多样，不应只有单一排布目标 | 共同观看与暂存竞争假设 | 摘要不冒充全文实验细节 |
| R4 视觉注意模型 | 信息价值、预期、转移努力应分别考虑 | 无输入窗口仍有视觉需求；距离成本独立 | 没有眼动，不称 SEEV 准确率；光标不等于视线 |
| R5 接近兼容性 | 整合任务与聚焦单项任务不同；第一项实验空间接近效应很小 | 整合提示显著增加接近权重；普通并用只弱计距离 | 不是越近一定越有效 |
| R6 Scalable Fabric | 焦点加上下文与空间组织可用于任务管理 | 暂存入口、相对方位、局部工作组 | 不宣称复现其用户研究结果 |
| R7 平铺与重叠比较 | 特定任务和用户下平铺可能更快 | 平铺、叠边、叠放共同竞争 | 不宣称叠放普遍更好 |
| R8 Elastic Windows | 组操作在其研究任务中有收益 | 列/组候选减少逐窗补调需求 | 不借用其实验倍数作为本软件收益 |
| R9 FoXpace | 内容和工作行为可作为自动窗口管理输入 | 区分内容观测与几何代理，保留提示接口 | 没有部署其内容分析或眼动系统 |

论文约束模型结构、比较对象与验证方法，不会把工程初值自动变成实验测量。所有评分系数仍需实际桌面验证。

## 4. 已接入的执行路径

入口仍是 `IntentLayoutPlanner.CreateDetailedPlan`。主窗口、快捷键的 Smart 主动路径调用它，不是单独的 JSON 排序器。Classic 不变；随手辅助保持独立。

### 4.1 证据分层

`TaskEvidence` 记录 Joint、Parked、Integrated、Uncertainty、SameColumn 与 Source。它们是有界的假设权重，不是校准概率。左右分离、轴向重合、同列和标题错位用于形成竞争解释，不单凭相交面积判断用途。

`TaskPairHint` 的明确说明优先于几何猜测。它是适配与测试接口，普通使用无需每次选择模式。自动路径目前使用几何上下文、最近手动缩放、明确偏好及用户已启用的视频检测所提供的被动视觉提示；没有通用应用语义识别。无输入的后台窗口不会因此被赋予零观看价值。旧 v1 数据不能事后获得这些缺失语义。

最近手动缩放只增加有限的重排版成本，不恢复历史尺寸，也不锁死当前尺寸。普通后续手动操作可能表示换了任务，不直接产生负面偏好标签。

### 4.2 候选与目标

保留当前、局部整理、左右/上下/网格、宽松/紧凑叠放、普通窗口相对层级都参与比较。新增 `task-edge`、`task-fit`、`task-bleed`、`task-columns`，对应共同观看贴边、信息尺度调整、边缘空间交换和保留同列组的重排。

同时比较缩小和放大，信息尺度有饱和点，不无限奖励填满。比例包括当前粗比例及几种不等宽比例，不强制各半。超宽屏的局部工作组没有全屏铺满奖励。候选集合有界，不穷举所有排列。

总成本由八项组成，日志分别记录：

| 项目 | 含义 |
| --- | --- |
| InformationLoss | 屏内可见内容代理、有限信息容量不足、高置信视频比例差异 |
| Switching | 暴露入口与任务相关的视觉转移距离 |
| Continuity | 相对方位、同列关系、重新寻找内容的成本 |
| Reflow | 改变尺寸后的重排版和适应成本，不是手工拖边框的成本 |
| PeripheralLoss | 工作区外信息损失 |
| Alignment | 小权重整齐度，不能凌驾于任务用途 |
| Uncertainty | 模糊关系下大改动的风险，不等于全部不动 |
| Feedback | 明确撤销留下的当前上下文负面样本 |

遮挡使用并集，并将工作区外区域计为遮挡。屏外像素不会刷高可见面积。中心 64% 宽、84% 高只是既有几何代理，完整可见性也进入成本；它不识别字幕、菜单、游戏 HUD。`ProtectPeriphery` 提示可以保护边缘，全局出屏也可关闭。不能把“中心通常重要”包装成已经识别了当前内容。

### 4.3 小幅出屏与原生边界

默认 24 DIP，同时不超过原窗口宽度 3.5%，界面允许 0–48 DIP。只允许已知物理屏幕左/右外侧，不允许上下出屏、不进入相邻显示器、不侵占侧边任务栏。拓扑由主程序提供，缺少拓扑的回放不能假装知道外侧边缘。

保持可操作入口、固定尺寸、置顶窗口锚点、其他组和对话框障碍，以及应用前身份/DPI/几何复核。普通相对 Z 调整不等于永久置顶，不抢输入焦点。原生接口语义以微软说明 [R10] 为准。

### 4.4 可选物理观看模型

托盘偏好可输入当前工作区实际宽度及眼睛到屏幕中心的距离。仅匹配工作区和 DPI 时，以平面投影 `atan((x-center)/widthPixels * widthMm / distanceMm)` 计算水平角位置与成对角度差。没有输入时使用归一化空间距离，诊断明确为 `normalized-distance-not-gaze`。

未勾选时，输入框示例值不参与算法。不会用 Windows 125% 缩放猜测屏幕物理尺寸或观看距离。曲面屏只是平面近似；更换硬件即使分辨率不变也应重设。目前保存一份工作区校准，不声称测量所有显示器。

### 4.5 反馈、重复点击和隐私

只有请求到实际落点核验匹配、且没有新上下文时，连续重复调用才短期复用。它用于防止累计放大或漂移，不是满意度判断。五分钟后失效，手势和设置变化立即失效，身份、DPI、工作区、层级或前台变化也不能复用。一次无操作的核验不会错误清掉已验证状态。

明确撤销把局部候选加入有界的会话内负面集合；普通手动后续不直接否定上一布局。没有改动不会产生正面标签。负面集合不写盘，不外推成永久厌恶缩放。

`human-factors.json` 只保存明确偏好与可选物理参数，不保存窗口身份、应用名或原始日志。诊断中的任务关系按局部索引关联匿名窗口编号，不输出 native handles。本 PR 不发布用户原始记录。旧间距参考兼容，但不从不缩放频率推导缩放惩罚。

## 5. 验证与尚未证明的部分

自动测试检验实现约束，不是受试者实验：相同几何的不同任务提示、无历史缩放也可缩放、贴边/出屏/相邻屏/任务栏、并集可见性、后台视觉需求、同列保护、物理参数匹配、成本可核对、固定和置顶窗口、请求核验、撤销与普通后续、后处理保护等。

旧测试若仅要求某个旧候选名字或永不改变宽度，应改为检查对应使用需求；安全、隔屏、隐藏窗口、原生层级及撤销回归不能为通过测试而删除。CI 的实际结果与验证提交见 PR，不在这里预先宣布通过数量。

生产 Core 的离线回放可以检查候选和约束。旧日志缺少真实层级、内容、可缩放属性；构造场景不是现场重建，也不是意图准确率。原始记录必须留在本地。现场验收应比较剩余补调次数/时间、重要信息误遮挡、任务可用性、撤销原因，而不只看面积利用率。

需要本人对暂存、看剧加游戏、对照、粗摆后懒得缩放、右侧同列、超宽局部工作、重要边缘分别交替试用。现有默认值是可审查起点，不能在没有这种反馈时宣称已经得到最优个人模型。

## 6. 文献与阅读范围

R1. Oulasvirta, Jokinen & Howes (2022), *Computational Rationality as a Theory of Interaction*, CHI. DOI https://doi.org/10.1145/3491102.3517739 。核对作者机构摘要与出版信息：https://research.aalto.fi/en/publications/computational-rationality-as-a-theory-of-interaction/ 。

R2. Horvitz (1999), *Principles of Mixed-Initiative User Interfaces*. 阅读作者提供的正文及前两页原则：https://erichorvitz.com/chi99horvitz.pdf 。

R3. Hutchings & Stasko (2004), *Revisiting display space management: understanding current practice to inform next-generation design*. 会议摘要：https://graphicsinterface.org/proceedings/gi2004/gi2004-16/ 。

R4. Steelman, McCarley & Wickens (2017), *Theory-based Models of Attention in Visual Workspaces*. DOI https://doi.org/10.1080/10447318.2016.1232228 。作者机构摘要：https://digitalcommons.mtu.edu/michigantech-p/9378/ 。本版不是该模型复现。

R5. Wickens & Andre (1990), *Proximity Compatibility and Information Display*. 出版社原始摘要，含两项实验与反例：https://journals.sagepub.com/doi/10.1177/001872089003200105 。

R6. Robertson et al., *Scalable Fabric: A Flexible Representation for Task Management*. 微软研究页面标注 December 2003：https://www.microsoft.com/en-us/research/publication/scalable-fabric-flexible-representation-task-management/ 。已核对摘要。

R7. Bly & Rosenberg (1986), *A comparison of tiled and overlapping windows*. DOI https://doi.org/10.1145/22627.22356 。原始摘要的机构存档：https://dis.ijs.si/mitjal/genre/online/data/file0495.htm 。

R8. Kandogan & Shneiderman (1997), *Elastic Windows: Evaluation of Multi-Window Operations*. ACM 会议论文 HTML：https://chi1997.acm.org/proceedings/paper/ek.html 。

R9. Yoshida, Ozono & Shintani (2017), *Developing an Automatic Window Manipulation System Considering Content on Application Windows and User's Behavior*. 出版者摘要：https://www.iaiai.org/journals/index.php/IJSCAI/article/view/98 。DOI https://doi.org/10.52731/ijscai.v1.i2.98 。

R10. Microsoft SetWindowPos：https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setwindowpos ；DPI awareness：https://learn.microsoft.com/en-us/windows/win32/hidpi/dpi-awareness-context 。

核对日期：2026-09-19。文献说明模型来源与边界，不构成体验效果已经验证的声明。
