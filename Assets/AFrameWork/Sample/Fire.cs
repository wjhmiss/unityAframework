using System.Collections.Generic;
using UnityEngine;
using AFrameWork.Core;

namespace AFrameWork.Sample
{
    /// <summary>
    /// 火球类，实现范围伤害检测系统
    /// 当继承 ObjectBase 的物体进入范围时，自动造成伤害
    /// 使用触发器检测 + 时间缓存机制实现高效伤害计算
    ///
    /// 性能优化设计详见类末尾的「Fire 使用说明」文档
    /// </summary>
    public class Fire : ObjectBase
    {
        #region 配置属性

        /// <summary>
        /// 物体属性配置，包含火球的攻击属性和伤害配置
        /// </summary>
        protected override ObjectStatsConfig ObjectStatsConfig => new ObjectStatsConfig
        {
            // 基础属性
            Type = ObjectType.Projectile,
            FactionID = 100,              // 中立阵营
            MaxHealth = 1f,
            CurrentHealth = 1f,
            PhysicalAttack = 0f,
            PhysicalDefense = 0f,
            MagicAttack = 15f,
            MagicDefense = 0f,
            MagicPenetration = 0.2f,

            // 伤害配置属性
            BaseDamage = 10f,
            DamageType = DamageType.Magic,
            DamageRadius = 5f,
            DamageInterval = 0.5f,
            IsContinuousDamage = true,
            DamageDuration = 10f,
            CanDealDamage = true
        };

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
            m_rigidbody = AddObjectComponent<Rigidbody>(rb =>
            {
                rb.mass = 1f;
                rb.useGravity = false;
                rb.constraints = RigidbodyConstraints.FreezeAll;
            });

            // 添加 SphereCollider 作为触发器（用于 OnTriggerEnter/Stay/Exit 检测）
            float damageRadius = ObjectStatsConfig.DamageRadius;
            AddObjectComponent<SphereCollider>(c =>
            {
                c.isTrigger = true;         // 设置为触发器
                c.radius = damageRadius;    // 触发器范围 = 伤害范围
                c.center = Vector3.zero;
            });

#if UNITY_EDITOR
            Debug.Log($"火球初始化完成，伤害范围：{damageRadius} 米");
#endif
        }

        #endregion

        #region MonoBehaviour 方法

        protected override void Awake()
        {
            base.Awake();
            m_creationTime = Time.time;
        }

        protected override void Update()
        {
            base.Update();

            // 检查火球持续时间
            CheckDuration();
        }

        protected override void OnDestroy()
        {
            // 清理伤害计时器
            m_damageTimers.Clear();
            base.OnDestroy();
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

            // 立即造成首次伤害
            ApplyDamageToTarget(target);

            // 记录伤害时间（如果是持续伤害）
            ObjectStatsConfig stats = GetObjectStats();
            if (stats.IsContinuousDamage)
            {
                m_damageTimers[target] = Time.time;
            }

#if UNITY_EDITOR
            Debug.Log($"物体 {target.name} 进入火球范围，首次伤害已应用");
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
            Debug.Log($"物体 {target.name} 离开火球范围");
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
            return Time.time - lastDamageTime >= stats.DamageInterval;
        }

        /// <summary>
        /// 对目标应用伤害
        /// </summary>
        private void ApplyDamageToTarget(ObjectBase target)
        {
            if (target == null || target.IsDead())
            {
                return;
            }

            float actualDamage = CalculateDamage(target);
            target.TakeDamage(actualDamage);

#if UNITY_EDITOR
            Debug.Log($"火球对 {target.name} 造成 {actualDamage} 点魔法伤害");
#endif
        }

