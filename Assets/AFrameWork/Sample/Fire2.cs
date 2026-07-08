using System.Collections.Generic;
using UnityEngine;
using AFrameWork.Core;
using AFrameWork.Core.SmallBase;

namespace AFrameWork.Sample
{
    /// <summary>
    /// 火球类，继承 WeaponBase（轻量武器基类，不继承 ObjectBase），实现范围伤害检测系统
    /// 当继承 ObjectBase 的物体进入范围时，自动造成伤害
    /// 使用触发器检测 + 时间缓存机制实现高效伤害计算
    ///
    /// 性能优化设计详见类末尾的「Fire 使用说明」文档
    /// </summary>
    public class Fire2 : WeaponBase
    {
        #region 配置属性

        /// <summary>
        /// 物体属性配置，包含火球的攻击属性和伤害配置
        /// </summary>
        protected override ObjectStatsConfig ObjectStatsConfig => ObjectStatsConfig.CreateFire();

        #endregion

        #region 字段

        // 伤害计时器字典（记录每个对象的上次伤害时间）
        // 预分配容量 8 避免运行时扩容产生的 GC 压力（MMO 场景下触发器范围内可能有多个对象）
        private Dictionary<ObjectBase, float> m_damageTimers = new Dictionary<ObjectBase, float>(8);

        // 火球创建时间
        private float m_creationTime;

        // 标记是否已销毁
        private bool m_isDestroyed = false;

        #endregion

        #region 初始化方法

        /// <summary>
        /// 重写 SetupComponents，使用 AddObjectComponent 动态添加组件
        /// 直接传入组件类型和初始化回调，无需额外的配置类
        /// </summary>
        protected override void SetupComponents()
        {
            base.SetupComponents();

            // 添加 Rigidbody（火球不受重力影响）
            // m_rigidbody = AddObjectComponent<Rigidbody>(rb =>
            // {
            //     rb.mass = 1f;
            //     rb.useGravity = false;
            //     rb.constraints = RigidbodyConstraints.FreezeAll;
            // });

            // 添加 SphereCollider 作为触发器（用于 OnTriggerEnter/Stay/Exit 检测）
            float damageRadius = ObjectStatsConfig.DamageRadius;
            AddObjectComponent<SphereCollider>(c =>
            {
                c.isTrigger = true;         // 设置为触发器
                c.radius = damageRadius;    // 触发器范围 = 伤害范围
                c.center = Vector3.zero;
            });

#if UNITY_EDITOR
            // Debug.Log($"火球初始化完成，伤害范围：{damageRadius} 米");
#endif
        }

        #endregion

        #region MonoBehaviour 方法

        protected override void Awake()
        {
            base.Awake();
            m_creationTime = Time.time;
        }

        private void Update()
        {
            // 检查火球持续时间
            CheckDuration();
        }

        private void OnDestroy()
        {
            // 清理伤害计时器
            m_damageTimers.Clear();
        }

        #endregion

        #region 触发器检测方法

        /// <summary>
        /// 当物体进入触发器范围时调用
        /// </summary>
        private void OnTriggerEnter(Collider other)
        {
            // 使用 TryGetComponent 替代 GetComponent，避免热路径中的空引用检查开销
            if (!other.TryGetComponent<ObjectBase>(out ObjectBase target) || target == this)
            {
                return;
            }

            // 阵营关系判定：中立阵营(FactionID=100)仅伤害敌对方
            ObjectStatsConfig myStats = GetObjectStats();
            ObjectStatsConfig targetStats = target.GetObjectStats();
            if (!CanDealDamageTo(targetStats))
            {
                return;
            }

            // 立即造成首次伤害
            ApplyDamageToTarget(target);

            // 记录伤害时间（如果是持续伤害）
            if (myStats.IsContinuousDamage)
            {
                m_damageTimers[target] = Time.time;
            }

#if UNITY_EDITOR
            // Debug.Log($"物体 {target.name} 进入火球范围，首次伤害已应用");
#endif
        }

