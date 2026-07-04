using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Pool;
using AFrameWork.Core;
using AFrameWork.Core.SmallBase;

namespace AFrameWork.Sample
{
    /// <summary>
    /// 投射物管理器（方案 A 对象池 + 方案 E 批量 Update）。
    ///
    /// 职责：
    ///   1. 持有 ObjectPool&lt;Bullet&gt;，预热 + 复用，消除 Instantiate/Destroy GC
    ///   2. 单点 Update 批量驱动所有活跃子弹的 Tick(deltaTime)，消除每子弹 MonoBehaviour.Update 的 native 边界开销
    ///   3. 对外提供 FireBullet(...) API
    ///
    /// 使用方式：
    ///   - 在场景中创建一个 GameObject 挂载 ProjectileManager（单例）
    ///   - Inspector 中拖入 Bullet 预制体
    ///   - 调用 ProjectileManager.Instance.FireBullet(pos, dir, owner, damage) 发射
    ///
    /// 回收时序（避免迭代中修改列表）：
    ///   - Bullet.Tick / OnTriggerEnter 仅调用 ProjectileBase.Release() 设置 m_isActive=false
    ///   - 本类 Update 逆序遍历，对 !IsActive 的子弹执行 swap-remove + pool.Release
    /// </summary>
    public class ProjectileManager : MonoBehaviour
    {
        #region 单例

        private static ProjectileManager s_instance;

        /// <summary>全局单例。场景中需存在一个挂载 ProjectileManager 的 GameObject。</summary>
        public static ProjectileManager Instance
        {
            get
            {
                if (s_instance == null)
                {
                    s_instance = FindObjectOfType<ProjectileManager>();
                }
                return s_instance;
            }
        }

        #endregion

        #region Inspector 配置

        [Header("子弹池配置")]
        [Tooltip("Bullet 预制体（需挂载 Bullet 组件）")]
        [SerializeField]
        private GameObject m_bulletPrefab;

        [Tooltip("预热数量（场景加载时预先创建的子弹数）")]
        [SerializeField]
        private int m_prewarmCount = 30;

        [Tooltip("池最大容量（超过时销毁而非回收）")]
        [SerializeField]
        private int m_maxPoolSize = 300;

        #endregion

        #region 运行时字段

        // 对象池（UnityEngine.Pool.ObjectPool）
        private ObjectPool<Bullet> m_bulletPool;

        // 活跃子弹列表（被 Tick 驱动的子弹）
        // 初始容量 128，减少运行中扩容次数
        private readonly List<Bullet> m_activeBullets = new List<Bullet>(128);

        // 池根节点（所有池化子弹的父对象，保持 Hierarchy 整洁）
        private Transform m_poolRoot;

        #endregion

        #region Unity 生命周期

        private void Awake()
        {
            // 单例初始化（允许多场景中存在一个）
            if (s_instance != null && s_instance != this)
            {
                Debug.LogWarning($"[{GetType().Name}] 已存在实例，重复实例将被销毁。", this);
                Destroy(gameObject);
                return;
            }
            s_instance = this;

            // 创建池根节点
            m_poolRoot = new GameObject("BulletPool_Root").transform;
            m_poolRoot.SetParent(transform, false);

            // 创建对象池
            m_bulletPool = new ObjectPool<Bullet>(
                createFunc: CreateBulletInstance,
                actionOnGet: OnBulletGet,
                actionOnRelease: OnBulletRelease,
                actionOnDestroy: OnBulletDestroy,
                collectionCheck: true,
                defaultCapacity: 16,
                maxSize: m_maxPoolSize);
        }

        private void Start()
        {
            // 预热池：创建 -> 归还，使池中常备 m_prewarmCount 颗子弹
            PrewarmPool();
        }

        private void OnDestroy()
        {
            if (s_instance == this)
            {
                s_instance = null;
            }
            m_activeBullets.Clear();
        }

        /// <summary>
        /// 单点批量 Update：驱动所有活跃子弹的 Tick。
        /// 逆序遍历 + swap-remove，处理 Tick/OnTriggerEnter 中标记的 Release。
        /// </summary>
        private void Update()
        {
            float deltaTime = Time.deltaTime;

            // 逆序遍历：swap-remove 时将末尾元素移到当前索引，逆序保证被移过来的元素已处理过
            for (int i = m_activeBullets.Count - 1; i >= 0; i--)
            {
                Bullet bullet = m_activeBullets[i];

                // 防御性：池外销毁或意外 null
                if (bullet == null)
                {
                    SwapRemoveAt(i);
                    continue;
                }

                // 仍活跃则推进 Tick
                if (bullet.IsActive)
                {
                    bullet.Tick(deltaTime);
                }

                // Tick 或物理回调已请求回收 → 从列表移除并归还池
                if (!bullet.IsActive)
                {
                    SwapRemoveAt(i);
                    m_bulletPool.Release(bullet);
                }
            }
        }

