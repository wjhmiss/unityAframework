using System;
using System.Collections.Generic;
using UnityEngine;

namespace AFrameWork.Core.SmallBase
{
    /// <summary>
    /// 轻量武器基类(不继承 ObjectBase),供剑、火球、陷阱等"无动画、无移动"的攻击物体公用。
    ///
    /// 设计目标:
    ///   - 脱离 ObjectBase,避免 PlayableGraph/动画槽位/组件缓存字典/Addressables 句柄等无关开销
    ///   - 武器属性保留在自身 m_objectStats(与 Sword 原行为一致),owner 属性通过活引用实时读取
    ///   - 阵营判定直接委托 m_objectStats.CanDealDamageTo(无需镜像 int 字段)
    ///   - 伤害计算委托 ObjectStatsConfig.CalculateAttack(累加武器+owner 属性)
    ///   - 伤害应用通过 target.TakeDamage(保留 ObjectBase 的无敌检查/OnDamaged/OnDeath)
    ///
    /// 与 SimpleObjectBase 的差异:
    ///   - SimpleObjectBase 面向子弹(高频生成/销毁,使用池,owner 属性用克隆快照锁定)
    ///   - WeaponBase 面向武器(少量持久对象,owner 属性用活引用实时读取)
    ///   - SimpleObjectBase 用 Tick(deltaTime) 由 Pool 单点驱动
    ///   - WeaponBase 不提供 Update/FixedUpdate,子类按需自己实现(Sword 不需要,Fire 需要)
    ///
    /// 子类只需重写:
    ///   - ObjectStatsConfig:自身属性配置(如 ObjectStatsConfig.CreateSword())
    ///   - SetupComponents():添加 Collider/Rigidbody 等组件
    ///   - OnTriggerEnter/Stay/Exit:碰撞伤害逻辑(调用 CalculateDamage/ApplyDamageTo)
    ///   - 可选 Start:调用 InheritOwnerFactionInfo 继承 owner 阵营
    /// </summary>
    public abstract class WeaponBase : MonoBehaviour
    {
        #region 字段

        // 缓存的 Rigidbody(子类在 SetupComponents 中赋值,可为 null)
        protected Rigidbody m_rigidbody;

        // 缓存的 Collider(子类在 SetupComponents 中赋值)
        protected Collider m_collider;

        // 自身属性配置实例(运行时数据,Awake 中从 ObjectStatsConfig 属性克隆)
        protected ObjectStatsConfig m_objectStats;

        // 持有该武器的父级 ObjectBase(缓存引用,避免每次碰撞时向上查找;可为 null 如 Fire)
        protected ObjectBase m_owner;

        // 持有者的 ObjectStatsConfig(缓存,用于伤害计算累加;可为 null 如 Fire)
        protected ObjectStatsConfig m_ownerStats;

        // 标记是否已完成组件初始化
        private bool m_isComponentsInitialized = false;

        // 静态复用的 Renderer 缓冲区,避免 CalculateObjectBounds 每次调用分配新数组
        // 安全性:Awake/SetupComponents 由 Unity 主线程顺序执行,静态缓冲区不会并发访问
        private static readonly List<Renderer> s_rendererBuffer = new List<Renderer>(16);

        // 2 攻击方 CalculateAttack 复用数组(OnTriggerEnter 单线程,无并发风险;避免 params 数组分配)
        private static readonly ObjectStatsConfig[] s_twoAttackerBuffer = new ObjectStatsConfig[2];

        #endregion

        #region 配置属性

        /// <summary>
        /// 物体属性配置属性,子类重写此属性来提供初始属性配置。
        /// WeaponBase 会根据此配置创建运行时的属性实例(克隆)。
        /// </summary>
        protected virtual ObjectStatsConfig ObjectStatsConfig => null;

        #endregion

        #region Unity 生命周期

        protected virtual void Awake()
        {
            SetupComponents();
            SetupObjectStats();
            m_isComponentsInitialized = true;
        }

        // 不提供 Update/FixedUpdate/OnEnable/OnDisable/OnDestroy 默认实现
        // 子类按需自己实现(Sword 不需要任何 Update;Fire 需要 Update 检查持续时间)

        #endregion

        #region 初始化方法

        /// <summary>
        /// 子类重写此方法来动态创建和配置所需的组件(如 Collider/Rigidbody)。
        /// 在 Awake 中自动调用,子类应使用 AddObjectComponent<T>() 添加组件。
        /// </summary>
        protected virtual void SetupComponents() { }

        /// <summary>
        /// 初始化物体属性配置(从 ObjectStatsConfig 属性克隆到 m_objectStats)。
        /// 克隆避免修改原始静态配置(如 ObjectStatsConfig.CreateSword() 返回的模板)。
        /// </summary>
        private void SetupObjectStats()
        {
            ObjectStatsConfig config = ObjectStatsConfig;
            if (config == null)
            {
                return;
            }

            m_objectStats = new ObjectStatsConfig();
            config.CopyTo(m_objectStats);
        }

