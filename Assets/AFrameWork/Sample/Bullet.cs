using UnityEngine;
using AFrameWork.Core;

namespace AFrameWork.Sample
{
    /// <summary>
    /// 子弹类，继承 ObjectBase，作为武器投射物。
    /// 生成后朝单一方向匀速移动，触碰敌人时造成物理伤害并销毁。
    /// 超过最大有效距离后自动消失。
    /// 阵营信息从父级 ObjectBase 继承（参考 Sword）。
    /// </summary>
    public class Bullet : ObjectBase
    {
        #region 常量

        private const float k_maxDistance = 8f;
        private const float k_speed = 4f;
        private const float k_maxDistanceSqr = k_maxDistance * k_maxDistance;

        #endregion

        #region 配置属性

        protected override MovementConfig MovementConfig => null;

        protected override ObjectStatsConfig ObjectStatsConfig => new ObjectStatsConfig
        {
            Type = ObjectType.Weapon,
            FactionID = 0,              // Start 中由父级覆盖
            MaxHealth = 1f,
            CurrentHealth = 1f,
            PhysicalAttack = 10f,
            PhysicalDefense = 0f,
            MagicAttack = 0f,
            MagicDefense = 0f,
            BaseDamage = 10f,
            DamageType = DamageType.Physical,
            CanDealDamage = true,
            MoveSpeed = k_speed
        };

        #endregion

        #region 字段

        private ObjectBase m_owner;
        private Vector3 m_moveDirection;
        private Vector3 m_startPosition;
        private bool m_isDestroyed;
        private bool m_isInitialized;

        #endregion

        #region 初始化方法

        protected override void SetupComponents()
        {
            base.SetupComponents();

            // Rigidbody — 非 kinematic，通过 velocity 驱动飞行
            m_rigidbody = AddObjectComponent<Rigidbody>(rb =>
            {
                rb.mass = 0.1f;
                rb.useGravity = false;
                rb.constraints = RigidbodyConstraints.FreezeRotation;
            });

            // CapsuleCollider — 包裹子弹，设为触发器用于检测敌人
            AddCapsuleCollider(CalculateObjectBounds(), Vector3.one, Vector3.zero, cc =>
            {
                cc.isTrigger = true;
            });
        }

        private void Start()
        {
            // 已通过 Initialize 外部初始化时，跳过阵营继承
            if (!m_isInitialized)
            {
                InheritOwnerFactionInfo();
            }

            m_startPosition = transform.position;

            // 未指定方向时使用自身朝向
            if (m_moveDirection == Vector3.zero)
            {
                m_moveDirection = transform.forward;
            }

            m_rigidbody.velocity = m_moveDirection * k_speed;
        }

        #endregion

        #region 移动与距离检测

        protected override void Update()
        {
            base.Update();

            if (m_isDestroyed) return;

            // 超过最大有效距离后销毁
            Vector3 offset = transform.position - m_startPosition;
            if (offset.sqrMagnitude >= k_maxDistanceSqr)
            {
                DestroyBullet();
            }
        }

        /// <summary>
        /// 设置子弹飞行方向（外部调用，应在生成后立即设置）。
        /// 若 Start 已执行则立即应用速度。
        /// </summary>
        public void SetDirection(Vector3 direction)
        {
            m_moveDirection = direction.normalized;

            // 子弹模型默认子弹头朝上（+Y），旋转使其朝向飞行方向
            if (m_moveDirection != Vector3.zero)
            {
                transform.rotation = Quaternion.LookRotation(m_moveDirection) * Quaternion.Euler(90f, 0f, 0f);
            }

            if (m_rigidbody != null)
            {
                m_rigidbody.velocity = m_moveDirection * k_speed;
            }
        }

        /// <summary>
        /// 外部初始化子弹的拥有者和飞行方向。
        /// Instantiate 后立即调用，在 Start 执行前完成阵营继承和方向设置。
        /// </summary>
        public void Initialize(ObjectBase owner, Vector3 direction)
        {
            m_isInitialized = true;
            m_owner = owner;

            if (owner != null && owner.HasObjectStats())
            {
                ObjectStatsConfig ownerStats = owner.GetObjectStats();
                ObjectStatsConfig myStats = GetObjectStats();

                myStats.FactionID = ownerStats.FactionID;
                myStats.TeamID = ownerStats.TeamID;
                myStats.GuildID = ownerStats.GuildID;
                myStats.AllianceID = ownerStats.AllianceID;
                myStats.CurrentPVPMode = ownerStats.CurrentPVPMode;
            }

            SetDirection(direction);
        }

        #endregion

        #region 碰撞检测与伤害

        private void OnTriggerEnter(Collider other)
        {
            if (m_isDestroyed) return;
            if (!HasObjectStats()) return;

            ObjectStatsConfig myStats = GetObjectStats();
            if (!myStats.CanDealDamage) return;

            if (!other.TryGetComponent<ObjectBase>(out ObjectBase target)) return;
            if (target == m_owner) return;
            if (!target.HasObjectStats()) return;

            ObjectStatsConfig targetStats = target.GetObjectStats();
            if (targetStats.IsDead()) return;

            // 阵营关系判定
            if (!myStats.CanDealDamageTo(targetStats)) return;

            float damage = myStats.CalculateDamage(targetStats);
            target.TakeDamage(damage);

            DestroyBullet();
        }

        #endregion

        #region 阵营继承（参考 Sword）

        private void InheritOwnerFactionInfo()
        {
            m_owner = FindParentObjectBase();
            if (m_owner == null || !m_owner.HasObjectStats()) return;

            ObjectStatsConfig ownerStats = m_owner.GetObjectStats();
            ObjectStatsConfig myStats = GetObjectStats();

            myStats.FactionID = ownerStats.FactionID;
            myStats.TeamID = ownerStats.TeamID;
            myStats.GuildID = ownerStats.GuildID;
            myStats.AllianceID = ownerStats.AllianceID;
            myStats.CurrentPVPMode = ownerStats.CurrentPVPMode;
        }

        private ObjectBase FindParentObjectBase()
        {
            Transform current = transform.parent;
            while (current != null)
            {
                if (current.TryGetComponent<ObjectBase>(out ObjectBase parentObj))
                    return parentObj;
                current = current.parent;
            }
            return null;
        }

        #endregion

        #region 销毁

        private void DestroyBullet()
        {
            if (m_isDestroyed) return;
            m_isDestroyed = true;
            Destroy(gameObject);
        }

        #endregion
    }
}
