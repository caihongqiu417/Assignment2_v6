# Team01 Hard 交付与实验记录

当前 Hard Final 为训练安全系数 `0.020`、训练 seed 2、编号 200,018 步的 `Team01_Hard_E7_SafetyLambda_Lambda020_seed2`。在两个不同的 200 局固定评估批次，分别取得 **157／7／36（78.5%）** 和 **155／3／42（77.5%）**，共 312／10／78（78.0%）。它在工作区有正式验收、且推理动作未干预的 Hard 固定评估中取得最高记录。旧 Hard_E2 的 47.0% 来自另批开局，未做本次同开局对比。

- [Hard_Final.onnx](../../Assets/Hard_Final.onnx) · [来源、SHA-256 与评估批次](Hard_FinalSelection.json) · [400 局记录](Hard_Final_rounds.csv)
- [E3](Team01_Hard_E3.md) · [E4](Team01_Hard_E4_iteration.md) · [E5 至 E7 补档](Team01_Hard_E5_E7_history.md) · [再碰预测及先导](Team01_Hard_Recontact_Heldout.md)
- [FigJam 最新 Final 与留组结果](https://www.figma.com/board/p53uuCyg5W2HjcHfJ08aW4?node-id=57-2)

原 E3／E4 文档保留各实验当时的选择。Hard Final 已导出为独立的 Unity ONNX 资产。要用它评估，在 Hard 场景的 Player Behavior Parameters 中将 Model 设为 `Assets/Hard_Final.onnx`，使用 Inference Only 和原 OscillatingAxes 对手。原训练场景与共用 `RollingControl.onnx` 保持原接线。模型来源与逐局终局已在原固定评估环境验证。
