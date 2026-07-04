using UnityEngine;
using AFrameWork.Core;

namespace AFrameWork.Sample
{
    /// <summary>
    /// 投射物轻量基类（不继承 ObjectBase），供子弹、箭矢、火球等大量生成/销毁的飞行物体公用。
    ///
    /// 设计目标（对应优化方案 A/B/C/E）：
    ///   - 方案 B：脱离 ObjectBase，避免 PlayableGraph/动画槽位/组件缓存字典等无关开销
    ///   - 方案 C：owner 阵营信息以 int 字段直接存储，避免每次访问 ObjectStatsConfig 属性产生 GC
    ///   - 方案 A：提供 OnGetFromPool/OnReleaseToPool 钩子，配合 ProjectileManager 的 ObjectPool 复用
    ///   - 方案 E：以 Tick(deltaTime) 取代 MonoBehaviour.Update，由 ProjectileManager 单点批量驱动
    ///
    /// 子类只需重写：
    ///   - SetupCollider()：配置碰撞体形状/尺寸（子弹用 Sphere，箭矢用 Capsule 等）
    ///   - Tick(deltaTime)：每帧位移与超距/寿命检测（调用 Release() 回收）
    ///   - 可选 OnHitTarget / OnExpire：命中/过期时的子类特效逻辑
    ///
    /// 阵营判定镜像 ObjectStatsConfig.CanDealDamageTo，使用存储的 owner 字段比较，
    /// 命中时仅读取 target 的 ObjectStatsConfig（已缓存于 target 侧，无 GC）。
    /// </summary>
    public abstract class ProjectileBase : MonoBehaviour
    {
        #region 默认运动参数（子类可在 Initialize 中覆盖）

        protected const float k_defaultSpeed = 4f;
        protected const float k_defaultMaxDistance = 8f;
        protected const float k_defaultMaxDistanceSqr = k_defaultMaxDistance * k_defaultMaxDistance;

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

        // ===== 伤害参数（发射时预计算，命中时应用防御） =====
        protected float m_damage;
        protected DamageType m_damageType = DamageType.Physical;
        protected float m_armorPenetration;
        protected float m_magicPenetration;

        // ===== 池生命周期标志 =====
        // 是否处于活跃飞行状态（false 时由 Manager 回收）
        private bool m_isActive = false;
        // 是否已初始化（用于跳过未初始化的 Tick）
        protected bool m_isInitialized = false;

        #endregion

        #region 公开属性（供 ProjectileManager 查询）

        /// <summary>是否仍处于活跃飞行状态。false 表示需要被 Manager 回收。</summary>
        public bool IsActive => m_isActive;

        /// <summary>Rigidbody 引用（Manager 可用于批量设置）</summary>
        public Rigidbody CachedRigidbody => m_rigidbody;

        #endregion

        #region Unity 生命周期

        protected virtual void Awake()
        {
            m_rigidbody = GetComponent<Rigidbody>();
            if (m_rigidbody == null)
            {
                m_rigidbody = gameObject.AddComponent<Rigidbody>();
            }

            // 投射物通用 Rigidbody 配置：无重力、冻结旋转
            m_rigidbody.useGravity = false;
            m_rigidbody.constraints = RigidbodyConstraints.FreezeRotation;
            m_rigidbody.interpolation = RigidbodyInterpolation.Interpolate;

            // 子类创建具体形状的 Collider 并设为触发器
            SetupCollider();
        }

        // 不使用 Start / Update —— 初始化在 Initialize 中完成，每帧逻辑由 Manager 调用 Tick

        #endregion

        #region 子类必须/可选重写的虚方法

        /// <summary>
        /// 子类创建并配置碰撞体（在 Awake 中调用）。
        /// 必须将 Collider.isTrigger 设为 true 以使用 OnTriggerEnter 检测命中。
        /// </summary>
        protected abstract void SetupCollider();

        /// <summary>
        /// 每帧由 ProjectileManager 单点调用，子类实现位移推进与超距/寿命检测。
        /// 检测到需要回收时调用 Release()。
        /// 注意：不要在此方法中直接调用对象池 Release，仅调用本类 Release() 设置标志。
        /// </summary>
        public abstract void Tick(float deltaTime);

        /// <summary>命中有效目标后的回调（伤害已由基类应用），子类可播放命中特效。</summary>
        protected virtual void OnHitTarget(ObjectBase target, float finalDamage) { }

        /// <summary>因超距/寿命到期回收时的回调，子类可播放消失特效。</summary>
        protected virtual void OnExpire() { }

        /// <summary>
        /// 子类可重写以覆盖默认运动参数（速度/最大距离）。
        /// 在 Initialize 之后由基类调用，子类按需设置 m_speed / m_maxDistanceSqr。
        /// </summary>
        protected virtual void ConfigureMotion() { }

        #endregion

        #region 初始化（由 ProjectileManager 在 Get 后调用）

