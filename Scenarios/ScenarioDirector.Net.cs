using InnoVault.Cinematics;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using Terraria.ModLoader.IO;

namespace InnoVault.Scenarios
{
    //场景的网络消息。客户端发请求与进度回报，服务器发裁决结果、场景树的变化与换舞台的各个步骤。
    //只在末尾追加：两端的编号必须一致
    internal enum ScenarioNetOp : byte
    {
        //客户端 → 服务器：激活、停用或换舞台的请求
        Request,
        //服务器 → 发请求的客户端：请求被拒绝
        Reject,
        //服务器 → 客户端：一个不拥有舞台的实例已经激活（也用来在快照里逐个下发现有实例）
        Spawn,
        //服务器 → 客户端：一个实例已经停用
        End,
        //服务器 → 客户端：一个实例的同步成员与附加数据
        State,
        //服务器 → 客户端：开始离场
        TravelBegin,
        //客户端 → 服务器：幕布已经合上
        TravelReady,
        //服务器 → 客户端：离场期间取消
        TravelCancel,
        //服务器 → 客户端：停用旧舞台、卸载本地世界
        TravelCommit,
        //客户端 → 服务器：本地的旧舞台已经卸载，可以接收新舞台
        ClientLeft,
        //客户端 → 服务器：已经出生在当前舞台里（进服或换舞台抵达），请求场景树快照
        StageEntered,
        //服务器 → 客户端：快照开头，含舞台场景与这次抵达的安排；其余实例随后经 Spawn 逐个到达
        Snapshot,
        //服务器 → 客户端：快照结束
        SnapshotEnd,
        //客户端 → 服务器：已经在落点站好
        TravelArrived,
        //服务器 → 客户端：拉开幕布
        TravelOpen,
        //服务器 → 随换舞台抵达的客户端：这次转场的数据包，紧跟其后的快照激活新舞台场景时读到的就是裁决端写下的内容
        TravelPayload,
    }

    public static partial class ScenarioDirector
    {
        //服务器等客户端合上幕布的宽限帧数（在离场演出超时与幕布合上帧数之外）
        private const int DepartureGraceFrames = 180;
        //服务器等客户端抵达新舞台的最长帧数，超时后不再等，幕布照常拉开
        private const int ArrivalTimeoutFrames = 1800;
        //客户端等不到服务器的下一步时自行放弃的帧数
        private const int ClientStallFrames = 3600;
        //客户端等落点所在区块到齐的最长帧数
        private const int LandingWaitFrames = 600;
        //一个实例的网络状态上限：快照开头最多带两段，连包头必须放得进一个 65535 字节的包
        private const int MaxBlobLength = 30000;
        private const int MaxKeyLength = 256;
        private const int MaxDetailLength = 512;
        //转场数据包下发给客户端时的上限：独占一个包，连包头放得进 65535 字节
        private const int MaxPayloadLength = 60000;
        //框架自己写进数据包的键都带这个前缀，只在裁决端使用，不下发
        internal const string ReservedPayloadPrefix = "InnoVault:";

        private const byte SnapshotPendingTravel = 1;
        private const byte SnapshotWaitForOpen = 2;
        private const byte SnapshotHasLanding = 4;

        private static readonly Dictionary<int, VaultScenario> _byNetId = [];
        private static int _nextNetId;
        //换舞台的编号：服务器每次递增，客户端沿用，用来丢弃过期的进度回报
        private static int _travelId;

        //服务器：这一步还在等的客户端（离场时等幕布合上，抵达时等到场）
        private static readonly HashSet<int> _awaiting = [];
        //服务器：已经随队离开旧舞台、还没出生在当前舞台里的客户端。有人在途期间不接受新请求
        private static readonly HashSet<int> _inTransit = [];
        //服务器：已经随队离开、还没回报卸载完旧舞台的客户端。这段时间它们发来的游戏包说的还是旧舞台里的事，宿主据此丢弃
        private static readonly HashSet<int> _leaving = [];
        private static readonly List<int> _clientScratch = [];
        private static int _waitFrames;
        //服务器：最近一次换舞台的转场，在途客户端抵达时据此解析落点
        private static ScenarioTransition _lastTravel;

        //客户端：幕布合上的回报是否已经发出
        private static bool _readySent;
        //客户端：正在接收快照（Snapshot 与 SnapshotEnd 之间），以及这份快照里出现过的实例
        private static bool _snapshotOpen;
        private static readonly HashSet<int> _snapshotSeen = [];
        //客户端：快照已经套用、玩家已经放到落点，正在等区块到齐与服务器的拉幕通知
        private static bool _landingPending;
        private static Point? _landing;
        private static bool _waitForOpen;
        private static bool _openReceived;
        private static bool _arrivedSent;
        private static int _stallFrames;
        //客户端：服务器发来的转场数据包，等随后的快照激活新舞台场景之前并进这一端的转场
        private static byte[] _incomingPayload;

        /// <summary>
        /// 当前端是否对场景树有裁决权：单人与服务器为真，多人客户端为假。只该发生一次的玩法逻辑（生成 NPC、改写物块）放在它为真的一端
        /// </summary>
        public static bool IsAuthority => ScenarioPlatform.IsAuthority;

        /// <summary>
        /// 多人客户端上，自己发出的请求被服务器拒绝时触发（主线程）。参数依次是请求的种类、目标场景的模板（可能为空）与拒绝结果。
        /// 用来给玩家反馈；订阅者在模组卸载时会被统一清空
        /// </summary>
        public static event Action<ScenarioRequestKind, VaultScenario, ScenarioResult> RequestRejected;

        /// <summary>按网络编号取处于激活中的场景实例（见 <see cref="VaultScenario.NetId"/>），没有时返回空</summary>
        /// <param name="netId">网络编号</param>
        public static VaultScenario FindByNetId(int netId)
            => netId != 0 && _byNetId.TryGetValue(netId, out VaultScenario instance) ? instance : null;

        /// <summary>
        /// （服务器）照原样执行一个客户端请求，不再经过裁决钩子。用于投票、确认这类门阀：在裁决钩子里拒绝并留下请求对象，
        /// 条件满足后调用这里。请求里的参数会套用到一个新建的实例上，此刻的合法性（父场景是否还在、是否正在换舞台）重新校验
        /// </summary>
        /// <param name="request">裁决钩子收到的请求对象</param>
        public static ScenarioResult Execute(ScenarioRequest request) {
            if (request == null) {
                return Reject(ScenarioRejectReason.NotRegistered, "request is null");
            }
            if (!ScenarioPlatform.IsAuthority) {
                return Reject(ScenarioRejectReason.Unauthorized, "a client request can only be executed on the server");
            }
            return request.Kind switch {
                ScenarioRequestKind.Activate => Activate(request.Template, request.Parent, ArgumentSetup(request.Arguments)),
                ScenarioRequestKind.Deactivate => Deactivate(request.Instance),
                ScenarioRequestKind.Travel => Travel(request.Template, request.Anchor, ArgumentSetup(request.Arguments)),
                _ => Reject(ScenarioRejectReason.NotRegistered, $"unknown request kind {(int)request.Kind}"),
            };
        }

