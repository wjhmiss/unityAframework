using UnityEngine;
using AFrameWork.Core;

namespace AFrameWork.Core.SmallBase
{
    /// <summary>
    /// 轻量可池化对象基类（不继承 ObjectBase），供子弹、箭矢、火球、特效实例等大量生成/销毁的物体公用。
    ///
    /// 设计目标（对应优化方案 A/B/C/E）：
    ///   - 方案 B：脱离 ObjectBase，避免 PlayableGraph/动画槽位/组件缓存字典等无关开销
    ///   - 方案 C：owner 阵营信息以 int 字段直接存储，阵营判定热路径无 GC；
    ///             战斗属性通过 Clone 快照在发射时锁定，命中时复用完整伤害公式
    ///   - 方案 A：提供 OnGetFromPool/OnReleaseToPool 钩子，配合 SimpleObjectPool 的 ObjectPool 复用
    ///   - 方案 E：以 Tick(deltaTime) 取代 MonoBehaviour.Update，由 SimpleObjectPool 单点批量驱动
    ///
    /// 子类只需重写：
    ///   - SetupCollider()：配置碰撞体形状/尺寸（子弹用 Sphere，箭矢用 Capsule 等）
    ///   - Tick(deltaTime)：每帧逻辑与寿命检测（调用 Deactivate() 回收）
    ///   - 可选 OnHit / OnLifetimeEnd：命中/过期时的子类特效逻辑
    ///   - 可选 ConfigureParameters：覆盖速度/最大距离等参数
    ///
    /// 扩展示例（新增 Arrow）：
    ///   1. public class Arrow : SimpleObjectBase { SetupCollider→Capsule; Tick→寿命+下坠; ConfigureParameters→速度 }
    ///   2. SimpleObjectPool.Instance.RegisterPrefab&lt;Arrow&gt;(arrowPrefab, prewarm:20);
    ///   3. SimpleObjectPool.Instance.Launch&lt;Arrow&gt;(pos, dir, owner);
    ///
    /// 阵营判定镜像 ObjectStatsConfig.CanDealDamageTo，使用存储的 owner int 字段比较，命中热路径无 GC。
    /// 伤害计算委托 ObjectStatsConfig.CalculateAttack（暴击/闪避/穿透/防御减免），
    /// 通过 ObjectBase.TakeDamage 应用以保留无敌检查/OnDamaged/OnDeath。
    /// </summary>
    public abstract class SimpleObjectBase : MonoBehaviour
    {
        #region 默认运动参数（子类可在 Initialize 中覆盖）

        protected const float k_defaultSpeed = 4f;
        protected const float k_defaultMaxDistance = 8f;
        protected const float k_defaultMaxDistanceSqr = k_defaultMaxDistance * k_defaultMaxDistance;

        // 最小命中距离：飞出此距离后才可触发命中，防止生成时与碰撞体重叠导致立即回收
        protected const float k_minHitDistance = 0.5f;
        protected const float k_minHitDistanceSqr = k_minHitDistance * k_minHitDistance;

        #endregion

        #region 运行时字段

        // 缓存的 Rigidbody（Awake 中获取或添加）
        protected Rigidbody m_rigidbody;

        // 缓存的 Collider（SetupCollider 中创建）
        protected Collider m_collider;

        // ===== 移动状态 =====
        protected Vector3 m_moveDirection;
        protected Vector3 m_startPosition;
        protected float m_speed = k_defaultSpeed;
        protected float m_maxDistanceSqr = k_defaultMaxDistanceSqr;

        // ===== Owner 阵营信息（轻量 int，避免 ObjectStatsConfig 分配） =====
        protected int m_ownerFactionID;
        protected int m_ownerTeamID = -1;
        protected int m_ownerGuildID = -1;
        protected int m_ownerAllianceID = -1;
        protected PVPMode m_ownerPVPMode = PVPMode.None;

        // ===== Owner 属性快照（发射时克隆，命中时用于完整伤害计算：暴击/闪避/穿透/防御减免） =====
        // 持有快照而非活引用：避免 owner 销毁后 fake-null，且发射时属性锁定可预测
        protected ObjectStatsConfig m_attackerStats;

        // ===== Owner ObjectBase 活引用（仅用于 UI 实时刷新生命值/魔法值，不参与伤害计算） =====
        // 伤害计算使用 m_attackerStats 克隆快照；此引用仅供 SetLastAttackRefs 传递给 UI 面板
        protected ObjectBase m_ownerRef;

        /// <summary>
        /// 子弹类型共享属性（所有实例公用一个）。子类重写返回 static readonly 实例。
        /// 命中时与 m_attackerStats（owner 克隆）一起传给 CalculateAttack 累加（类似 Sword 的武器+持有者）。
        /// 默认 null：只使用 owner 克隆（向后兼容）。
        /// </summary>
        protected virtual ObjectStatsConfig SharedStats => null;

