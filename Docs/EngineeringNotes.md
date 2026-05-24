# 工程笔记

维护过程中踩过、且容易被反复踩中的具体陷阱。新条目向下追加，每条自包含、可被独立阅读。

---

## 帧时长（Frame Delta）取值的几个陷阱

- 最后更新：2026-05
- 涉及子系统：`UIHandles`、`Models3D/Animation`

### 背景

tModLoader 沿用 XNA Game Loop 的 Update / Draw 双流水线：Update 以固定 60Hz 推进游戏逻辑，Draw 跟随显示器刷新率运行。任何"按时间推进"的状态——UI 动画的指数衰减、骨骼动画的关键帧采样、粒子生命周期的累加——都需要一个"距离上一次推进过去了多少时间"的 delta。

直觉上会去查 `Microsoft.Xna.Framework.GameTime`，在 Terraria 里它就是 `Main.gameTimeCache`。这条路上有几个一旦踩中就难定位的坑，本节把症状、原因和推荐做法摆清楚。

### 误用

在 Draw 上下文里读 `Main.gameTimeCache.ElapsedGameTime.TotalSeconds` 当作 delta，行为不可靠：

- 首次读到的可能是"从游戏启动到当前帧"累积下来的脏值（实测可达 0.5s+，看起来像一次合法的大 delta，本质是初始化阶段未结算）；
- 之后每帧读到的可能是几微秒级别，动画看起来"几乎不动、偶尔抽搐一下"；
- 该字段在 Update 流水线里才有明确定义。Draw 期间它只是上次 Update 写入的残值，何时被覆盖与游戏当前的 `IsFixedTimeStep` / 帧率限制设置相关，不能假设。

`Models3D` 动画系统最初就这么写过，结果 `AnimationPlayer.Time` 在 10 秒墙钟内只推进 3 毫秒；现场症状是"按右键看到模型偶尔抽搐一下、姿态几乎不变"。定位过程参见末尾"诊断方法论"。

### 推荐做法 A：Stopwatch 测墙钟

适用于"不需要跟游戏暂停同步"的场景——UI 动画（含主菜单，那里的 Draw 频率不一定等于 60Hz）、粒子、纯视觉特效。

参考实现 `UIHandles/UIHandleLoader.cs` 的 `CurrentFrameDelta`：

```140:165:UIHandles\UIHandleLoader.cs
        public static float CurrentFrameDelta {
            get {
                long now = _frameStopwatch.ElapsedTicks;
                //同帧内多次读取直接返回缓存值，保证多个UI/Layer看到的是同一个帧时长
                if (now < _cachedFrameExpiryTicks) {
                    return _cachedFrameDelta;
                }

                long lastTicks = _lastFrameTicks;
                _lastFrameTicks = now;

                //首次调用 (lastTicks == 0) 返回安全默认值，避免把"从游戏启动到现在"的时长一次性吃进去
                if (lastTicks == 0) {
                    _cachedFrameDelta = 1f;
                }
                else {
                    double seconds = (now - lastTicks) / (double)Stopwatch.Frequency;
                    _cachedFrameDelta = MathHelper.Clamp((float)(seconds * 60.0), 0.05f, 5f);
                }

                //缓存窗口设为 1ms，足以覆盖同一 Draw 调用内所有UI/Layer的迭代，
                //且远小于常见显示器刷新率（60~240Hz 即 16.67~4.17ms）
                _cachedFrameExpiryTicks = now + Stopwatch.Frequency / 1000;
                return _cachedFrameDelta;
            }
        }
```

这段同时处理了三类边界：

- **首帧**（`lastTicks == 0`）用 `1f` 兜底，避免把启动累积时长当成一帧 delta 灌入；
- **同帧多次读取**用 1ms 短窗口缓存返回同一个值，避免多个 UI / Layer 调用者瓜分这一帧的时长；
- **极端帧率**（最小化 / 卡顿）钳制到 `[0.05, 5]`，避免单帧 60 倍速跳变。

热重载也要顾到。`ModSystem.Load` 里要把 `_lastFrameTicks` / `_cachedFrameExpiryTicks` / `_cachedFrameDelta` 一并复位，否则热重载后第一次读取会吃掉一段超大时间差——这个细节在 `UIHandleLoader.Load` 里有对应处理。

### 推荐做法 B：TotalGameTime 差值

适用于"需要跟游戏物理同步"的场景，比如希望游戏暂停时动画也跟着停。用 `Main.gameTimeCache.TotalGameTime` 的差值，而不是 `ElapsedGameTime` 的瞬时值。`TotalGameTime` 单调递增，差值结果稳定。

参考实现 `Models3D/Runtime/Model3DRenderer.cs` 的 `ResolveDeltaSeconds`：