        /// <summary>
        /// 权威端绕过原版的放置与挖掘、直接整块改写了一片物块之后调用：服务器把这片范围所在的区块（连同箱子、告示牌与物块实体）
        /// 重新发给已经收过它们的客户端。单人模式与客户端上什么都不做。按区块整块重发，只适合搭建、还原这类低频操作
        /// </summary>
        /// <param name="tiles">改写的范围（物块坐标）</param>
        public static void SyncRegion(Rectangle tiles) {
            if (ScenarioPlatform.IsServer && tiles.Width > 0 && tiles.Height > 0) {
                ScenarioPlatform.RegionChanged?.Invoke(tiles);
            }
        }

        //多人下当前端没有接上网络后端：导演层保持离线
        private static bool NetOffline => ScenarioPlatform.NetRole != ScenarioNetRole.SinglePlayer && ScenarioPlatform.Net == null;

        //（客户端）自己这边已经就绪，正盖着幕布等其他玩家：离场时等所有人合上幕布，抵达后等所有人到齐
        internal static bool WaitingForPlayers => ScenarioPlatform.IsClient && IsTraveling && TravelPhase switch {
            ScenarioTravelPhase.Departing => _readySent,
            ScenarioTravelPhase.Loading => _arrivedSent && !_openReceived,
            _ => false,
        };

        //（服务器）这个客户端是否已经随队离开、但还没回报卸载完旧舞台
        internal static bool IsLeavingStage(int client) => _leaving.Count > 0 && _leaving.Contains(client);

        //（服务器）这个槽位已经换了一个新连接：上一个连接留下的换舞台状态与它无关
        internal static void ForgetClient(int client) {
            _leaving.Remove(client);
            _inTransit.Remove(client);
            _awaiting.Remove(client);
        }

        //多人特有的“暂时不能处理请求”：客户端还没拿到场景树，服务器上还有客户端没抵达当前舞台
        private static bool NetBusy() {
            switch (ScenarioPlatform.NetRole) {
                case ScenarioNetRole.Client:
                    return Stage == null;
                case ScenarioNetRole.Server:
                    PruneDisconnected(_inTransit);
                    PruneDisconnected(_leaving);
                    return _inTransit.Count > 0;
                default:
                    return false;
            }
        }

        private static int NextNetId() {
            if (++_nextNetId <= 0) {
                _nextNetId = 1;
            }
            return _nextNetId;
        }

        //服务器在激活时分配编号；客户端的镜像在创建时已经带着服务器给的编号；单人下始终是 0
        private static void RegisterNetId(VaultScenario instance) {
            if (instance.NetId == 0 && ScenarioPlatform.IsServer) {
                instance.NetId = NextNetId();
            }
            if (instance.NetId != 0) {
                _byNetId[instance.NetId] = instance;
            }
        }

        private static void UnregisterNetId(VaultScenario instance) {
            if (instance.NetId != 0 && _byNetId.TryGetValue(instance.NetId, out VaultScenario registered) && ReferenceEquals(registered, instance)) {
                _byNetId.Remove(instance.NetId);
            }
        }

        private static void ClearNetState() {
            _byNetId.Clear();
            _awaiting.Clear();
            _inTransit.Clear();
            _leaving.Clear();
            _snapshotSeen.Clear();
            _waitFrames = 0;
            _lastTravel = null;
            _readySent = false;
            _snapshotOpen = false;
            _landingPending = false;
            _landing = null;
            _waitForOpen = false;
            _openReceived = false;
            _arrivedSent = false;
            _stallFrames = 0;
            _incomingPayload = null;
        }

        private static void PruneDisconnected(HashSet<int> clients) {
            if (clients.Count == 0) {
                return;
            }
            IScenarioNetHost net = ScenarioPlatform.Net;
            if (net == null) {
                clients.Clear();
                return;
            }
            clients.RemoveWhere(client => !net.IsConnected(client));
        }

        #region 网络状态的读写

        private static byte[] Capture(Action<BinaryWriter> write) {
            using MemoryStream stream = new();
            using BinaryWriter writer = new(stream);
            write(writer);
            writer.Flush();
            return stream.ToArray();
        }

        //实例的完整网络状态：同步成员（Compose 之前套用），然后是附加数据与各组件的状态（Compose 之后套用）
        private static byte[] CaptureState(VaultScenario instance) {
            try {
                return Capture(writer => {
                    instance.WriteArguments(writer);
                    instance.WriteNetState(writer);
                });
            } catch (Exception ex) {
                ScenarioPlatform.Error($"Scenario.Net.Write.{instance.Key}", $"[Scenarios] Writing the network state of {instance.Key} threw: {ex}");
                return [];
            }
        }

        //客户端请求只带同步成员：附加数据与组件状态由服务器上的实例自己产生
        private static byte[] CaptureArguments(VaultScenario instance) {
            try {
                return Capture(instance.WriteArguments);
            } catch (Exception ex) {
                ScenarioPlatform.Error($"Scenario.Net.Write.{instance.Key}", $"[Scenarios] Writing the request arguments of {instance.Key} threw: {ex}");
                return [];
            }
        }

        //每个实例的状态单独带长度前缀：场景或组件的读写不对称时只坏自己这一段，不会把整个包读错位
        private static void WriteBlob(BinaryWriter writer, byte[] blob) {
            if (blob == null || blob.Length > MaxBlobLength) {
                if (blob != null) {
                    ScenarioPlatform.Error("Scenario.Net.BlobTooLarge", $"[Scenarios] A scenario's network state is {blob.Length} bytes (limit {MaxBlobLength}); it was not sent");
                }
                writer.Write((ushort)0);
                return;
            }
            writer.Write((ushort)blob.Length);
            writer.Write(blob);
        }

        private static byte[] ReadBlob(BinaryReader reader) {
            int length = reader.ReadUInt16();
            if (length > MaxBlobLength) {
                throw new InvalidDataException($"scenario state of {length} bytes exceeds the limit");
            }
            byte[] blob = reader.ReadBytes(length);
            if (blob.Length != length) {
                throw new EndOfStreamException("scenario state is truncated");
            }
            return blob;
        }

        private static string ReadText(BinaryReader reader, int limit) {
            string text = reader.ReadString();
            if (text.Length > limit) {
                throw new InvalidDataException($"scenario text of {text.Length} characters exceeds the limit");
            }
            return text;
        }

        //数据包里下发给客户端的部分：框架自己的键只在裁决端用，不发。写不出来或超限时整个不发，客户端读到的就只有它自己写的内容
        private static byte[] CapturePayload(ScenarioTransition transition) {
            try {
                TagCompound shared = null;
                foreach (KeyValuePair<string, object> pair in transition.Payload) {
                    if (!pair.Key.StartsWith(ReservedPayloadPrefix, StringComparison.Ordinal)) {
                        (shared ??= [])[pair.Key] = pair.Value;
                    }
                }
                if (shared == null) {
                    return [];
                }
                byte[] blob = Capture(writer => TagIO.Write(shared, writer));
                if (blob.Length > MaxPayloadLength) {
                    ScenarioPlatform.Error("Scenario.Net.PayloadTooLarge",
                        $"[Scenarios] The travel payload is {blob.Length} bytes (limit {MaxPayloadLength}); it was not sent to clients");
                    return [];
                }
                return blob;
            } catch (Exception ex) {
                ScenarioPlatform.Error("Scenario.Net.PayloadWrite", $"[Scenarios] Writing the travel payload for clients threw: {ex}");
                return [];
            }
        }

