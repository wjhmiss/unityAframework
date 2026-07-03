using UnityEngine;

namespace AFrameWork.Core
{
    /// <summary>
    /// 移动配置类，用于配置 Rigidbody 的移动控制参数
    /// 注意：移动速度由 ObjectStatsConfig.MoveSpeed 提供，不在此类中配置
    /// </summary>
    [System.Serializable]
    public class MovementConfig
    {
        // 是否保持 Y 轴速度（用于重力）
        [SerializeField]
        [Tooltip("是否保持 Y 轴速度，用于保持重力效果")]
        public bool IsKeepYVelocity = true;

        // 是否自动读取输入
        [SerializeField]
        [Tooltip("是否自动读取键盘输入（WASD 或箭头键）")]
        public bool IsAutoReadInput = true;

        // 停止时的减速速率（惯性控制）
        [SerializeField]
        [Tooltip("停止移动时的减速速率（m/s²）。0 = 无减速（滑行不止），值越大停止越快，20 = 默认，1000+ = 近乎立即停止")]
        public float DecelerationRate = 20f;

        // 移动方向偏移角度（度）
        [SerializeField]
        [Tooltip("移动方向偏移角度（度，绕 Y 轴顺时针为正）。用于补偿视角偏差，0 = 无偏移")]
        public float MovementAngleOffset = 0f;

        /// <summary>
        /// 自定义配置构造函数
        /// </summary>
        public MovementConfig(bool keepYVelocity = true, bool autoReadInput = true, float decelerationRate = 20f, float movementAngleOffset = 0f)
        {
            IsKeepYVelocity = keepYVelocity;
            IsAutoReadInput = autoReadInput;
            DecelerationRate = decelerationRate;
            MovementAngleOffset = movementAngleOffset;
        }
    }