        #endregion

        #region 组件管理(简化版,无 Dictionary 缓存)

        /// <summary>
        /// 动态添加组件到当前游戏对象。
        /// 如果组件已存在,则返回现有组件(不重新初始化)。
        /// 简化版:不使用 Dictionary 缓存(武器只用 1-2 个组件,字典开销大于收益)。
        /// </summary>
        protected T AddObjectComponent<T>() where T : Component
        {
            T component = GetComponent<T>();
            if (component == null)
            {
                component = gameObject.AddComponent<T>();
            }
            return component;
        }

        /// <summary>
        /// 动态添加组件,并执行初始化配置。
        /// 如果组件已存在,则返回现有组件(不会重新初始化)。
        /// </summary>
        protected T AddObjectComponent<T>(Action<T> initializer) where T : Component
        {
            T component = AddObjectComponent<T>();
            initializer?.Invoke(component);
            return component;
        }

        #endregion

        #region 包围盒计算(从 ObjectBase 复制核心逻辑,简化版)

        /// <summary>
        /// 根据对象所有 Renderer 的 Mesh 本地包围盒,计算本对象本地空间的 Bounds。
        /// 使用 Mesh.bounds / SkinnedMeshRenderer.localBounds(本地空间)而非 Renderer.bounds(世界空间 AABB),
        /// 通过矩阵变换将各子 Renderer 的本地包围盒合并到本对象本地空间,对旋转对象计算更精确。
        /// 包含当前对象及所有子对象的 Renderer。
        /// </summary>
        /// <returns>本地空间的包围盒;如果没有 Renderer 则返回默认 Bounds</returns>
        protected Bounds CalculateObjectBounds()
        {
            // 使用静态缓冲区避免每次调用分配 Renderer[] 数组
            s_rendererBuffer.Clear();
            GetComponentsInChildren(true, s_rendererBuffer);
            int rendererCount = s_rendererBuffer.Count;

            if (rendererCount == 0)
            {
                // 没有 Renderer 时返回默认 Bounds(单位立方体)
                return new Bounds(Vector3.zero, Vector3.one);
            }

            // 使用 Mesh 的本地空间 bounds,通过矩阵变换合并到本对象本地空间
            bool hasBounds = false;
            Bounds resultBounds = new Bounds(Vector3.zero, Vector3.zero);

            // 缓存 worldToLocalMatrix 避免每次循环重复计算
            Matrix4x4 worldToLocal = transform.worldToLocalMatrix;

            for (int r = 0; r < rendererCount; r++)
            {
                Renderer currentRenderer = s_rendererBuffer[r];

                // 跳过特效类 Renderer(VFXRenderer / ParticleSystemRenderer),
                // 它们的 bounds 通常远大于角色实际体型,会导致 Collider 范围偏大
                string rendererTypeName = currentRenderer.GetType().Name;
                if (rendererTypeName == "VFXRenderer"
                    || currentRenderer is ParticleSystemRenderer)
                {
                    continue;
                }

                // 初始化为 default 避免 CS0165
                Bounds meshLocalBounds = default;
                bool valid = false;

                // SkinnedMeshRenderer.localBounds 不反映蒙皮后实际顶点位置,使用 Renderer.bounds 替代
                bool useWorldSpaceBounds = false;

                if (currentRenderer is SkinnedMeshRenderer smr)
                {
                    meshLocalBounds = smr.bounds;
                    valid = true;
                    useWorldSpaceBounds = true;
                }
                else if (currentRenderer.TryGetComponent<MeshFilter>(out MeshFilter mf) && mf.sharedMesh != null)
                {
                    meshLocalBounds = mf.sharedMesh.bounds;
                    valid = true;
                }

                if (!valid)
                {
                    continue;
                }

                // 跳过退化 bounds(空 Mesh)
                if (meshLocalBounds.size.sqrMagnitude < 0.0001f)
                {
                    continue;
                }

                // 世界空间 bounds 直接用 worldToLocal;本地空间 bounds 需要 renderer→local 变换
                Matrix4x4 rendererToLocal = useWorldSpaceBounds
                    ? worldToLocal
                    : worldToLocal * currentRenderer.transform.localToWorldMatrix;

                // 将 Mesh 本地包围盒的 8 个角点变换到本对象本地空间,合并得到精确的本地 AABB
                Vector3 center = meshLocalBounds.center;
                Vector3 ext = meshLocalBounds.extents;

                for (int i = 0; i < 8; i++)
                {
                    Vector3 corner = center + new Vector3(
                        ((i & 1) == 0 ? ext.x : -ext.x),
                        ((i & 2) == 0 ? ext.y : -ext.y),
                        ((i & 4) == 0 ? ext.z : -ext.z));

                    Vector3 localCorner = rendererToLocal.MultiplyPoint(corner);

                    if (!hasBounds)
                    {
                        resultBounds = new Bounds(localCorner, Vector3.zero);
                        hasBounds = true;
                    }
                    else
                    {
                        resultBounds.Encapsulate(localCorner);
                    }
                }
            }

            if (!hasBounds)
            {
                // 没有 Mesh 数据(如 SpriteRenderer/ParticleSystemRenderer),回退到世界空间 bounds
                Bounds worldBounds = s_rendererBuffer[0].bounds;
                for (int i = 1; i < rendererCount; i++)
                {
                    worldBounds.Encapsulate(s_rendererBuffer[i].bounds);
                }

                Vector3 fallbackCenter = transform.InverseTransformPoint(worldBounds.center);
                Vector3 scale = transform.lossyScale;
                Vector3 fallbackSize = Vector3.Scale(worldBounds.size, new Vector3(1f / scale.x, 1f / scale.y, 1f / scale.z));
                return new Bounds(fallbackCenter, fallbackSize);
            }

            return resultBounds;
        }