        //（客户端）把服务器发来的数据包并进这一端的转场：同名的键以服务器为准，这一端自己写的其余键保留
        private static void MergeIncomingPayload(ScenarioTransition transition) {
            byte[] blob = _incomingPayload;
            _incomingPayload = null;
            if (blob == null || blob.Length == 0 || transition == null) {
                return;
            }
            try {
                using MemoryStream stream = new(blob, writable: false);
                using BinaryReader reader = new(stream);
                foreach (KeyValuePair<string, object> pair in TagIO.Read(reader)) {
                    transition.Payload[pair.Key] = pair.Value;
                }
            } catch (Exception ex) {
                ScenarioPlatform.Error("Scenario.Net.PayloadRead", $"[Scenarios] Reading the travel payload from the server threw: {ex}");
            }
        }

        private static Action<VaultScenario> ArgumentSetup(byte[] arguments) {
            if (arguments == null || arguments.Length == 0) {
                return null;
            }
            return created => {
                using MemoryStream stream = new(arguments, writable: false);
                using BinaryReader reader = new(stream);
                created.ReadArguments(reader);
            };
        }

        private static bool TryCreateFromState(VaultScenario template, byte[] state, out VaultScenario instance, out ScenarioResult failure) {
            if (state == null || state.Length == 0) {
                return TryCreate(template, null, out instance, out failure);
            }
            using MemoryStream stream = new(state, writable: false);
            using BinaryReader reader = new(stream);
            return TryCreate(template, created => created.ReadArguments(reader), out instance, out failure,
                created => created.ReadNetState(reader));
        }

        //客户端：按服务器发来的状态建一个镜像实例（尚未激活）。建不出来时记错并返回空
        private static VaultScenario CreateMirror(string key, byte[] state, int netId) {
            VaultScenario template = VaultScenario.FindTemplate(key);
            if (template == null) {
                ScenarioPlatform.Error($"Scenario.Net.Unknown.{key}", $"[Scenarios] The server activated {key}, which is not registered on this client");
                return null;
            }
            if (!TryCreateFromState(template, state, out VaultScenario instance, out ScenarioResult failure)) {
                ScenarioPlatform.Error($"Scenario.Net.Mirror.{key}", $"[Scenarios] Mirroring {key} from the server failed: {failure}");
                return null;
            }
            instance.NetId = netId;
            return instance;
        }

        private static void ApplyState(VaultScenario instance, byte[] state) {
            if (state == null || state.Length == 0) {
                return;
            }
            try {
                using MemoryStream stream = new(state, writable: false);
                using BinaryReader reader = new(stream);
                instance.ReadArguments(reader);
                //服务器改了节点开关：这一端跟着套用（重建合成快照、通知节点）。尚未激活的实例只记下开关，激活时作为初始状态
                if (instance.ReadNetState(reader) && instance.Status == ScenarioStatus.Active) {
                    ApplyNodeSwitches(instance);
                }
            } catch (Exception ex) {
                ScenarioPlatform.Error($"Scenario.Net.Read.{instance.Key}", $"[Scenarios] Reading the network state of {instance.Key} threw: {ex}");
            }
        }

        #endregion

        #region 客户端：发请求

        private static ScenarioResult RequestActivate(VaultScenario template, VaultScenario parent, Action<VaultScenario> setup) {
            //本地先组合一遍：声明不合法时不必发包，同时取出要随请求发送的参数
            if (!TryCreate(template, setup, out VaultScenario probe, out ScenarioResult failure)) {
                return failure;
            }
            if (probe.OwnsStage) {
                return Reject(ScenarioRejectReason.RequiresTravel, template.Key);
            }
            SendRequest(ScenarioRequestKind.Activate, template, parent?.NetId ?? 0, null, probe);
            return ScenarioResult.Sent();
        }

        private static ScenarioResult RequestDeactivate(VaultScenario instance) {
            SendRequest(ScenarioRequestKind.Deactivate, instance.Template, instance.NetId, null, null);
            return ScenarioResult.Sent();
        }

        private static ScenarioResult RequestTravel(VaultScenario template, string anchor, Action<VaultScenario> setup) {
            if (!TryCreate(template, setup, out VaultScenario probe, out ScenarioResult failure)) {
                return failure;
            }
            if (!probe.OwnsStage) {
                return Reject(ScenarioRejectReason.RequiresStage, template.Key);
            }
            string invalid = probe.Stage.ValidateStage();
            if (invalid != null) {
                return Reject(ScenarioRejectReason.InvalidStage, $"{template.Key}: {invalid}");
            }
            SendRequest(ScenarioRequestKind.Travel, template, 0, anchor, probe);
            return ScenarioResult.Sent();
        }

        //reference：激活请求里是父场景的编号（0 表示没有指定），停用请求里是要停用的实例的编号
        private static void SendRequest(ScenarioRequestKind kind, VaultScenario template, int reference, string anchor, VaultScenario probe) {
            byte[] arguments = probe == null ? [] : CaptureArguments(probe);
            string key = template.Key;
            ScenarioPlatform.Net?.SendToServer(ScenarioNetOp.Request, writer => {
                writer.Write((byte)kind);
                writer.Write(key);
                writer.Write(reference);
                writer.Write(anchor ?? string.Empty);
                WriteBlob(writer, arguments);
            });
        }

        #endregion

        #region 服务器：场景树的同步

        //一个不拥有舞台的实例刚进入激活状态。舞台场景不在这里发：客户端在进入舞台时经快照拿到它
        private static void AnnounceActivated(VaultScenario instance) {
            if (!ScenarioPlatform.IsServer || ScenarioPlatform.Net == null || instance.OwnsStage || instance.NetId == 0) {
                return;
            }
            SendSpawn(-1, instance);
        }

        private static void AnnounceDeactivated(VaultScenario instance) {
            if (!ScenarioPlatform.IsServer || ScenarioPlatform.Net == null || instance.NetId == 0) {
                return;
            }
            int netId = instance.NetId;
            ScenarioPlatform.Net.SendToClient(-1, ScenarioNetOp.End, writer => writer.Write(netId));
        }

        private static void SendSpawn(int client, VaultScenario instance) {
            byte[] state = CaptureState(instance);
            int netId = instance.NetId;
            string key = instance.Template.Key;
            int parentId = instance.Parent?.NetId ?? 0;
            //没有父场景的非舞台实例就是会话层的
            bool sessionLayer = instance.Parent == null;
            ScenarioPlatform.Net.SendToClient(client, ScenarioNetOp.Spawn, writer => {
                writer.Write(netId);
                writer.Write(key);
                writer.Write(parentId);
                writer.Write(sessionLayer);
                WriteBlob(writer, state);
            });
        }

