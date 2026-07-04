using UnityEngine;
using AFrameWork.Core;
using AFrameWork.Core.SmallBase;

namespace AFrameWork.Sample
{
    /// <summary>
    /// 子弹类，继承 SimpleObjectBase（轻量基类，不继承 ObjectBase）。
    ///
    /// 优化说明（对应方案 A/B/C/E）：
    ///   - 方案 B：脱离 ObjectBase，避免 PlayableGraph/动画槽位/组件缓存字典等无关开销
    ///   - 方案 C：阵营信息以 int 字段存储于 SimpleObjectBase，命中时无 ObjectStatsConfig 分配
    ///   - 方案 A：由 SimpleObjectPool 的 ObjectPool 复用，无 Instantiate/Destroy
    ///   - 方案 E：每帧逻辑在 Tick(deltaTime) 中由 Pool 单点批量驱动
    ///
    /// 子类职责：
    ///   - SetupCollider：创建球形触发器（比 Capsule 计算更轻）
    ///   - Tick：超距检测，超距时调用 Deactivate() 回收
    ///   - ConfigureParameters：覆盖速度与最大飞行距离
    /// </summary>
    public class Bullet : SimpleObjectBase
    {
        #region 子弹参数常量

        // 飞行速度（米/秒）
        private const float k_speed = 4f;

        // 最大飞行距离（米，超过后回收）
        public const float k_maxDistance = 8f;

        // 最大飞行距离的平方（避免每帧开方）
        private const float k_maxDistanceSqr = k_maxDistance * k_maxDistance;

        // 碰撞体半径（米）
        private const float k_colliderRadius = 0.2f;

        #endregion

        #region SimpleObjectBase 抽象方法实现

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
            // 补偿根节点缩放，确保世界空间碰撞体半径为 k_colliderRadius
            // 否则 scale=6 时世界半径达 1.2m，子弹会在远距离命中敌方导致"突然消失"
            float maxScale = Mathf.Max(Mathf.Abs(transform.lossyScale.x), Mathf.Abs(transform.lossyScale.y), Mathf.Abs(transform.lossyScale.z), 0.0001f);
            collider.radius = k_colliderRadius / maxScale;
            collider.isTrigger = true;
            m_collider = collider;
        }

        /// <summary>
        /// 每帧由 SimpleObjectPool 调用。
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
                OnLifetimeEnd();
                Deactivate();
            }
        }

        /// <summary>覆盖基类默认运动参数为子弹专用值。</summary>
        protected override void ConfigureParameters()
        {
            m_speed = k_speed;
            m_maxDistanceSqr = k_maxDistanceSqr;
        }

        #endregion

        #region 可选回调（子类按需启用）

        /*
        // 命中目标时的特效播放示例：
        protected override void OnHit(ObjectBase target, float finalDamage)
        {
            // TODO: 播放命中特效 / 音效
        }

        // 超距消失时的特效播放示例：
        protected override void OnLifetimeEnd()
        {
            // TODO: 播放消失特效
        }
        */

        #endregion
    }
}