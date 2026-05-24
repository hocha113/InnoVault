using InnoVault.StateMachines;

namespace InnoVault.BehaviorTrees
{
    /// <summary>
    /// 行为树节点的抽象基类，参考 Unreal AI / Unity 流行 BT 库的通用契约<br/>
    /// 每帧由根节点向下递归调用<see cref="Tick"/>，复合节点根据子节点返回值决定继续/终止；<br/>
    /// 当外部决定中断时（例如 BT 作为 FSM 的某个状态、状态被切走），需要调用<see cref="Reset"/>恢复内部进度
    /// </summary>
    /// <typeparam name="TContext">行为树承载的上下文类型，通常与同位<see cref="VaultStateMachine{TContext}"/>共享</typeparam>
    public abstract class BTNode<TContext>
    {
        /// <summary>
        /// 上一次<see cref="Tick"/>返回的状态。复合节点需要据此知道是否要从"上次<see cref="BTStatus.Running"/>的子节点"继续<br/>
        /// 框架在<see cref="Tick"/>外壳里维护此字段，子类只需要正确返回<see cref="BTStatus"/>即可
        /// </summary>
        public BTStatus LastStatus { get; protected set; } = BTStatus.Failure;

        /// <summary>
        /// 单帧推进。<see cref="Blackboard"/>由根节点和<see cref="BehaviorTrees.BehaviorTreeBuilder"/>注入，可与同位状态机共享
        /// </summary>
        /// <param name="ctx">上下文实例</param>
        /// <param name="blackboard">参数存储；可被同位状态机共享</param>
        /// <returns>本帧执行结果</returns>
        public abstract BTStatus Tick(TContext ctx, Blackboard blackboard);

        /// <summary>
        /// 重置节点内部进度（计时器、子节点索引等）<br/>
        /// 复合节点重置时应递归 Reset 所有子节点，<see cref="BTStatus.Running"/>过程中"被外部强行打断"时调用
        /// </summary>
        public virtual void Reset() {
            LastStatus = BTStatus.Failure;
        }
    }
}