        #endregion

        #region 对外 API

        /// <summary>
        /// 发射一颗子弹。
        /// </summary>
        /// <param name="position">发射位置</param>
        /// <param name="direction">飞行方向（会归一化）</param>
        /// <param name="owner">发射者（用于继承阵营与穿透属性）</param>
        /// <param name="damage">伤害值（建议传入 owner 的攻击力）</param>
        /// <param name="damageType">伤害类型</param>
        /// <returns>发射的 Bullet；池满或未配置时返回 null</returns>
        public Bullet FireBullet(
            Vector3 position,
            Vector3 direction,
            ObjectBase owner,
            float damage,
            DamageType damageType = DamageType.Physical)
        {
            if (m_bulletPool == null)
            {
                Debug.LogError($"[{GetType().Name}] 对象池未初始化，无法发射子弹。", this);
                return null;
            }

            Bullet bullet = m_bulletPool.Get();
            bullet.Initialize(position, direction, owner, damage, damageType);
            return bullet;
        }

        /// <summary>当前活跃飞行中的子弹数量。</summary>
        public int ActiveCount => m_activeBullets.Count;

        #endregion

        #region 对象池回调

        private Bullet CreateBulletInstance()
        {
            if (m_bulletPrefab == null)
            {
                Debug.LogError($"[{GetType().Name}] m_bulletPrefab 未配置！", this);
                return null;
            }

            // 实例化到池根节点下
            GameObject obj = Instantiate(m_bulletPrefab, m_poolRoot);
            Bullet bullet = obj.GetComponent<Bullet>();
            if (bullet == null)
            {
                bullet = obj.AddComponent<Bullet>();
            }
            // 初始为非活跃状态，等待 OnGetFromPool 激活
            obj.SetActive(false);
            return bullet;
        }

        private void OnBulletGet(Bullet bullet)
        {
            if (bullet == null) return;
            bullet.OnGetFromPool();
            // 加入活跃列表（若不在其中）
            if (!m_activeBullets.Contains(bullet))
            {
                m_activeBullets.Add(bullet);
            }
        }

        private void OnBulletRelease(Bullet bullet)
        {
            if (bullet == null) return;
            bullet.OnReleaseToPool();
            // 注意：从 m_activeBullets 移除在 Update 的 swap-remove 中已完成，
            // 此处不重复移除（避免 Release 路径下的二次 O(N) 查找）
        }

        private void OnBulletDestroy(Bullet bullet)
        {
            if (bullet != null && bullet.gameObject != null)
            {
                Destroy(bullet.gameObject);
            }
        }

        #endregion

        #region 内部工具

        /// <summary>预热池：创建 N 颗子弹后立即归还，使池内常备实例。</summary>
        private void PrewarmPool()
        {
            if (m_bulletPool == null || m_bulletPrefab == null) return;

            // 使用临时列表暂存 Get 出来的子弹
            List<Bullet> temp = new List<Bullet>(m_prewarmCount);
            for (int i = 0; i < m_prewarmCount; i++)
            {
                Bullet b = m_bulletPool.Get();
                if (b != null) temp.Add(b);
            }
            // 全部归还（OnReleaseToPool 会 SetActive(false)）
            for (int i = 0; i < temp.Count; i++)
            {
                m_bulletPool.Release(temp[i]);
            }
            // Prewarm 期间 Get 会把子弹加入 m_activeBullets，但 Update 尚未运行无法 swap-remove。
            // 此处统一清空，保证预热后活跃列表为空（子弹全部在池中待命）。
            m_activeBullets.Clear();
        }

        /// <summary>
        /// swap-remove：将末尾元素移到 index 处并移除末尾，O(1)。
        /// 逆序遍历中，末尾元素已处理过，因此无需重新 Tick。
        /// </summary>
        private void SwapRemoveAt(int index)
        {
            int last = m_activeBullets.Count - 1;
            if (index != last)
            {
                m_activeBullets[index] = m_activeBullets[last];
            }
            m_activeBullets.RemoveAt(last);
        }

        #endregion
    }
}