        // 2 攻击方 CalculateAttack 复用数组（OnTriggerEnter 单线程，无并发风险；避免 params 数组分配）
        private static readonly ObjectStatsConfig[] s_twoAttackerBuffer = new ObjectStatsConfig[2];

        // ===== 池生命周期标志 =====
        // 是否仍处于活跃状态（false 时由 Pool 回收）
        private bool m_isAlive = false;
        // 是否已初始化（用于跳过未初始化的 Tick）
        protected bool m_isInitialized = false;

        #endregion

        #region 公开属性（供 SimpleObjectPool 查询）

        /// <summary>是否仍处于活跃状态。false 表示需要被 Pool 回收。</summary>
        public bool IsAlive => m_isAlive;

        /// <summary>Rigidbody 引用（Pool 可用于批量设置）</summary>
        public Rigidbody RigidbodyRef => m_rigidbody;

        #endregion

        #region Unity 生命周期

        protected virtual void Awake()
        {
            m_rigidbody = GetComponent<Rigidbody>();
            if (m_rigidbody == null)
            {
                m_rigidbody = gameObject.AddComponent<Rigidbody>();
            }

            // 通用 Rigidbody 配置：无重力、冻结旋转
            m_rigidbody.useGravity = false;
            m_rigidbody.constraints = RigidbodyConstraints.FreezeRotation;
            m_rigidbody.interpolation = RigidbodyInterpolation.Interpolate;

            // 子类创建具体形状的 Collider 并设为触发器
            SetupCollider();
        }

        // 不使用 Start / Update —— 初始化在 Initialize 中完成，每帧逻辑由 Pool 调用 Tick

        #endregion

        #region 子类必须/可选重写的虚方法

        /// <summary>
        /// 子类创建并配置碰撞体（在 Awake 中调用）。
        /// 必须将 Collider.isTrigger 设为 true 以使用 OnTriggerEnter 检测命中。
        /// </summary>
        protected abstract void SetupCollider();

        /// <summary>
        /// 每帧由 SimpleObjectPool 单点调用，子类实现位移推进与寿命检测。
        /// 检测到需要回收时调用 Deactivate()。
        /// 注意：不要在此方法中直接调用对象池 Release，仅调用本类 Deactivate() 设置标志。
        /// </summary>
        public abstract void Tick(float deltaTime);

        /// <summary>命中有效目标后的回调（伤害已由基类应用），子类可播放命中特效。</summary>
        protected virtual void OnHit(ObjectBase target, float finalDamage) { }

        /// <summary>因寿命到期回收时的回调，子类可播放消失特效。</summary>
        protected virtual void OnLifetimeEnd() { }

        /// <summary>
        /// 子类可重写以覆盖默认运动参数（速度/最大距离）。
        /// 在 Initialize 之后由基类调用，子类按需设置 m_speed / m_maxDistanceSqr。
        /// </summary>
        protected virtual void ConfigureParameters() { }

        #endregion

        #region 初始化（由 SimpleObjectPool 在 Get 后调用）

        /// <summary>
        /// 初始化：位置、方向、Owner 属性快照（含阵营与战斗属性）。
        /// 由 Pool 在从池中取出后立即调用。
        /// 伤害参数不再由调用方传入，而是克隆 owner 的 ObjectStatsConfig 作为快照，
        /// 命中时通过 ObjectStatsConfig.CalculateAttack 走完整伤害公式（暴击/闪避/穿透/防御减免）。
        /// </summary>
        public virtual void Initialize(
            Vector3 position,
            Vector3 direction,
            ObjectBase owner)
        {
            // 重置位置与朝向（子弹模型默认朝上 +Y，旋转使其朝向飞行方向）
            transform.position = position;
            if (direction.sqrMagnitude > 0.0001f)
            {
                transform.rotation = Quaternion.LookRotation(direction) * Quaternion.Euler(90f, 0f, 0f);
            }

            m_moveDirection = direction.normalized;
            m_startPosition = position;

            // 继承 Owner 阵营信息（int 字段，用于 CanDealDamageTo 镜像判定，命中热路径无 GC）
            // 同时克隆一份 ObjectStatsConfig 快照用于完整伤害计算
            if (owner != null && owner.HasObjectStats())
            {
                ObjectStatsConfig ownerStats = owner.GetObjectStats();
                m_ownerFactionID = ownerStats.FactionID;
                m_ownerTeamID = ownerStats.TeamID;
                m_ownerGuildID = ownerStats.GuildID;
                m_ownerAllianceID = ownerStats.AllianceID;
                m_ownerPVPMode = ownerStats.CurrentPVPMode;

                // 克隆 owner 属性快照：发射时锁定攻击/穿透/暴击/命中等战斗属性，
                // owner 后续 buff/装备变化不影响已发射的投射物，且 owner 销毁后仍可安全读取
                m_attackerStats = ownerStats.Clone();

                // 保存 owner 活引用（仅用于 UI 实时刷新，不参与伤害计算）
                m_ownerRef = owner;
            }
            else
            {
                m_ownerFactionID = 0;
                m_ownerTeamID = -1;
                m_ownerGuildID = -1;
                m_ownerAllianceID = -1;
                m_ownerPVPMode = PVPMode.None;
                m_attackerStats = null;
                m_ownerRef = null;
            }

            // 子类覆盖运动参数
            m_speed = k_defaultSpeed;
            m_maxDistanceSqr = k_defaultMaxDistanceSqr;
            ConfigureParameters();

            // 同步 Rigidbody 位置/朝向并应用初速度
            // 必须显式同步 position/rotation 以清除插值缓冲，
            // 否则池复用时插值会从旧位置/朝向平滑过渡，导致首帧方向错误（Bug: 子弹头朝上）
            if (m_rigidbody != null)
            {
                m_rigidbody.position = position;
                m_rigidbody.rotation = transform.rotation;
                m_rigidbody.velocity = m_moveDirection * m_speed;
            }

            m_isInitialized = true;
            m_isAlive = true;
        }