        //每帧结束前把标了 NetUpdate 的实例重新发给客户端
        private static void FlushNetUpdates() {
            if (!ScenarioPlatform.IsServer || ScenarioPlatform.Net == null) {
                return;
            }
            for (int i = 0; i < _active.Count; i++) {
                VaultScenario instance = _active[i];
                if (!instance.NetUpdate) {
                    continue;
                }
                instance.NetUpdate = false;
                if (instance.Status != ScenarioStatus.Active || instance.NetId == 0) {
                    continue;
                }
                byte[] state = CaptureState(instance);
                int netId = instance.NetId;
                ScenarioPlatform.Net.SendToClient(-1, ScenarioNetOp.State, writer => {
                    writer.Write(netId);
                    WriteBlob(writer, state);
                });
            }
        }

        //给刚出生在当前舞台里的客户端发整棵场景树：开头是舞台场景与这次抵达的安排，其余实例按激活顺序逐个发（父场景总在子场景之前）
        private static void SendSnapshot(int client, bool traveler) {
            IScenarioNetHost net = ScenarioPlatform.Net;
            VaultScenario stage = Stage;
            if (net == null || stage == null || stage.NetId == 0) {
                return;
            }
            bool pending = IsTraveling && TravelPhase == ScenarioTravelPhase.Departing && _travel?.To != null;
            bool waitForOpen = traveler && IsTraveling && TravelPhase == ScenarioTravelPhase.Loading && _awaiting.Contains(client);
            Point landing = default;
            bool hasLanding = traveler && _lastTravel != null
                && ScenarioPlatform.StageBackend != null && ScenarioPlatform.StageBackend.TryResolveLanding(_lastTravel, out landing);

            //随换舞台抵达的客户端先拿到这次转场的数据包：紧跟着的快照会激活新舞台场景，那时钩子读到的就是裁决端写下的内容
            if (traveler && _lastTravel != null) {
                byte[] payload = CapturePayload(_lastTravel);
                if (payload.Length > 0) {
                    net.SendToClient(client, ScenarioNetOp.TravelPayload, writer => {
                        writer.Write(payload.Length);
                        writer.Write(payload);
                    });
                }
            }

            byte flags = 0;
            if (pending) {
                flags |= SnapshotPendingTravel;
            }
            if (waitForOpen) {
                flags |= SnapshotWaitForOpen;
            }
            if (hasLanding) {
                flags |= SnapshotHasLanding;
            }
            int travelId = _travelId;
            int stageId = stage.NetId;
            string stageKey = stage.Template.Key;
            byte[] stageState = CaptureState(stage);
            VaultScenario target = pending ? _travel.To : null;
            int targetId = target?.NetId ?? 0;
            string targetKey = target?.Template.Key;
            string targetAnchor = pending ? _travel.Anchor ?? string.Empty : null;
            byte[] targetState = pending ? CaptureState(target) : null;
            net.SendToClient(client, ScenarioNetOp.Snapshot, writer => {
                writer.Write(flags);
                writer.Write(travelId);
                writer.Write(stageId);
                writer.Write(stageKey);
                WriteBlob(writer, stageState);
                if (pending) {
                    writer.Write(targetId);
                    writer.Write(targetKey);
                    writer.Write(targetAnchor);
                    WriteBlob(writer, targetState);
                }
                if (hasLanding) {
                    writer.Write(landing.X);
                    writer.Write(landing.Y);
                }
            });
            for (int i = 0; i < _active.Count; i++) {
                VaultScenario instance = _active[i];
                if (ReferenceEquals(instance, stage) || instance.OwnsStage || instance.Status != ScenarioStatus.Active || instance.NetId == 0) {
                    continue;
                }
                SendSpawn(client, instance);
            }
            net.SendToClient(client, ScenarioNetOp.SnapshotEnd, static _ => { });
            if (pending) {
                //赶上离场的客户端也要等它回报幕布合上
                _awaiting.Add(client);
            }
        }

        #endregion

        #region 服务器：处理客户端请求

        private static void ServeRequest(int sender, ScenarioRequestKind kind, string key, int reference, string anchor, byte[] arguments) {
            VaultScenario template = VaultScenario.FindTemplate(key);
            ScenarioResult result;
            try {
                result = kind switch {
                    ScenarioRequestKind.Activate => ServeActivate(sender, template, reference, arguments),
                    ScenarioRequestKind.Deactivate => ServeDeactivate(sender, reference),
                    ScenarioRequestKind.Travel => ServeTravel(sender, template, anchor, arguments),
                    _ => Reject(ScenarioRejectReason.NotRegistered, $"unknown request kind {(int)kind}"),
                };
            } catch (Exception ex) {
                ScenarioPlatform.Error($"Scenario.Net.Request.{key}", $"[Scenarios] Handling a {kind} request for {key} from player {sender} threw: {ex}");
                result = Reject(ScenarioRejectReason.InvalidComposition, ex.Message);
            }
            if (result.Accepted) {
                return;
            }
            //拒绝一律记日志并回告发起者：静默的拒绝在联机里无从排查
            ScenarioPlatform.Info($"[Scenarios] {kind} request for {key} from player {sender} was rejected: {result}");
            ScenarioRejectReason reason = result.Reason;
            string detail = result.Detail ?? string.Empty;
            if (detail.Length > MaxDetailLength) {
                detail = detail[..MaxDetailLength];
            }
            ScenarioPlatform.Net?.SendToClient(sender, ScenarioNetOp.Reject, writer => {
                writer.Write((byte)kind);
                writer.Write(key);
                writer.Write((byte)reason);
                writer.Write(detail);
            });
        }

        private static ScenarioResult ServeActivate(int sender, VaultScenario template, int reference, byte[] arguments) {
            if (template == null) {
                return Reject(ScenarioRejectReason.NotRegistered, "the scenario is not registered on the server");
            }
            ScenarioRejectReason reason = Review();
            if (reason != ScenarioRejectReason.None) {
                return Reject(reason);
            }
            VaultScenario parent = null;
            if (reference != 0) {
                parent = FindByNetId(reference);
                if (parent == null || parent.Status != ScenarioStatus.Active) {
                    return Reject(ScenarioRejectReason.InvalidParent, $"no active instance #{reference}");
                }
            }
            return ActivateCore(template, parent, ArgumentSetup(arguments),
                instance => Authorize(new ScenarioRequest(ScenarioRequestKind.Activate, template, instance, parent, null, null, sender, arguments)));
        }

        private static ScenarioResult ServeDeactivate(int sender, int reference) {
            VaultScenario instance = FindByNetId(reference);
            if (instance == null || instance.Status != ScenarioStatus.Active) {
                return Reject(ScenarioRejectReason.NotActive, $"no active instance #{reference}");
            }
            if (instance.OwnsStage) {
                return Reject(ScenarioRejectReason.CannotDeactivateStage, instance.Key);
            }
            ScenarioRejectReason reason = Review();
            if (reason != ScenarioRejectReason.None) {
                return Reject(reason);
            }
            if (Authorize(new ScenarioRequest(ScenarioRequestKind.Deactivate, instance.Template, instance, null, null, null, sender, null)) is ScenarioResult denied) {
                return denied;
            }
            return Deactivate(instance);
        }

