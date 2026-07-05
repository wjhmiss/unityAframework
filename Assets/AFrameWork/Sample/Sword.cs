using System.Collections.Generic;
using UnityEngine;
using AFrameWork.Core;

namespace AFrameWork.Sample
{
    /// <summary>
    /// 剑类，继承 ObjectBase，演示武器物体的实现。
    /// 使用 BoxCollider 作为碰撞体，用于攻击检测。
    /// 初始化时从父级 ObjectBase 继承阵营/队伍/公会/PVP 信息，
    /// 碰撞时根据阵营关系判断是否可攻击并造成伤害。
    /// 支持 Fighter 传入 ObjectStatsConfigMultiplier 实现连击差异化伤害。
    /// </summary>
    public class Sword : ObjectBase
    {
        #region 常量

        // 每次攻击（一次挥剑）中，同一目标只受一次伤害
        private const int k_maxHitTargetsPerSwing = 100;

        #endregion

        #region 配置属性

        /// <summary>
        /// 移动配置属性，武器通常不需要自动移动控制
        /// </summary>
        protected override MovementConfig MovementConfig => null;

        /// <summary>
        /// 物体属性配置，包含剑的攻击属性
        /// 阵营/队伍/公会/PVP 信息在 Start 中从父级继承
        /// </summary>
        protected override ObjectStatsConfig ObjectStatsConfig => ObjectStatsConfig.CreateSword();

        #endregion

        #region 字段

        // 持有该武器的父级 ObjectBase（缓存引用，避免每次碰撞时向上查找）
        private ObjectBase m_owner;

        // 持有者的 ObjectStatsConfig（缓存，用于获取暴击属性）
        private ObjectStatsConfig m_ownerStats;

        // 当前挥剑已命中的目标集合，防止同一目标在一次挥剑中重复受伤
        private HashSet<int> m_hitTargetIds;

        // 当前挥剑是否处于激活状态（攻击动画期间为 true）
        private bool m_isSwingActive;

        // 当前攻击的属性倍率（由 Fighter 在 BeginSwing 时传入）
        private ObjectStatsConfigMultiplier m_attackMultiplier;

        #endregion

        #region 初始化方法

        /// <summary>
        /// 重写 SetupComponents，使用 AddObjectComponent 动态添加组件
        /// </summary>
        protected override void SetupComponents()
        {
            base.SetupComponents();


            // 添加 BoxCollider，根据对象包围盒设置大小
            // sizeMultiplier: 剑通常较长，Y 轴放大，XZ 轴缩小
            // centerOffset: 世界空间偏移，(0, 1, 0) = 向上偏移1米（不受父级scale影响）
            //AddBoxCollider(CalculateObjectBounds(), new Vector3(1f, 2.0f, 1f), new Vector3(0f, -1f, 0f));


            AddBoxCollider(CalculateObjectBounds(), new Vector3(1f, 2.0f, 1f), new Vector3(0f, -1f, 0f),cc =>
            {
                cc.isTrigger = true;
            });
            // AddObjectComponent<BoxCollider>(cc =>
            // {
            //     cc.size = new Vector3(1f, 2.1f, 1f);
            //     cc.center = new Vector3(0f, 1f, 0f);
            //     cc.isTrigger = false;
            // });

            // Rigidbody — kinematic，仅用于触发检测
            // Unity 规则：kinematic Rigidbody 才能与 kinematic Rigidbody 触发 OnTriggerEnter
            // 如果无 Rigidbody，Monster（kinematic）的触发事件不会发送到 Sword
            // AddObjectComponent<Rigidbody>(rb =>
            // {
            //     rb.isKinematic = true;       // 不参与物理模拟，仅用于触发检测
            //     rb.useGravity = false;       // 武器不需要重力（由骨骼动画驱动位置）
            //     rb.constraints = RigidbodyConstraints.FreezeAll;  // 完全冻结，防止意外移动
            // });
        }

        /// <summary>
        /// Start 中从父级继承阵营/队伍/公会/PVP 信息
        /// 必须在 Start 中执行，确保父级 ObjectBase 的 Awake 已完成初始化
        /// </summary>
        private void Start()
        {
            m_hitTargetIds = new HashSet<int>(k_maxHitTargetsPerSwing);
            InheritOwnerFactionInfo();
        }