        #endregion

        #region 池钩子（由 SimpleObjectPool 调用）

        /// <summary>从池中取出时调用，重置状态并激活 GameObject。</summary>
        public virtual void OnGetFromPool()
        {
            gameObject.SetActive(true);
            m_isAlive = true;
            m_isInitialized = false;
            m_moveDirection = Vector3.zero;
            m_startPosition = Vector3.zero;

            if (m_rigidbody != null)
            {
                m_rigidbody.velocity = Vector3.zero;
                m_rigidbody.angularVelocity = Vector3.zero;
            }
        }

        /// <summary>归还池时调用，停用 GameObject 并清理状态。</summary>
        public virtual void OnReleaseToPool()
        {
            m_isAlive = false;
            m_isInitialized = false;

            // 释放 owner 属性快照引用（下次 Initialize 会重新 Clone）
            m_attackerStats = null;
            // 释放 owner 活引用（下次 Initialize 会重新设置）
            m_ownerRef = null;

            if (m_rigidbody != null)
            {
                m_rigidbody.velocity = Vector3.zero;
                m_rigidbody.angularVelocity = Vector3.zero;
            }

            gameObject.SetActive(false);
        }

        /// <summary>
        /// 请求回收：仅设置 m_isAlive=false，由 SimpleObjectPool.Update 统一调用 pool.Release。
        /// 这样避免在 Tick/OnTriggerEnter 中修改 m_activeObjects 列表导致的迭代冲突。
        /// 命名为 Deactivate 以与 ObjectPool.Release 区分。
        /// </summary>
        protected void Deactivate()
        {
            m_isAlive = false;
        }

        #endregion

        #region 碰撞命中处理

