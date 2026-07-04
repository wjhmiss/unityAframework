using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Pool;
using AFrameWork.Core;

namespace AFrameWork.Core.SmallBase
{
    /// <summary>
    /// 通用轻量对象池（方案 A 对象池 + 方案 E 批量 Update）。
    ///
    /// 通用化设计：管理所有 SimpleObjectBase 子类（Bullet/Arrow/Fireball/EffectInstance...），
    /// 每种子类维护独立的 ObjectPool，活跃实例统一存入 List&lt;SimpleObjectBase&gt; 多态驱动。
    ///
    /// 性能说明（多类型共管不影响性能）：
    ///   - 单 MonoBehaviour.Update 单点驱动，消除 N 个 Pool 的 native 边界开销
    ///   - List&lt;SimpleObjectBase&gt; 存引用，类型混存不恶化 CPU cache（本就不连续）
    ///   - Tick 是虚方法分发，1000 实例约 1μs，可忽略
    ///   - 真正瓶颈（Rigidbody 物理/Collider 触发/池 Get/Release）只与活跃实例数有关
    ///
    /// 使用方式：
    ///   1. Inspector 配置：在 m_prefabEntries 列表中添加 (Type FullName, Prefab, Prewarm)
    ///   2. 或运行时调用 RegisterPrefab&lt;T&gt;(prefab, prewarm)
    ///   3. 发射：SimpleObjectPool.Instance.Launch&lt;Bullet&gt;(pos, dir, owner, damage)
    ///
    /// 回收时序：
    ///   - 子类 Tick/OnTriggerEnter 仅调 SimpleObjectBase.Deactivate() 设置 m_isAlive=false
    ///   - 本类 Update 逆序 + swap-remove，对 !IsAlive 的实例归还对应池
    /// </summary>
    public class SimpleObjectPool : MonoBehaviour
    {
        #region 单例

        private static SimpleObjectPool s_instance;

        /// <summary>全局单例。场景中需存在一个挂载 SimpleObjectPool 的 GameObject。</summary>
        public static SimpleObjectPool Instance
        {
            get
            {
                if (s_instance == null)
                {
                    s_instance = FindObjectOfType<SimpleObjectPool>();
                }
                return s_instance;
            }
        }

        #endregion

        #region Inspector 配置（混合注册方式：Inspector + 运行时 API）

        /// <summary>
        /// Inspector 配置条目：每条对应一种 SimpleObjectBase 子类的 prefab。
        /// typeName 必须是 SimpleObjectBase 子类的完整类型名（含命名空间），
        /// 启动时通过 Type.GetType 解析。
        /// </summary>
        [Serializable]
        public class PrefabEntry
        {
            [Tooltip("SimpleObjectBase 子类的完整类型名（含命名空间），例如 AFrameWork.Sample.Bullet")]
            public string typeName;

            [Tooltip("对象预制体（根节点需挂载对应 SimpleObjectBase 子类组件）")]
            public GameObject prefab;

            [Tooltip("预热数量（场景加载时预先创建的实例数）")]
            [Min(0)]
            public int prewarm = 10;
        }

        [Header("对象池配置")]
        [Tooltip("所有对象 prefab 配置。启动时自动注册。")]
        [SerializeField]
        private List<PrefabEntry> m_prefabEntries = new List<PrefabEntry>();

        [Tooltip("池最大容量（每种类型独立限制，超过时销毁而非回收）")]
        [SerializeField]
        private int m_maxPoolSize = 300;

        #endregion

        #region 运行时字段

        /// <summary>
        /// 类型 → 池条目。每种子类独立池，避免类型混存导致回收错误。
        /// </summary>
        private readonly Dictionary<Type, PoolEntry> m_pools = new Dictionary<Type, PoolEntry>(8);

        /// <summary>
        /// 所有活跃实例（不论类型），统一 Tick 驱动。
        /// 多态分发到各子类的 Tick 实现。
        /// </summary>
        private readonly List<SimpleObjectBase> m_activeObjects = new List<SimpleObjectBase>(128);

        /// <summary>池根节点（所有池化实例的父对象，保持 Hierarchy 整洁）</summary>
        private Transform m_poolRoot;

        /// <summary>池条目：持有 ObjectPool 与对应类型，便于 Launch 时泛型分发。</summary>
        private class PoolEntry
        {
            public Type Type;
            public GameObject Prefab;
            public ObjectPool<SimpleObjectBase> Pool;
        }

        #endregion

        #region Unity 生命周期