        /// <summary>
        /// 初始化投射物：位置、方向、Owner 阵营、伤害参数。
        /// 由 Manager 在从池中取出后立即调用。
        /// </summary>
        public virtual void Initialize(
            Vector3 position,
            Vector3 direction,
            ObjectBase owner,
            float damage,
            DamageType damageType = DamageType.Physical)
        {
            // 重置位置与朝向（子弹模型默认朝上 +Y，旋转使其朝向飞行方向）
            transform.position = position;
            if (direction.sqrMagnitude > 0.0001f)
            {
                transform.rotation = Quaternion.LookRotation(direction) * Quaternion.Euler(90f, 0f, 0f);
            }

            m_moveDirection = direction.normalized;
            m_startPosition = position;
            m_damage = damage;
            m_damageType = damageType;
            m_armorPenetration = 0f;
            m_magicPenetration = 0f;

            // 继承 Owner 阵营信息（直接读 int 字段，避免持有 ObjectStatsConfig 引用）
            if (owner != null && owner.HasObjectStats())
            {
                ObjectStatsConfig ownerStats = owner.GetObjectStats();
                m_ownerFactionID = ownerStats.FactionID;
                m_ownerTeamID = ownerStats.TeamID;
                m_ownerGuildID = ownerStats.GuildID;
                m_ownerAllianceID = ownerStats.AllianceID;
                m_ownerPVPMode = ownerStats.CurrentPVPMode;

                // 继承穿透属性，使命中时能正确计算防御减免
                m_armorPenetration = ownerStats.ArmorPenetration;
                m_magicPenetration = ownerStats.MagicPenetration;
            }
            else
            {
                m_ownerFactionID = 0;
                m_ownerTeamID = -1;
                m_ownerGuildID = -1;
                m_ownerAllianceID = -1;
                m_ownerPVPMode = PVPMode.None;
            }

            // 子类覆盖运动参数
            m_speed = k_defaultSpeed;
            m_maxDistanceSqr = k_defaultMaxDistanceSqr;
            ConfigureMotion();

            // 应用初速度
            if (m_rigidbody != null)
            {
                m_rigidbody.velocity = m_moveDirection * m_speed;
            }

            m_isInitialized = true;
            m_isActive = true;
        }

        #endregion

        #region 池钩子（由 ProjectileManager 调用）

        /// <summary>从池中取出时调用，重置状态并激活 GameObject。</summary>
        public virtual void OnGetFromPool()
        {
            gameObject.SetActive(true);
            m_isActive = true;
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
            m_isActive = false;
            m_isInitialized = false;

            if (m_rigidbody != null)
            {
                m_rigidbody.velocity = Vector3.zero;
                m_rigidbody.angularVelocity = Vector3.zero;
            }

            gameObject.SetActive(false);
        }

        /// <summary>
        /// 请求回收：仅设置 m_isActive=false，由 ProjectileManager.Update 统一调用 pool.Release。
        /// 这样避免在 Tick/OnTriggerEnter 中修改 m_activeBullets 列表导致的迭代冲突。
        /// </summary>
        protected void Release()
        {
            m_isActive = false;
        }

        #endregion

        #region 碰撞命中处理

        protected virtual void OnTriggerEnter(Collider other)
        {
            if (!m_isActive) return;

            // 使用 TryGetComponent（比 GetComponent + null check 更高效）
            if (!other.TryGetComponent<ObjectBase>(out ObjectBase target)) return;
            if (!target.HasObjectStats()) return;

            ObjectStatsConfig targetStats = target.GetObjectStats();
            if (targetStats.IsDead()) return;

            // 阵营判定（使用存储的 owner 字段，无 GC）
            if (!CanDealDamageTo(targetStats)) return;

            // 计算最终伤害（应用目标防御与穿透）
            float finalDamage = CalculateFinalDamage(targetStats);
            target.TakeDamage(finalDamage);

            OnHitTarget(target, finalDamage);

            // 命中后回收
            Release();
        }

        #endregion

        #region 伤害计算（无 GC，使用预存伤害值 + 目标防御）

        /// <summary>
        /// 根据伤害类型应用目标防御减免。
        /// m_damage 在发射时由 owner 攻击力预计算，此处仅做防御端减免。
        /// </summary>
        protected virtual float CalculateFinalDamage(ObjectStatsConfig targetStats)
        {
            if (targetStats == null) return m_damage;

            switch (m_damageType)
            {
                case DamageType.Physical:
                {
                    float effectiveDefense = targetStats.PhysicalDefense * (1f - m_armorPenetration);
                    return m_damage * (100f / (100f + effectiveDefense));
                }
                case DamageType.Magic:
                {
                    float effectiveDefense = targetStats.MagicDefense * (1f - m_magicPenetration);
                    return m_damage * (100f / (100f + effectiveDefense));
                }
                case DamageType.True:
                default:
                    return m_damage;
            }
        }

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
}