        private static ScenarioResult ServeTravel(int sender, VaultScenario template, string anchor, byte[] arguments) {
            if (template == null) {
                return Reject(ScenarioRejectReason.NotRegistered, "the scenario is not registered on the server");
            }
            ScenarioRejectReason reason = Review();
            if (reason != ScenarioRejectReason.None) {
                return Reject(reason);
            }
            if (Stage != null && ReferenceEquals(Stage.Template, template)) {
                return Reject(ScenarioRejectReason.AlreadyThere, template.Key);
            }
            VaultScenario leaving = Stage;
            return TravelCore(template, anchor, ArgumentSetup(arguments),
                instance => Authorize(new ScenarioRequest(ScenarioRequestKind.Travel, template, instance, null, leaving, anchor, sender, arguments)));
        }

        //裁决钩子：请求涉及的实例，换舞台时再问将被离开的那块舞台，然后是各个全局钩子。钩子抛异常按拒绝处理。返回空表示放行
        private static ScenarioResult? Authorize(ScenarioRequest request) {
            VaultScenario instance = request.Instance;
            if (!Guard(instance, "AuthorizeRequest", () => instance.InvokeAuthorize(request), false)) {
                return Reject(ScenarioRejectReason.Unauthorized, $"{instance.Key} refused the request");
            }
            //回主世界的目标是框架自己的根场景，消费者没法重写它：要不要放人走，只有被离开的舞台自己清楚
            VaultScenario leaving = request.From;
            if (leaving != null && !ReferenceEquals(leaving, instance) && leaving.Status == ScenarioStatus.Active
                && !Guard(leaving, "AuthorizeRequest", () => leaving.InvokeAuthorize(request), false)) {
                return Reject(ScenarioRejectReason.Unauthorized, $"{leaving.Key} refused the request");
            }
            foreach (GlobalScenario global in GlobalScenario.Instances) {
                if (!Guard(instance, $"{global.GetType().Name}.AuthorizeRequest", () => global.AuthorizeRequest(instance, request), false)) {
                    return Reject(ScenarioRejectReason.Unauthorized, $"{global.GetType().Name} refused the request");
                }
            }
            return null;
        }

        #endregion

        #region 服务器：换舞台

        //离场：通知已经在当前舞台里的客户端播离场演出、合上幕布，等它们回报
        private static void BeginServerDeparture(ScenarioTransition transition) {
            _travelId = _travelId == int.MaxValue ? 1 : _travelId + 1;
            _waitingForClip = false;
            _waitFrames = 0;
            _awaiting.Clear();
            VaultScenario target = transition.To;
            //目标舞台场景的编号先定下来：客户端按它认出抵达后快照里的同一个实例
            if (target.NetId == 0) {
                target.NetId = NextNetId();
            }
            IScenarioNetHost net = ScenarioPlatform.Net;
            if (net == null) {
                return;
            }
            net.CollectClients(_awaiting);
            byte[] state = CaptureState(target);
            int travelId = _travelId;
            int netId = target.NetId;
            string key = target.Template.Key;
            string anchor = transition.Anchor ?? string.Empty;
            net.SendToClient(-1, ScenarioNetOp.TravelBegin, writer => {
                writer.Write(travelId);
                writer.Write(netId);
                writer.Write(key);
                writer.Write(anchor);
                WriteBlob(writer, state);
            });
        }

        private static void AdvanceServerTravel() {
            switch (TravelPhase) {
                case ScenarioTravelPhase.Departing:
                    AdvanceServerDeparture();
                    break;
                case ScenarioTravelPhase.Loading:
                    //换世界在 Begin 那一次调用里同步做完，跨帧停在这个阶段只可能是在等客户端到场
                    AdvanceServerArrivalWait();
                    break;
                case ScenarioTravelPhase.Arriving:
                    AdvanceArrival();
                    break;
            }
        }

        private static void AdvanceServerDeparture() {
            IScenarioStageBackend backend = ScenarioPlatform.StageBackend;
            string notReady = backend == null ? "no stage backend installed on this endpoint" : backend.CheckReady(_travel);
            if (notReady != null) {
                int travelId = _travelId;
                CancelTravel(notReady);
                ScenarioPlatform.Net?.SendToClient(-1, ScenarioNetOp.TravelCancel, writer => writer.Write(travelId));
                return;
            }
            PruneDisconnected(_awaiting);
            _waitFrames++;
            int deadline = (_departureClip != null ? Math.Max(_departureTimeout, 0) : 0) + CloseFrames + DepartureGraceFrames;
            if (_awaiting.Count > 0 && _waitFrames < deadline) {
                return;
            }
            BeginBackendTravel();
        }

        //提交：服务器的旧舞台子树已经停用。让客户端停用各自的旧舞台子树并卸载本地世界，然后把它们置为在途（此后不再向它们广播世界同步）
        private static void CommitServerTravel(ScenarioTransition transition) {
            if (!ScenarioPlatform.IsServer) {
                return;
            }
            _lastTravel = transition;
            _awaiting.Clear();
            _waitFrames = 0;
            IScenarioNetHost net = ScenarioPlatform.Net;
            if (net == null) {
                return;
            }
            int travelId = _travelId;
            net.SendToClient(-1, ScenarioNetOp.TravelCommit, writer => writer.Write(travelId));
            _clientScratch.Clear();
            net.CollectClients(_clientScratch);
            foreach (int client in _clientScratch) {
                _inTransit.Add(client);
                _leaving.Add(client);
                _awaiting.Add(client);
            }
            ScenarioPlatform.StageBackend?.OnTravelCommitted(_clientScratch);
        }

        //目标舞台读不进来、改回主世界：在途客户端的落点改按新的转场解析
        private static void RedirectServerTravel(ScenarioTransition redirected) {
            if (ScenarioPlatform.IsServer) {
                _lastTravel = redirected;
            }
        }

        //服务器读完了新舞台（新舞台场景已在 NotifyStageLoaded 里激活）：幕布继续合着，等在途的客户端到场
        private static void ServerStageReady() {
            TravelPhase = ScenarioTravelPhase.Loading;
            _waitFrames = 0;
        }

        private static void AdvanceServerArrivalWait() {
            PruneDisconnected(_awaiting);
            _waitFrames++;
            if (_awaiting.Count > 0 && _waitFrames < ArrivalTimeoutFrames) {
                return;
            }
            if (_awaiting.Count > 0) {
                ScenarioPlatform.Info($"[Scenarios] {_awaiting.Count} client(s) had not arrived in {Stage?.Key} after {ArrivalTimeoutFrames} frames; opening the curtain without them");
            }
            _awaiting.Clear();
            TravelPhase = ScenarioTravelPhase.Arriving;
            _phaseFrames = 0;
            _curtainCoverage = 1f;
            int travelId = _travelId;
            ScenarioPlatform.Net?.SendToClient(-1, ScenarioNetOp.TravelOpen, writer => writer.Write(travelId));
            if (OpenFrames == 0) {
                FinishTravel();
            }
        }