        private void Awake()
        {
            // 单例初始化
            if (s_instance != null && s_instance != this)
            {
                Debug.LogWarning($"[{GetType().Name}] 已存在实例，重复实例将被销毁。", this);
                Destroy(gameObject);
                return;
            }
            s_instance = this;

            // 创建池根节点
            m_poolRoot = new GameObject("SimpleObjectPool_Root").transform;
            m_poolRoot.SetParent(transform, false);

            // 遍历 Inspector 配置，自动注册
            for (int i = 0; i < m_prefabEntries.Count; i++)
            {
                PrefabEntry entry = m_prefabEntries[i];
                if (entry == null || string.IsNullOrEmpty(entry.typeName) || entry.prefab == null)
                {
                    Debug.LogWarning($"[{GetType().Name}] PrefabEntry[{i}] 配置不完整，已跳过。", this);
                    continue;
                }

                Type t = Type.GetType(entry.typeName);
                if (t == null)
                {
                    Debug.LogError($"[{GetType().Name}] 无法解析类型：{entry.typeName}。请检查 typeName 是否包含命名空间。", this);
                    continue;
                }

                if (!typeof(SimpleObjectBase).IsAssignableFrom(t))
                {
                    Debug.LogError($"[{GetType().Name}] 类型 {entry.typeName} 不继承 SimpleObjectBase，已跳过。", this);
                    continue;
                }

                RegisterPrefabInternal(t, entry.prefab, entry.prewarm);
            }
        }

        private void OnDestroy()
        {
            if (s_instance == this)
            {
                s_instance = null;
            }
            m_activeObjects.Clear();
            m_pools.Clear();
        }

        /// <summary>
        /// 单点批量 Update：驱动所有活跃对象的 Tick。
        /// 逆序遍历 + swap-remove，处理 Tick/OnTriggerEnter 中标记的 Deactivate。
        /// </summary>
        private void Update()
        {
            float deltaTime = Time.deltaTime;

            for (int i = m_activeObjects.Count - 1; i >= 0; i--)
            {
                SimpleObjectBase obj = m_activeObjects[i];

                // 防御性：池外销毁或意外 null
                if (obj == null)
                {
                    SwapRemoveAt(i);
                    continue;
                }

                // 仍活跃则推进 Tick（多态分发到子类）
                if (obj.IsAlive)
                {
                    obj.Tick(deltaTime);
                }

                // Tick 或物理回调已请求回收 → 从列表移除并归还对应池
                if (!obj.IsAlive)
                {
                    SwapRemoveAt(i);
                    ReleaseToPool(obj);
                }
            }
        }

        #endregion

        #region 对外 API

        /// <summary>
        /// 运行时注册 prefab。可在场景引导脚本或子类静态初始化时调用。
        /// 重复注册同类型会覆盖旧池（需调用者自行避免在活跃期间重复注册）。
        /// </summary>
        /// <typeparam name="T">SimpleObjectBase 子类类型</typeparam>
        /// <param name="prefab">对象预制体（根节点需挂载 T 组件）</param>
        /// <param name="prewarm">预热数量</param>
        public void RegisterPrefab<T>(GameObject prefab, int prewarm = 0) where T : SimpleObjectBase
        {
            if (prefab == null)
            {
                Debug.LogError($"[{GetType().Name}] RegisterPrefab<{typeof(T).Name}>: prefab 为空。", this);
                return;
            }
            RegisterPrefabInternal(typeof(T), prefab, prewarm);
        }

        /// <summary>
        /// 从池中取出一个指定类型的对象并初始化。
        /// </summary>
        /// <typeparam name="T">SimpleObjectBase 子类类型（需已注册 prefab）</typeparam>
        /// <param name="position">初始位置</param>
        /// <param name="direction">初始方向（会归一化）</param>
        /// <param name="owner">发射者（用于继承阵营与穿透属性）</param>
        /// <param name="damage">伤害值</param>
        /// <param name="damageType">伤害类型</param>
        /// <returns>取出的对象实例；未注册或池异常时返回 null</returns>
        public T Launch<T>(
            Vector3 position,
            Vector3 direction,
            ObjectBase owner,
            float damage,
            DamageType damageType = DamageType.Physical) where T : SimpleObjectBase
        {
            if (!m_pools.TryGetValue(typeof(T), out PoolEntry entry))
            {
                Debug.LogError($"[{GetType().Name}] 类型 {typeof(T).Name} 未注册 prefab，无法取出。请先调用 RegisterPrefab<{typeof(T).Name}>。", this);
                return null;
            }

            SimpleObjectBase obj = entry.Pool.Get();
            if (obj == null)
            {
                Debug.LogError($"[{GetType().Name}] 池 {typeof(T).Name} 返回 null 实例（prefab 可能未挂载组件）。", this);
                return null;
            }
            obj.Initialize(position, direction, owner, damage, damageType);
            return (T)obj;
        }