```755:779:Models3D\Runtime\Model3DRenderer.cs
        private static float ResolveDeltaSeconds(Animation.AnimationPlayer player) {
            const float FixedFallback = 1f / 60f;
            GameTime gt = Main.gameTimeCache;
            if (gt == null || player == null) {
                return FixedFallback;
            }
            double total = gt.TotalGameTime.TotalSeconds;
            double last = player.LastDriverTotalSeconds;
            //首次调用：仅记录时刻，下一帧才开始推进，避免把"加载阶段累积的虚假大 delta"灌进动画
            if (last < 0.0) {
                player.LastDriverTotalSeconds = total;
                return FixedFallback;
            }
            double delta = total - last;
            //同帧（multi-Draw）或游戏暂停（TotalGameTime 不前进）→ 0，避免重复推进
            if (delta <= 0.0) {
                return 0f;
            }
            player.LastDriverTotalSeconds = total;
            //长时间停留在加载界面 / 切换世界后单帧巨大跳变 → 兜底，避免一帧跨过整段动画
            if (delta > 1.0) {
                return FixedFallback;
            }
            return (float)delta;
        }
```

四类边界各自处理：首帧只记时刻、返回固定步长兜底；差值为 0（同帧 multi-Draw、游戏暂停）返回 0 不重复推进；差值过大（> 1s，切世界 / 长卡顿）回退到 1/60s；其余情况返回真实差值。

"上次时刻"挂在被驱动者本身（这里是 `AnimationPlayer.LastDriverTotalSeconds`），不要做成全局静态。多实例并存时，全局静态会导致"第一个实例吃光这一帧的 delta、其它实例永远拿到 0"，相互抢占之后只有一个能正常推进。

### 选用建议

| 场景                                         | 用法                          |
| -------------------------------------------- | ----------------------------- |
| UI 动画（含主菜单）                          | 做法 A（Stopwatch 墙钟）      |
| 粒子、纯视觉特效                             | 做法 A                        |
| 与游戏世界同步、希望暂停时停推的动画         | 做法 B（TotalGameTime 差值）  |
| 距离"上一次相同事件"过去了多久（节流、冷却） | 做法 A 的 Stopwatch 模式      |

新写代码遇到"需要一个 delta"的需求时，先确认场景，再去复用上面两段已有实现；尽量不要再造第三个变体。两个实现的注释里都有指向本节的关键词，IDE 全文搜索 `gameTimeCache` 或 `ResolveDeltaSeconds` 能直接找到。

### 诊断方法论

上述陷阱有一个共同症状：**数学上看一切都对、运行结果就是不对**。出现这种情况时，最便宜的工具是在调用链尾部加打印，而不是去推更深的数学。

骨骼动画那次定位过程是一个反面案例：先怀疑 IBM 与 bind global 矩阵的数学闭合性，用 PowerShell 推了一段数值，未果；真凶其实是 `AnimationPlayer.Time` 根本没在推进。换路径之后，加一段每秒打印 `Time / Duration / Speed / leaf joint skinMatrix` 的诊断日志，第一次进游戏跑 30 秒就看清楚了。如果一开始就先把运行时数据落到日志里，会省掉大量时间。

诊断模板已留在 `Models3D/Animation/AnimationPlayer.cs` 中，以 `public bool Diagnostic` 开关的形式存在（默认 `false`、零运行时开销）。打开后输出：

- 一次性的骨架信息：每个 joint 的 bind TRS、IBM、bindGlobal、`IBM × bindGlobal`（理论值应为单位阵，便于一眼看出 bind 空间是否对齐）；
- 每秒一次的运行时摘要：Time、Duration、Speed、Fading 状态、leaf joint 当前 TRS 与 skinMatrix。

任何带"时间推进"的子系统在出现类似症状时，可以照这个模板加一个 `Diagnostic` 字段、在更新链路尾部按计数打印关键中间量。比逐行 review 代码或推数学快得多。

---

## 维护规约

- 每条以 `## ` 开头、自包含可独立阅读，不要依赖前面条目的上下文。
- 顶部列出最后更新日期与涉及子系统，便于后人快速判断是否相关。
- 引用具体代码时给出 `起始行:结束行:相对路径` 的代码块，便于 IDE 跳转。
- 标题用陈述句概括坑的本质（"xxx 取值的几个陷阱"），不要写成动词式（"如何处理 xxx"）；前者便于检索，后者容易和教程类文档混在一起。
- 新坑追加到文件末尾，旧条目原则上不动；如果旧条目过时或被推翻，在末尾追加一条新的并在旧条目顶部加 `> 已被 xxx 取代` 即可。
