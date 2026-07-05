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

    /// <summary>
    /// Bullet 使用说明：
    /// ============================================================
    /// 子弹类，继承 SimpleObjectBase（轻量基类，不继承 ObjectBase）。
    /// 演示最简单的投射物实现：球形触发器 + 超距回收。
    ///
    /// ════════════════════════════════════════════════════════════
    /// 【优化方案对应】
    /// ════════════════════════════════════════════════════════════
    ///   - 方案 B：脱离 ObjectBase，避免 PlayableGraph/动画槽位/组件缓存字典等无关开销
    ///   - 方案 C：阵营信息以 int 字段存储于 SimpleObjectBase，命中时无 ObjectStatsConfig 分配
    ///   - 方案 A：由 SimpleObjectPool 的 ObjectPool 复用，无 Instantiate/Destroy
    ///   - 方案 E：每帧逻辑在 Tick(deltaTime) 中由 Pool 单点批量驱动
    ///
    /// ════════════════════════════════════════════════════════════
    /// 【子弹参数常量】
    /// ════════════════════════════════════════════════════════════
    ///   k_speed = 4f
    ///     - 飞行速度（米/秒）
    ///     - 通过 ConfigureParameters 覆盖基类默认值 k_defaultSpeed(4f)
    ///     - 实际飞行由 Rigidbody.velocity 驱动（Initialize 中设置）
    ///
    ///   k_maxDistance = 8f
    ///     - 最大飞行距离（米，超过后回收）
    ///     - 公开常量，可供外部查询（如 UI 显示射程）
    ///
    ///   k_maxDistanceSqr = k_maxDistance * k_maxDistance
    ///     - 最大飞行距离的平方（避免每帧开方）
    ///     - Tick 中使用 sqrMagnitude 比较此值
    ///
    ///   k_colliderRadius = 0.2f
    ///     - 碰撞体半径（米）
    ///     - SetupCollider 中设置 SphereCollider.radius
    ///     - 需要补偿根节点缩放（见下方 SetupCollider 说明）
    ///
    /// ════════════════════════════════════════════════════════════
    /// 【SetupCollider 碰撞体配置】
    /// ════════════════════════════════════════════════════════════
    ///   - 使用 SphereCollider（比 Capsule 计算更轻，适合小子弹）
    ///   - isTrigger = true（使用 OnTriggerEnter 检测命中）
    ///   - 半径补偿根节点缩放：
    ///       maxScale = Max(|lossyScale.x|, |lossyScale.y|, |lossyScale.z|, 0.0001f)
    ///       collider.radius = k_colliderRadius / maxScale
    ///     原因：Unity Collider.radius 是局部坐标值，世界空间半径 = radius × lossyScale
    ///     若不补偿，scale=6 时世界半径达 1.2m，子弹会在远距离命中敌方导致"突然消失"
    ///
    /// ════════════════════════════════════════════════════════════
    /// 【Tick 每帧逻辑】
    /// ════════════════════════════════════════════════════════════
    ///   - 由 SimpleObjectPool.Update 每帧调用
    ///   - 超距检测：使用 sqrMagnitude 避免开方
    ///       Vector3 offset = transform.position - m_startPosition;
    ///       if (offset.sqrMagnitude >= k_maxDistanceSqr) → 回收
    ///   - 超距时调用 OnLifetimeEnd()（子类可播放消失特效）+ Deactivate()
    ///   - 注意：位移由 Rigidbody.velocity 驱动（Initialize 中设置），此处不做位移计算
    ///
    /// ════════════════════════════════════════════════════════════
    /// 【ConfigureParameters 参数覆盖】
    /// ════════════════════════════════════════════════════════════
    ///   - 在 Initialize 中由基类调用（ConfigureParameters 之后）
    ///   - 覆盖基类默认运动参数为子弹专用值：
    ///       m_speed = k_speed (4f)
    ///       m_maxDistanceSqr = k_maxDistanceSqr (64)
    ///   - 子类按需设置，不设置则使用基类默认值
    ///
    /// ════════════════════════════════════════════════════════════
    /// 【可选回调】
    /// ════════════════════════════════════════════════════════════
    ///   OnHit(target, finalDamage)：
    ///     - 命中有效目标后的回调（伤害已由基类应用）
    ///     - 子类可在此播放命中特效 / 音效
    ///     - 示例：播放火花 ParticleSystem、播放金属撞击音效
    ///
    ///   OnLifetimeEnd()：
    ///     - 因寿命到期回收时的回调
    ///     - 子类可在此播放消失特效
    ///     - 示例：播放淡出动画、生成烟雾粒子
    ///     - 注意：OnLifetimeEnd 在 Deactivate 之前调用
    ///
    /// ════════════════════════════════════════════════════════════
    /// 【命中流程】
    /// ════════════════════════════════════════════════════════════
    ///   完整命中流程（基类 SimpleObjectBase.OnTriggerEnter 处理）：
    ///     1. m_isAlive 检查
    ///     2. 最小命中距离检查（防止生成点重叠立即回收）
    ///     3. TryGetComponent&lt;ObjectBase&gt; 获取目标
    ///     4. 目标属性和死亡检查
    ///     5. CanDealDamageTo 阵营判定（使用 owner 的 int 字段，无 GC）
    ///     6. CalculateFinalDamage 计算最终伤害（含防御减免）
    ///     7. target.TakeDamage 应用伤害
    ///     8. OnHit 回调（Bullet 可重写播放特效）
    ///     9. Deactivate 请求回收
    ///
    /// ────────────────────────────────────────────────────────────
    /// 示例 1：基本使用（自动注册）
    /// ────────────────────────────────────────────────────────────
    /// <code>
    /// // 1. 创建 Bullet.prefab，根节点挂载 Bullet 组件
    /// // 2. 在 Addressables Groups 中标记为 Addressable 并添加 "PoolObjects" label
    /// // 3. 场景中创建 GameObject 挂载 SimpleObjectPool 组件
    /// // 4. 运行时自动注册并预热
    /// // 5. 发射：
    /// SimpleObjectPool.Instance.Launch&lt;Bullet&gt;(
    ///     position: spawnPos,
    ///     direction: targetDir,
    ///     owner: shooter,
    ///     damage: shooterStats.PhysicalAttack,
    ///     damageType: DamageType.Physical
    /// );
    /// </code>
    ///
    /// ────────────────────────────────────────────────────────────
    /// 示例 2：自定义命中特效
    /// ────────────────────────────────────────────────────────────
    /// <code>
    /// public class BulletWithVFX : Bullet
    /// {
    ///     public ParticleSystem hitEffect;
    ///
    ///     protected override void OnHit(ObjectBase target, float finalDamage)
    ///     {
    ///         if (hitEffect != null)
    ///         {
    ///             hitEffect.transform.position = transform.position;
    ///             hitEffect.Play();
    ///         }
    ///     }
    /// }
    /// </code>
    ///
    /// ────────────────────────────────────────────────────────────
    /// 示例 3：自定义消失特效
    /// ────────────────────────────────────────────────────────────
    /// <code>
    /// public class BulletWithFade : Bullet
    /// {
    ///     protected override void OnLifetimeEnd()
    ///     {
    ///         // 播放淡出特效后基类会调用 Deactivate 回收
    ///         EffectManager.PlayFadeEffect(transform.position);
    ///     }
    /// }
    /// </code>
    ///
    /// ────────────────────────────────────────────────────────────
    /// 示例 4：调整子弹参数
    /// ────────────────────────────────────────────────────────────
    /// <code>
    /// public class FastBullet : Bullet
    /// {
    ///     private const float k_fastSpeed = 12f;
    ///     private const float k_fastMaxDistance = 20f;
    ///     private const float k_fastMaxDistanceSqr = k_fastMaxDistance * k_fastMaxDistance;
    ///
    ///     protected override void ConfigureParameters()
    ///     {
    ///         m_speed = k_fastSpeed;
    ///         m_maxDistanceSqr = k_fastMaxDistanceSqr;
    ///     }
    /// }
    /// </code>
    ///
    /// ────────────────────────────────────────────────────────────
    /// 示例 5：与 TargetRegistry 配合自动瞄准
    /// ────────────────────────────────────────────────────────────
    /// <code>
    /// // 在发射器中查找最近敌方
    /// ObjectBase target = TargetRegistry.FindNearest(transform.position, this);
    /// if (target != null)
    /// {
    ///     Vector3 dir = (target.transform.position - transform.position).normalized;
    ///     SimpleObjectPool.Instance.Launch&lt;Bullet&gt;(
    ///         position: transform.position,
    ///         direction: dir,
    ///         owner: this,
    ///         damage: GetObjectStats().PhysicalAttack
    ///     );
    /// }
    /// </code>
    /// </summary>
}