        /// <summary>当前活跃对象总数量（所有类型合计）。</summary>
        public int ActiveCount => m_activeObjects.Count;

        /// <summary>指定类型的已注册状态。</summary>
        public bool IsRegistered<T>() where T : SimpleObjectBase => m_pools.ContainsKey(typeof(T));

        #endregion

        #region 内部：池注册与回调

        /// <summary>注册通用实现（非泛型版本，供 Inspector 自动注册复用）。</summary>
        private void RegisterPrefabInternal(Type type, GameObject prefab, int prewarm)
        {
            // 若已存在同类型池，先清空旧的（防止重复注册）
            if (m_pools.TryGetValue(type, out PoolEntry oldEntry))
            {
                oldEntry.Pool.Clear();
                m_pools.Remove(type);
            }

            PoolEntry entry = new PoolEntry
            {
                Type = type,
                Prefab = prefab,
                Pool = new ObjectPool<SimpleObjectBase>(
                    createFunc: () => CreateInstance(type, prefab),
                    actionOnGet: OnObjectGet,
                    actionOnRelease: OnObjectRelease,
                    actionOnDestroy: OnObjectDestroy,
                    collectionCheck: true,
                    defaultCapacity: 16,
                    maxSize: m_maxPoolSize)
            };
            m_pools[type] = entry;

            // 预热
            if (prewarm > 0)
            {
                PrewarmPool(entry, prewarm);
            }
        }

        /// <summary>创建实例：Instantiate prefab 并获取指定类型的 SimpleObjectBase 组件。</summary>
        private SimpleObjectBase CreateInstance(Type type, GameObject prefab)
        {
            GameObject obj = Instantiate(prefab, m_poolRoot);
            SimpleObjectBase instance = obj.GetComponent(type) as SimpleObjectBase;
            if (instance == null)
            {
                Debug.LogError($"[{GetType().Name}] prefab {prefab.name} 未挂载 {type.Name} 组件。", this);
                Destroy(obj);
                return null;
            }
            obj.SetActive(false);
            return instance;
        }

        private void OnObjectGet(SimpleObjectBase obj)
        {
            if (obj == null) return;
            obj.OnGetFromPool();
            if (!m_activeObjects.Contains(obj))
            {
                m_activeObjects.Add(obj);
            }
        }

        private void OnObjectRelease(SimpleObjectBase obj)
        {
            if (obj == null) return;
            obj.OnReleaseToPool();
            // 活跃列表移除在 Update 的 swap-remove 中完成，此处不重复
        }

        private void OnObjectDestroy(SimpleObjectBase obj)
        {
            if (obj != null && obj.gameObject != null)
            {
                Destroy(obj.gameObject);
            }
        }

        /// <summary>归还到对应类型的池。按实例运行时类型查找。</summary>
        private void ReleaseToPool(SimpleObjectBase obj)
        {
            if (obj == null) return;
            Type t = obj.GetType();
            if (m_pools.TryGetValue(t, out PoolEntry entry))
            {
                entry.Pool.Release(obj);
            }
            else
            {
                // 找不到池（类型未注册或已注销），直接销毁避免泄漏
                Debug.LogWarning($"[{GetType().Name}] 类型 {t.Name} 的池未找到，直接销毁实例。", this);
                if (obj.gameObject != null) Destroy(obj.gameObject);
            }
        }

        /// <summary>预热指定类型的池。</summary>
        private void PrewarmPool(PoolEntry entry, int count)
        {
            List<SimpleObjectBase> temp = new List<SimpleObjectBase>(count);
            for (int i = 0; i < count; i++)
            {
                SimpleObjectBase p = entry.Pool.Get();
                if (p != null) temp.Add(p);
            }
            for (int i = 0; i < temp.Count; i++)
            {
                entry.Pool.Release(temp[i]);
            }
            // Prewarm 期间 Get 会加入活跃列表，Update 未运行无法 swap-remove，此处统一清空
            m_activeObjects.Clear();
        }

        #endregion

        #region 内部工具

        /// <summary>
        /// swap-remove：将末尾元素移到 index 处并移除末尾，O(1)。
        /// 逆序遍历中，末尾元素已处理过，因此无需重新 Tick。
        /// </summary>
        private void SwapRemoveAt(int index)
        {
            int last = m_activeObjects.Count - 1;
            if (index != last)
            {
                m_activeObjects[index] = m_activeObjects[last];
            }
            m_activeObjects.RemoveAt(last);
        }

        #endregion
    }
}