        private static void ServeTravelReady(int sender, int travelId) {
            if (IsTraveling && TravelPhase == ScenarioTravelPhase.Departing && travelId == _travelId) {
                _awaiting.Remove(sender);
            }
        }

        private static void ServeClientLeft(int sender) {
            //只有刚随队离开的客户端需要重新接收世界，而且只接纳一次；其余的请求无视，免得被用来反复索要区块
            if (!_leaving.Remove(sender) || !_inTransit.Contains(sender)) {
                return;
            }
            ScenarioPlatform.StageBackend?.AdmitClient(sender);
        }

        private static void ServeStageEntered(int sender) {
            //还没回报卸载完旧舞台的客户端发来的进入通知，是它在旧舞台里出生时发的（那时这次换舞台还没提交）：
            //不能当成抵达，等它真正出生在新舞台里会再发一次
            if (!InSession || _leaving.Contains(sender)) {
                return;
            }
            bool traveler = _inTransit.Remove(sender);
            ScenarioPlatform.StageBackend?.OnClientEntered(sender, traveler);
            SendSnapshot(sender, traveler);
        }

        private static void ServeTravelArrived(int sender, int travelId) {
            if (travelId == _travelId) {
                _awaiting.Remove(sender);
            }
        }

        #endregion

        #region 客户端：场景树镜像

        //客户端的本地玩家出生进入舞台：进服时开始会话，换舞台途中则是抵达了新舞台。两种情况都向服务器要一份场景树快照
        private static void ClientEnteredStage() {
            if (NetOffline) {
                NoteMultiplayer();
                return;
            }
            IScenarioStageBackend backend = ScenarioPlatform.StageBackend;
            if (IsTraveling && InBackendTravel) {
                TravelPhase = ScenarioTravelPhase.Loading;
                _phaseFrames = 0;
                _curtainCoverage = 1f;
                backend?.OnLocalPlayerEntered();
            }
            else {
                if (InSession) {
                    ScenarioPlatform.Error("Scenario.StaleSession", "[Scenarios] Entered a world while the previous session was still open; ending it now");
                    NotifyDisconnected();
                }
                _sessionActive = true;
                ScenarioPlatform.SessionRole = ScenarioNetRole.Client;
                _curtainCoverage = 0f;
                backend?.OnSessionStarted();
            }
            _landingPending = false;
            _landing = null;
            _waitForOpen = false;
            _openReceived = false;
            _arrivedSent = false;
            _snapshotOpen = false;
            _stallFrames = 0;
            ScenarioPlatform.Net.SendToServer(ScenarioNetOp.StageEntered, static _ => { });
        }

        private static void MirrorSpawn(int netId, string key, int parentId, bool sessionLayer, byte[] state) {
            if (!InSession || netId == 0) {
                return;
            }
            //刚出生、还没收到快照时到达的增量：服务器处理这一端的进入通知时这个实例已经在场景树里，随后的快照会带上它
            if (Stage == null && !_snapshotOpen) {
                return;
            }
            if (_snapshotOpen) {
                _snapshotSeen.Add(netId);
            }
            if (_byNetId.ContainsKey(netId)) {
                //快照里的实例本地已经有了（换舞台后存活下来的会话层场景，或快照与增量重复）
                return;
            }
            VaultScenario parent = null;
            if (parentId != 0) {
                parent = FindByNetId(parentId);
                if (parent == null || parent.Status != ScenarioStatus.Active) {
                    ScenarioPlatform.Error($"Scenario.Net.Orphan.{key}", $"[Scenarios] {key} #{netId} names parent #{parentId}, which is not active on this client; skipped");
                    return;
                }
            }
            else if (!sessionLayer) {
                ScenarioPlatform.Error($"Scenario.Net.Orphan.{key}", $"[Scenarios] {key} #{netId} has neither a parent nor the session layer; skipped");
                return;
            }
            VaultScenario instance = CreateMirror(key, state, netId);
            if (instance == null) {
                return;
            }
            if (instance.OwnsStage) {
                ScenarioPlatform.Error($"Scenario.Net.Stage.{key}", $"[Scenarios] {key} owns a stage and cannot be mirrored as an overlay; skipped");
                return;
            }
            ActivateTree(instance, parent, parent == null, new ScenarioTransition(ScenarioTransitionKind.Activation, null, instance));
        }

        private static void MirrorEnd(int netId) {
            VaultScenario instance = FindByNetId(netId);
            if (instance == null || instance.Status != ScenarioStatus.Active || instance.OwnsStage) {
                return;
            }
            DeactivateTree(instance, new ScenarioTransition(ScenarioTransitionKind.Deactivation, instance, null));
        }

        private static void MirrorState(int netId, byte[] state) {
            VaultScenario instance = FindByNetId(netId);
            if (instance != null) {
                ApplyState(instance, state);
            }
        }

        private static void MirrorReject(ScenarioRequestKind kind, string key, ScenarioRejectReason reason, string detail) {
            ScenarioResult result = Reject(reason, detail.Length == 0 ? null : detail);
            ScenarioPlatform.Info($"[Scenarios] The server rejected the {kind} request for {key}: {result}");
            Action<ScenarioRequestKind, VaultScenario, ScenarioResult> handlers = RequestRejected;
            if (handlers == null) {
                return;
            }
            try {
                handlers(kind, VaultScenario.FindTemplate(key), result);
            } catch (Exception ex) {
                ScenarioPlatform.Error("Scenario.Net.RequestRejected", $"[Scenarios] A RequestRejected handler threw: {ex}");
            }
        }

        //快照开头：建起（或认出）舞台场景的镜像，记下这次抵达的安排。其余实例随后经 Spawn 到达，SnapshotEnd 收尾
        private static void MirrorSnapshot(byte flags, int travelId, int stageId, string stageKey, byte[] stageState,
            int pendingId, string pendingKey, string pendingAnchor, byte[] pendingState, Point? landing) {
            if (!InSession) {
                return;
            }
            _snapshotOpen = true;
            _snapshotSeen.Clear();
            _snapshotSeen.Add(stageId);
            _travelId = travelId;

            if (Stage == null || Stage.NetId != stageId) {
                if (Stage != null) {
                    //本地镜像的舞台与服务器不一致（错过了一次换舞台）：以服务器为准
                    VaultScenario stale = Stage;
                    Stage = null;
                    Root = null;
                    DeactivateTree(stale, new ScenarioTransition(ScenarioTransitionKind.Travel, stale, null));
                }
                VaultScenario arriving = null;
                VaultScenario expected = IsTraveling ? _travel?.To : null;
                if (expected != null && expected.NetId == stageId && expected.Status == ScenarioStatus.Pending) {
                    //离场时就建好的目标实例：补上服务器激活之后的状态（例如生成期间写下的锚点）
                    arriving = expected;
                    ApplyState(arriving, stageState);
                }
                else {
                    arriving = CreateMirror(stageKey, stageState, stageId);
                }
                if (arriving != null) {
                    ScenarioTransition transition;
                    if (IsTraveling) {
                        if (!ReferenceEquals(_travel?.To, arriving)) {
                            //服务器改了目的地（目标舞台读不进来，退回了主世界），或者这一端没赶上离场通知：
                            //换一个转场对象，这一端的停用钩子已经写下的数据包内容接着带过去
                            ScenarioTransition previous = _travel;
                            _travel = new ScenarioTransition(ScenarioTransitionKind.Travel, previous?.From, arriving, previous?.Anchor);
                            previous?.CopyPayloadTo(_travel);
                        }
                        transition = _travel;
                        MergeIncomingPayload(transition);
                    }
                    else {
                        transition = new ScenarioTransition(ScenarioTransitionKind.SessionStart, null, arriving);
                        _incomingPayload = null;
                    }
                    Stage = arriving;
                    Root = arriving as RootScenario;
                    ActivateTree(arriving, null, false, transition);
                }
            }

            _landing = landing;
            _waitForOpen = (flags & SnapshotWaitForOpen) != 0;
            if ((flags & SnapshotPendingTravel) != 0 && !IsTraveling) {
                //进服时正赶上其他人离场：直接合上幕布跟上
                MirrorTravelBegin(travelId, pendingId, pendingKey, pendingAnchor, pendingState, late: true);
            }
        }