        /// <summary>
        /// 计算实际伤害值（考虑属性和防御）
        /// </summary>
        private float CalculateDamage(ObjectBase target)
        {
            ObjectStatsConfig attackerStats = GetObjectStats();
            ObjectStatsConfig targetStats = target.GetObjectStats();
            return attackerStats.CalculateDamage(targetStats);
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
            if (Time.time - m_creationTime >= stats.DamageDuration)
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
            Debug.Log("火球持续时间结束，自动销毁");
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
                stats.DamageRadius = radius;

                SphereCollider sphereCollider = GetObjectComponent<SphereCollider>();
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
                stats.DamageDuration = duration;
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
    /// 火球类，继承 ObjectBase，实现范围伤害检测系统。
    /// 使用 SphereCollider 触发器检测进入范围内的物体，按间隔造成持续伤害。
    /// 典型应用场景：法师的范围火焰技能、燃烧地面、陷阱区域、BOSS 的 AOE 技能。
    ///
    /// ════════════════════════════════════════════════════════════
    /// 【与 ObjectBase 的关系】
    /// ════════════════════════════════════════════════════════════
    ///   - Fire 继承 ObjectBase，复用父类的属性系统、组件管理、生命周期管理
    ///   - 未重写 MovementConfig（默认 null），表示火球不使用移动系统
    ///     实际场景中可通过外部脚本（如 SkillLauncher）设置火球位置或使用 Rigidbody 移动
    ///   - 伤害计算委托给 ObjectStatsConfig.CalculateDamage（含暴击、防御、穿透公式）
    ///   - 通过 GetObjectStats() 获取自身属性配置，避免每次属性访问产生新实例
    ///   - 父类自动处理：Awake 缓存 MovementConfig（null）、CalculateObjectBounds 零分配
    ///
    /// ════════════════════════════════════════════════════════════
    /// 【字段详解】
    /// ════════════════════════════════════════════════════════════
    ///   m_damageTimers : Dictionary&lt;ObjectBase, float&gt;
    ///     - 键：进入触发器范围的目标对象
    ///     - 值：上次对该目标造成伤害的时间（Time.time）
    ///     - 用途：实现 DamageInterval 间隔伤害，防止每物理帧重复造成伤害
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
    ///   - 停留范围（OnTriggerStay）：按 DamageInterval 间隔持续造成伤害
    ///   - 离开范围（OnTriggerExit）：清除伤害计时器，停止持续伤害
    ///   - 持续时间到达（DamageDuration）：Update 中检测，自动销毁火球
    ///
    ///   持续伤害 vs 一次性伤害：
    ///     - IsContinuousDamage = true（默认）：启用持续伤害，OnTriggerStay 按间隔触发
    ///     - IsContinuousDamage = false：仅 OnTriggerEnter 造成一次伤害，OnTriggerStay 直接返回
    ///     - 配合 DamageInterval 控制持续伤害频率（如 0.5 秒一次）
    ///
    /// ════════════════════════════════════════════════════════════
    /// 【触发器回调详细流程】
    /// ════════════════════════════════════════════════════════════
    ///   OnTriggerEnter(Collider other) 流程：
    ///     1. TryGetComponent&lt;ObjectBase&gt; 获取目标，失败或目标为自身则返回
    ///     2. ApplyDamageToTarget(target) —— 立即造成首次伤害
    ///        ├─ 空引用检查（target == null）和死亡检查（IsDead）
    ///        ├─ CalculateDamage(target) 计算实际伤害（委托给 ObjectStatsConfig）
    ///        └─ target.TakeDamage(actualDamage) 应用伤害
    ///     3. 若 IsContinuousDamage = true，记录 m_damageTimers[target] = Time.time
    ///
    ///   OnTriggerStay(Collider other) 流程：
    ///     1. 检查 IsContinuousDamage，false 则直接返回
    ///     2. TryGetComponent 获取目标，失败或目标为自身则返回
    ///     3. CanApplyDamage(target) 检查伤害间隔
    ///        ├─ 不在字典中 → 返回 true（首次进入但未触发 Enter 的边缘情况）
    ///        └─ Time.time - lastDamageTime &gt;= DamageInterval → 返回 true
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
    ///     2. 调用 CalculateDamage(target) 计算伤害值
    ///     3. target.TakeDamage(actualDamage) 应用伤害（触发目标 OnDamaged 回调）
    ///
    ///   CalculateDamage(target) 内部：
    ///     attackerStats = GetObjectStats()  // 火球自身属性
    ///     targetStats   = target.GetObjectStats()  // 目标属性
    ///     return attackerStats.CalculateDamage(targetStats)
    ///       ↓ 委托给 ObjectStatsConfig 的伤害公式
    ///     公式（DamageType.Magic 时）：
    ///       实际伤害 = (BaseDamage + MagicAttack) × (1 - 目标 MagicDefense 减免)
    ///       减免 = MagicDefense × (1 - MagicPenetration)
    ///     暴击判定：
    ///       Random.value &lt; CriticalRate → 暴击，伤害 × CriticalDamageMultiplier
    ///     命中判定：
    ///       Random.value &gt; HitRate → 未命中，伤害为 0
    ///
    ///   伤害类型对照（DamageType 枚举）：
    ///     Physical：基于 PhysicalAttack + ArmorPenetration
    ///     Magic   ：基于 MagicAttack + MagicPenetration（火球默认）
    ///     True    ：无视防御的固定伤害
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
    ///   Awake（重写）：
    ///     1. base.Awake() —— 父类初始化（缓存 MovementConfig、计算 Bounds 等）
    ///     2. m_creationTime = Time.time —— 记录创建时间用于持续时间判定
    ///
    ///   Update（重写）：
    ///     1. base.Update() —— 父类更新（移动、动画、帧事件等，火球未启用这些系统）
    ///     2. CheckDuration() —— 检查持续时间是否到期
    ///
    ///   OnDestroy（重写）：
    ///     1. m_damageTimers.Clear() —— 清空计时器，防止引用泄漏
    ///     2. base.OnDestroy() —— 父类清理（释放 Playable 资源、Addressables 句柄等）
    ///
    ///   注意：火球未重写 FixedUpdate（父类默认处理移动，但火球 MovementConfig = null
    ///        所以父类 FixedUpdate 会跳过移动逻辑）
    ///
    /// ════════════════════════════════════════════════════════════
    /// 【初始化流程】
    /// ════════════════════════════════════════════════════════════
    ///   1. Awake → SetupComponents()：
    ///      - base.SetupComponents()：父类添加默认组件
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
    ///       MagicAttack = 15f（魔法攻击力）
    ///       MagicPenetration = 0.2f（20% 魔法穿透，无视部分魔防）
    ///     伤害配置：
    ///       BaseDamage = 10f（基础伤害）
    ///       DamageType = DamageType.Magic（魔法伤害）
    ///       DamageRadius = 5f（5 米范围伤害）
    ///       DamageInterval = 0.5f（每 0.5 秒造成一次伤害）
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
    ///   4. 父类 ObjectBase 自动处理的优化（子类无感知）：
    ///      - CalculateObjectBounds 使用静态缓冲区零分配
    ///      - MovementConfig 在 Awake 缓存（火球未使用移动，但仍会缓存 null）
    ///      - GetObjectStats() 返回缓存引用，避免每次访问属性时 new ObjectStatsConfig
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
    ///      - 重写 OnDamaged/OnDeath 播放爆炸特效
    ///      - 在 SetupComponents 中加载 VFX Graph 通过 Addressables
    ///
    ///   2. 添加移动逻辑：
    ///      - 重写 MovementConfig 返回非 null 配置实现飞行火球
    ///      - 或在 Update 中通过 Rigidbody.velocity 控制飞行方向
    ///
    ///   3. 添加伤害类型变化：
    ///      - 运行时修改 ObjectStatsConfig.DamageType 实现伤害类型切换
    ///      - 配合 SetDamageRadius 实现技能变形
    ///
    ///   4. 添加目标过滤：
    ///      - 在 ApplyDamageToTarget 中添加自定义条件（如只伤害特定阵营）
    ///      - 或在 OnTriggerEnter 中调用 CanDealDamageTo 进行阵营判定
    /// </summary>
}