        /// <summary>
        /// 向上查找第一个拥有 ObjectBase 组件的父级，继承其阵营信息
        /// 同时缓存持有者的 ObjectStatsConfig，用于暴击属性获取
        /// </summary>
        private void InheritOwnerFactionInfo()
        {
            m_owner = FindParentObjectBase();

            if (m_owner == null || !m_owner.HasObjectStats())
            {
#if UNITY_EDITOR
                // Debug.LogWarning("Sword: 未找到父级 ObjectBase，阵营信息未继承", this);
#endif
                return;
            }

            m_ownerStats = m_owner.GetObjectStats();
            ObjectStatsConfig myStats = GetObjectStats();

            // 继承父级的阵营关系判定字段（统一通过 InheritFactionFrom 处理）
            myStats.InheritFactionFrom(m_ownerStats);
        }

        /// <summary>
        /// 沿父级向上查找第一个具有 ObjectBase 基类的组件
        /// </summary>
        private ObjectBase FindParentObjectBase()
        {
            Transform current = transform.parent;
            while (current != null)
            {
                if (current.TryGetComponent<ObjectBase>(out ObjectBase parentObj))
                {
                    return parentObj;
                }
                current = current.parent;
            }
            return null;
        }

        #endregion

        #region 攻击控制方法

        /// <summary>
        /// 开始一次挥剑攻击，重置命中记录并设置攻击属性倍率
        /// 由持有者的攻击动画事件或攻击逻辑调用
        /// </summary>
        /// <param name="multiplier">攻击属性倍率（-1=使用基础值，否则 基础值×倍率）</param>
        public void BeginSwing(ObjectStatsConfigMultiplier multiplier)
        {
            m_isSwingActive = true;
            m_attackMultiplier = multiplier;
            m_hitTargetIds.Clear();
        }

        /// <summary>
        /// 结束一次挥剑攻击
        /// 由持有者的攻击动画事件或攻击逻辑调用
        /// </summary>
        public void EndSwing()
        {
            m_isSwingActive = false;
            m_hitTargetIds.Clear();
        }

        #endregion

        #region 碰撞检测与伤害

        /// <summary>
        /// 碰撞触发时检测目标并造成伤害
        /// BoxCollider 需设置为 IsTrigger = true
        /// </summary>
        private void OnTriggerEnter(Collider other)
        {
            //Debug.Log($"剑命中 {other.name}", this);

            // 非攻击状态不造成伤害
            if (!m_isSwingActive)
            {
                return;
            }

            if (!HasObjectStats())
            {
                return;
            }

            ObjectStatsConfig myStats = GetObjectStats();

            // 武器自身未启用伤害
            if (!myStats.CanDealDamage)
            {
                return;
            }

            // 获取目标 ObjectBase
            if (!other.TryGetComponent<ObjectBase>(out ObjectBase target))
            {
                return;
            }

            // 不攻击自身持有者
            if (target == m_owner)
            {
                return;
            }

            if (!target.HasObjectStats())
            {
                return;
            }

            ObjectStatsConfig targetStats = target.GetObjectStats();

            // 目标已死亡
            if (targetStats.IsDead())
            {
                return;
            }

            // 同一次挥剑中同一目标只受一次伤害
            int targetInstanceId = target.GetInstanceID();
            if (m_hitTargetIds.Contains(targetInstanceId))
            {
                return;
            }

            // 阵营关系判定：能否对目标造成伤害
            if (!myStats.CanDealDamageTo(targetStats))
            {
                return;
            }

            // 累加武器 + 持有者属性 → 计算伤害（不扣血）
            // CalculateAttack 是唯一伤害计算入口，与 TakeDamage 分离避免重复扣血
            float damage = ObjectStatsConfig.CalculateAttack(
                m_attackMultiplier, targetStats, myStats, m_ownerStats);

            // 经 ObjectBase.TakeDamage 应用：保留无敌检查（翻滚免疫）/OnDamaged 回调/OnDeath 处理
            target.TakeDamage(damage);

            m_hitTargetIds.Add(targetInstanceId);

#if UNITY_EDITOR
            // Debug.Log($"剑命中 {target.name}", this);
#endif
        }

