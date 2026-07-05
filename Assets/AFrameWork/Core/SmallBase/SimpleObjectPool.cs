using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Pool;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

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
    ///   1. Addressables 配置：在 Addressables Groups 窗口为预制体添加 "PoolObjects" label
    ///   2. 预制体要求：根节点必须挂载 SimpleObjectBase 子类组件（用于推断类型）
    ///   3. 自动注册：Start 时自动遍历 Addressables 中标记 PoolObjects label 的预制体并注册
    ///   4. 发射：SimpleObjectPool.Instance.Launch&lt;Bullet&gt;(pos, dir, owner)
    ///   5. 运行时注册：可选调用 RegisterPrefab&lt;T&gt;(prefab, prewarm) 手动注册
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

        #region Addressables 配置常量

        // Addressables label：所有需要池化的预制体都应标记此 label
        // 在 Addressables Groups 窗口中为预制体添加 "PoolObjects" label
        private const string k_poolLabel = "PoolObjects";

        // 默认预热数量：每种类型初始化时预先创建的实例数
        private const int k_defaultPrewarmCount = 10;

        // 池最大容量：每种类型独立限制，超过时销毁而非回收
        private const int k_maxPoolSize = 300;

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
        }

        /// <summary>
        /// 异步初始化：遍历 Addressables 中标记 PoolObjects label 的预制体并注册。
        /// 自动从预制体挂载的 SimpleObjectBase 组件推断类型。
        /// </summary>
        private async void Start()
        {
            await InitializePoolsAsync();
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
        /// 伤害参数不再由调用方传入：Initialize 会克隆 owner 的 ObjectStatsConfig 作为快照，
        /// 命中时通过 ObjectStatsConfig.CalculateAttack 走完整伤害公式。
        /// </summary>
        /// <typeparam name="T">SimpleObjectBase 子类类型（需已注册 prefab）</typeparam>
        /// <param name="position">初始位置</param>
        /// <param name="direction">初始方向（会归一化）</param>
        /// <param name="owner">发射者（用于继承阵营与战斗属性快照）</param>
        /// <returns>取出的对象实例；未注册或池异常时返回 null</returns>
        public T Launch<T>(
            Vector3 position,
            Vector3 direction,
            ObjectBase owner) where T : SimpleObjectBase
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
            obj.Initialize(position, direction, owner);
            return (T)obj;
        }

        /// <summary>当前活跃对象总数量（所有类型合计）。</summary>
        public int ActiveCount => m_activeObjects.Count;

        /// <summary>指定类型的已注册状态。</summary>
        public bool IsRegistered<T>() where T : SimpleObjectBase => m_pools.ContainsKey(typeof(T));

        #endregion

        #region 内部：池注册与回调

        /// <summary>
        /// 异步初始化对象池：遍历 Addressables 中标记 PoolObjects label 的所有预制体并注册。
        /// 从预制体挂载的 SimpleObjectBase 组件推断类型,自动预热默认数量。
        /// </summary>
        private async Task InitializePoolsAsync()
        {
            // 查找所有标记 PoolObjects label 的资源位置
            // Addressables 1.22.3 推荐使用 LoadResourceLocationsAsync(object key, Type type) 重载
            // AsyncOperationHandle<IList<IResourceLocation>> locationsHandle =
            //     Addressables.LoadResourceLocationsAsync(k_poolLabel, typeof(GameObject));

            List<string> keys = new List<string>() { k_poolLabel };
            AsyncOperationHandle<IList<GameObject>> assetsHandle =
                Addressables.LoadAssetsAsync<GameObject>(
                    keys,
                    null, // 无需回调，直接等待 Task 结果
                    Addressables.MergeMode.Union,
                    false
                );

            IList<GameObject> prefabs = null;
            bool handleCreated = false;
            try
            {
                prefabs = await assetsHandle.Task;
                handleCreated = true;
#if UNITY_EDITOR
                //Debug.Log($"[{GetType().Name}] 找到 {prefabs.Count} 个 Addressables label '{k_poolLabel}' 的预制体。");
#endif
            }
            catch (Exception ex)
            {
                Debug.LogError($"[{GetType().Name}] 加载 Addressables label '{k_poolLabel}' 失败：{ex.Message}", this);
                if (handleCreated)
                {
                    Addressables.Release(assetsHandle);
                }
                return;
            }

            if (prefabs == null || prefabs.Count == 0)
            {
#if UNITY_EDITOR
                Debug.LogWarning($"[{GetType().Name}] Addressables 中未找到标记 '{k_poolLabel}' 的预制体。请在 Addressables Groups 窗口为预制体添加此 label。", this);
#endif
                Addressables.Release(assetsHandle);
                return;
            }

            // 直接使用已加载的 GameObject，无需二次加载
            for (int i = 0; i < prefabs.Count; i++)
            {
                GameObject prefab = prefabs[i];
                if (prefab == null) continue;

                // 从预制体获取 SimpleObjectBase 组件推断类型
                SimpleObjectBase component = prefab.GetComponent<SimpleObjectBase>();
                if (component == null)
                {
                    Debug.LogWarning($"[{GetType().Name}] 预制体 '{prefab.name}' 未挂载 SimpleObjectBase 组件，已跳过。", this);
                    continue;
                }

                Type type = component.GetType();
                if (m_pools.ContainsKey(type))
                {
#if UNITY_EDITOR
                    //Debug.LogWarning($"[{GetType().Name}] 类型 '{type.Name}' 已注册，跳过重复注册。", this);
#endif
                    continue;
                }

                // 注册到对象池
                RegisterPrefabInternal(type, prefab, k_defaultPrewarmCount);

#if UNITY_EDITOR
                Debug.Log($"[{GetType().Name}] 已注册类型 '{type.Name}'，预热数量 {k_defaultPrewarmCount}。", this);
#endif
            }

            // 释放加载句柄（预制体实例由各自池管理，不在此释放）
            Addressables.Release(assetsHandle);
        }

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
                    maxSize: k_maxPoolSize)
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

    /// <summary>
    /// SimpleObjectPool 使用说明：
    /// ============================================================
    /// 通用轻量对象池（方案 A 对象池 + 方案 E 批量 Update）。
    /// 管理所有 SimpleObjectBase 子类（Bullet/Arrow/Fireball/EffectInstance...），
    /// 每种子类维护独立的 ObjectPool，活跃实例统一存入 List&lt;SimpleObjectBase&gt; 多态驱动。
    /// 单 MonoBehaviour.Update 单点驱动，消除 N 个 Pool 的 native 边界开销。
    ///
    /// ════════════════════════════════════════════════════════════
    /// 【单例设计】
    /// ════════════════════════════════════════════════════════════
    ///   - s_instance 静态字段，场景中需存在一个挂载 SimpleObjectPool 的 GameObject
    ///   - Instance 属性延迟查找（FindObjectOfType），Awake 时缓存
    ///   - 重复实例在 Awake 中被销毁并输出警告
    ///   - OnDestroy 清理 s_instance（仅当指向自身时）
    ///
    /// ════════════════════════════════════════════════════════════
    /// 【Addressables 配置常量】
    /// ════════════════════════════════════════════════════════════
    ///   k_poolLabel = "PoolObjects"
    ///     - 所有需要池化的预制体都应在 Addressables Groups 窗口标记此 label
    ///     - Start 时通过 LoadAssetsAsync 自动加载所有标记此 label 的预制体
    ///
    ///   k_defaultPrewarmCount = 10
    ///     - 每种类型初始化时预先创建的实例数（预热数量，非最大容量）
    ///     - 预热避免运行时首次 Instantiate 的卡顿
    ///
    ///   k_maxPoolSize = 300
    ///     - 每种类型池的最大容量（硬限制）
    ///     - 超过此数量的归还实例会被销毁而非回收
    ///     - 防止内存泄漏，同时允许运行时临时扩容
    ///
    ///   注意：预热数量（prewarm）≠ 最大容量（maxSize）
    ///        prewarm = 10 表示初始创建 10 个实例
    ///        maxSize = 300 表示最多缓存 300 个，超过时销毁
    ///
    /// ════════════════════════════════════════════════════════════
    /// 【运行时字段详解】
    /// ════════════════════════════════════════════════════════════
    ///   m_pools : Dictionary&lt;Type, PoolEntry&gt;
    ///     - 类型 → 池条目映射，每种子类独立池
    ///     - 预分配容量 8，避免运行时扩容
    ///     - 避免类型混存导致回收错误
    ///
    ///   m_activeObjects : List&lt;SimpleObjectBase&gt;
    ///     - 所有活跃实例（不论类型），统一 Tick 驱动
    ///     - 预分配容量 128，按需扩容
    ///     - 多态分发到各子类的 Tick 实现
    ///
    ///   m_poolRoot : Transform
    ///     - 池根节点，所有池化实例的父对象
    ///     - 保持 Hierarchy 整洁
    ///
    ///   PoolEntry（内部类）：
    ///     - Type  — 池管理的类型
    ///     - Prefab — 预制体引用
    ///     - Pool  — ObjectPool&lt;SimpleObjectBase&gt; 实例
    ///
    /// ════════════════════════════════════════════════════════════
    /// 【Unity 生命周期】
    /// ════════════════════════════════════════════════════════════
    ///   Awake：
    ///     - 单例初始化（重复实例销毁）
    ///     - 创建池根节点 SimpleObjectPool_Root
    ///
    ///   Start（async void）：
    ///     - 调用 InitializePoolsAsync() 异步初始化
    ///     - 自动从 Addressables 加载标记 PoolObjects label 的预制体
    ///     - 从预制体挂载的 SimpleObjectBase 组件推断类型
    ///     - 自动注册并预热 k_defaultPrewarmCount 个实例
    ///
    ///   Update：
    ///     - 单点批量驱动所有活跃对象的 Tick
    ///     - 逆序遍历 + swap-remove，处理 Tick/OnTriggerEnter 中标记的 Deactivate
    ///     - 防御性：池外销毁或意外 null 的实例会被移除
    ///     - 仍活跃则推进 Tick（多态分发到子类）
    ///     - !IsAlive 的实例从列表移除并归还对应池
    ///
    ///   OnDestroy：
    ///     - 清理 s_instance
    ///     - 清空 m_activeObjects 和 m_pools
    ///
    /// ════════════════════════════════════════════════════════════
    /// 【对外 API】
    /// ════════════════════════════════════════════════════════════
    ///   RegisterPrefab&lt;T&gt;(prefab, prewarm)：
    ///     - 运行时注册 prefab（可在场景引导脚本或子类静态初始化时调用）
    ///     - 重复注册同类型会覆盖旧池（需调用者自行避免在活跃期间重复注册）
    ///     - prefab 根节点需挂载 T 组件
    ///
    ///   Launch&lt;T&gt;(position, direction, owner, damage, damageType)：
    ///     - 从池中取出指定类型的对象并初始化
    ///     - 未注册时返回 null 并输出错误日志
    ///     - 池返回 null 时输出错误（prefab 可能未挂载组件）
    ///     - 取出后调用 Initialize 完成初始化
    ///
    ///   ActiveCount：当前活跃对象总数量（所有类型合计）
    ///
    ///   IsRegistered&lt;T&gt;()：指定类型的已注册状态
    ///
    /// ════════════════════════════════════════════════════════════
    /// 【异步初始化流程】
    /// ════════════════════════════════════════════════════════════
    ///   InitializePoolsAsync() 完整流程：
    ///     1. 通过 Addressables.LoadAssetsAsync 加载标记 PoolObjects label 的所有预制体
    ///        - 使用 MergeMode.Union 合并多个 label 的结果
    ///     2. 加载失败时输出错误日志并释放句柄
    ///     3. 加载结果为空时输出警告并释放句柄
    ///     4. 遍历所有预制体：
    ///        - 获取 SimpleObjectBase 组件推断类型
    ///        - 未挂载组件的预制体跳过并输出警告
    ///        - 已注册的类型跳过（避免重复注册）
    ///        - 调用 RegisterPrefabInternal 注册并预热
    ///     5. 释放加载句柄（预制体实例由各自池管理，不在此释放）
    ///
    /// ════════════════════════════════════════════════════════════
    /// 【池回调详解】
    /// ════════════════════════════════════════════════════════════
    ///   CreateInstance(type, prefab)：
    ///     - Instantiate prefab 到 m_poolRoot 下
    ///     - 获取指定类型的 SimpleObjectBase 组件
    ///     - 未挂载组件时销毁实例并输出错误
    ///     - SetActive(false) 后返回实例
    ///
    ///   OnObjectGet(obj)：
    ///     - 调用 obj.OnGetFromPool() 重置状态
    ///     - 加入 m_activeObjects 列表（Contains 检查避免重复添加）
    ///
    ///   OnObjectRelease(obj)：
    ///     - 调用 obj.OnReleaseToPool() 清理状态
    ///     - 活跃列表移除在 Update 的 swap-remove 中完成，此处不重复
    ///
    ///   OnObjectDestroy(obj)：
    ///     - 销毁 GameObject（池超过 maxSize 时调用）
    ///
    ///   ReleaseToPool(obj)：
    ///     - 按实例运行时类型查找对应池
    ///     - 找到则 Release 归还
    ///     - 找不到（类型未注册或已注销）直接销毁避免泄漏
    ///
    /// ════════════════════════════════════════════════════════════
    /// 【预热机制】
    /// ════════════════════════════════════════════════════════════
    ///   PrewarmPool(entry, count)：
    ///     - 通过 Get + Release 循环创建并归还 count 个实例
    ///     - Prewarm 期间 Get 会加入活跃列表
    ///     - Update 未运行无法 swap-remove，此处统一清空 m_activeObjects
    ///     - 预热后的实例进入池的空闲栈，运行时 Get 直接复用
    ///
    /// ════════════════════════════════════════════════════════════
    /// 【swap-remove 优化】
    /// ════════════════════════════════════════════════════════════
    ///   SwapRemoveAt(index)：
    ///     - 将末尾元素移到 index 处并移除末尾，O(1)
    ///     - 逆序遍历中，末尾元素已处理过，因此无需重新 Tick
    ///     - 比传统的 RemoveAt（O(N) 元素移动）更高效
    ///
    /// ════════════════════════════════════════════════════════════
    /// 【性能说明（多类型共管不影响性能）】
    /// ════════════════════════════════════════════════════════════
    ///   - 单 MonoBehaviour.Update 单点驱动，消除 N 个 Pool 的 native 边界开销
    ///   - List&lt;SimpleObjectBase&gt; 存引用，类型混存不恶化 CPU cache（本就不连续）
    ///   - Tick 是虚方法分发，1000 实例约 1μs，可忽略
    ///   - 真正瓶颈（Rigidbody 物理/Collider 触发/池 Get/Release）只与活跃实例数有关
    ///   - 逆序遍历 + swap-remove，O(1) 移除，避免 List.RemoveAt 的 O(N) 移动
    ///
    /// ════════════════════════════════════════════════════════════
    /// 【回收时序】
    /// ════════════════════════════════════════════════════════════
    ///   1. 子类 Tick/OnTriggerEnter 仅调 SimpleObjectBase.Deactivate() 设置 m_isAlive=false
    ///   2. 本类 Update 逆序遍历检测 !IsAlive
    ///   3. swap-remove 从 m_activeObjects 移除
    ///   4. ReleaseToPool 归还对应池
    ///   注意：子类不直接调用 Pool.Release，避免在物理回调中修改列表导致迭代冲突
    ///
    /// ────────────────────────────────────────────────────────────
    /// 示例 1：Addressables 配置（自动注册）
    /// ────────────────────────────────────────────────────────────
    /// <code>
    /// // 1. 创建预制体（如 Bullet.prefab），根节点挂载 Bullet 组件
    /// // 2. 在 Addressables Groups 窗口将预制体标记为 Addressable
    /// // 3. 为预制体添加 "PoolObjects" label
    /// // 4. 场景中创建一个 GameObject 挂载 SimpleObjectPool 组件
    /// // 5. 运行时 SimpleObjectPool.Start 自动注册并预热 10 个实例
    /// </code>
    ///
    /// ────────────────────────────────────────────────────────────
    /// 示例 2：运行时手动注册
    /// ────────────────────────────────────────────────────────────
    /// <code>
    /// // 在场景引导脚本中
    /// public class SceneBootstrap : MonoBehaviour
    /// {
    ///     public GameObject arrowPrefab;
    ///
    ///     private IEnumerator Start()
    ///     {
    ///         // 等待 SimpleObjectPool 初始化完成
    ///         yield return new WaitUntil(() =&gt; SimpleObjectPool.Instance != null);
    ///
    ///         // 手动注册 Arrow 类型，预热 20 个
    ///         SimpleObjectPool.Instance.RegisterPrefab&lt;Arrow&gt;(arrowPrefab, prewarm: 20);
    ///     }
    /// }
    /// </code>
    ///
    /// ────────────────────────────────────────────────────────────
    /// 示例 3：发射子弹
    /// ────────────────────────────────────────────────────────────
    /// <code>
    /// // 在 Fighter 或武器脚本中
    /// public class Fighter : ObjectBase
    /// {
    ///     private void FireBullet()
    ///     {
    ///         // 伤害由 owner 的 ObjectStatsConfig 自动克隆为快照，命中时走完整伤害公式
    ///         SimpleObjectPool.Instance.Launch&lt;Bullet&gt;(
    ///             position: transform.position + Vector3.up * 1.5f,
    ///             direction: transform.forward,
    ///             owner: this
    ///         );
    ///     }
    /// }
    /// </code>
    ///
    /// ────────────────────────────────────────────────────────────
    /// 示例 4：查找最近敌方并发射（配合 TargetRegistry）
    /// ────────────────────────────────────────────────────────────
    /// <code>
    /// public class Turret : ObjectBase
    /// {
    ///     private void Update()
    ///     {
    ///         ObjectBase target = TargetRegistry.FindNearest(transform.position, this);
    ///         if (target == null) return;
    ///
    ///         Vector3 dir = (target.transform.position - transform.position).normalized;
    ///         SimpleObjectPool.Instance.Launch&lt;Bullet&gt;(
    ///             position: transform.position,
    ///             direction: dir,
    ///             owner: this
    ///         );
    ///     }
    /// }
    /// </code>
    ///
    /// ────────────────────────────────────────────────────────────
    /// 示例 5：扩展新的池化类型
    /// ────────────────────────────────────────────────────────────
    /// <code>
    /// // 1. 定义子类
    /// public class Arrow : SimpleObjectBase
    /// {
    ///     protected override void SetupCollider() { /* CapsuleCollider */ }
    ///     public override void Tick(float dt) { /* 寿命+下坠检测 */ }
    ///     protected override void ConfigureParameters()
    ///     {
    ///         m_speed = 12f;
    ///         m_maxDistanceSqr = 20f * 20f;
    ///     }
    /// }
    ///
    /// // 2. 预制体标记 "PoolObjects" label
    /// // 3. 自动注册后即可发射
    /// SimpleObjectPool.Instance.Launch&lt;Arrow&gt;(pos, dir, owner);
    /// </code>
    ///
    /// ────────────────────────────────────────────────────────────
    /// 示例 6：查询池状态
    /// ────────────────────────────────────────────────────────────
    /// <code>
    /// // 当前活跃对象总数（所有类型合计）
    /// int activeCount = SimpleObjectPool.Instance.ActiveCount;
    ///
    /// // 检查类型是否已注册
    /// bool registered = SimpleObjectPool.Instance.IsRegistered&lt;Bullet&gt;();
    /// </code>
    /// </summary>
}