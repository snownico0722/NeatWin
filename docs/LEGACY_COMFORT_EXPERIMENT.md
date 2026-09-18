# Comfort V2 实验收口

按合并全部历史 PR 的请求，将 #3 的独立 `ComfortSmartTidySolver` 实现保留用于对比，不将旧实验重新设为产品默认入口。

合并冲突按较新的 #4–#7 人因任务排布处理：`SmartTidySolver`、后处理和原有产品回归保留 main 版本。#3 的“只平移、默认保尺寸”不覆盖 v3 的任务驱动缩放、叠边和层级逻辑。旧分支及完整提交历史仍可追溯。

`ComfortExperimentTests` 直接测试该独立实验；正常 Smart 路径仍为 `IntentLayoutPlanner`。本文件不将实验称为已部署的人因模型，也不新增用户模式开关。