        private static void MirrorSnapshotEnd() {
            if (!InSession || !_snapshotOpen) {
                return;
            }
            _snapshotOpen = false;
            //服务器上已经不在的镜像：这一端在途期间被停用的会话层场景
            VaultScenario[] mirrors = [.. _active];
            foreach (VaultScenario mirror in mirrors) {
                if (mirror.Status == ScenarioStatus.Active && mirror.NetId != 0 && !_snapshotSeen.Contains(mirror.NetId)) {
                    DeactivateTree(mirror, new ScenarioTransition(ScenarioTransitionKind.Deactivation, mirror, null));
                }
            }
            _snapshotSeen.Clear();
            if (IsTraveling && TravelPhase == ScenarioTravelPhase.Loading) {
                ScenarioPlatform.StageBackend?.PlaceLocalPlayer(_travel, _landing);
                _landingPending = true;
                _stallFrames = 0;
            }
        }

        #endregion

        #region 客户端：换舞台

        private static void MirrorTravelBegin(int travelId, int netId, string key, string anchor, byte[] state, bool late) {
            if (!InSession || Stage == null) {
                //还没拿到场景树：稍后的快照会带上这次离场
                return;
            }
            if (IsTraveling) {
                if (travelId == _travelId) {
                    return;
                }
                CancelTravel("superseded by a newer travel from the server");
            }
            VaultScenario target = CreateMirror(key, state, netId);
            ScenarioTransition transition = new(ScenarioTransitionKind.Travel, Stage, target, string.IsNullOrEmpty(anchor) ? null : anchor);
            IsTraveling = true;
            _travel = transition;
            _travelId = travelId;
            _curtain = ResolveCurtain(transition);
            _phaseFrames = 0;
            _stallFrames = 0;
            _readySent = false;
            TravelPhase = ScenarioTravelPhase.Departing;
            ScenarioCinematics cinematics = transition.From?.FindEnabled<ScenarioCinematics>();
            _departureClip = cinematics?.DepartureClip;
            _departureTimeout = cinematics?.DepartureTimeout ?? ScenarioCinematics.DefaultDepartureTimeout;
            _waitingForClip = !late && _departureClip != null && CutsceneDirector.Play(_departureClip);
            if (late) {
                _curtainCoverage = 1f;
            }
            RaiseTravelBegan(transition);
        }

        private static void MirrorTravelCancel(int travelId) {
            if (IsTraveling && TravelPhase == ScenarioTravelPhase.Departing && travelId == _travelId) {
                CancelTravel("the server cancelled the travel");
            }
        }

        //服务器已经停用旧舞台并开始换世界：这一端也停用旧舞台子树，卸载本地世界，然后在原连接上重新接收新舞台
        private static void MirrorTravelCommit(int travelId) {
            if (!InSession) {
                return;
            }
            if (IsTraveling && TravelPhase != ScenarioTravelPhase.Departing) {
                //已经在换了（重复的提交）
                return;
            }
            if (!IsTraveling || _travelId != travelId) {
                //没赶上这次离场的通知：目标场景从抵达后的快照里拿
                if (IsTraveling) {
                    CancelTravel("superseded by a newer travel from the server");
                }
                IsTraveling = true;
                _travel = new ScenarioTransition(ScenarioTransitionKind.Travel, Stage, null);
                _travelId = travelId;
                _curtain = null;
                RaiseTravelBegan(_travel);
            }
            _waitingForClip = false;
            _curtainCoverage = 1f;
            _stallFrames = 0;
            BeginBackendTravel();
        }

        //客户端后端卸载完本地的旧舞台时在主线程调用：告诉服务器可以下发新舞台了
        internal static void NotifyLeftStage() {
            if (!ScenarioPlatform.IsClient) {
                return;
            }
            int travelId = _travelId;
            ScenarioPlatform.Net?.SendToServer(ScenarioNetOp.ClientLeft, writer => writer.Write(travelId));
        }

        //客户端与服务器的连接断开时由宿主在主线程调用。断开时可能正卡在换舞台的菜单阶段，那时原版不会派发世界卸载：
        //直接结束这次切换与会话，免得状态留到下一次进入世界
        internal static void NotifyDisconnected() {
            if (!InSession) {
                return;
            }
            DropTravel();
            EndSession(stageAlreadySaved: true);
        }

        private static void AdvanceClientTravel() {
            switch (TravelPhase) {
                case ScenarioTravelPhase.Departing:
                    AdvanceClientDeparture();
                    break;
                case ScenarioTravelPhase.Loading:
                    AdvanceClientLanding();
                    break;
                case ScenarioTravelPhase.Arriving:
                    AdvanceArrival();
                    break;
            }
        }

        //离场：等离场演出播完（或超时），合上幕布，回报服务器，然后盖着幕布等提交
        private static void AdvanceClientDeparture() {
            if (_waitingForClip) {
                if (CutsceneDirector.CurrentClip?.GetType() == _departureClip && _phaseFrames < _departureTimeout) {
                    _phaseFrames++;
                    return;
                }
                _waitingForClip = false;
                _phaseFrames = 0;
            }
            if (_curtainCoverage < 1f) {
                int close = CloseFrames;
                _phaseFrames++;
                _curtainCoverage = close == 0 ? 1f : Math.Min(1f, _phaseFrames / (float)close);
                if (_curtainCoverage < 1f) {
                    return;
                }
            }
            if (!_readySent) {
                _readySent = true;
                int travelId = _travelId;
                ScenarioPlatform.Net?.SendToServer(ScenarioNetOp.TravelReady, writer => writer.Write(travelId));
            }
            if (++_stallFrames > ClientStallFrames) {
                CancelTravel("the server never committed the travel");
            }
        }

