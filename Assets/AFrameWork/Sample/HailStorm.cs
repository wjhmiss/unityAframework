using System.Collections.Generic;
using UnityEngine;
using AFrameWork.Core;
using AFrameWork.Core.SmallBase;

namespace AFrameWork.Sample
{
    /// <summary>
    /// 冰雹类，继承 WeaponBase（轻量武器基类，不继承 ObjectBase），实现范围伤害检测系统。
    /// 当继承 ObjectBase 的物体进入范围时，自动造成持续冰霜伤害并减速目标移动速度。
    /// 使用触发器检测 + 时间缓存机制实现高效伤害计算。
    ///
    /// 与 Fire 的区别：
    ///   - 伤害类型：冰雹以物理伤害为主（物理攻击力高），附带少量魔法穿透
    ///   - 视觉效果：冰晶粒子从上方落下（模拟冰雹），地面结霜效果
    ///   - 特殊效果：进入范围的物体会被减速（通过修改 MoveSpeed 倍率实现）
    /// </summary>
    public class HailStorm : WeaponBase
    {
        #region 配置属性

        /// <summary>
        /// 物体属性配置，包含冰雹的攻击属性和伤害配置
        /// </summary>
        protected override ObjectStatsConfig ObjectStatsConfig => ObjectStatsConfig.CreateHailStorm();

        #endregion

        #region 字段

        // 伤害计时器字典（记录每个对象的上次伤害时间）
        private Dictionary<ObjectBase, float> m_damageTimers = new Dictionary<ObjectBase, float>(8);

        // 减速状态字典（记录被减速的目标及其原始速度倍率）
        private Dictionary<ObjectBase, float> m_slowedTargets = new Dictionary<ObjectBase, float>(8);

        // 创建时间
        private float m_creationTime;

        // 是否已销毁
        private bool m_isDestroyed = false;

        #endregion

        #region 初始化方法

        protected override void SetupComponents()
        {
            base.SetupComponents();

            // 添加 SphereCollider 作为触发器（用于 OnTriggerEnter/Stay/Exit 检测）
            float damageRadius = ObjectStatsConfig.DamageRadius;
            AddObjectComponent<SphereCollider>(c =>
            {
                c.isTrigger = true;
                c.radius = damageRadius;
                c.center = Vector3.zero;
            });
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
            CheckDuration();
        }

        private void OnDestroy()
        {
            // 清理伤害计时器和减速状态
            m_damageTimers.Clear();
            ClearAllSlowEffects();
        }

        #endregion

        #region 触发器检测方法

        private void OnTriggerEnter(Collider other)
        {
            if (!other.TryGetComponent<ObjectBase>(out ObjectBase target) || target == this)
            {
                return;
            }

            // 阵营关系判定
            ObjectStatsConfig myStats = GetObjectStats();
            ObjectStatsConfig targetStats = target.GetObjectStats();
            if (!CanDealDamageTo(targetStats))
            {
                return;
            }

            ApplyDamageToTarget(target);
            ApplySlowEffect(target);

            if (myStats.IsContinuousDamage)
            {
                m_damageTimers[target] = Time.time;
            }

#if UNITY_EDITOR
#endif
        }

        private void OnTriggerStay(Collider other)
        {
            ObjectStatsConfig stats = GetObjectStats();
            if (!stats.IsContinuousDamage)
            {
                return;
            }

            if (!other.TryGetComponent<ObjectBase>(out ObjectBase target) || target == this)
            {
                return;
            }

            // 阵营关系判定
            ObjectStatsConfig targetStats = target.GetObjectStats();
            if (!CanDealDamageTo(targetStats))
            {
                return;
            }

            if (CanApplyDamage(target))
            {
                ApplyDamageToTarget(target);
                m_damageTimers[target] = Time.time;
            }
        }

        private void OnTriggerExit(Collider other)
        {
            if (!other.TryGetComponent<ObjectBase>(out ObjectBase target) || target == this)
            {
                return;
            }

            m_damageTimers.Remove(target);
            RemoveSlowEffect(target);
        }

        #endregion

        #region 伤害计算方法

        private bool CanApplyDamage(ObjectBase target)
        {
            if (!m_damageTimers.TryGetValue(target, out float lastDamageTime))
            {
                return true;
            }

            ObjectStatsConfig stats = GetObjectStats();
            return Time.time - lastDamageTime >= 1f / stats.CastSpeed;
        }

        private void ApplyDamageToTarget(ObjectBase target)
        {
            if (target == null || target.IsDead())
            {
                return;
            }

            ObjectStatsConfig myStats = GetObjectStats();
            if (!myStats.CanDealDamage)
            {
                return;
            }

            ObjectStatsConfig targetStats = target.GetObjectStats();
            float damage = CalculateDamage(targetStats, new ObjectStatsConfigMultiplier());
            ApplyDamageTo(target, damage);
        }

        #endregion

        #region 减速效果方法

        /// <summary>
        /// 对目标应用减速效果（降低 MoveSpeed）。
        /// 减速倍率由 ObjectStatsConfig.SlowFactor 控制（0.3 表示降至原速度的30%）。
        /// 记录原始速度以便离开范围时恢复。
        /// </summary>
        private void ApplySlowEffect(ObjectBase target)
        {
            if (target == null || m_slowedTargets.ContainsKey(target))
            {
                return;
            }

            ObjectStatsConfig targetStats = target.GetObjectStats();
            float originalSpeed = targetStats.MoveSpeed;
            float slowFactor = GetObjectStats().SlowFactor;

            // 应用减速：将目标 MoveSpeed 降低到 slowFactor 比例
            targetStats.ApplySlow(slowFactor);
            m_slowedTargets[target] = originalSpeed;
        }

        /// <summary>
        /// 移除单个目标的减速效果，恢复原始速度
        /// </summary>
        private void RemoveSlowEffect(ObjectBase target)
        {
            if (target == null || !m_slowedTargets.TryGetValue(target, out float originalSpeed))
            {
                return;
            }

            ObjectStatsConfig targetStats = target.GetObjectStats();
            targetStats.RestoreSpeed(originalSpeed);
            m_slowedTargets.Remove(target);
        }

        /// <summary>
        /// 清除所有目标的减速效果（销毁时调用）
        /// </summary>
        private void ClearAllSlowEffects()
        {
            foreach (var pair in m_slowedTargets)
            {
                if (pair.Key != null && !pair.Key.IsDead())
                {
                    ObjectStatsConfig stats = pair.Key.GetObjectStats();
                    stats?.RestoreSpeed(pair.Value);
                }
            }
            m_slowedTargets.Clear();
        }

        #endregion

        #region 持续时间控制方法

        private void CheckDuration()
        {
            if (m_isDestroyed)
            {
                return;
            }

            ObjectStatsConfig stats = GetObjectStats();
            float duration = stats.DamageDuration;
            if (duration > 0f && Time.time - m_creationTime >= duration)
            {
                DestroyHailStorm();
            }
        }

        private void DestroyHailStorm()
        {
            if (m_isDestroyed)
            {
                return;
            }

            m_isDestroyed = true;
            Destroy(gameObject);
        }

        #endregion

        #region 公共方法

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

        public void SetDuration(float duration)
        {
            ObjectStatsConfig stats = GetObjectStats();
            if (stats != null)
            {
                stats.SetDamageDuration(duration);
            }
        }

        public void ClearDamageTimers()
        {
            m_damageTimers.Clear();
        }

        /// <summary>
        /// 获取当前受影响的目标数量（用于调试/UI显示）
        /// </summary>
        public int AffectedTargetCount => m_slowedTargets.Count;

        #endregion
    }
}