        /// <summary>
        /// 当物体在触发器范围内停留时调用（持续伤害）
        /// </summary>
        private void OnTriggerStay(Collider other)
        {
            ObjectStatsConfig stats = GetObjectStats();
            if (!stats.IsContinuousDamage)
            {
                return;
            }

            // 使用 TryGetComponent 替代 GetComponent
            if (!other.TryGetComponent<ObjectBase>(out ObjectBase target) || target == this)
            {
                return;
            }

            // 阵营关系判定：友方不受持续伤害
            ObjectStatsConfig targetStats = target.GetObjectStats();
            if (!CanDealDamageTo(targetStats))
            {
                return;
            }

            // 检查伤害间隔
            if (CanApplyDamage(target))
            {
                ApplyDamageToTarget(target);
                m_damageTimers[target] = Time.time;
            }
        }

        /// <summary>
        /// 当物体离开触发器范围时调用
        /// </summary>
        private void OnTriggerExit(Collider other)
        {
            // 使用 TryGetComponent 替代 GetComponent
            if (!other.TryGetComponent<ObjectBase>(out ObjectBase target) || target == this)
            {
                return;
            }

            // 清除伤害计时器
            m_damageTimers.Remove(target);

#if UNITY_EDITOR
            // Debug.Log($"物体 {target.name} 离开火球范围");
#endif
        }

        #endregion

        #region 伤害计算方法

        /// <summary>
        /// 检查是否可以对目标造成伤害（基于伤害间隔）
        /// </summary>
        private bool CanApplyDamage(ObjectBase target)
        {
            if (!m_damageTimers.TryGetValue(target, out float lastDamageTime))
            {
                return true;
            }

            ObjectStatsConfig stats = GetObjectStats();
            // 魔法持续伤害间隔 = 1f / CastSpeed（CastSpeed=2 时，间隔=0.5秒）
            return Time.time - lastDamageTime >= 1f / stats.CastSpeed;
        }

        /// <summary>
        /// 对目标应用伤害
        /// CalculateDamage 计算伤害（不扣血），ApplyDamageTo 应用（保留无敌/回调/死亡）
        /// </summary>
        private void ApplyDamageToTarget(ObjectBase target)
        {
            if (target == null || target.IsDead())
            {
                return;
            }

            // 伤害功能未启用时跳过（与 Sword 保持一致）
            ObjectStatsConfig myStats = GetObjectStats();
            if (!myStats.CanDealDamage)
            {
                return;
            }

            ObjectStatsConfig targetStats = target.GetObjectStats();
            // Fire 无 owner，CalculateDamage 内部只用 m_objectStats（武器自身属性）
            float damage = CalculateDamage(targetStats, new ObjectStatsConfigMultiplier());
            // ApplyDamageTo 内部调用 target.TakeDamage + SetLastAttackRefs(target, null, m_owner)
            // Fire 的 m_owner 为 null，UI 不显示攻击方卡片，只显示目标
            ApplyDamageTo(target, damage);

#if UNITY_EDITOR
            // Debug.Log($"火球对 {target.name} 造成魔法伤害");
#endif
        }

        #endregion

        #region 持续时间控制方法

        /// <summary>
        /// 检查火球持续时间，超时则销毁
        /// </summary>
        private void CheckDuration()
        {
            if (m_isDestroyed)
            {
                return;
            }

            ObjectStatsConfig stats = GetObjectStats();
            // DamageDuration <= 0 表示永久存活（无时间限制）
            float duration = stats.DamageDuration;
            if (duration > 0f && Time.time - m_creationTime >= duration)
            {
                DestroyFire();
            }
        }

        /// <summary>
        /// 销毁火球
        /// </summary>
        private void DestroyFire()
        {
            if (m_isDestroyed)
            {
                return;
            }

            m_isDestroyed = true;
#if UNITY_EDITOR
            // Debug.Log("火球持续时间结束，自动销毁");
#endif
            Destroy(gameObject);
        }

        #endregion

        #region 公共方法