        //抵达：已经出生在新舞台里、幕布合着。依次等快照、等落点所在的区块到齐、等服务器通知拉幕，每一步都有超时
        private static void AdvanceClientLanding() {
            _stallFrames++;
            if (!_landingPending) {
                if (_stallFrames > ClientStallFrames) {
                    ScenarioPlatform.Info("[Scenarios] The server never sent the scenario snapshot for this stage; opening the curtain anyway");
                    BeginArrival();
                }
                return;
            }
            IScenarioStageBackend backend = ScenarioPlatform.StageBackend;
            if (backend != null && !backend.IsLocalPlayerLoaded() && _stallFrames < LandingWaitFrames) {
                return;
            }
            if (_waitForOpen && !_openReceived) {
                if (!_arrivedSent) {
                    _arrivedSent = true;
                    int travelId = _travelId;
                    ScenarioPlatform.Net?.SendToServer(ScenarioNetOp.TravelArrived, writer => writer.Write(travelId));
                }
                if (_stallFrames < ClientStallFrames) {
                    return;
                }
            }
            BeginArrival();
        }

        private static void BeginArrival() {
            _landingPending = false;
            TravelPhase = ScenarioTravelPhase.Arriving;
            _phaseFrames = 0;
            _curtainCoverage = 1f;
            if (OpenFrames == 0) {
                FinishTravel();
            }
        }

        private static void MirrorTravelOpen(int travelId) {
            if (IsTraveling && travelId == _travelId) {
                _openReceived = true;
            }
        }

        #endregion

        //宿主收到一条场景消息时在主线程调用。每种消息先把字段全部读完再判断方向与合法性：提前返回会把没读完的字节留给后面的读取
        internal static void HandleNet(ScenarioNetOp op, BinaryReader reader, int sender) {
            bool server = ScenarioPlatform.IsServer;
            bool client = ScenarioPlatform.IsClient;
            switch (op) {
                case ScenarioNetOp.Request: {
                    ScenarioRequestKind kind = (ScenarioRequestKind)reader.ReadByte();
                    string key = ReadText(reader, MaxKeyLength);
                    int reference = reader.ReadInt32();
                    string anchor = ReadText(reader, MaxKeyLength);
                    byte[] arguments = ReadBlob(reader);
                    if (server) {
                        ServeRequest(sender, kind, key, reference, anchor.Length == 0 ? null : anchor, arguments);
                    }
                    break;
                }
                case ScenarioNetOp.Reject: {
                    ScenarioRequestKind kind = (ScenarioRequestKind)reader.ReadByte();
                    string key = ReadText(reader, MaxKeyLength);
                    ScenarioRejectReason reason = (ScenarioRejectReason)reader.ReadByte();
                    string detail = ReadText(reader, MaxDetailLength);
                    if (client) {
                        MirrorReject(kind, key, reason, detail);
                    }
                    break;
                }
                case ScenarioNetOp.Spawn: {
                    int netId = reader.ReadInt32();
                    string key = ReadText(reader, MaxKeyLength);
                    int parentId = reader.ReadInt32();
                    bool sessionLayer = reader.ReadBoolean();
                    byte[] state = ReadBlob(reader);
                    if (client) {
                        MirrorSpawn(netId, key, parentId, sessionLayer, state);
                    }
                    break;
                }
                case ScenarioNetOp.End: {
                    int netId = reader.ReadInt32();
                    if (client) {
                        MirrorEnd(netId);
                    }
                    break;
                }
                case ScenarioNetOp.State: {
                    int netId = reader.ReadInt32();
                    byte[] state = ReadBlob(reader);
                    if (client) {
                        MirrorState(netId, state);
                    }
                    break;
                }
                case ScenarioNetOp.TravelBegin: {
                    int travelId = reader.ReadInt32();
                    int netId = reader.ReadInt32();
                    string key = ReadText(reader, MaxKeyLength);
                    string anchor = ReadText(reader, MaxKeyLength);
                    byte[] state = ReadBlob(reader);
                    if (client) {
                        MirrorTravelBegin(travelId, netId, key, anchor, state, late: false);
                    }
                    break;
                }
                case ScenarioNetOp.TravelReady: {
                    int travelId = reader.ReadInt32();
                    if (server) {
                        ServeTravelReady(sender, travelId);
                    }
                    break;
                }
                case ScenarioNetOp.TravelCancel: {
                    int travelId = reader.ReadInt32();
                    if (client) {
                        MirrorTravelCancel(travelId);
                    }
                    break;
                }
                case ScenarioNetOp.TravelCommit: {
                    int travelId = reader.ReadInt32();
                    if (client) {
                        MirrorTravelCommit(travelId);
                    }
                    break;
                }
                case ScenarioNetOp.ClientLeft: {
                    reader.ReadInt32();
                    if (server) {
                        ServeClientLeft(sender);
                    }
                    break;
                }
                case ScenarioNetOp.StageEntered: {
                    if (server) {
                        ServeStageEntered(sender);
                    }
                    break;
                }
                case ScenarioNetOp.Snapshot: {
                    byte flags = reader.ReadByte();
                    int travelId = reader.ReadInt32();
                    int stageId = reader.ReadInt32();
                    string stageKey = ReadText(reader, MaxKeyLength);
                    byte[] stageState = ReadBlob(reader);
                    int pendingId = 0;
                    string pendingKey = null;
                    string pendingAnchor = null;
                    byte[] pendingState = null;
                    if ((flags & SnapshotPendingTravel) != 0) {
                        pendingId = reader.ReadInt32();
                        pendingKey = ReadText(reader, MaxKeyLength);
                        pendingAnchor = ReadText(reader, MaxKeyLength);
                        pendingState = ReadBlob(reader);
                    }
                    Point? landing = null;
                    if ((flags & SnapshotHasLanding) != 0) {
                        int x = reader.ReadInt32();
                        int y = reader.ReadInt32();
                        landing = new Point(x, y);
                    }
                    if (client) {
                        MirrorSnapshot(flags, travelId, stageId, stageKey, stageState, pendingId, pendingKey, pendingAnchor, pendingState, landing);
                    }
                    break;
                }
                case ScenarioNetOp.SnapshotEnd: {
                    if (client) {
                        MirrorSnapshotEnd();
                    }
                    break;
                }
                case ScenarioNetOp.TravelArrived: {
                    int travelId = reader.ReadInt32();
                    if (server) {
                        ServeTravelArrived(sender, travelId);
                    }
                    break;
                }
                case ScenarioNetOp.TravelOpen: {
                    int travelId = reader.ReadInt32();
                    if (client) {
                        MirrorTravelOpen(travelId);
                    }
                    break;
                }
                case ScenarioNetOp.TravelPayload: {
                    int length = reader.ReadInt32();
                    if (length < 0 || length > MaxPayloadLength) {
                        throw new InvalidDataException($"travel payload of {length} bytes exceeds the limit");
                    }
                    byte[] payload = reader.ReadBytes(length);
                    if (payload.Length != length) {
                        throw new EndOfStreamException("travel payload is truncated");
                    }
                    if (client) {
                        _incomingPayload = payload;
                    }
                    break;
                }
                default:
                    throw new InvalidDataException($"unknown scenario message {(byte)op}");
            }
        }
    }
}