    /// <summary>
    /// MovementConfig 使用说明：
    /// ============================================================
    /// 移动配置数据类，用于配置 ObjectBase 子类的 Rigidbody 移动控制参数。
    /// 注意：移动速度不在本类配置，由 ObjectStatsConfig.MoveSpeed 提供。
    ///
    /// ════════════════════════════════════════════════════════════
    /// 【与 ObjectBase 的关系】
    /// ════════════════════════════════════════════════════════════
    ///   ObjectBase 通过 protected virtual MovementConfig 属性获取配置：
    ///     - 子类重写属性返回 new MovementConfig(...) 提供配置
    ///     - 返回 null 则完全禁用移动控制系统（投射物/陷阱等）
    ///     - ObjectBase.Awake 会一次性缓存到 m_movementConfig 字段
    ///     - 热路径（Update/FixedUpdate）使用缓存字段，避免每帧 new 产生 GC
    ///   如需运行时改变配置，直接修改 ObjectBase.m_movementConfig 字段
    ///
    /// ════════════════════════════════════════════════════════════
    /// 【属性详解】
    /// ════════════════════════════════════════════════════════════
    ///   - IsKeepYVelocity：是否保持 Y 轴速度（用于重力）
    ///     类型：bool，默认值：true
    ///     true  = 移动时保留垂直速度，重力效果正常（角色、怪物）
    ///             ApplyHorizontalVelocity 中：rb.velocity = new Vector3(x, rb.velocity.y, z)
    ///     false = 移动时清零垂直速度，不受重力影响（飞行单位、特殊技能）
    ///             ApplyHorizontalVelocity 中：rb.velocity = m_horizontalVelocity（含 y=0）
    ///
    ///   - IsAutoReadInput：是否自动读取键盘输入（WASD/箭头键）
    ///     类型：bool，默认值：true
    ///     true  = 自动读取输入并移动（玩家控制的角色）
    ///             ObjectBase.HandleInput 在 Update 中读取 Input.GetAxisRaw("Horizontal/Vertical")
    ///             使用 GetAxisRaw 避免平滑插值带来的延迟感
    ///     false = 需手动调用 SetMovementInput() 控制移动（怪物 AI、NPC）
    ///             外部脚本在 Update 中计算方向后调用 SetMovementInput(direction)
    ///
    ///   - DecelerationRate：停止时的减速速率（惯性控制，单位 m/s²）
    ///     类型：float，默认值：20f，范围：[0, +∞)
    ///     0f    = 无减速，角色保持当前速度滑行不止（冰面、绝对光滑）
    ///     5f    = 较大惯性，滑行明显（约 1.2 秒从 6m/s 停止）
    ///     20f   = 默认值，快速停止但有轻微惯性（约 0.3 秒从 6m/s 停止）
    ///     200f  = 近乎立即停止（约 0.04 秒从 6m/s 停止）
    ///     1000f = 几乎瞬时停止（1 帧内停止）
    ///     计算公式：newSpeed = max(0, currentSpeed - DecelerationRate * Time.fixedDeltaTime)
    ///
    ///   - MovementAngleOffset：移动方向偏移角度（度，绕 Y 轴旋转）
    ///     类型：float，默认值：0f，范围：[-360, 360]
    ///     0f    = 无偏移，WASD 按标准方向移动（W=+Z, S=-Z, A=-X, D=+X）
    ///     -45f  = 移动方向左偏 45°（补偿斜视角，W 实际朝 -X+Z 方向移动）
    ///     45f   = 移动方向右偏 45°（补偿斜视角，W 实际朝 +X+Z 方向移动）
    ///     90f   = 移动方向右偏 90°（W 实际朝 +X 方向移动）
    ///     180f  = 反向（W 实际朝 -Z 方向移动）
    ///     用于等距视角（Isometric）、2.5D 等非标准朝向场景的输入方向修正。
    ///     原理：读取输入后用 Quaternion.Euler(0, offset, 0) 旋转移动向量。
    ///
    /// ════════════════════════════════════════════════════════════
    /// 【移动速度说明】
    /// ════════════════════════════════════════════════════════════
    ///   移动速度由 ObjectStatsConfig.MoveSpeed 决定，本类不包含速度参数。
    ///   设计原因：移动速度属于角色属性，与攻击力、生命值等统一管理，
    ///   便于通过Buff、装备、技能等系统动态修改。
    ///   运行时修改速度：warrior.SetMoveSpeed(8f) 或 warrior.GetObjectStats().MoveSpeed = 8f
    ///   读取速度：warrior.GetMoveSpeed()（ObjectBase.HandleMovement 通过此方法获取）
    ///
    /// ════════════════════════════════════════════════════════════
    /// 【技术原理】
    /// ════════════════════════════════════════════════════════════
    ///   1. 缓存速度独立计算减速：
    ///      - ObjectBase 维护 m_horizontalVelocity（缓存水平速度）
    ///      - 减速基于缓存速度计算，不受 Rigidbody 摩擦力影响
    ///      - 确保减速效果精确匹配 DecelerationRate 配置
    ///
    ///   2. 外力响应机制：
    ///      - 当 Rigidbody.velocity 大于缓存速度时（被外力推动），自动同步到缓存
    ///      - 然后按 DecelerationRate 减速，实现被推动后逐渐停止的效果
    ///      - 适用场景：爆炸推力、玩家撞击、技能击退等
    ///
    ///   3. 移动方向偏移实现：
    ///      - Awake 中预计算 Quaternion.Euler(0, MovementAngleOffset, 0) 到 m_movementAngleRotation
    ///      - HandleInput 中读取输入后用预计算的旋转矩阵变换移动向量
    ///      - 通过 m_hasMovementAngleOffset 标志避免无偏移时的多余计算
    ///
    /// ════════════════════════════════════════════════════════════
    /// 【性能优化】（由 ObjectBase 自动处理，子类无感知）
    /// ════════════════════════════════════════════════════════════
    ///   - MovementConfig 属性在子类中返回 new 实例，ObjectBase.Awake 会一次性缓存到 m_movementConfig 字段
    ///   - Update/FixedUpdate 热路径中使用缓存字段，避免每帧 new MovementConfig 产生 GC 分配
    ///   - MovementAngleOffset 对应的 Quaternion.Euler 在 Awake 预计算，避免每帧调用
    ///   - 减速计算复用 currentSpeed（m_horizontalVelocity * (newSpeed / currentSpeed)），避免 .normalized 的重复 Mathf.Sqrt
    ///   - 如需运行时改变配置，直接修改 ObjectBase.m_movementConfig 字段即可
    ///
    /// ════════════════════════════════════════════════════════════
    /// 【使用场景对照表】
    /// ════════════════════════════════════════════════════════════
    ///   场景              | IsKeepYVelocity | IsAutoReadInput | DecelerationRate | MovementAngleOffset
    ///   ------------------|-----------------|-----------------|------------------|-------------------
    ///   玩家角色（俯视）  | true            | true            | 50-200           | 0
    ///   玩家角色（斜45°）| true            | true            | 50-200           | -45 或 45
    ///   怪物（AI 控制）   | true            | false           | 5-20             | 0
    ///   飞行单位          | false           | false           | 30-50            | 0
    ///   投射物/陷阱       | (返回 null 禁用移动系统)            | -                | -
    ///   冰面效果          | true            | true            | 0                | 0
    ///
    /// ────────────────────────────────────────────────────────────
    /// 示例 1：玩家角色（近乎立即停止）
    /// ────────────────────────────────────────────────────────────
    /// <code>
    /// protected override MovementConfig MovementConfig =&gt; new MovementConfig(
    ///     keepYVelocity: true,      // 保持重力效果
    ///     autoReadInput: true,      // 自动读取 WASD/箭头键
    ///     decelerationRate: 200f    // 近乎立即停止（0.04 秒内停止）
    /// );
    /// </code>
    ///
    /// ────────────────────────────────────────────────────────────
    /// 示例 2：玩家角色（默认惯性，稍微滑行）
    /// ────────────────────────────────────────────────────────────
    /// <code>
    /// protected override MovementConfig MovementConfig =&gt; new MovementConfig(
    ///     keepYVelocity: true,
    ///     autoReadInput: true,
    ///     decelerationRate: 20f     // 默认值，快速停止但有轻微惯性
    /// );
    /// </code>
    ///
    /// ────────────────────────────────────────────────────────────
    /// 示例 3：冰面效果（无减速，永久滑行）
    /// ────────────────────────────────────────────────────────────
    /// <code>
    /// protected override MovementConfig MovementConfig =&gt; new MovementConfig(
    ///     keepYVelocity: true,
    ///     autoReadInput: true,
    ///     decelerationRate: 0f      // 无减速，角色滑行不止（冰面效果）
    /// );
    /// </code>
    ///
    /// ────────────────────────────────────────────────────────────
    /// 示例 4：怪物 AI（保持重力 + 手动控制）
    /// ────────────────────────────────────────────────────────────
    /// <code>
    /// protected override MovementConfig MovementConfig =&gt; new MovementConfig(
    ///     keepYVelocity: true,
    ///     autoReadInput: false,     // 关闭自动输入，由 AI 脚本控制
    ///     decelerationRate: 10f     // 怪物通常有中等惯性
    /// );
    ///
    /// // AI 脚本中手动控制移动
    /// private void Update()
    /// {
    ///     Vector3 dir = (target.position - transform.position).normalized;
    ///     monster.SetMovementInput(dir);
    /// }
    /// </code>
    ///
    /// ────────────────────────────────────────────────────────────
    /// 示例 5：飞行单位（无重力 + 快速停止）
    /// ────────────────────────────────────────────────────────────
    /// <code>
    /// protected override MovementConfig MovementConfig =&gt; new MovementConfig(
    ///     keepYVelocity: false,     // 不受重力影响
    ///     autoReadInput: false,
    ///     decelerationRate: 30f     // 飞行单位通常停止较快
    /// );
    /// </code>
    ///
    /// ────────────────────────────────────────────────────────────
    /// 示例 6：投射物/陷阱（禁用移动控制）
    /// ────────────────────────────────────────────────────────────
    /// <code>
    /// // 返回 null 完全禁用移动系统
    /// protected override MovementConfig MovementConfig =&gt; null;
    /// </code>
    ///
    /// ────────────────────────────────────────────────────────────
    /// 示例 7：等距视角角色（移动方向偏移 + 近乎立即停止）
    /// ────────────────────────────────────────────────────────────
    /// <code>
    /// protected override MovementConfig MovementConfig =&gt; new MovementConfig(
    ///     keepYVelocity: true,
    ///     autoReadInput: true,
    ///     decelerationRate: 50f,     // 近乎立即停止
    ///     movementAngleOffset: -45f // 移动方向左偏45度，补偿视角偏差
    /// );
    /// // 效果：按 W 实际朝左前方移动，适配斜45度俯视视角
    /// </code>
    /// </summary>
}