        /// <summary>
        /// 添加 BoxCollider 并根据传入的包围盒设置大小和位置。
        /// 子类可先调用 CalculateObjectBounds() 获取包围盒,调整后再传入。
        /// </summary>
        /// <param name="bounds">本地空间的包围盒,用于设置 Collider 的大小和位置</param>
        /// <param name="sizeMultiplier">XYZ 轴分别的缩放因子,默认 (1,1,1) 不缩放</param>
        /// <param name="centerOffset">中心点的偏移量(世界空间,单位=米),默认 Vector3.zero 不偏移。
        /// 内部会除以 transform.lossyScale 转换到本地空间,因此无论父级缩放多大,
        /// (0, 1, 0) 始终表示在世界空间中向上偏移 1 米。</param>
        /// <param name="extraConfig">额外的配置回调,用于设置 isTrigger、material 等属性。</param>
        /// <returns>配置好的 BoxCollider 实例</returns>
        protected BoxCollider AddBoxCollider(
            Bounds bounds,
            Vector3 sizeMultiplier,
            Vector3 centerOffset = default,
            Action<BoxCollider> extraConfig = null)
        {
            Vector3 scaledSize = Vector3.Scale(bounds.size, sizeMultiplier);
            Vector3 lossyScale = transform.lossyScale;
            Vector3 localOffset = new Vector3(
                centerOffset.x / (lossyScale.x != 0f ? lossyScale.x : 1f),
                centerOffset.y / (lossyScale.y != 0f ? lossyScale.y : 1f),
                centerOffset.z / (lossyScale.z != 0f ? lossyScale.z : 1f));
            Vector3 adjustedCenter = bounds.center + localOffset;

            return AddObjectComponent<BoxCollider>(c =>
            {
                c.center = adjustedCenter;
                c.size = scaledSize;
                extraConfig?.Invoke(c);
            });
        }

        /// <summary>
        /// 添加 CapsuleCollider 并根据传入的包围盒设置大小和位置。
        /// 自动选择最长的轴作为胶囊方向,并计算合适的半径和高度。
        /// </summary>
        protected CapsuleCollider AddCapsuleCollider(
            Bounds bounds,
            Vector3 sizeMultiplier,
            Vector3 centerOffset = default,
            Action<CapsuleCollider> extraConfig = null)
        {
            Vector3 scaledSize = Vector3.Scale(bounds.size, sizeMultiplier);
            Vector3 lossyScale = transform.lossyScale;
            Vector3 localOffset = new Vector3(
                centerOffset.x / (lossyScale.x != 0f ? lossyScale.x : 1f),
                centerOffset.y / (lossyScale.y != 0f ? lossyScale.y : 1f),
                centerOffset.z / (lossyScale.z != 0f ? lossyScale.z : 1f));
            Vector3 adjustedCenter = bounds.center + localOffset;

            return AddObjectComponent<CapsuleCollider>(c =>
            {
                int direction = 1; // 默认 Y 轴
                float maxExtent = scaledSize.y;

                if (scaledSize.x > maxExtent)
                {
                    direction = 0;
                    maxExtent = scaledSize.x;
                }

                if (scaledSize.z > maxExtent)
                {
                    direction = 2;
                    maxExtent = scaledSize.z;
                }

                switch (direction)
                {
                    case 0:
                        c.radius = Mathf.Max(scaledSize.y, scaledSize.z) * 0.5f;
                        c.height = scaledSize.x;
                        break;
                    case 1:
                        c.radius = Mathf.Max(scaledSize.x, scaledSize.z) * 0.5f;
                        c.height = scaledSize.y;
                        break;
                    case 2:
                        c.radius = Mathf.Max(scaledSize.x, scaledSize.y) * 0.5f;
                        c.height = scaledSize.z;
                        break;
                }

                c.center = adjustedCenter;
                c.direction = direction;
                extraConfig?.Invoke(c);
            });
        }

