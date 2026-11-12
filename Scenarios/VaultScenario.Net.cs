using System.Collections.Generic;
using System.IO;

namespace InnoVault.Scenarios
{
    public abstract partial class VaultScenario
    {
        /// <summary>
        /// 这个实例的网络编号。多人模式下由服务器在激活时分配，各客户端上的同一个实例编号相同，可以用它在自己的数据包里指代实例
        /// （<see cref="ScenarioDirector.FindByNetId"/>）；单人模式与场景模板上为 0
        /// </summary>
        public int NetId { get; internal set; }

        /// <summary>
        /// 当前端是否对场景树有裁决权：单人与服务器为真，多人客户端为假。场景的钩子在每一端都会执行，
        /// 生成 NPC、改写物块、推进玩法状态这类只该发生一次的事放在它为真的一端，演出与界面放在客户端
        /// </summary>
        public bool IsAuthority => ScenarioPlatform.IsAuthority;

        /// <summary>
        /// 服务器上置为真时，这一帧结束前把这个实例标了 <see cref="SyncVarAttribute"/> 的成员、<see cref="SendExtraData"/> 的内容与各组件的网络状态
        /// 重新发给所有客户端，发完自动复位。客户端与单人模式下没有作用
        /// </summary>
        public bool NetUpdate { get; set; }

        /// <summary>
        /// 写入 <see cref="SyncVarAttribute"/> 之外需要同步的数据，与 <see cref="ReceiveExtraData"/> 成对重写。
        /// 服务器在实例激活、客户端进入当前舞台、以及 <see cref="NetUpdate"/> 为真时调用
        /// </summary>
        /// <param name="writer">只写这个实例的数据，框架已经为它单独分段</param>
        protected virtual void SendExtraData(BinaryWriter writer) { }

        /// <summary>
        /// 读取 <see cref="SendExtraData"/> 写入的数据，在客户端调用。首次调用发生在 Compose 之后、激活之前；
        /// 标了 <see cref="SyncVarAttribute"/> 的成员在 Compose 之前就已经套用，所以决定挂哪些组件的参数应当用同步成员
        /// </summary>
        /// <param name="reader">只含这个实例的数据</param>
        protected virtual void ReceiveExtraData(BinaryReader reader) { }

        /// <summary>
        /// 多人模式下服务器收到客户端发来的请求时调用，返回 <see langword="false"/> 时请求以 <see cref="ScenarioRejectReason.Unauthorized"/> 被拒绝。
        /// 缺省放行。激活与换舞台请求里它在按请求参数组合好的新实例上调用，停用请求里在要停用的实例上调用<br/>
        /// 换舞台请求还会在当前的舞台场景（将被离开的那个，<see cref="ScenarioRequest.From"/>）上再调用一次：回主世界的目标是框架自带的
        /// <see cref="RootScenario"/>，要不要放人走只能由被离开的舞台来定。用 <c>ReferenceEquals(request.From, this)</c> 区分“有人想离开我”与“有人想进入我”<br/>
        /// 服务器自己的代码与单人模式发起的请求不经过这里，那些入口由调用方自己把关
        /// </summary>
        /// <param name="request">客户端的请求</param>
        protected virtual bool AuthorizeRequest(ScenarioRequest request) => true;

        internal bool InvokeAuthorize(ScenarioRequest request) => AuthorizeRequest(request);

        //激活参数：标了 [SyncVar] 的成员，在 Compose 之前套用
        internal void WriteArguments(BinaryWriter writer) => SyncVarManager.Send(this, writer);

        internal void ReadArguments(BinaryReader reader) => SyncVarManager.Receive(this, reader);

        //组合完成之后的状态：场景的附加数据，各节点的启用标志，然后按节点树的先序是各节点的网络状态
        internal void WriteNetState(BinaryWriter writer) {
            SendExtraData(writer);
            IReadOnlyList<ScenarioNode> nodes = AllNodes;
            //先写节点数：客户端据此核对两端组合出来的是不是同一棵树，对不上时只丢开关、不读错位
            writer.Write((ushort)nodes.Count);
            byte bits = 0;
            int used = 0;
            for (int i = 0; i < nodes.Count; i++) {
                if (nodes[i].Enabled) {
                    bits |= (byte)(1 << used);
                }
                if (++used == 8) {
                    writer.Write(bits);
                    bits = 0;
                    used = 0;
                }
            }
            if (used > 0) {
                writer.Write(bits);
            }
            for (int i = 0; i < nodes.Count; i++) {
                nodes[i].InvokeNetSend(writer);
            }
        }

        //返回是否有节点的启用标志被改动（调用方据此重建合成快照）
        internal bool ReadNetState(BinaryReader reader) {
            ReceiveExtraData(reader);
            IReadOnlyList<ScenarioNode> nodes = AllNodes;
            int count = reader.ReadUInt16();
            byte[] flags = reader.ReadBytes((count + 7) / 8);
            bool changed = false;
            if (count == nodes.Count && flags.Length == (count + 7) / 8) {
                for (int i = 0; i < count; i++) {
                    if (nodes[i].SetEnabledSilently((flags[i >> 3] & (1 << (i & 7))) != 0)) {
                        changed = true;
                    }
                }
            }
            else {
                ScenarioPlatform.Error($"Scenario.Net.Tree.{Key}",
                    $"[Scenarios] {Key} has {nodes.Count} nodes on this endpoint but {count} on the server; Compose must build the same tree on every endpoint. Node switches were not applied");
            }
            for (int i = 0; i < nodes.Count; i++) {
                nodes[i].InvokeNetReceive(reader);
            }
            return changed;
        }
    }
}
