using InnoVault.Actors;
using System;

namespace InnoVault.StateMachines
{
    /// <summary>
    /// 上下文契约：要从中取出关联的<see cref="Actor"/><br/>
    /// 自定义上下文类型实现此接口即可"零配置"接入<see cref="ActorStateMachine{TContext}"/>
    /// </summary>
    public interface IActorStateContext
    {
        /// <summary>
        /// 返回当前 Actor 实例
        /// </summary>
        Actor Actor { get; }
    }

    /// <summary>
    /// 面向 InnoVault <see cref="Actor"/> 的<see cref="VaultStateMachine{TContext}"/>轻量适配器<br/>
    /// 由于 Actor <b>没有</b>原版的<c>ai[]</c>数组，本类<b>默认不</b>挂载<see cref="INetStateSync{TContext}"/>，<br/>
    /// 调用方可以：<br/>
    /// 1) 在自定义<see cref="Actor"/>上加一个<see cref="SyncVarAttribute"/>标注的<c>int StateNetId</c>字段，<br/>
    ///   然后在构造时调用<see cref="UseStateNetIdField"/>把读写两端接入 Actor 同步链路；<br/>
    /// 2) 或自行实现<see cref="INetStateSync{TContext}"/>，赋值给<see cref="VaultStateMachine{TContext}.NetSync"/>
    /// </summary>
    /// <typeparam name="TContext">实现了<see cref="IActorStateContext"/>的上下文类型</typeparam>
    public class ActorStateMachine<TContext> : VaultStateMachine<TContext> where TContext : IActorStateContext
    {
        /// <summary>
        /// 构造一个面向 Actor 的状态机。默认<b>无</b>网络同步；如需同步请用<see cref="UseStateNetIdField"/>或直接赋值<see cref="VaultStateMachine{TContext}.NetSync"/>
        /// </summary>
        public ActorStateMachine(TContext context) : base(context) { }

        /// <summary>
        /// 便捷方法：把"读 / 写 Actor 上的某个 int 状态字段"接入网络同步<br/>
        /// 字段本身应当<see cref="SyncVarAttribute"/>标注或在<see cref="Actor"/>子类中加入<c>NetUpdate=true</c>逻辑<br/>
        /// 框架会在状态切换时调用<paramref name="writer"/>并把<see cref="Actor.NetUpdate"/>设为<see langword="true"/>，触发<see cref="ActorNetWork"/>发包
        /// </summary>
        /// <param name="reader">从 Actor 读取当前状态 ID 的访问器</param>
        /// <param name="writer">把新状态 ID 写入 Actor 的访问器</param>
        public ActorStateMachine<TContext> UseStateNetIdField(Func<Actor, int> reader, Action<Actor, int> writer) {
            NetSync = new ActorFieldNetSync(reader, writer);
            return this;
        }

        private sealed class ActorFieldNetSync : INetStateSync<TContext>
        {
            private readonly Func<Actor, int> _reader;
            private readonly Action<Actor, int> _writer;

            public ActorFieldNetSync(Func<Actor, int> reader, Action<Actor, int> writer) {
                _reader = reader ?? throw new ArgumentNullException(nameof(reader));
                _writer = writer ?? throw new ArgumentNullException(nameof(writer));
            }

            public void WriteState(VaultStateMachine<TContext> machine, int stateId) {
                Actor actor = machine.Context.Actor;
                if (actor == null) {
                    return;
                }
                _writer(actor, stateId);
                actor.NetUpdate = true;
            }

            public int ReadState(VaultStateMachine<TContext> machine) {
                Actor actor = machine.Context.Actor;
                return actor == null ? -1 : _reader(actor);
            }
        }
    }
}