        #endregion

        #region Owner 继承(从 Sword 提取到基类)

        /// <summary>
        /// 沿父级向上查找第一个具有 ObjectBase 基类的组件。
        /// 用于武器(作为子对象)查找持有者(Fighter 等)。
        /// </summary>
        protected ObjectBase FindParentObjectBase()
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

        /// <summary>
        /// 继承 owner 的阵营/队伍/公会/PVP 信息到自身 m_objectStats。
        /// 同时缓存 m_owner 和 m_ownerStats,用于伤害计算累加和 UI 实时引用。
        /// 必须在 Start 中执行,确保父级 ObjectBase 的 Awake 已完成初始化。
        /// 无 owner 时(如 Fire)直接返回,使用自身阵营信息。
        /// </summary>
        protected void InheritOwnerFactionInfo()
        {
            m_owner = FindParentObjectBase();

            if (m_owner == null || !m_owner.HasObjectStats())
            {
                return;
            }

            m_ownerStats = m_owner.GetObjectStats();

            if (m_objectStats != null)
            {
                // 继承父级的阵营关系判定字段(FactionID/TeamID/GuildID/AllianceID/PVPMode)
                m_objectStats.InheritFactionFrom(m_ownerStats);
            }
        }

        #endregion

        #region 阵营判定(委托 m_objectStats)

        /// <summary>
        /// 综合判定能否对目标造成伤害。
        /// 默认委托 m_objectStats.CanDealDamageTo(阵营信息已通过 InheritOwnerFactionInfo 继承)。
        /// 子类可重写以接入 FactionRelationManager 等自定义逻辑。
        /// </summary>
        protected virtual bool CanDealDamageTo(ObjectStatsConfig target)
        {
            if (m_objectStats == null || target == null)
            {
                return false;
            }
            return m_objectStats.CanDealDamageTo(target);
        }

        #endregion

        #region 伤害计算与应用

        /// <summary>
        /// 计算伤害(不扣血):累加武器自身属性 + owner 属性,委托 ObjectStatsConfig.CalculateAttack。
        /// 内部使用静态缓冲区 s_twoAttackerBuffer 避免 params 数组分配。
        /// 无 owner 时(如 Fire)只使用武器自身属性。
        /// </summary>
        /// <param name="targetStats">目标属性配置(仅读取防御/闪避,不修改)</param>
        /// <param name="multiplier">攻击参数倍率(无倍率传 new ObjectStatsConfigMultiplier())</param>
        /// <returns>计算出的伤害值(被闪避返回 0)</returns>
        protected float CalculateDamage(ObjectStatsConfig targetStats, ObjectStatsConfigMultiplier multiplier)
        {
            if (m_objectStats == null)
            {
                return 0f;
            }

            // 无 owner:只使用武器自身属性
            if (m_ownerStats == null)
            {
                return ObjectStatsConfig.CalculateAttack(multiplier, targetStats, m_objectStats);
            }

            // 有 owner:累加武器自身 + owner(用静态缓冲区避免 params 数组分配)
            s_twoAttackerBuffer[0] = m_objectStats;
            s_twoAttackerBuffer[1] = m_ownerStats;
            return ObjectStatsConfig.CalculateAttack(multiplier, targetStats, s_twoAttackerBuffer);
        }

        /// <summary>
        /// 应用伤害到目标:调用 target.TakeDamage + 记录 UI 实时引用。
        /// 经 ObjectBase.TakeDamage 保留无敌检查(翻滚免疫)/OnDamaged 回调/OnDeath 处理。
        /// UI 引用:武器自身位置传 null(与 SimpleObjectBase 模式一致,UI 回退到快照显示),
        /// owner 位置传 m_owner(可能为 null,如 Fire 无 owner)。
        /// </summary>
        protected void ApplyDamageTo(ObjectBase target, float damage)
        {
            target.TakeDamage(damage);

            // 武器自身位置传 null(UI 不显示武器 ObjectBase 卡片,回退到快照)
            // owner 位置传 m_owner(可能为 null,如 Fire 无 owner)
            ObjectStatsConfig.SetLastAttackRefs(target, (ObjectBase)null, m_owner);
        }

        #endregion

        #region 属性访问

        /// <summary>
        /// 获取运行时物体属性配置实例。
        /// </summary>
        public ObjectStatsConfig GetObjectStats()
        {
            return m_objectStats;
        }

        /// <summary>
        /// 检查是否拥有物体属性配置。
        /// </summary>
        public bool HasObjectStats()
        {
            return m_objectStats != null;
        }

        #endregion
    }
}