        protected virtual void OnTriggerEnter(Collider other)
        {
            if (!m_isAlive) return;
            // 防止子弹在生成点附近被立即触发回收（生成点可能与敌方碰撞体重叠）
            Vector3 travelOffset = transform.position - m_startPosition;
            if (travelOffset.sqrMagnitude < k_minHitDistanceSqr) return;
            // 使用 TryGetComponent（比 GetComponent + null check 更高效）
            if (!other.TryGetComponent<ObjectBase>(out ObjectBase target)) return;
            if (!target.HasObjectStats()) return;

            ObjectStatsConfig targetStats = target.GetObjectStats();
            if (targetStats.IsDead()) return;

            // 阵营判定（使用存储的 owner int 字段，命中热路径无 GC）
            if (!CanDealDamageTo(targetStats)) return;

            // 计算最终伤害（完整公式：累加攻击/穿透 → 闪避 → 防御减免 → 暴击，不扣血）
            // CalculateAttack 是唯一伤害计算入口，与 TakeDamage 分离避免重复扣血
            // 子类可重写 SharedStats 提供子弹自身属性，与 owner 克隆累加（类似 Sword 的武器+持有者）
            ObjectStatsConfig sharedStats = SharedStats;
            float finalDamage;

            if (sharedStats != null)
            {
                // 子弹共享属性 + owner 克隆（CalculateAttack 内部跳过 null 攻击方）
                // 用 s_twoAttackerBuffer 避免 params 数组分配
                s_twoAttackerBuffer[0] = sharedStats;
                s_twoAttackerBuffer[1] = m_attackerStats;
                finalDamage = ObjectStatsConfig.CalculateAttack(
                    new ObjectStatsConfigMultiplier(), targetStats, s_twoAttackerBuffer);
            }
            else if (m_attackerStats != null)
            {
                // 仅 owner 克隆（向后兼容：子类未重写 SharedStats）
                finalDamage = ObjectStatsConfig.CalculateAttack(
                    new ObjectStatsConfigMultiplier(), targetStats, m_attackerStats);
            }
            else
            {
                // 无任何攻击方属性（owner 无属性且子类无 SharedStats），子弹穿过不回收
                return;
            }

            // 通过 ObjectBase.TakeDamage 应用：保留无敌检查、OnDamaged 回调、OnDeath 处理
            target.TakeDamage(finalDamage);

            // 记录 ObjectBase 活引用供 UI 实时读取生命值/魔法值
            // 攻击方引用与 AttackerSnapshots 对应：sharedStats 位置为 null（无 ObjectBase），m_attackerStats 位置为 m_ownerRef
            if (sharedStats != null)
            {
                // 攻击方 = [子弹共享属性, owner 克隆] → 引用 = [null, m_ownerRef]
                // 显式转换 null 为 ObjectBase，避免被解释为 params 数组本身为 null
                ObjectStatsConfig.SetLastAttackRefs(target, (ObjectBase)null, m_ownerRef);
            }
            else
            {
                // 攻击方 = [owner 克隆] → 引用 = [m_ownerRef]
                ObjectStatsConfig.SetLastAttackRefs(target, m_ownerRef);
            }

            OnHit(target, finalDamage);

            // 命中后回收
            Deactivate();
        }

        #endregion

        #region 伤害计算（委托 ObjectStatsConfig.CalculateAttack，完整公式）

        // 伤害计算已委托给 ObjectStatsConfig.CalculateAttack（静态）：
        //   累加攻击方属性 → 闪避判定 → 防御减免 → 暴击判定
        // 子类无需重写；如需自定义伤害公式，重写 OnTriggerEnter 并自行调用计算方法即可。
        // m_attackerStats 在 Initialize 时由 owner.ObjectStatsConfig.Clone() 创建，
        // 包含 PhysicalAttack/MagicAttack/TrueDamage/ArmorPenetration/MagicPenetration/
        // CriticalRate/CriticalDamageMultiplier/HitRate 等完整战斗属性。

        #endregion

        #region 阵营判定（镜像 ObjectStatsConfig.CanDealDamageTo，使用存储的 owner 字段）

        /// <summary>
        /// 综合判定能否对目标造成伤害。
        /// 判定优先级：TeamID > GuildID > AllianceID > FactionID + FactionRelation + PVPMode。
        /// 与 ObjectStatsConfig.CanDealDamageTo 逻辑一致，但操作 owner 的 int 字段而非 ObjectStatsConfig。
        /// </summary>
        protected virtual bool CanDealDamageTo(ObjectStatsConfig target)
        {
            if (target == null) return false;

            // 优先级1：同一队伍不能互相伤害
            if (m_ownerTeamID > -1 && target.TeamID > -1 && m_ownerTeamID == target.TeamID)
            {
                return false;
            }

            // 优先级2：同一公会（双方 Open PVP 例外）
            if (m_ownerGuildID > -1 && target.GuildID > -1 && m_ownerGuildID == target.GuildID)
            {
                if (m_ownerPVPMode == PVPMode.Open && target.CurrentPVPMode == PVPMode.Open)
                {
                    return true;
                }
                return false;
            }

            // 优先级3：同一同盟（双方 Open PVP 例外）
            if (m_ownerAllianceID > -1 && target.AllianceID > -1 && m_ownerAllianceID == target.AllianceID)
            {
                if (m_ownerPVPMode == PVPMode.Open && target.CurrentPVPMode == PVPMode.Open)
                {
                    return true;
                }
                return false;
            }

            // 优先级4：阵营关系
            FactionRelationType relation = GetFactionRelation(target.FactionID);
            switch (relation)
            {
                case FactionRelationType.Friendly:
                case FactionRelationType.Alliance:
                    return m_ownerPVPMode == PVPMode.Open && target.CurrentPVPMode == PVPMode.Open;
                case FactionRelationType.Neutral:
                case FactionRelationType.Hostile:
                default:
                    return true;
            }
        }

