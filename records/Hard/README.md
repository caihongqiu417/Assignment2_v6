# Team01 Hard 交付与实验记录

当前 Hard Final 为 **E10 PPO ε=0.15**，训练 seed 4、编号 200,023 步的独立 ONNX。在 200 个冻结共用开局上取得 **157／8／35（78.5%）**，上一版 Final 为 151／11／38（75.5%）。本轮多 6 胜（+3.0 个百分点），还差 4 胜超过 80%。ε 单因素的四项验收只通过掉落项。 [E10 实验与复算](Team01_Hard_E10_PPO_Epsilon/README.md) · [来源与 SHA-256](Hard_FinalSelection.json) · [Hard_Final.onnx](../../Assets/Hard_Final.onnx)

上一版为 E7 Team01_Hard_E7_SafetyLambda_Lambda020_seed2，另两批评估为 157／7／36 和 155／3／42。Hard B0 的十局原始参考为 1 胜／0 平／9 负，使用历史开局。 [上一版 Final 与历史记录](https://github.com/caihongqiu417/Assignment2_v6/tree/5df61cfb7783a42df3fc15cac3c4b4c482610142/records/Hard) · [历史 400 局](Hard_Final_rounds.csv)

- [E3](Team01_Hard_E3.md) · [E4](Team01_Hard_E4_iteration.md) · [E5 至 E7 补档](Team01_Hard_E5_E7_history.md) · [再碰预测及先导](Team01_Hard_Recontact_Heldout.md)

在 Hard 场景的 Player Behavior Parameters 中将 Model 设为 Assets/Hard_Final.onnx，使用 Inference Only 和原 OscillatingAxes 对手。原训练场景与共用 RollingControl.onnx 的接线沿用。固定模型评估核对了实际加载文件的 SHA-256。
