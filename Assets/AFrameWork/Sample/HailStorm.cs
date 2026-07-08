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

        // 暴雪特效根对象（作为子对象，随 HailStorm 自动销毁）
        private GameObject m_blizzardVfxRoot;

        // 旋转下落雪花粒子系统
        private ParticleSystem m_swirlSnowPs;

        // 地面飞溅粒子系统
        private ParticleSystem m_groundSplashPs;

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

            // 创建暴雪特效（范围 = 伤害范围）
            CreateBlizzardVfx(damageRadius);
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
            RotateSwirlEmission();
        }

        /// <summary>
        /// 旋转 SwirlSnow 的 transform 绕 Y 轴，使所有粒子在局部空间中跟随旋转，形成螺旋下落效果。
        /// simulationSpace=Local，已发射的粒子也会随 transform 旋转，肉眼可见的螺旋轨迹。
        /// </summary>
        private void RotateSwirlEmission()
        {
            if (m_swirlSnowPs != null)
            {
                m_swirlSnowPs.transform.Rotate(0f, k_swirlEmissionRotationSpeed * Time.deltaTime, 0f);
            }
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

        #region 暴雪特效方法

        // 雪花生成高度倍数（相对于 DamageRadius，雪花从高空生成）
        private const float k_swirlSnowHeightMultiplier = 1.5f;

        // 雪花发射半径倍数（相对于 DamageRadius，顶部下落区域略大于伤害范围）
        private const float k_swirlSnowRadiusMultiplier = 1.3f;

        // 雪花旋转速度范围（绕 Y 轴，单位：unit/sec）
        // DamageRadius=6 时，周长=2π×6≈37.7，轨道速度20~30，下落约1.9秒内边缘粒子可旋转1.0~1.5圈
        private const float k_swirlSnowOrbitalSpeedMin = 20f;
        private const float k_swirlSnowOrbitalSpeedMax = 30f;

        // 雪花径向速度范围（向中心汇聚，负值=向内）—— 较大，下落过程中粒子快速向中心汇聚，形成中心密集边缘稀疏
        private const float k_swirlSnowRadialSpeedMin = -1.5f;
        private const float k_swirlSnowRadialSpeedMax = -0.5f;

        // 发射点绕 Y 轴旋转速度（度/秒）—— 旋转发射点产生可见螺旋流
        // 360°/s = 每秒一圈，下落约1.9秒内可完成约1.9圈螺旋
        private const float k_swirlEmissionRotationSpeed = 360f;

        // 雪花重力倍数（小，下落慢，确保旋转至少一圈后才落地）
        // DamageRadius=6 时生成高度=9m，重力0.5 → 有效重力4.9m/s²，下落约1.9秒
        private const float k_swirlSnowGravityMultiplier = 0.5f;

        // 雪花大小范围（有大有小，单位：米）
        private const float k_swirlSnowSizeMin = 0.01f;
        private const float k_swirlSnowSizeMax = 0.04f;

        // 雪花发射速率（每秒粒子数）
        private const float k_swirlSnowEmissionRate = 1200f;

        // 雪地覆盖层透明度
        private const float k_snowGroundAlpha = 0.85f;

        // URP 粒子材质（静态缓存，避免每次创建 HailStorm 都生成新材质）
        private static Material s_snowMaterial;

        // 内置球体 mesh（静态缓存，用于渲染小球状雪花）
        private static Mesh s_sphereMesh;

        // 地面弹跳用的圆形 mesh（所有法线朝 +Y，用于 Mesh 粒子发射器）
        private static Mesh s_circleMesh;

        // 雪地覆盖层纹理（静态缓存，径向渐变白色圆盘）
        private static Texture2D s_snowGroundTexture;

        // 雪地覆盖层材质（静态缓存）
        private static Material s_snowGroundMaterial;

        // 地面飞溅发射速率（每秒粒子数）
        private const float k_groundSplashEmissionRate = 1000f;

        /// <summary>
        /// 获取或创建 URP 粒子材质（白色，静态缓存）
        /// 使用 URP Particles/Unlit 着色器，支持顶点颜色透明度淡出
        /// </summary>
        private static Material GetOrCreateSnowMaterial()
        {
            if (s_snowMaterial == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
                if (shader != null)
                {
                    s_snowMaterial = new Material(shader);
                    s_snowMaterial.SetColor("_BaseColor", Color.white);
                }
            }
            return s_snowMaterial;
        }

        /// <summary>
        /// 获取内置球体 mesh（静态缓存，用于渲染小球状雪花）
        /// Resources.GetBuiltinResource 在运行时和构建中均可访问引擎内置 mesh
        /// </summary>
        private static Mesh GetSphereMesh()
        {
            if (s_sphereMesh == null)
            {
                s_sphereMesh = Resources.GetBuiltinResource<Mesh>("Sphere.fbx");
            }
            return s_sphereMesh;
        }

        /// <summary>
        /// 获取或创建地面弹跳用的圆形 mesh
        /// 顶点在 XZ 平面上均匀分布在整个圆形面积内，所有法线朝 +Y，用于 Mesh 粒子发射器使粒子向上发射
        /// </summary>
        private static Mesh GetOrCreateCircleMesh(float radius)
        {
            if (s_circleMesh == null)
            {
                s_circleMesh = new Mesh();
                // 在圆形面积内均匀生成顶点（极坐标网格）
                var vertList = new System.Collections.Generic.List<Vector3>();
                int rings = 6; // 同心环数
                int baseSegments = 8;
                // 中心顶点
                vertList.Add(Vector3.zero);
                for (int r = 1; r <= rings; r++)
                {
                    float rNorm = (float)r / rings;
                    int segs = baseSegments * r; // 越外层顶点越多
                    for (int s = 0; s < segs; s++)
                    {
                        float angle = (float)s / segs * Mathf.PI * 2f;
                        vertList.Add(new Vector3(
                            Mathf.Cos(angle) * radius * rNorm,
                            0f,
                            Mathf.Sin(angle) * radius * rNorm));
                    }
                }
                s_circleMesh.vertices = vertList.ToArray();
                // 每个顶点的法线略微倾斜朝向不同方向（以 +Y 为主，混合随机水平分量）
                // Mesh shape 沿法线方向发射粒子，倾斜法线使粒子向各个不同方向弹起
                Vector3[] normals = new Vector3[vertList.Count];
                // 用固定种子保证每次生成的法线分布一致
                UnityEngine.Random.State prevState = UnityEngine.Random.state;
                UnityEngine.Random.InitState(42);
                for (int i = 0; i < normals.Length; i++)
                {
                    float nx = UnityEngine.Random.Range(-0.5f, 0.5f);
                    float nz = UnityEngine.Random.Range(-0.5f, 0.5f);
                    normals[i] = new Vector3(nx, 1f, nz).normalized;
                }
                UnityEngine.Random.state = prevState;
                s_circleMesh.normals = normals;
            }
            return s_circleMesh;
        }
        private static Texture2D GetOrCreateSnowGroundTexture()
        {
            if (s_snowGroundTexture == null)
            {
                int size = 256;
                s_snowGroundTexture = new Texture2D(size, size, TextureFormat.RGBA32, false);
                s_snowGroundTexture.filterMode = FilterMode.Bilinear;
                float center = size * 0.5f;
                float maxDist = center;
                for (int y = 0; y < size; y++)
                {
                    for (int x = 0; x < size; x++)
                    {
                        float dist = Mathf.Sqrt((x - center) * (x - center) + (y - center) * (y - center));
                        float t = Mathf.Clamp01(dist / maxDist);
                        // 渐变：中心不透明，边缘透明；保留更大的不透明区域使圆盘更明显
                        float alpha = 1f - t * t;
                        s_snowGroundTexture.SetPixel(x, y, new Color(1f, 1f, 1f, alpha * k_snowGroundAlpha));
                    }
                }
                s_snowGroundTexture.Apply();
            }
            return s_snowGroundTexture;
        }

        /// <summary>
        /// 获取或创建雪地覆盖层材质（URP Particles/Unlit，支持透明渐变纹理）
        /// </summary>
        private static Material GetOrCreateSnowGroundMaterial()
        {
            if (s_snowGroundMaterial == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
                if (shader != null)
                {
                    s_snowGroundMaterial = new Material(shader);
                    // URP Particles/Unlit: _BaseColor 与 _BaseMap 相乘
                    s_snowGroundMaterial.SetColor("_BaseColor", Color.white);
                    Texture2D tex = GetOrCreateSnowGroundTexture();
                    s_snowGroundMaterial.SetTexture("_BaseMap", tex);
                    // 确保使用透明混合
                    s_snowGroundMaterial.SetFloat("_Surface", 1f); // Transparent
                    s_snowGroundMaterial.SetFloat("_Blend", 0f); // Alpha blending
                    s_snowGroundMaterial.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                    s_snowGroundMaterial.EnableKeyword("_ALPHAPREMULTIPLY_ON");
                    s_snowGroundMaterial.renderQueue = 3000; // Transparent queue
                }
            }
            return s_snowGroundMaterial;
        }

        /// <summary>
        /// 创建暴雪特效：旋转下落雪花 + 地面飞溅
        /// VFX 作为子对象，随 HailStorm GameObject 自动销毁
        /// </summary>
        private void CreateBlizzardVfx(float radius)
        {
            m_blizzardVfxRoot = new GameObject("BlizzardVFX");
            m_blizzardVfxRoot.transform.SetParent(transform, false);
            m_blizzardVfxRoot.transform.localPosition = Vector3.zero;

            CreateSwirlSnowVfx(m_blizzardVfxRoot.transform, radius);
            CreateGroundSplashVfx(m_blizzardVfxRoot.transform, radius);
            CreateSnowGround(m_blizzardVfxRoot.transform, radius);
        }

        /// <summary>
        /// 创建旋转下落雪花粒子系统（龙卷风效果）
        /// 白色小球状粒子，大小不一，从高空生成后绕中心 Y 轴螺旋旋转、向中心汇聚并下落
        /// </summary>
        private void CreateSwirlSnowVfx(Transform parent, float radius)
        {
            GameObject swirlObj = new GameObject("SwirlSnow");
            swirlObj.transform.SetParent(parent, false);
            // 粒子系统位于 HailStorm 中心（localPosition=0），确保 orbitalY 旋转中心是 HailStorm 的 Y 轴
            swirlObj.transform.localPosition = Vector3.zero;

            ParticleSystem ps = swirlObj.AddComponent<ParticleSystem>();
            m_swirlSnowPs = ps;

            // 主模块配置
            var main = ps.main;
            main.duration = 5f;
            main.loop = true;
            // 根据生成高度和重力计算下落到地面所需时间：t = √(2h / g)
            // 粒子到达地面即消失，不会穿透地面
            float fallHeight = radius * k_swirlSnowHeightMultiplier;
            float effectiveGravity = k_swirlSnowGravityMultiplier * Physics.gravity.y; // 负值
            float fallTime = Mathf.Sqrt(2f * fallHeight / Mathf.Abs(effectiveGravity));
            main.startLifetime = fallTime;
            main.startSpeed = 0f;          // 靠重力下落，不靠 shape 速度
            main.startSize = new ParticleSystem.MinMaxCurve(k_swirlSnowSizeMin, k_swirlSnowSizeMax); // 有大有小
            main.startColor = Color.white; // 纯白小球
            main.gravityModifier = k_swirlSnowGravityMultiplier; // 较小重力，下落慢，螺旋轨迹清晰
            main.maxParticles = 5000;
            main.simulationSpace = ParticleSystemSimulationSpace.Local; // 局部空间模拟，旋转 transform 时所有粒子跟随旋转

            // 形状：圆形发射器，半径 = DamageRadius，圆面水平朝上（XZ 平面）
            // shape.position 偏移到高空生成粒子，但粒子系统仍在 HailStorm 中心（旋转中心不变）
            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = radius * k_swirlSnowRadiusMultiplier;
            shape.radiusThickness = 1f;    // 实心圆（全圆面生成粒子），配合较大径向向内速度形成中心密集、边缘稀疏
            shape.rotation = new Vector3(-90f, 0f, 0f); // 圆面从 XY 平面旋转到 XZ 平面，粒子在整个水平圆形区域分布
            shape.position = new Vector3(0f, radius * k_swirlSnowHeightMultiplier, 0f); // 粒子在高空生成

            // 发射速率
            var emission = ps.emission;
            emission.rateOverTime = k_swirlSnowEmissionRate;

            // 龙卷风效果：径向向中心汇聚（旋转由 RotateSwirlEmission 旋转 transform 实现）
            var velocityOverLifetime = ps.velocityOverLifetime;
            velocityOverLifetime.enabled = true;
            velocityOverLifetime.space = ParticleSystemSimulationSpace.Local;
            // 径向速度为负值，向中心汇聚（龙卷风吸力）
            velocityOverLifetime.radial = new ParticleSystem.MinMaxCurve(
                k_swirlSnowRadialSpeedMin, k_swirlSnowRadialSpeedMax);

            // 透明度随生命周期降低
            var colorOverLifetime = ps.colorOverLifetime;
            colorOverLifetime.enabled = true;
            Gradient gradient = new Gradient();
            gradient.SetKeys(
                new GradientColorKey[]
                {
                    new GradientColorKey(Color.white, 0f),
                    new GradientColorKey(Color.white, 1f)
                },
                new GradientAlphaKey[]
                {
                    new GradientAlphaKey(1f, 0f),
                    new GradientAlphaKey(0.5f, 0.7f),
                    new GradientAlphaKey(0f, 1f)
                });
            colorOverLifetime.color = new ParticleSystem.MinMaxGradient(gradient);

            // 渲染：小球状体（Mesh 模式 + 内置 Sphere mesh）
            ParticleSystemRenderer renderer = ps.GetComponent<ParticleSystemRenderer>();
            Mesh sphereMesh = GetSphereMesh();
            if (sphereMesh != null)
            {
                renderer.renderMode = ParticleSystemRenderMode.Mesh;
                renderer.mesh = sphereMesh;
            }

            // 设置 URP 粒子材质（白色，支持顶点颜色透明度淡出）
            Material snowMat = GetOrCreateSnowMaterial();
            if (snowMat != null)
            {
                renderer.material = snowMat;
            }

            ps.Play();
        }

        /// <summary>
        /// 创建地面飞溅粒子系统
        /// 粒子从地面圆形区域向上飞溅后受重力落回，模拟雪花落地弹跳效果
        /// </summary>
        private void CreateGroundSplashVfx(Transform parent, float radius)
        {
            GameObject splashObj = new GameObject("GroundSplash");
            splashObj.transform.SetParent(parent, false);
            // 放置在世界 y=0 地面上（BlizzardVFX 是 HailStorm 子对象，需要偏移到地面高度）
            splashObj.transform.position = new Vector3(transform.position.x, 0.01f, transform.position.z);

            ParticleSystem ps = splashObj.AddComponent<ParticleSystem>();
            m_groundSplashPs = ps;

            // 主模块配置
            var main = ps.main;
            main.duration = 5f;
            main.loop = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.3f, 0.5f); // 短生命周期，弹跳弧线
            main.startSpeed = new ParticleSystem.MinMaxCurve(1f, 2.5f); // 向上弹跳速度（Hemisphere 默认沿 +Y 发射）
            main.startSize = new ParticleSystem.MinMaxCurve(0.01f, 0.04f); // 与下落雪花一致
            main.startColor = Color.white; // 纯白，与雪花一致
            main.gravityModifier = 0.5f;  // 低重力，弹跳弧线自然，下落不穿透太多
            main.maxParticles = 1500;
            // Local 空间：Hemisphere 的 startSpeed 沿 +Y 发射生效；BlizzardVFX 不旋转所以 local Y = world Y
            main.simulationSpace = ParticleSystemSimulationSpace.Local;

            // 形状：Mesh 发射器，使用圆形 mesh（法线略倾斜朝各方向，粒子向不同方向弹起）
            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Mesh;
            shape.mesh = GetOrCreateCircleMesh(radius);
            shape.meshShapeType = 0; // Vertex

            // 发射速率
            var emission = ps.emission;
            emission.rateOverTime = k_groundSplashEmissionRate;

            // 透明度随生命周期快速降低
            var colorOverLifetime = ps.colorOverLifetime;
            colorOverLifetime.enabled = true;
            Gradient gradient = new Gradient();
            gradient.SetKeys(
                new GradientColorKey[]
                {
                    new GradientColorKey(Color.white, 0f),
                    new GradientColorKey(Color.white, 1f)
                },
                new GradientAlphaKey[]
                {
                    new GradientAlphaKey(0.9f, 0f),
                    new GradientAlphaKey(0f, 1f)
                });
            colorOverLifetime.color = new ParticleSystem.MinMaxGradient(gradient);

            // 渲染：与雪花一致的小球状体
            ParticleSystemRenderer renderer = ps.GetComponent<ParticleSystemRenderer>();
            Mesh sphereMesh = GetSphereMesh();
            if (sphereMesh != null)
            {
                renderer.renderMode = ParticleSystemRenderMode.Mesh;
                renderer.mesh = sphereMesh;
            }
            Material snowMat = GetOrCreateSnowMaterial();
            if (snowMat != null)
            {
                renderer.material = snowMat;
            }

            ps.Play();
        }

        /// <summary>
        /// 创建雪地覆盖层：地面上的白色圆盘，模拟积雪效果
        /// 使用径向渐变纹理（中心不透明边缘透明），平铺在地面上
        /// </summary>
        private void CreateSnowGround(Transform parent, float radius)
        {
            GameObject snowGround = new GameObject("SnowGround");
            snowGround.transform.SetParent(parent, false);
            // 放置在世界 y=0 地面上，稍微抬高避免与地面 Z-fighting
            snowGround.transform.position = new Vector3(transform.position.x, 0.05f, transform.position.z);
            // mesh 顶点已在 XZ 平面（y=0），无需旋转
            snowGround.transform.localRotation = Quaternion.identity;
            // 缩放到 2*radius 覆盖整个伤害范围
            snowGround.transform.localScale = new Vector3(radius * 2f, 1f, radius * 2f);

            // 程序化创建平面 mesh（不依赖内置 Quad，兼容 Tuanjie 引擎）
            MeshFilter mf = snowGround.AddComponent<MeshFilter>();
            Mesh planeMesh = new Mesh();
            planeMesh.vertices = new Vector3[]
            {
                new Vector3(-0.5f, 0f, -0.5f),
                new Vector3(0.5f, 0f, -0.5f),
                new Vector3(0.5f, 0f, 0.5f),
                new Vector3(-0.5f, 0f, 0.5f)
            };
            planeMesh.triangles = new int[] { 0, 2, 1, 0, 3, 2 };
            planeMesh.uv = new Vector2[]
            {
                new Vector2(0, 0),
                new Vector2(1, 0),
                new Vector2(1, 1),
                new Vector2(0, 1)
            };
            planeMesh.normals = new Vector3[]
            {
                Vector3.up, Vector3.up, Vector3.up, Vector3.up
            };
            planeMesh.RecalculateBounds();
            mf.mesh = planeMesh;

            MeshRenderer mr = snowGround.AddComponent<MeshRenderer>();
            Material snowGroundMat = GetOrCreateSnowGroundMaterial();
            if (snowGroundMat != null)
            {
                mr.material = snowGroundMat;
            }
        }

        /// <summary>
        /// 更新暴雪特效的缩放（当 DamageRadius 变化时调用）
        /// </summary>
        private void UpdateBlizzardVfxScale(float radius)
        {
            if (m_swirlSnowPs != null)
            {
                var shape = m_swirlSnowPs.shape;
                shape.radius = radius * k_swirlSnowRadiusMultiplier;
                // 同步更新生成高度（保持高度与半径的比例）
                shape.position = new Vector3(0f, radius * k_swirlSnowHeightMultiplier, 0f);

                // 同步更新粒子生命周期（匹配新的下落时间）
                float fallHeight = radius * k_swirlSnowHeightMultiplier;
                float effectiveGravity = k_swirlSnowGravityMultiplier * Mathf.Abs(Physics.gravity.y);
                float fallTime = Mathf.Sqrt(2f * fallHeight / effectiveGravity);
                var main = m_swirlSnowPs.main;
                main.startLifetime = fallTime;
            }

            if (m_groundSplashPs != null)
            {
                var shape = m_groundSplashPs.shape;
                shape.radius = radius;
            }

            // 同步更新雪地覆盖层缩放
            if (m_blizzardVfxRoot != null)
            {
                Transform snowGround = m_blizzardVfxRoot.transform.Find("SnowGround");
                if (snowGround != null)
                {
                    snowGround.localScale = new Vector3(radius * 2f, 1f, radius * 2f);
                }
            }
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

                // 同步更新暴雪特效范围
                UpdateBlizzardVfxScale(radius);
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
