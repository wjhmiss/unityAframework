using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace AFrameWork.GameUI
{
    /// <summary>
    /// 血条控制器 - 管理多个血条的生命周期、对象池化、批量更新
    /// 提供全局血条管理接口，优化性能，避免频繁创建/销毁
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class HealthBarController : MonoBehaviour
    {
        // ══════════════════════════════════════════════════════════════════════════
        // 常量定义
        // ══════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// 默认血条池大小
        /// </summary>
        private const int k_defaultPoolSize = 20;

        /// <summary>
        /// 血条池最大容量
        /// </summary>
        private const int k_maxPoolSize = 100;

        /// <summary>
        /// 默认头部偏移值
        /// </summary>
        private const float k_defaultHeadOffset = 2.0f;

        // ══════════════════════════════════════════════════════════════════════════
        // 字段定义
        // ══════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// UIDocument 组件引用
        /// </summary>
        private UIDocument m_uiDocument;

        /// <summary>
        /// 血条池（用于对象复用）
        /// </summary>
        private Queue<GameObject> m_healthBarPool;

        /// <summary>
        /// 活跃的血条字典（目标 -> 血条组件）
        /// </summary>
        private Dictionary<Transform, HealthBar> m_activeHealthBars;

        /// <summary>
        /// 血条父容器
        /// </summary>
        private Transform m_healthBarContainer;

        /// <summary>
        /// 默认血条配置
        /// </summary>
        private HealthBarConfig m_defaultConfig;

        /// <summary>
        /// 血条预制体（动态创建）
        /// </summary>
        private GameObject m_healthBarPrefab;

        /// <summary>
        /// 是否已初始化
        /// </summary>
        private bool m_isInitialized;

        /// <summary>
        /// 血条 UXML 资源引用
        /// </summary>
        [Tooltip("血条 UXML 资源")]
        [SerializeField]
        private VisualTreeAsset m_healthBarUxml;

        /// <summary>
        /// 血条 USS 资源引用
        /// </summary>
        [Tooltip("血条 USS 样式资源")]
        [SerializeField]
        private StyleSheet m_healthBarUss;

        /// <summary>
        /// 血条池初始大小
        /// </summary>
        [Tooltip("血条池初始大小")]
        [SerializeField]
        private int m_poolSize = k_defaultPoolSize;

        // ══════════════════════════════════════════════════════════════════════════
        // 属性
        // ══════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// 获取活跃血条数量
        /// </summary>
        public int ActiveCount => m_activeHealthBars != null ? m_activeHealthBars.Count : 0;

        /// <summary>
        /// 获取血条池剩余数量
        /// </summary>
        public int PoolCount => m_healthBarPool != null ? m_healthBarPool.Count : 0;

        /// <summary>
        /// 获取血条 UXML 模板引用
        /// </summary>
        public VisualTreeAsset HealthBarUxml => m_healthBarUxml;

        /// <summary>
        /// 获取血条 USS 样式引用
        /// </summary>
        public StyleSheet HealthBarUss => m_healthBarUss;

        /// <summary>
        /// 场景中的单例引用（Awake 时缓存，OnDestroy 时清除）
        /// 避免 Fighter/Monster 调用 FindObjectOfType 遍历整个场景
        /// </summary>
        public static HealthBarController Instance { get; private set; }

        // ══════════════════════════════════════════════════════════════════════════
        // MonoBehaviour 方法
        // ══════════════════════════════════════════════════════════════════════════

        private void Awake()
        {
            Instance = this;

            // 初始化 UIDocument 引用
            m_uiDocument = GetComponent<UIDocument>();

            if (m_uiDocument == null)
            {
#if UNITY_EDITOR
                Debug.LogError($"[{GetType().Name}] UIDocument component is missing!", this);
#endif
                return;
            }

            // 确保 UIDocument 的 rootVisualElement 存在
            var root = m_uiDocument.rootVisualElement;
            if (root == null)
            {
#if UNITY_EDITOR
                Debug.LogError($"[{GetType().Name}] UIDocument.rootVisualElement is null!", this);
#endif
                return;
            }

            // 添加 USS 样式到 root（如果配置了）
            if (m_healthBarUss != null)
            {
                root.styleSheets.Add(m_healthBarUss);
#if UNITY_EDITOR
                Debug.Log($"[{GetType().Name}] Added USS stylesheet to UIDocument root");
#endif
            }
            else
            {
#if UNITY_EDITOR
                Debug.LogWarning($"[{GetType().Name}] Health Bar Uss resource is null! Please configure it in Inspector.", this);
#endif
            }

            // 清除 UIDocument 的 visualTreeAsset 可能预先生成的 health-bar 元素
            // 避免静态元素残留在 (0,0) 位置（血条应由 HealthBar 动态创建和定位）
            root.Query<VisualElement>(name: "health-bar").ForEach(e => e.RemoveFromHierarchy());

            // 创建血条容器（用于组织 Unity GameObject）
            m_healthBarContainer = new GameObject("HealthBarContainer").transform;
            m_healthBarContainer.SetParent(transform);

            // 初始化数据结构
            m_healthBarPool = new Queue<GameObject>(m_poolSize);
            m_activeHealthBars = new Dictionary<Transform, HealthBar>(m_poolSize);

            // 设置默认配置
            m_defaultConfig = HealthBarConfig.CreateDefault();

            // 创建血条预制体模板
            CreateHealthBarPrefab();

            // 初始化血条池
            InitializePool();
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;

            // 清理所有血条
            ClearAllHealthBars();

            // 清理血条池
            ClearPool();

            // 销毁容器
            if (m_healthBarContainer != null)
            {
                Destroy(m_healthBarContainer.gameObject);
            }
        }

        // ══════════════════════════════════════════════════════════════════════════
        // 公共方法
        // ══════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// 设置默认血条配置
        /// </summary>
        /// <param name="config">血条配置</param>
        public void SetDefaultConfig(HealthBarConfig config)
        {
            m_defaultConfig = config;
        }

        /// <summary>
        /// 创建血条并绑定到目标对象
        /// </summary>
        /// <param name="target">目标 Transform</param>
        /// <param name="headOffset">头部偏移（可选）</param>
        /// <returns>创建的血条组件</returns>
        public HealthBar CreateHealthBar(Transform target, float headOffset = k_defaultHeadOffset)
        {
            return CreateHealthBar(target, m_defaultConfig, headOffset);
        }

        /// <summary>
        /// 创建血条并绑定到目标对象（自定义配置）
        /// </summary>
        /// <param name="target">目标 Transform</param>
        /// <param name="config">血条配置</param>
        /// <param name="headOffset">头部偏移（可选）</param>
        /// <returns>创建的血条组件</returns>
        public HealthBar CreateHealthBar(Transform target, HealthBarConfig config, float headOffset = k_defaultHeadOffset)
        {
            if (target == null)
            {
#if UNITY_EDITOR
                Debug.LogError($"[{GetType().Name}] Target transform is null!", this);
#endif
                return null;
            }

            // 检查是否已存在血条
            if (m_activeHealthBars.ContainsKey(target))
            {
#if UNITY_EDITOR
                Debug.LogWarning($"[{GetType().Name}] Health bar already exists for target: {target.name}", this);
#endif
                return m_activeHealthBars[target];
            }

            // 从池中获取或创建新血条
            GameObject healthBarObject = GetFromPool();
            if (healthBarObject == null)
            {
                healthBarObject = CreateNewHealthBar();
            }

            // 设置血条父容器
            healthBarObject.transform.SetParent(m_healthBarContainer);

            // 获取 HealthBar 组件
            HealthBar healthBar = healthBarObject.GetComponent<HealthBar>();
            if (healthBar == null)
            {
                healthBar = healthBarObject.AddComponent<HealthBar>();
            }

            // 配置血条
            healthBar.SetConfig(config);
            healthBar.SetTarget(target, headOffset, this);  // 传入控制器引用

            // 添加到活跃字典
            m_activeHealthBars[target] = healthBar;

            return healthBar;
        }

        /// <summary>
        /// 移除目标对象的血条
        /// </summary>
        /// <param name="target">目标 Transform</param>
        public void RemoveHealthBar(Transform target)
        {
            if (target == null || !m_activeHealthBars.ContainsKey(target))
            {
                return;
            }

            // 获取血条组件
            HealthBar healthBar = m_activeHealthBars[target];

            // 从字典移除
            m_activeHealthBars.Remove(target);

            // 隐藏血条
            healthBar.HideImmediate();

            // 返回池中
            ReturnToPool(healthBar.gameObject);
        }

        /// <summary>
        /// 更新目标对象的血量
        /// </summary>
        /// <param name="target">目标 Transform</param>
        /// <param name="currentHealth">当前血量</param>
        /// <param name="maxHealth">最大血量</param>
        public void UpdateHealth(Transform target, float currentHealth, float maxHealth)
        {
            if (target == null || !m_activeHealthBars.ContainsKey(target))
            {
                return;
            }

            HealthBar healthBar = m_activeHealthBars[target];
            healthBar.UpdateHealth(currentHealth, maxHealth);
        }

        /// <summary>
        /// 显示目标对象的血条
        /// </summary>
        /// <param name="target">目标 Transform</param>
        public void ShowHealthBar(Transform target)
        {
            if (target == null || !m_activeHealthBars.ContainsKey(target))
            {
                return;
            }

            HealthBar healthBar = m_activeHealthBars[target];
            healthBar.Show();
        }

        /// <summary>
        /// 隐藏目标对象的血条
        /// </summary>
        /// <param name="target">目标 Transform</param>
        public void HideHealthBar(Transform target)
        {
            if (target == null || !m_activeHealthBars.ContainsKey(target))
            {
                return;
            }

            HealthBar healthBar = m_activeHealthBars[target];
            healthBar.Hide();
        }

        /// <summary>
        /// 清理所有血条
        /// </summary>
        public void ClearAllHealthBars()
        {
            if (m_activeHealthBars == null)
            {
                return;
            }

            // 遍历所有活跃血条
            foreach (var kvp in m_activeHealthBars)
            {
                HealthBar healthBar = kvp.Value;
                if (healthBar != null)
                {
                    healthBar.HideImmediate();
                    ReturnToPool(healthBar.gameObject);
                }
            }

            // 清空字典
            m_activeHealthBars.Clear();
        }

        /// <summary>
        /// 获取目标对象的血条组件
        /// </summary>
        /// <param name="target">目标 Transform</param>
        /// <returns>血条组件，不存在则返回 null</returns>
        public HealthBar GetHealthBar(Transform target)
        {
            if (target == null || !m_activeHealthBars.ContainsKey(target))
            {
                return null;
            }

            return m_activeHealthBars[target];
        }

        /// <summary>
        /// 批量更新血条配置
        /// </summary>
        /// <param name="config">新配置</param>
        public void UpdateAllConfigs(HealthBarConfig config)
        {
            if (m_activeHealthBars == null)
            {
                return;
            }

            foreach (var kvp in m_activeHealthBars)
            {
                HealthBar healthBar = kvp.Value;
                if (healthBar != null)
                {
                    healthBar.SetConfig(config);
                }
            }
        }

        // ══════════════════════════════════════════════════════════════════════════
        // 私有方法
        // ══════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// 创建血条预制体模板
        /// </summary>
        private void CreateHealthBarPrefab()
        {
            // 创建预制体模板对象（纯 GameObject，不带 UIDocument）
            // 血条 VisualElement 由 HealthBar 动态添加到控制器的 UIDocument root
            m_healthBarPrefab = new GameObject("HealthBarTemplate");
            m_healthBarPrefab.SetActive(false);

            // 设置父容器
            m_healthBarPrefab.transform.SetParent(m_healthBarContainer);
        }

        /// <summary>
        /// 初始化血条池
        /// </summary>
        private void InitializePool()
        {
            // 预创建血条对象
            for (int i = 0; i < m_poolSize; i++)
            {
                GameObject healthBarObject = CreateNewHealthBar();
                ReturnToPool(healthBarObject);
            }

            m_isInitialized = true;
        }

        /// <summary>
        /// 创建新的血条对象
        /// </summary>
        /// <returns>新创建的血条对象</returns>
        private GameObject CreateNewHealthBar()
        {
            if (m_healthBarPrefab == null)
            {
#if UNITY_EDITOR
                Debug.LogError($"[{GetType().Name}] Health bar prefab template is null!", this);
#endif
                return null;
            }

            // 克隆预制体模板
            GameObject healthBarObject = Instantiate(m_healthBarPrefab);
            healthBarObject.name = "HealthBar";

            // 确保 HealthBar 组件存在
            HealthBar healthBar = healthBarObject.GetComponent<HealthBar>();
            if (healthBar == null)
            {
                healthBar = healthBarObject.AddComponent<HealthBar>();
            }

            return healthBarObject;
        }

        /// <summary>
        /// 从池中获取血条对象
        /// </summary>
        /// <returns>血条对象，池为空则返回 null</returns>
        private GameObject GetFromPool()
        {
            if (m_healthBarPool == null || m_healthBarPool.Count == 0)
            {
                return null;
            }

            GameObject healthBarObject = m_healthBarPool.Dequeue();
            healthBarObject.SetActive(true);

            return healthBarObject;
        }

        /// <summary>
        /// 将血条对象返回池中
        /// </summary>
        /// <param name="healthBarObject">血条对象</param>
        private void ReturnToPool(GameObject healthBarObject)
        {
            if (healthBarObject == null || m_healthBarPool == null)
            {
                return;
            }

            // 检查池容量限制
            if (m_healthBarPool.Count >= k_maxPoolSize)
            {
                // 超过最大容量，销毁对象
                Destroy(healthBarObject);
                return;
            }

            // 隐藏对象
            healthBarObject.SetActive(false);

            // 重置父容器
            healthBarObject.transform.SetParent(m_healthBarContainer);

            // 添加到池中
            m_healthBarPool.Enqueue(healthBarObject);
        }

        /// <summary>
        /// 清理血条池
        /// </summary>
        private void ClearPool()
        {
            if (m_healthBarPool == null)
            {
                return;
            }

            // 销毁池中所有对象
            while (m_healthBarPool.Count > 0)
            {
                GameObject healthBarObject = m_healthBarPool.Dequeue();
                if (healthBarObject != null)
                {
                    Destroy(healthBarObject);
                }
            }

            m_healthBarPool.Clear();
        }

        /* ══════════════════════════════════════════════════════════════════════════
           【使用说明】

           1. 基本设置：
              // 获取或添加 HealthBarController 组件
              HealthBarController controller = gameObject.GetComponent<HealthBarController>();
              if (controller == null)
              {
                  controller = gameObject.AddComponent<HealthBarController>();
              }

              // 设置默认配置（可选）
              HealthBarConfig config = HealthBarConfig.CreateDefault();
              controller.SetDefaultConfig(config);

           2. 创建血条：
              // 使用默认配置创建血条
              HealthBar healthBar = controller.CreateHealthBar(targetTransform, 2.0f);

              // 使用自定义配置创建血条
              HealthBarConfig customConfig = new HealthBarConfig(120f, 15f, -25f, true);
              HealthBar healthBar = controller.CreateHealthBar(targetTransform, customConfig, 2.5f);

           3. 更新血量：
              // 更新单个目标的血量
              controller.UpdateHealth(targetTransform, 80f, 100f);

              // 或通过血条组件更新
              HealthBar healthBar = controller.GetHealthBar(targetTransform);
              if (healthBar != null)
              {
                  healthBar.UpdateHealth(80f, 100f);
              }

           4. 显示/隐藏控制：
              // 显示单个血条
              controller.ShowHealthBar(targetTransform);

              // 隐藏单个血条
              controller.HideHealthBar(targetTransform);

              // 或通过血条组件控制
              HealthBar healthBar = controller.GetHealthBar(targetTransform);
              if (healthBar != null)
              {
                  healthBar.Show();  // 带淡入动画
                  healthBar.HideImmediate();  // 立即隐藏
              }

           5. 移除血条：
              // 移除单个血条
              controller.RemoveHealthBar(targetTransform);

              // 清理所有血条
              controller.ClearAllHealthBars();

           6. 批量操作：
              // 批量更新配置
              HealthBarConfig newConfig = HealthBarConfig.CreateLarge();
              controller.UpdateAllConfigs(newConfig);

              // 获取活跃血条数量
              int activeCount = controller.ActiveCount;

              // 获取血条池剩余数量
              int poolCount = controller.PoolCount;

           7. 预设配置使用：
              // 设置紧凑型配置为默认
              controller.SetDefaultConfig(HealthBarConfig.CreateCompact());

              // 为 Boss 创建大型血条
              HealthBar bossHealthBar = controller.CreateHealthBar(bossTransform, HealthBarConfig.CreateLarge(), 3.0f);

           8. UXML/USS 资源设置：
              // 在 Inspector 中设置 UXML 和 USS 资源
              // 或在代码中动态加载（需要使用 Addressables 或 Resources）
              // controller.m_healthBarUxml = Resources.Load<VisualTreeAsset>("HealthBar");
              // controller.m_healthBarUss = Resources.Load<StyleSheet>("HealthBar");

           9. 性能优化建议：
              - 血条池大小（PoolSize）根据场景中预期的最大血条数量设置
              - 建议值：普通场景 20-30，大型战斗场景 50-100
              - 血条池会自动扩容，超过 MaxPoolSize 时会销毁多余对象
              - 移除血条时会返回池中复用，避免频繁创建/销毁
              - UpdateInterval 配置控制更新频率，建议 0.05-0.1秒

           10. 注意事项：
               - 确保 UIDocument 组件已添加
               - 确保 UXML 和 USS 资源已正确设置
               - 血条控制器会自动创建 HealthBarContainer 作为血条父容器
               - 血条对象池会预创建 PoolSize 数量的对象
               - 当目标对象被销毁时，应手动调用 RemoveHealthBar 清理
               - 血条池最大容量限制为 MaxPoolSize（100），防止内存泄漏

           11. 与 ObjectBase 配合使用：
               // 在 ObjectBase 或子类中集成血条系统
               public class Enemy : ObjectBase
               {
                   private HealthBarController m_healthBarController;
                   private HealthBar m_healthBar;

                   protected override void Awake()
                   {
                       base.Awake();
                       // 获取血条控制器（通常是场景中的管理器）
                       m_healthBarController = FindObjectOfType<HealthBarController>();
                   }

                   protected override void Start()
                   {
                       base.Start();
                       // 创建血条
                       if (m_healthBarController != null)
                       {
                           m_healthBar = m_healthBarController.CreateHealthBar(transform, 2.0f);
                           m_healthBar.InitializeHealth(m_currentHealth, m_maxHealth);
                           m_healthBar.Show();
                       }
                   }

                   public override void TakeDamage(float damage)
                   {
                       base.TakeDamage(damage);
                       // 更新血条
                       if (m_healthBar != null)
                       {
                           m_healthBar.UpdateHealth(m_currentHealth, m_maxHealth);
                       }
                   }

                   protected override void OnDeath()
                   {
                       base.OnDeath();
                       // 移除血条
                       if (m_healthBarController != null)
                       {
                           m_healthBarController.RemoveHealthBar(transform);
                       }
                   }
               }

           ══════════════════════════════════════════════════════════════════════════ */
    }
}