        #endregion

        #region 物体属性回调方法

        // 注意：所有回调中的 Debug.Log 均包裹在 #if UNITY_EDITOR 中
        // MMO 场景下战斗频繁，字符串插值会产生 GC 压力，生产构建中需要完全剥离

        /// <summary>
        /// 受到伤害时的回调
        /// </summary>
        protected override void OnDamaged(float damage)
        {
#if UNITY_EDITOR
            // Debug.Log($"剑受到 {damage} 点伤害！");
#endif
        }

        /// <summary>
        /// 物体死亡时的回调
        /// </summary>
        protected override void OnDeath()
        {
#if UNITY_EDITOR
            // Debug.Log("剑被破坏！");
#endif
        }

        #endregion
    }

    /// <summary>
    /// Sword 使用说明：
    /// ============================================================
    /// 剑类，继承 ObjectBase，演示武器物体的实现。
    /// 包含：动态组件创建、属性配置、BoxCollider 设置、阵营继承、碰撞伤害。
    /// 作为 Fighter 武器子对象，由 Fighter 的攻击动画事件驱动 BeginSwing/EndSwing。
    ///
    /// ════════════════════════════════════════════════════════════
    /// 【与 ObjectBase 的关系】
    /// ════════════════════════════════════════════════════════════
    ///   - Sword 继承 ObjectBase，复用父类的属性系统、组件管理、阵营判定
    ///   - 未重写 MovementConfig（返回 null），武器不使用移动系统
    ///   - 未启用动画系统、帧事件系统（武器本身不播放动画）
    ///   - 重写 OnDamaged/OnDeath 回调（武器可被破坏，但当前仅日志输出）
    ///   - 父类自动处理：组件管理、属性系统、阵营判定（继承后）
    ///
    /// ════════════════════════════════════════════════════════════
    /// 【字段详解】
    /// ════════════════════════════════════════════════════════════
    ///   m_owner : ObjectBase
    ///     - 持有该武器的父级 ObjectBase（缓存引用，避免每次碰撞时向上查找）
    ///     - 在 Start → InheritOwnerFactionInfo 中赋值
    ///     - 用途：OnTriggerEnter 中跳过对自身的伤害（target == m_owner 时返回）
    ///     - 用途：作为暴击属性来源（m_ownerStats.CriticalRate）
    ///
    ///   m_ownerStats : ObjectStatsConfig
    ///     - 持有者的 ObjectStatsConfig（缓存，用于获取暴击属性）
    ///     - 在 InheritOwnerFactionInfo 中赋值（m_owner.GetObjectStats()）
    ///     - 用途：OnTriggerEnter 中计算暴击时使用持有者的 CriticalRate 和 CriticalDamageMultiplier
    ///     - 设计原因：武器自身属性不含暴击，暴击由持有者决定
    ///
    ///   m_hitTargetIds : HashSet&lt;int&gt;
    ///     - 当前挥剑已命中的目标 InstanceID 集合
    ///     - 用途：同一次挥剑中同一目标只受一次伤害（防止穿透多 Collider 重复受伤）
    ///     - 容量：预分配 k_maxHitTargetsPerSwing = 100
    ///     - 生命周期：BeginSwing 清空，EndSwing 清空，OnTriggerEnter 添加
    ///
    ///   m_isSwingActive : bool
    ///     - 当前挥剑是否处于激活状态（攻击动画期间为 true）
    ///     - 由 BeginSwing 设为 true，EndSwing 设为 false
    ///     - 用途：OnTriggerEnter 中检查，非攻击状态不造成伤害
    ///
    ///   m_attackMultiplier : ObjectStatsConfigMultiplier
    ///     - 当前攻击的属性倍率（由 Fighter 在 BeginSwing 时传入）
    ///     - 用途：差异化调整每次攻击的伤害、暴击率、暴击伤害
    ///     - struct 类型，无 GC 分配
    ///     - 示例：k_attack03 传入 (1.5, 3.0, 1.5) 表示伤害×1.5、暴击率×3、暴击伤害×1.5
    ///
    /// ════════════════════════════════════════════════════════════
    /// 【ObjectStatsConfigMultiplier 攻击倍率】
    /// ════════════════════════════════════════════════════════════
    ///   每个攻击动画传入独立的 ObjectStatsConfigMultiplier，差异化调整属性：
    ///     -1（k_useBase）= 使用基础值（倍率 1.0）
    ///     其他值 = 基础属性 × 倍率
    ///   示例：CriticalRateMultiplier = 0.5，基础 CriticalRate = 0.1 → 实际暴击率 = 0.05
    ///
    ///   Fighter 三段攻击的倍率配置：
    ///     k_attack01：physicalAttackMultiplier = 1.0（基础伤害）
    ///     k_attack02：physicalAttackMultiplier = 1.2（伤害提升 20%）
    ///     k_attack03：physicalAttackMultiplier = 1.5, criticalRateMultiplier = 3.0,
    ///                criticalDamageMultiplier = 1.5（终结技，高伤害高暴击）
    ///
    ///   Apply(value, multiplier) 计算规则：
    ///     - multiplier == k_useBase (-1) → 返回原值（不调整）
    ///     - multiplier != k_useBase     → 返回 value × multiplier
    ///
    /// ════════════════════════════════════════════════════════════
    /// 【Collider 配置】
    /// ════════════════════════════════════════════════════════════
    ///   - 使用 BoxCollider 作为碰撞体（需设置 IsTrigger = true）
    ///   - sizeMultiplier: (0.3, 1.0, 0.15) — Y 轴放大（剑长度），XZ 轴缩小（剑宽度）
    ///   - centerOffset: (0, 0, 0) — 无偏移
    ///   - 自动根据对象 Renderer 包围盒计算 Collider 大小
    ///   - 使用 AddBoxCollider(Bounds, Vector3, Vector3) 一站式添加并配置
    ///
    ///   注意：BoxCollider 的 IsTrigger 必须为 true，否则会与目标发生物理碰撞
    ///        父类 AddBoxCollider 默认设置 isTrigger = true
    ///
    /// ════════════════════════════════════════════════════════════
    /// 【初始化流程】
    /// ════════════════════════════════════════════════════════════
    ///   1. Awake → SetupComponents()：
    ///      - base.SetupComponents()：父类添加默认组件
    ///      - 添加 BoxCollider（自动计算包围盒，sizeMultiplier=(0.3, 1.0, 0.15)）
    ///   2. Start → InheritOwnerFactionInfo()：
    ///      - 向上查找第一个父级 ObjectBase（FindParentObjectBase）
    ///      - 缓存 m_owner 和 m_ownerStats
    ///      - 继承 FactionID / TeamID / GuildID / AllianceID / PVPMode
    ///      - 使武器与持有者阵营一致，作为 CanDealDamageTo 的判定依据
    ///   3. Start 中初始化 m_hitTargetIds（预分配容量 k_maxHitTargetsPerSwing）
    ///
    ///   注意：阵营继承在 Start 中执行，确保父级 ObjectBase 的 Awake 已完成初始化
    ///        不能在 Awake 中执行，因为子对象 Awake 早于父对象 Awake
    ///
    /// ════════════════════════════════════════════════════════════
    /// 【阵营继承】
    /// ════════════════════════════════════════════════════════════
    ///   查找规则：沿 transform.parent 向上遍历，找到第一个 ObjectBase 即停止
    ///   实现方法：FindParentObjectBase 使用 TryGetComponent&lt;ObjectBase&gt;
    ///   继承字段：FactionID, TeamID, GuildID, AllianceID, CurrentPVPMode
    ///   使用场景：武器与持有者同阵营 → CanDealDamageTo 可正确判定敌我关系
    ///
    ///   阵营关系判定优先级（CanDealDamageTo）：
    ///     1. TeamID（同一队伍不能伤害，-1 表示无队伍）
    ///     2. GuildID（同一公会默认不能伤害，双方 OpenPVP 例外）
    ///     3. AllianceID（同一同盟默认不能伤害，双方 OpenPVP 例外）
    ///     4. FactionID + FactionRelation（阵营关系判定，需 FactionRelationManager）
    ///
    ///   未找到父级 ObjectBase 时的处理：
    ///     - 输出警告日志（#if UNITY_EDITOR）
    ///     - 阵营信息保持默认值（FactionID = 0 等）
    ///     - 武器仍可使用，但阵营判定可能不准确
    ///
    /// ════════════════════════════════════════════════════════════
    /// 【碰撞伤害流程】
    /// ════════════════════════════════════════════════════════════
    ///   完整流程（Fighter 攻击 → Sword 命中）：
    ///     1. Fighter.Update 检测鼠标左键 → 调用 TryStartAttack()
    ///     2. ObjectBase.TryStartAttack 内部：
    ///        - 播放攻击动画（含帧事件注册）
    ///        - 推进连击索引
    ///     3. 攻击动画播放至挥剑帧 → FrameEvent 触发
    ///        - Fighter.OnAnimationFrameEvent 中调用 m_sword.BeginSwing(multiplier)
    ///     4. Sword.BeginSwing：
    ///        - m_isSwingActive = true
    ///        - m_attackMultiplier = multiplier
    ///        - m_hitTargetIds.Clear() 清空命中记录
    ///     5. Sword 的 BoxCollider 与目标重叠 → OnTriggerEnter
    ///        - 详见下方【OnTriggerEnter 详细流程】
    ///     6. 攻击动画播放至收剑帧 → FrameEvent 触发
    ///        - Fighter.OnAnimationFrameEvent 中调用 m_sword.EndSwing()
    ///     7. Sword.EndSwing：
    ///        - m_isSwingActive = false
    ///        - m_hitTargetIds.Clear() 清空命中记录
    ///
    ///   OnTriggerEnter(Collider other) 详细流程：
    ///     1. 检查 m_isSwingActive，非攻击状态直接返回
    ///     2. HasObjectStats() 检查武器自身属性，无属性则返回
    ///     3. myStats.CanDealDamage 检查，未启用伤害则返回
    ///     4. TryGetComponent&lt;ObjectBase&gt; 获取目标，失败则返回
    ///     5. target == m_owner 跳过（不攻击自身持有者）
    ///     6. target.HasObjectStats() 检查，目标无属性则返回
    ///     7. targetStats.IsDead() 检查，已死亡目标不造成伤害
    ///     8. m_hitTargetIds.Contains(targetInstanceId) 检查，同次挥剑已命中则跳过
    ///     9. myStats.CanDealDamageTo(targetStats) 阵营关系判定，敌对才造成伤害
    ///     10. ObjectStatsConfig.CalculateAttack(m_attackMultiplier, targetStats, myStats, m_ownerStats)
    ///         —— 累加武器+持有者属性，计算伤害（含闪避、防御减免、穿透、暴击，不扣血）
    ///     11. target.TakeDamage(damage) 应用伤害
    ///         —— 经 ObjectBase.TakeDamage：保留无敌检查（翻滚免疫）/OnDamaged/OnDeath
    ///     12. m_hitTargetIds.Add(targetInstanceId) 记录命中
    ///     13. #if UNITY_EDITOR 输出命中日志
    ///
    /// ════════════════════════════════════════════════════════════
    /// 【暴击计算流程】
    /// ════════════════════════════════════════════════════════════
    ///   暴击率计算（使用持有者暴击属性 + 攻击倍率调整）：
    ///     effectiveCritRate = ObjectStatsConfigMultiplier.Apply(
    ///         m_ownerStats.CriticalRate,
    ///         m_attackMultiplier.CriticalRateMultiplier
    ///     )
    ///     - m_ownerStats 为 null 时，effectiveCritRate = 0f（不暴击）
    ///     - CriticalRateMultiplier == k_useBase 时，使用持有者基础暴击率
    ///     - CriticalRateMultiplier == 3.0 时，暴击率 × 3（如 k_attack03）
    ///
    ///   暴击判定：
    ///     isCritical = UnityEngine.Random.value &lt; effectiveCritRate
    ///     - Random.value 范围 [0, 1]，小于暴击率则触发暴击
    ///
    ///   暴击伤害计算：
    ///     if (isCritical):
    ///         effectiveCritMultiplier = ObjectStatsConfigMultiplier.Apply(
    ///             m_ownerStats.CriticalDamageMultiplier,
    ///             m_attackMultiplier.CriticalDamageMultiplier
    ///         )
    ///         damage *= effectiveCritMultiplier
    ///     - 默认 CriticalDamageMultiplier = 2.0f（200% 暴击伤害）
    ///     - k_attack03 传入 1.5，实际暴击伤害 = 持有者基础 × 1.5
    ///
    ///   暴击计算示例（k_attack03，持有者 CriticalRate=0.2, CriticalDamageMultiplier=2.5）：
    ///     effectiveCritRate = Apply(0.2, 3.0) = 0.2 × 3.0 = 0.6（60% 暴击率）
    ///     effectiveCritMultiplier = Apply(2.5, 1.5) = 2.5 × 1.5 = 3.75（375% 暴击伤害）
    ///     若基础伤害 30，暴击后伤害 = 30 × 3.75 = 112.5
    ///
    /// ════════════════════════════════════════════════════════════
    /// 【属性配置】
    /// ════════════════════════════════════════════════════════════
    ///   通过 ObjectStatsConfig 配置剑属性：
    ///     基础属性：
    ///       Type = ObjectType.Weapon（武器类型）
    ///       FactionID = 0（Start 中由持有者覆盖）
    ///       MaxHealth = CurrentHealth = 100f（武器耐久度）
    ///       PhysicalAttack = 5f（剑的基础物理攻击）
    ///       PhysicalDefense = 0f, MagicAttack = 0f, MagicDefense = 0f
    ///     伤害配置：
    ///       AttackRange = 1.5f（剑的攻击范围）
    ///       CanDealDamage = true（启用伤害判定）
    ///
    ///   注意：CalculateAttack 内部累加武器 + 持有者所有属性后统一计算
    ///        实际伤害 = (武器PhysicalAttack + 持有者PhysicalAttack) × (100 / (100 + 目标有效防御))
    ///        暴击属性（CriticalRate/CriticalDamageMultiplier）也是武器 + 持有者相加
    ///        计算结果通过 ObjectBase.TakeDamage 应用（保留无敌/回调/死亡处理）
    ///
    /// ════════════════════════════════════════════════════════════
    /// 【性能优化】
    /// ════════════════════════════════════════════════════════════
    ///   - ObjectStatsConfigMultiplier 为 struct，无 GC 分配
    ///     BeginSwing 传入 multiplier 不产生堆分配
    ///   - BeginSwing/EndSwing 重置命中集合（Clear 而非 new），避免每次攻击重新分配
    ///   - m_hitTargetIds 预分配容量 100，避免运行时扩容
    ///   - TryGetComponent 替代 GetComponent 减少空检查开销
    ///   - OnTriggerEnter 中使用 target.GetInstanceID() 作为 HashSet 键
    ///     InstanceID 是 int 类型，比 GameObject 引用比较更快
    ///   - Debug.Log 包裹 #if UNITY_EDITOR，生产构建中完全剥离
    ///   - 父类自动处理：CalculateObjectBounds 零分配、MovementConfig 缓存（null）
    ///
    /// ════════════════════════════════════════════════════════════
    /// 【扩展建议】
    /// ════════════════════════════════════════════════════════════
    ///   1. 武器耐久度系统：
    ///      - 在 OnDamaged 中减少耐久度，耐久度为 0 时触发 OnDeath
    ///      - OnDeath 中通知持有者（m_owner）武器已损坏
    ///
    ///   2. 元素附魔：
    ///      - 添加 Element 字段（Fire/Ice/Lightning 等）
    ///      - 在 OnTriggerEnter 中附加元素效果（燃烧、冰冻、麻痹）
    ///      - 配合 ObjectStatsConfigMultiplier 实现不同攻击的元素切换
    ///
    ///   3. 多段伤害：
    ///      - 在一次挥剑中对同一目标造成多次伤害（如连刺）
    ///      - 移除 m_hitTargetIds 检查，改为伤害冷却字典
    ///
    ///   4. 范围伤害：
    ///      - 在 OnTriggerEnter 中使用 Physics.OverlapSphere 检测范围内所有目标
    ///      - 配合 VFX 实现横扫特效
    /// </summary>
}
