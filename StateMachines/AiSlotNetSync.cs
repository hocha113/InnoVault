using System;
using Terraria;

namespace InnoVault.StateMachines
{
    /// <summary>
    /// 通过"<c>ai[slot]</c>槽位"完成状态 ID 同步的<see cref="INetStateSync{TContext}"/>实现<br/>
    /// 适用于 NPC、Projectile 等原版会自动同步<c>ai[]</c>数组的实体；对自定义 Actor 则可手动传入访问器
    /// </summary>
    /// <remarks>
    /// 兼容性提示：原版的<c>npc.aiStyle</c>会自行占用<c>ai[0..3]</c>之间的部分槽位，<br/>
    /// 建议状态机使用<c>aiStyle = -1</c>的自定义 NPC，并选用<c>ai[3]</c>为状态槽；<br/>
    /// Projectile 同理，请确认目标弹幕的<see cref="Terraria.Projectile.aiStyle"/>未占用该槽
    /// </remarks>
    /// <typeparam name="TContext">状态机上下文类型</typeparam>
    public sealed class AiSlotNetSync<TContext> : INetStateSync<TContext>
    {
        /// <summary>
        /// 推荐的默认槽位。<c>ai[3]</c>在大多数原版 NPC AI 中未被使用，作为状态机槽较为安全
        /// </summary>
        public const int RecommendedSlot = 3;

        private readonly int _slot;
        private readonly Func<VaultStateMachine<TContext>, float[]> _aiAccessor;
        private readonly Action<VaultStateMachine<TContext>> _markNetUpdate;

        /// <summary>
        /// 通用构造：调用方提供<c>ai[]</c>数组的访问器与"标记 netUpdate"的回调<br/>
        /// 框架提供的便捷构造工厂见<see cref="ForNpc"/>、<see cref="ForProjectile"/>
        /// </summary>
        /// <param name="aiAccessor">从状态机返回当前关联实体的<c>ai</c>数组引用</param>
        /// <param name="markNetUpdate">服务端写入新状态后调用，用于将关联实体置为"待同步"</param>
        /// <param name="slot">同步槽位，超出<c>ai[]</c>长度时<see cref="WriteState"/>会被静默跳过</param>
        public AiSlotNetSync(Func<VaultStateMachine<TContext>, float[]> aiAccessor, Action<VaultStateMachine<TContext>> markNetUpdate, int slot = RecommendedSlot) {
            _aiAccessor = aiAccessor ?? throw new ArgumentNullException(nameof(aiAccessor));
            _markNetUpdate = markNetUpdate;
            _slot = slot;
        }

        /// <summary>
        /// 当前使用的同步槽位下标
        /// </summary>
        public int Slot => _slot;

        /// <inheritdoc/>
        public void WriteState(VaultStateMachine<TContext> machine, int stateId) {
            float[] ai = _aiAccessor(machine);
            if (ai == null || _slot < 0 || _slot >= ai.Length) {
                VaultMod.LoggerError($"AiSlotNetSync<{typeof(TContext).Name}>:bad_slot:{_slot}",
                    $"AiSlotNetSync configured with invalid slot {_slot}; ai[] is {(ai == null ? "null" : ai.Length.ToString())}.");
                return;
            }
            ai[_slot] = stateId;
            _markNetUpdate?.Invoke(machine);
        }

        /// <inheritdoc/>
        public int ReadState(VaultStateMachine<TContext> machine) {
            float[] ai = _aiAccessor(machine);
            if (ai == null || _slot < 0 || _slot >= ai.Length) {
                return -1;
            }
            return (int)ai[_slot];
        }

        /// <summary>
        /// 便捷工厂：构造一个用于"上下文持有 NPC 引用"场景的<see cref="AiSlotNetSync{TContext}"/><br/>
        /// 通过<paramref name="npcAccessor"/>从上下文中取出 NPC，框架自动接入<c>npc.ai</c>与<c>npc.netUpdate</c>
        /// </summary>
        public static AiSlotNetSync<TContext> ForNpc(Func<TContext, NPC> npcAccessor, int slot = RecommendedSlot) {
            return new AiSlotNetSync<TContext>(
                aiAccessor: machine => npcAccessor(machine.Context)?.ai,
                markNetUpdate: machine => {
                    NPC npc = npcAccessor(machine.Context);
                    if (npc != null) {
                        npc.netUpdate = true;
                    }
                },
                slot: slot);
        }

        /// <summary>
        /// 便捷工厂：构造一个用于"上下文持有 Projectile 引用"场景的<see cref="AiSlotNetSync{TContext}"/><br/>
        /// 通过<paramref name="projectileAccessor"/>从上下文中取出 Projectile，框架自动接入<c>proj.ai</c>与<c>proj.netUpdate</c>
        /// </summary>
        public static AiSlotNetSync<TContext> ForProjectile(Func<TContext, Projectile> projectileAccessor, int slot = RecommendedSlot) {
            return new AiSlotNetSync<TContext>(
                aiAccessor: machine => projectileAccessor(machine.Context)?.ai,
                markNetUpdate: machine => {
                    Projectile p = projectileAccessor(machine.Context);
                    if (p != null) {
                        p.netUpdate = true;
                    }
                },
                slot: slot);
        }
    }
}