        /// <summary>
        /// 设置伤害范围（供外部调用）
        /// 同时更新 ObjectStatsConfig.DamageRadius 和 SphereCollider.radius
        /// </summary>
        public void SetDamageRadius(float radius)
        {
            ObjectStatsConfig stats = GetObjectStats();
            if (stats != null)
            {
                stats.SetDamageRadius(radius);

                SphereCollider sphereCollider = GetComponent<SphereCollider>();
                if (sphereCollider != null)
                {
                    sphereCollider.radius = radius;
                }
            }
        }

        /// <summary>
        /// 设置火球持续时间（供外部调用）
        /// </summary>
        public void SetDuration(float duration)
        {
            ObjectStatsConfig stats = GetObjectStats();
            if (stats != null)
            {
                stats.SetDamageDuration(duration);
            }
        }

        /// <summary>
        /// 清除所有伤害计时器（重置伤害状态）
        /// </summary>
        public void ClearDamageTimers()
        {
            m_damageTimers.Clear();
        }

        #endregion
    }

    /// <summary>
    /// Fire 使用说明：
    /// ============================================================
    /// 火球类，继承 WeaponBase（轻量武器基类，不继承 ObjectBase），实现范围伤害检测系统。
    /// 使用 SphereCollider 触发器检测进入范围内的物体，按间隔造成持续伤害。
    /// 典型应用场景：法师的范围火焰技能、燃烧地面、陷阱区域、BOSS 的 AOE 技能。
    ///
    /// ════════════════════════════════════════════════════════════
    /// 【与 WeaponBase 的关系】
    /// ════════════════════════════════════════════════════════════
    ///   - Fire 继承 WeaponBase，复用基类的属性系统、组件管理
    ///   - WeaponBase 不提供移动系统（火球位置由外部脚本或 Rigidbody 控制）
    ///   - WeaponBase 不提供 Update/FixedUpdate/OnDestroy 默认实现，Fire 自行实现
    ///   - 伤害计算委托给 ObjectStatsConfig.CalculateAttack（含闪避、暴击、防御、穿透公式）
    ///   - 通过 GetObjectStats() 获取自身属性配置（基类 Awake 中克隆）
    ///   - 基类自动处理：CalculateObjectBounds 零分配（静态缓冲区）
    ///   - 设计原因：ObjectBase 含 PlayableGraph/动画槽位/Addressables 字典等 ~400+ 字节
    ///              与火球无关的开销，WeaponBase 直接继承 MonoBehaviour 避免这些浪费
    ///
    /// ════════════════════════════════════════════════════════════
    /// 【字段详解】
    /// ════════════════════════════════════════════════════════════
    ///   m_damageTimers : Dictionary&lt;ObjectBase, float&gt;
    ///     - 键：进入触发器范围的目标对象
    ///     - 值：上次对该目标造成伤害的时间（Time.time）
    ///     - 用途：实现持续伤害间隔（1f / CastSpeed），防止每物理帧重复造成伤害
    ///     - 预分配容量 8：避免运行时扩容产生的 GC 压力
    ///     - 生命周期：OnTriggerEnter 添加，OnTriggerExit 移除，OnDestroy 清空
    ///
    ///   m_creationTime : float
    ///     - 火球创建时间（Awake 中赋值为 Time.time）
    ///     - 用途：CheckDuration 中与 Time.time 比较，判断是否超过 DamageDuration
    ///
    ///   m_isDestroyed : bool
    ///     - 标记火球是否已销毁，防止 DestroyFire 重复调用
    ///     - 防御性设计：避免 Update 中的 CheckDuration 与外部调用 DestroyFire 竞争
    ///
    /// ════════════════════════════════════════════════════════════
    /// 【伤害机制】
    /// ════════════════════════════════════════════════════════════
    ///   - 进入范围（OnTriggerEnter）：立即造成首次伤害（无间隔等待）
    ///   - 停留范围（OnTriggerStay）：按 1f/CastSpeed 间隔持续造成伤害
    ///   - 离开范围（OnTriggerExit）：清除伤害计时器，停止持续伤害
    ///   - 持续时间到达（DamageDuration）：Update 中检测，自动销毁火球
    ///
    ///   持续伤害 vs 一次性伤害：
    ///     - IsContinuousDamage = true（默认）：启用持续伤害，OnTriggerStay 按间隔触发
    ///     - IsContinuousDamage = false：仅 OnTriggerEnter 造成一次伤害，OnTriggerStay 直接返回
    ///     - 配合 CastSpeed 控制持续伤害频率（interval = 1f / CastSpeed）
    ///
    /// ════════════════════════════════════════════════════════════
    /// 【触发器回调详细流程】
    /// ════════════════════════════════════════════════════════════
    ///   OnTriggerEnter(Collider other) 流程：
    ///     1. TryGetComponent&lt;ObjectBase&gt; 获取目标，失败或目标为自身则返回
    ///     2. ApplyDamageToTarget(target) —— 立即造成首次伤害
    ///        ├─ 空引用检查（target == null）和死亡检查（IsDead）
    ///        └─ CalculateAttack(...) 计算伤害 + target.TakeDamage(damage) 应用（保留无敌/回调/死亡）
    ///     3. 若 IsContinuousDamage = true，记录 m_damageTimers[target] = Time.time
    ///
    ///   OnTriggerStay(Collider other) 流程：
    ///     1. 检查 IsContinuousDamage，false 则直接返回
    ///     2. TryGetComponent 获取目标，失败或目标为自身则返回
    ///     3. CanApplyDamage(target) 检查伤害间隔
    ///        ├─ 不在字典中 → 返回 true（首次进入但未触发 Enter 的边缘情况）
    ///        └─ Time.time - lastDamageTime &gt;= 1f/CastSpeed → 返回 true
    ///     4. ApplyDamageToTarget(target) 造成伤害
    ///     5. 更新 m_damageTimers[target] = Time.time
    ///
    ///   OnTriggerExit(Collider other) 流程：
    ///     1. TryGetComponent 获取目标，失败则返回
    ///     2. m_damageTimers.Remove(target) 移除计时器
    ///     3. 若目标再次进入范围，OnTriggerEnter 会重新添加计时器
    ///
    /// ════════════════════════════════════════════════════════════
    /// 【伤害计算内部机制】
    /// ════════════════════════════════════════════════════════════
    ///   ApplyDamageToTarget(target) 内部：
    ///     1. 空引用和死亡检查（防御性编程，避免对已死亡目标造成伤害）
    ///     2. CalculateAttack(new ObjectStatsConfigMultiplier(), targetStats, attackerStats) 计算伤害（不扣血）
    ///     3. target.TakeDamage(damage) 应用伤害（经 ObjectBase.TakeDamage 保留无敌/回调/死亡）
    ///
    ///   CalculateAttack 内部流程：
    ///     attackerStats = GetObjectStats()  // 火球自身属性
    ///     targetStats   = target.GetObjectStats()  // 目标属性
    ///     累加所有攻击方属性 → 闪避判定 → 伤害计算 → 暴击判定（不扣血，返回伤害值）
    ///     公式（魔法伤害）：
    ///       实际伤害 = MagicAttack × (100 / (100 + 目标 MagicDefense × (1 - MagicPenetration)))
    ///       减免 = MagicDefense × (1 - MagicPenetration)
    ///     暴击判定：
    ///       Random.value &lt; CriticalRate → 暴击，伤害 × CriticalDamageMultiplier
    ///     命中判定：
    ///       Random.value &gt; HitRate → 未命中，伤害为 0
    ///
    ///   伤害类型对照：
    ///     PhysicalAttack > 0：计算物理伤害（受 PhysicalDefense + ArmorPenetration 减免）
    ///     MagicAttack > 0   ：计算魔法伤害（受 MagicDefense + MagicPenetration 减免，火球主要伤害来源）
    ///     TrueDamage > 0    ：无视防御的固定伤害
    ///     CalculateAttack 自动累加所有非零攻击类型
    ///
    /// ════════════════════════════════════════════════════════════
    /// 【持续时间控制机制】
    /// ════════════════════════════════════════════════════════════
    ///   Update 每帧调用 CheckDuration：
    ///     1. 检查 m_isDestroyed 标记，已销毁则直接返回
    ///     2. 获取 ObjectStatsConfig 中的 DamageDuration
    ///     3. Time.time - m_creationTime &gt;= DamageDuration → 调用 DestroyFire()
    ///
    ///   DestroyFire 内部：
    ///     1. 检查 m_isDestroyed，避免重复销毁
    ///     2. 设置 m_isDestroyed = true
    ///     3. 调用 Destroy(gameObject) 销毁 GameObject
    ///     4. OnDestroy 自动清理 m_damageTimers（防止内存泄漏）
    ///
    ///   外部控制：
    ///     - SetDuration(float)：运行时修改 DamageDuration，影响后续 CheckDuration 判定
    ///     - 直接调用 Destroy(gameObject)：会触发 OnDestroy 自动清理
    ///
    /// ════════════════════════════════════════════════════════════
    /// 【生命周期方法】
    /// ════════════════════════════════════════════════════════════
    ///   Awake（重写 WeaponBase.Awake）：
    ///     1. base.Awake() —— WeaponBase 初始化（SetupComponents + SetupObjectStats）
    ///     2. m_creationTime = Time.time —— 记录创建时间用于持续时间判定
    ///
    ///   Update（private，WeaponBase 不提供 Update）：
    ///     1. CheckDuration() —— 检查持续时间是否到期
    ///     （WeaponBase 无 Update/FixedUpdate，火球自行实现需要的帧逻辑）
    ///
    ///   OnDestroy（private，WeaponBase 不提供 OnDestroy）：
    ///     1. m_damageTimers.Clear() —— 清空计时器，防止引用泄漏
    ///     （WeaponBase 无 OnDestroy，火球自行清理；无 Playable/Addressables 资源需释放）
    ///
    ///   注意：WeaponBase 不提供 FixedUpdate（武器不需要物理移动）
    ///        火球的 Rigidbody 仅用于触发器检测（FreezeAll 约束），不参与物理模拟
    ///
    /// ════════════════════════════════════════════════════════════
    /// 【初始化流程】
    /// ════════════════════════════════════════════════════════════
    ///   1. Awake → SetupComponents()（WeaponBase.Awake 调用）：
    ///      - base.SetupComponents()：WeaponBase 基类初始化（当前为空实现）
    ///      - 添加 Rigidbody（mass=1, useGravity=false, FreezeAll 约束）
    ///        火球不受重力影响，位置由外部控制或保持原地
    ///      - 添加 SphereCollider（isTrigger=true, radius=DamageRadius, center=zero）
    ///        触发器范围 = 伤害范围，确保范围判定与可视化范围一致
    ///   2. Awake 中记录 m_creationTime = Time.time
    ///
    /// ════════════════════════════════════════════════════════════
    /// 【属性配置】
    /// ════════════════════════════════════════════════════════════
    ///   通过 ObjectStatsConfig 配置火球属性：
    ///     基础属性：
    ///       Type = ObjectType.Projectile（投射物类型）
    ///       FactionID = 100（中立阵营，可伤害所有非中立阵营）
    ///       MaxHealth = CurrentHealth = 1f（火球一击即毁，无需大量生命值）
    ///       MagicAttack = 25f（魔法攻击力，替代 BaseDamage + DamageType.Magic）
    ///       MagicPenetration = 0.2f（20% 魔法穿透，无视部分魔防）
    ///     速度属性：
    ///       CastSpeed = 2f（施法频率 2次/秒，即每 0.5 秒一次，替代 DamageInterval）
    ///     伤害配置：
    ///       DamageRadius = 5f（5 米范围伤害）
    ///       IsContinuousDamage = true（启用持续伤害）
    ///       DamageDuration = 10f（持续 10 秒后销毁）
    ///       CanDealDamage = true（启用伤害判定）
    ///
    /// ════════════════════════════════════════════════════════════
    /// 【性能优化】（针对 MMO 场景下大量火球和战斗对象）
    /// ════════════════════════════════════════════════════════════
    ///   1. 触发器回调使用 TryGetComponent：
    ///      - OnTriggerEnter/Stay/Exit 全部使用 TryGetComponent&lt;ObjectBase&gt; 替代 GetComponent
    ///      - TryGetComponent 避免了 GetComponent 内部的空引用检查开销
    ///      - MMO 场景下触发器范围内可能有多个对象，OnTriggerStay 每物理帧调用，优化收益显著
    ///
    ///   2. 伤害计时器字典预分配容量：
    ///      - m_damageTimers 初始化容量为 8（new Dictionary&lt;ObjectBase, float&gt;(8)）
    ///      - 避免运行时动态扩容产生的 GC 压力
    ///      - 容量 8 适用于大多数火球范围伤害场景（范围内同时存在 8 个以内目标）
    ///      - 若预期范围内目标更多，可调大初始容量
    ///
    ///   3. 热路径 Debug.Log 剥离：
    ///      - 触发器回调（OnTriggerEnter/Stay/Exit）中的日志包裹 #if UNITY_EDITOR
    ///      - 伤害应用（ApplyDamageToTarget）和销毁（DestroyFire）中的日志同样包裹
    ///      - 生产构建中完全剥离，避免字符串插值（$"..."）的 GC 分配
    ///      - MMO 场景下火球数量多、触发器回调频繁，此项优化收益显著
    ///
    ///   4. 基类 WeaponBase 自动处理的优化（子类无感知）：
    ///      - CalculateObjectBounds 使用静态缓冲区零分配
    ///      - GetObjectStats() 返回 Awake 中克隆的缓存引用，避免每次访问属性时 new
    ///      - CalculateDamage 使用静态 s_twoAttackerBuffer 避免 params 数组分配
    ///      - 相比继承 ObjectBase：无 PlayableGraph/动画槽位/Addressables 字典开销
    ///        且无 Update/FixedUpdate 的 native↔managed 边界开销
    ///
    /// ════════════════════════════════════════════════════════════
    /// 【公共方法】
    /// ════════════════════════════════════════════════════════════
    ///   - SetDamageRadius(float radius)：
    ///       运行时修改伤害范围，同步更新 ObjectStatsConfig.DamageRadius 和 SphereCollider.radius
    ///       适用场景：技能升级后扩大范围、BUFF 影响范围
    ///
    ///   - SetDuration(float duration)：
    ///       运行时修改持续时间，更新 ObjectStatsConfig.DamageDuration
    ///       适用场景：技能效果延长、特殊状态下持续更久
    ///
    ///   - ClearDamageTimers()：
    ///       清除所有伤害计时器，重置伤害状态
    ///       适用场景：技能重置、目标变更、测试调试
    ///       注意：清空后已进入范围的目标会在下次 OnTriggerStay 时立即受到伤害
    ///
    /// ════════════════════════════════════════════════════════════
    /// 【扩展建议】
    /// ════════════════════════════════════════════════════════════
    ///   1. 添加视觉效果：
    ///      - 在 DestroyFire 中播放爆炸特效（WeaponBase 不提供 OnDeath 回调）
    ///      - 在 SetupComponents 中加载 VFX Graph（自行管理资源生命周期）
    ///
    ///   2. 添加移动逻辑：
    ///      - 在 Update 中通过 Rigidbody.velocity 控制飞行方向
    ///      - 或在外部脚本（如 SkillLauncher）中驱动 transform.position
    ///      - 注意：WeaponBase 不提供 MovementConfig 系统
    ///
    ///   3. 添加伤害类型变化：
    ///      - 运行时修改 PhysicalAttack/MagicAttack/TrueDamage 实现伤害类型切换
    ///      - 配合 SetDamageRadius 实现技能变形
    ///
    ///   4. 添加目标过滤：
    ///      - 在 ApplyDamageToTarget 中添加自定义条件（如只伤害特定阵营）
    ///      - 或在 OnTriggerEnter 中调用 CanDealDamageTo 进行阵营判定
    /// </summary>
}