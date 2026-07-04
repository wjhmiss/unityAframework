using UnityEngine;
using AFrameWork.Core;
using AFrameWork.Core.SmallBase;

namespace AFrameWork.Sample
{
    /// <summary>
    /// 子弹类，继承 ProjectileBase（轻量基类，不继承 ObjectBase）。
    ///
    /// 优化说明（对应方案 A/B/C/E）：
    ///   - 方案 B：脱离 ObjectBase，避免 PlayableGraph/动画槽位/组件缓存字典等无关开销
    ///   - 方案 C：阵营信息以 int 字段存储于 ProjectileBase，命中时无 ObjectStatsConfig 分配
    ///   - 方案 A：由 ProjectileManager 的 ObjectPool 复用，无 Instantiate/Destroy
    ///   - 方案 E：每帧逻辑在 Tick(deltaTime) 中由 Manager 单点批量驱动
    ///
    /// 子类职责：
    ///   - SetupCollider：创建球形触发器（比 Capsule 计算更轻）
    ///   - Tick：超距检测，超距时调用 Release() 回收
    ///   - ConfigureMotion：覆盖速度与最大飞行距离
    /// </summary>
    public class Bullet : ProjectileBase
    {
        #region 子弹参数常量

        // 飞行速度（米/秒）
        private const float k_speed = 4f;

        // 最大飞行距离（米，超过后回收）
        private const float k_maxDistance = 8f;

        // 最大飞行距离的平方（避免每帧开方）
        private const float k_maxDistanceSqr = k_maxDistance * k_maxDistance;

        // 碰撞体半径（米）
        private const float k_colliderRadius = 0.2f;

        #endregion

        #region ProjectileBase 抽象方法实现

        /// <summary>
        /// 创建球形触发器。球体碰撞检测比 Capsule 更轻量，适合小子弹。
        /// </summary>
        protected override void SetupCollider()
        {
            SphereCollider collider = GetComponent<SphereCollider>();
            if (collider == null)
            {
                collider = gameObject.AddComponent<SphereCollider>();
            }
            collider.radius = k_colliderRadius;
            collider.isTrigger = true;
            m_collider = collider;
        }

        /// <summary>
        /// 每帧由 ProjectileManager 调用。
        /// 检测飞行距离是否超过最大值，超过则触发回收。
        /// 注意：位移由 Rigidbody.velocity 驱动（在 Initialize 中设置），此处不做位移计算。
        /// </summary>
        public override void Tick(float deltaTime)
        {
            if (!m_isInitialized) return;

            // 超距检测：使用 sqrMagnitude 避免开方
            Vector3 offset = transform.position - m_startPosition;
            if (offset.sqrMagnitude >= k_maxDistanceSqr)
            {
                OnExpire();
                Release();
            }
        }

        /// <summary>覆盖基类默认运动参数为子弹专用值。</summary>
        protected override void ConfigureMotion()
        {
            m_speed = k_speed;
            m_maxDistanceSqr = k_maxDistanceSqr;
        }

        #endregion

        #region 可选回调（子类按需启用）

        /*
        // 命中目标时的特效播放示例：
        protected override void OnHitTarget(ObjectBase target, float finalDamage)
        {
            // TODO: 播放命中特效 / 音效
        }

        // 超距消失时的特效播放示例：
        protected override void OnExpire()
        {
            // TODO: 播放消失特效
        }
        */

        #endregion
    }
}