        /// <summary>
        /// 查询阵营关系。复用 ObjectStatsConfig 的阵营 ID 范围规则：
        /// 玩家 1-10、怪物 11-50、NPC 51-99。
        /// </summary>
        protected virtual FactionRelationType GetFactionRelation(int targetFactionID)
        {
            if (m_ownerFactionID == targetFactionID)
            {
                return FactionRelationType.Friendly;
            }

            const int k_playerFactionMinID = 1;
            const int k_playerFactionMaxID = 10;
            const int k_monsterFactionMinID = 11;
            const int k_monsterFactionMaxID = 50;
            const int k_npcFactionMinID = 51;
            const int k_npcFactionMaxID = 99;

            bool ownerIsPlayer = m_ownerFactionID >= k_playerFactionMinID && m_ownerFactionID <= k_playerFactionMaxID;
            bool targetIsPlayer = targetFactionID >= k_playerFactionMinID && targetFactionID <= k_playerFactionMaxID;
            bool targetIsMonster = targetFactionID >= k_monsterFactionMinID && targetFactionID <= k_monsterFactionMaxID;
            bool targetIsNPC = targetFactionID >= k_npcFactionMinID && targetFactionID <= k_npcFactionMaxID;

            if (ownerIsPlayer && targetIsPlayer)
            {
                return FactionRelationType.Friendly;
            }
            if (ownerIsPlayer && targetIsMonster)
            {
                return FactionRelationType.Hostile;
            }
            if (m_ownerFactionID >= k_monsterFactionMinID && m_ownerFactionID <= k_monsterFactionMaxID
                && targetIsPlayer)
            {
                return FactionRelationType.Hostile;
            }
            if (targetIsNPC)
            {
                return FactionRelationType.Neutral;
            }
            return FactionRelationType.Neutral;
        }

        #endregion
    }

    /// <summary>
    /// SimpleObjectBase 使用说明：
    /// ============================================================
    /// 轻量可池化对象基类（不继承 ObjectBase），供子弹、箭矢、火球、特效实例等
    /// 大量生成/销毁的物体公用。以 Tick(deltaTime) 取代 MonoBehaviour.Update，
    /// 由 SimpleObjectPool 单点批量驱动，消除 N 个 Pool 的 native 边界开销。
    ///
    /// ════════════════════════════════════════════════════════════
    /// 【与 ObjectBase 的关系】
    /// ════════════════════════════════════════════════════════════
    ///   - SimpleObjectBase 直接继承 MonoBehaviour，不继承 ObjectBase
    ///   - 设计原因：ObjectBase 携带 PlayableGraph/动画槽位/组件缓存字典等重资产，
    ///     对投射物这类轻量高频物体是浪费，方案 B 专门剥离以减负
    ///   - 阵营信息以 int 字段镜像存储（m_ownerFactionID 等），避免持有 ObjectStatsConfig 引用
    ///   - 伤害计算与阵营判定逻辑镜像 ObjectStatsConfig.CanDealDamageTo，
    ///     但操作 owner 的 int 字段而非 ObjectStatsConfig 实例，命中时无 GC
    ///
    /// ════════════════════════════════════════════════════════════
    /// 【子类必须/可选重写的方法】
    /// ════════════════════════════════════════════════════════════
    ///   抽象方法（子类必须实现）：
    ///     SetupCollider()        — 创建并配置碰撞体形状/尺寸，必须设 isTrigger=true
    ///     Tick(float deltaTime)   — 每帧逻辑（位移推进、寿命检测），超期时调用 Deactivate()
    ///
    ///   可选虚方法（子类按需重写）：
    ///     OnHit(target, finalDamage) — 命中有效目标后的回调（伤害已应用），播放命中特效
    ///     OnLifetimeEnd()            — 因寿命到期回收时的回调，播放消失特效
    ///     ConfigureParameters()      — 覆盖默认运动参数（m_speed / m_maxDistanceSqr）
    ///     Initialize(...)            — 默认实现已完整，仅在需扩展初始化时重写
    ///     OnGetFromPool() / OnReleaseToPool() — 池生命周期钩子，默认实现已处理状态重置
    ///     CalculateAttack(...)          — 伤害计算委托 ObjectStatsConfig 静态方法，子类一般无需重写；
    ///                                           如需自定义公式可重写 OnTriggerEnter 自行调用
    ///     CanDealDamageTo(target) / GetFactionRelation(targetID) — 自定义阵营判定
    ///
    /// ════════════════════════════════════════════════════════════
    /// 【默认运动参数】
    /// ════════════════════════════════════════════════════════════
    ///   k_defaultSpeed = 4f             — 默认飞行速度（米/秒）
    ///   k_defaultMaxDistance = 8f       — 默认最大飞行距离（米）
    ///   k_defaultMaxDistanceSqr         — 上述距离的平方（避免每帧开方）
    ///   k_minHitDistance = 0.5f         — 最小命中距离（防止生成时与碰撞体重叠立即回收）
    ///   子类通过 ConfigureParameters 覆盖 m_speed / m_maxDistanceSqr
    ///
    /// ════════════════════════════════════════════════════════════
    /// 【运行时字段详解】
    /// ════════════════════════════════════════════════════════════
    ///   组件缓存：
    ///     m_rigidbody — Awake 中获取或添加，配置无重力+冻结旋转+插值
    ///     m_collider  — SetupCollider 中由子类创建并赋值
    ///
    ///   移动状态：
    ///     m_moveDirection  — 飞行方向（已归一化）
    ///     m_startPosition  — 生成位置（用于超距检测）
    ///     m_speed          — 当前飞行速度
    ///     m_maxDistanceSqr — 最大飞行距离平方
    ///
    ///   Owner 阵营信息（轻量 int，方案 C）：
    ///     m_ownerFactionID  — 阵营 ID（玩家 1-10、怪物 11-50、NPC 51-99）
    ///     m_ownerTeamID     — 队伍 ID（-1=无）
    ///     m_ownerGuildID    — 公会 ID（-1=无）
    ///     m_ownerAllianceID — 同盟 ID（-1=无）
    ///     m_ownerPVPMode    — PVP 模式
    ///
    ///   伤害参数（发射时克隆 owner 属性快照）：
    ///     m_attackerStats — owner 的 ObjectStatsConfig 克隆（含攻击/穿透/暴击/命中/闪避等）
    ///                      命中时作为 attacker 传入 CalculateAttack，走完整伤害公式
    ///                      持有快照而非活引用：owner 销毁后仍可安全读取，发射后属性锁定可预测
    ///
    ///   池生命周期标志：
    ///     m_isAlive       — 是否仍活跃（false 时由 Pool 回收）
    ///     m_isInitialized — 是否已初始化（跳过未初始化的 Tick）
    ///
    /// ════════════════════════════════════════════════════════════
    /// 【Initialize 初始化流程】
    /// ════════════════════════════════════════════════════════════
    ///   由 SimpleObjectPool 在从池中取出后立即调用：
    ///     1. 设置 transform.position 与 rotation（朝向飞行方向）
    ///     2. 归一化方向并记录 m_startPosition
    ///     3. 从 owner 的 ObjectStatsConfig 继承阵营信息（int 字段，用于 CanDealDamageTo 镜像判定）
    ///     4. 克隆 owner 的 ObjectStatsConfig 到 m_attackerStats（锁定攻击/穿透/暴击/命中等战斗属性）
    ///     5. 调用 ConfigureParameters() 让子类覆盖运动参数
    ///     6. 同步 Rigidbody 位置/朝向并应用初速度
    ///        （必须显式同步以清除插值缓冲，否则池复用首帧方向错误）
    ///     7. 设置 m_isInitialized=true 和 m_isAlive=true
    ///
    ///   注意：owner 为 null 或无属性时阵营信息清零（FactionID=0, TeamID=-1 等），m_attackerStats=null
    ///
    /// ════════════════════════════════════════════════════════════
    /// 【池生命周期钩子】
    /// ════════════════════════════════════════════════════════════
    ///   OnGetFromPool()：
    ///     - 激活 GameObject（SetActive(true)）
    ///     - m_isAlive=true, m_isInitialized=false
    ///     - 清零 Rigidbody 速度与角速度
    ///     - 重置移动方向与起始位置
    ///
    ///   OnReleaseToPool()：
    ///     - m_isAlive=false, m_isInitialized=false
    ///     - 清空 m_attackerStats 引用（下次 Initialize 会重新 Clone）
    ///     - 清零 Rigidbody 速度与角速度
    ///     - 停用 GameObject（SetActive(false)）
    ///
    ///   Deactivate()：
    ///     - 仅设置 m_isAlive=false，不直接调用 Pool.Release
    ///     - 由 SimpleObjectPool.Update 统一检测并归还池
    ///     - 这样避免在 Tick/OnTriggerEnter 中修改 m_activeObjects 列表导致迭代冲突
    ///
    /// ════════════════════════════════════════════════════════════
    /// 【碰撞命中处理流程】
    /// ════════════════════════════════════════════════════════════
    ///   OnTriggerEnter(Collider other) 完整流程：
    ///     1. m_isAlive 检查，已回收则返回
    ///     2. 最小命中距离检查（travelOffset.sqrMagnitude &lt; k_minHitDistanceSqr）
    ///        防止子弹在生成点附近被立即触发回收（生成点可能与敌方碰撞体重叠）
    ///     3. TryGetComponent&lt;ObjectBase&gt; 获取目标，失败则返回
    ///     4. target.HasObjectStats() 检查，无属性则返回
    ///     5. targetStats.IsDead() 检查，已死亡则返回
    ///     6. CanDealDamageTo(targetStats) 阵营判定，友方则返回（子弹穿过不回收）
    ///     7. 攻击方属性检查：
    ///        - SharedStats != null（子类重写）：子弹共享属性 + owner 克隆一起传入（2 攻击方累加）
    ///        - SharedStats == null 且 m_attackerStats != null：仅 owner 克隆（向后兼容）
    ///        - 两者都为 null：子弹穿过不回收（owner 无属性且子类无共享属性）
    ///     8. CalculateAttack(new ObjectStatsConfigMultiplier(), targetStats, attackers...) 计算最终伤害
    ///        （完整公式：累加所有攻击方属性 → 闪避 → 防御减免 → 暴击；被闪避返回 0，不扣血）
    ///     9. target.TakeDamage(finalDamage) 应用伤害（经 ObjectBase.TakeDamage 保留无敌/回调/死亡）
    ///     10. OnHit(target, finalDamage) 回调子类播放命中特效
    ///     11. Deactivate() 请求回收
    ///
    /// ════════════════════════════════════════════════════════════
    /// 【伤害计算公式】
    /// ════════════════════════════════════════════════════════════
    ///   委托 ObjectStatsConfig.CalculateAttack(new ObjectStatsConfigMultiplier(), targetStats, attackers)：
    ///   攻击方来源（按子类是否重写 SharedStats）：
    ///     - SharedStats（子类 static readonly）：子弹自身基础属性，所有实例共享，零 GC
    ///     - m_attackerStats（owner 克隆）：发射时锁定的 owner 战斗属性快照
    ///     两者同时存在时累加（类似 Sword 的武器+持有者），不存在时单独使用或跳过
    ///
    ///     1. 累加攻击方属性（PhysicalAttack / MagicAttack / TrueDamage / 穿透 / 暴击 / 命中）
    ///     2. 闪避判定（目标 EvasionRate vs 攻击方累加 HitRate，闪避返回 0）
    ///     3. 防御减免：
    ///        Physical：PhysicalAttack × (100 / (100 + 目标物防 × (1 - ArmorPenetration)))
    ///        Magic   ：MagicAttack × (100 / (100 + 目标魔防 × (1 - MagicPenetration)))
    ///        True    ：TrueDamage（无视防御）
    ///        三种伤害累加（与单一 DamageType 的旧实现不同，现在一发子弹可同时造成多类型伤害）
    ///     4. 暴击判定（Random.value &lt; 累加 CriticalRate → 伤害 × CriticalDamageMultiplier）
    ///
    ///   注意：CalculateAttack 只计算伤害不扣血，由 target.TakeDamage 应用。
    ///        这样分离计算与应用，避免重复扣血风险。
    ///        m_attackerStats 为发射时 Clone 的快照，owner 后续 buff/装备变化不影响已发射投射物。
    ///
    /// ════════════════════════════════════════════════════════════
    /// 【阵营判定规则】
    /// ════════════════════════════════════════════════════════════
    ///   CanDealDamageTo(target) 判定优先级（与 ObjectStatsConfig 一致）：
    ///     1. TeamID（同一队伍不能伤害，-1=无队伍）
    ///     2. GuildID（同一公会默认不能伤害，双方 Open PVP 例外）
    ///     3. AllianceID（同一同盟默认不能伤害，双方 Open PVP 例外）
    ///     4. FactionID + FactionRelation：
    ///        - Friendly/Alliance：双方 Open PVP 才可伤害
    ///        - Neutral/Hostile：可以伤害
    ///
    ///   GetFactionRelation(targetFactionID) 默认实现（与 ObjectStatsConfig 镜像）：
    ///     - 同 FactionID → Friendly
    ///     - 玩家(1-10) ↔ 玩家(1-10) → Friendly
    ///     - 玩家(1-10) ↔ 怪物(11-50) → Hostile
    ///     - 涉及 NPC(51-99) → Neutral
    ///     - 其他 → Neutral
    ///     子类可重写以接入 FactionRelationManager
    ///
    /// ════════════════════════════════════════════════════════════
    /// 【性能优化】
    /// ════════════════════════════════════════════════════════════
    ///   - 方案 A：配合 SimpleObjectPool 的 ObjectPool 复用，无 Instantiate/Destroy
    ///   - 方案 B：脱离 ObjectBase，避免 PlayableGraph/动画槽位/组件缓存字典等开销
    ///   - 方案 C：owner 阵营信息以 int 字段存储，命中时无 ObjectStatsConfig 分配
    ///   - 方案 E：Tick(deltaTime) 由 Pool 单点批量驱动，消除 N 个 Update 的 native 开销
    ///   - 距离比较使用 sqrMagnitude 避免开方
    ///   - OnTriggerEnter 使用 TryGetComponent 减少空检查开销
    ///   - Deactivate() 仅设标志，避免在物理回调中修改列表导致迭代冲突
    ///
    /// ────────────────────────────────────────────────────────────
    /// 示例 1：自定义子弹（继承 SimpleObjectBase）
    /// ────────────────────────────────────────────────────────────
    /// <code>
    /// public class Bullet : SimpleObjectBase
    /// {
    ///     private const float k_speed = 8f;
    ///     private const float k_maxDistance = 15f;
    ///     private const float k_maxDistanceSqr = k_maxDistance * k_maxDistance;
    ///     private const float k_colliderRadius = 0.2f;
    ///
    ///     protected override void SetupCollider()
    ///     {
    ///         SphereCollider collider = GetComponent&lt;SphereCollider&gt;();
    ///         if (collider == null) collider = gameObject.AddComponent&lt;SphereCollider&gt;();
    ///         collider.radius = k_colliderRadius;
    ///         collider.isTrigger = true;
    ///         m_collider = collider;
    ///     }
    ///
    ///     public override void Tick(float deltaTime)
    ///     {
    ///         if (!m_isInitialized) return;
    ///         Vector3 offset = transform.position - m_startPosition;
    ///         if (offset.sqrMagnitude >= k_maxDistanceSqr)
    ///         {
    ///             OnLifetimeEnd();
    ///             Deactivate();
    ///         }
    ///     }
    ///
    ///     protected override void ConfigureParameters()
    ///     {
    ///         m_speed = k_speed;
    ///         m_maxDistanceSqr = k_maxDistanceSqr;
    ///     }
    /// }
    /// </code>
    ///
    /// ────────────────────────────────────────────────────────────
    /// 示例 2：注册并发射
    /// ────────────────────────────────────────────────────────────
    /// <code>
    /// // 1. 在 Addressables Groups 中为预制体添加 "PoolObjects" label
    /// //    预制体根节点必须挂载 SimpleObjectBase 子类组件
    /// // 2. SimpleObjectPool.Instance.Start 时自动注册
    /// // 3. 发射（伤害由 owner 的 ObjectStatsConfig 自动克隆，无需传入 damage/damageType）：
    /// Bullet bullet = SimpleObjectPool.Instance.Launch&lt;Bullet&gt;(
    ///     position: spawnPos,
    ///     direction: targetDir,
    ///     owner: shooter
    /// );
    /// </code>
    ///
    /// ────────────────────────────────────────────────────────────
    /// 示例 3：自定义阵营判定（接入 FactionRelationManager）
    /// ────────────────────────────────────────────────────────────
    /// <code>
    /// public class Arrow : SimpleObjectBase
    /// {
    ///     protected override FactionRelationType GetFactionRelation(int targetFactionID)
    ///     {
    ///         return FactionRelationManager.GetFactionRelation(m_ownerFactionID, targetFactionID);
    ///     }
    /// }
    /// </code>
    ///
    /// ────────────────────────────────────────────────────────────
    /// 示例 4：扩展 Initialize（添加自定义参数）
    /// ────────────────────────────────────────────────────────────
    /// <code>
    /// public class HomingMissile : SimpleObjectBase
    /// {
    ///     private Transform m_target;
    ///
    ///     public override void Initialize(
    ///         Vector3 position, Vector3 direction,
    ///         ObjectBase owner)
    ///     {
    ///         base.Initialize(position, direction, owner);
    ///         m_target = FindNearestEnemy(position, m_ownerFactionID);
    ///     }
    ///
    ///     public override void Tick(float deltaTime)
    ///     {
    ///         if (m_target != null)
    ///         {
    ///             Vector3 newDir = (m_target.position - transform.position).normalized;
    ///             m_rigidbody.velocity = newDir * m_speed;
    ///         }
    ///         // ... 超距检测 ...
    ///     }
    /// }
    /// </code>
    ///
    /// ────────────────────────────────────────────────────────────
    /// 示例 5：固定伤害技能子弹（覆盖克隆的攻击属性）
    /// ────────────────────────────────────────────────────────────
    /// <code>
    /// public class FixedDamageBullet : Bullet
    /// {
    ///     public override void Initialize(Vector3 position, Vector3 direction, ObjectBase owner)
    ///     {
    ///         base.Initialize(position, direction, owner);
    ///         // 覆盖克隆快照的攻击属性为固定值，忽略 owner 的攻击力
    ///         if (m_attackerStats != null)
    ///         {
    ///             m_attackerStats.PhysicalAttack = 50f;
    ///             m_attackerStats.MagicAttack = 0f;
    ///             m_attackerStats.TrueDamage = 0f;
    ///         }
    ///     }
    /// }
    /// </code>
    /// </summary>
}