using System;
using System.Collections.Generic;
using UnityEngine;

namespace AFrameWork.GameUI
{
    /// <summary>
    /// GPU 实例化血条数据结构 — 与着色器中的 HealthBarInstance 结构严格对应 (48 bytes)
    /// </summary>
    [Serializable]
    public struct HealthBarInstance
    {
        /// <summary>xyz = 世界坐标(含头部偏移), w = 填充百分比(0-1)</summary>
        public Vector4 position;

        /// <summary>rgba 填充颜色</summary>
        public Vector4 color;

        /// <summary>x = 宽度, y = 高度, z = 未使用, w = 可见性(1=可见, 0=隐藏)</summary>
        public Vector4 size;
    }

    /// <summary>
    /// GPU 实例化血条管理器 — 使用 DrawMeshInstancedIndirect 在单次 Draw Call 中渲染上千个血条。
    /// 适用于大规模战斗场景 (1000+ 角色)，与现有 HealthBarController (UI Toolkit) 可共存。
    /// 使用 [DefaultExecutionOrder] 确保在 Cinemachine Brain (LateUpdate) 之后执行，
    /// 消除相机移动时的帧时序错位（抖动/忽大忽小）。
    /// </summary>
    [DefaultExecutionOrder(10000)]
    public class HealthBarGPUInstanced : MonoBehaviour
    {
        // ══════════════════════════════════════════════════════════════════════════
        // 常量
        // ══════════════════════════════════════════════════════════════════════════

        private const int k_defaultCapacity = 2000;
        private const int k_instanceStride = 48;  // 3 × Vector4 = 48 bytes
        private const int k_quadIndexCount = 6;

        private static readonly Bounds k_largeBounds =
            new Bounds(Vector3.zero, Vector3.one * 10000f);

        // ══════════════════════════════════════════════════════════════════════════
        // 配置
        // ══════════════════════════════════════════════════════════════════════════

        [Tooltip("初始容量 (预分配数组大小)")]
        [SerializeField]
        private int m_initialCapacity = k_defaultCapacity;

        [Tooltip("血条宽度 (世界单位)")]
        [SerializeField]
        private float m_barWidth = 5.0f;

        [Tooltip("血条高度 (世界单位)")]
        [SerializeField]
        private float m_barHeight = 1.0f;

        [Tooltip("最大渲染距离 (超出此距离的血条自动隐藏)")]
        [SerializeField]
        private float m_maxRenderDistance = 200f;

        [Tooltip("填充条平滑过渡速度")]
        [SerializeField]
        private float m_fillSpeed = 5f;

        [Tooltip("正常血量颜色")]
        [SerializeField]
        private Color m_normalColor = new Color(220f / 255f, 50f / 255f, 50f / 255f, 1f);

        [Tooltip("低血量颜色")]
        [SerializeField]
        private Color m_lowColor = new Color(1f, 165f / 255f, 0f, 1f);

        [Tooltip("危急血量颜色")]
        [SerializeField]
        private Color m_criticalColor = new Color(1f, 0f, 0f, 1f);

        [Tooltip("低血量阈值 (0-1)")]
        [Range(0f, 1f)]
        [SerializeField]
        private float m_lowThreshold = 0.3f;

        [Tooltip("危急血量阈值 (0-1)")]
        [Range(0f, 1f)]
        [SerializeField]
        private float m_criticalThreshold = 0.1f;

        // ══════════════════════════════════════════════════════════════════════════
        // GPU 实例数据
        // ══════════════════════════════════════════════════════════════════════════

        private HealthBarInstance[] m_instanceData;
        private ComputeBuffer m_instanceBuffer;
        private ComputeBuffer m_argsBuffer;
        private uint[] m_args;

        // ══════════════════════════════════════════════════════════════════════════
        // CPU 侧追踪数据
        // ══════════════════════════════════════════════════════════════════════════

        private Transform[] m_targets;
        private float[] m_headOffsets;
        private float[] m_currentHealth;
        private float[] m_maxHealth;
        private float[] m_displayFill;

        // ══════════════════════════════════════════════════════════════════════════
        // 渲染资源
        // ══════════════════════════════════════════════════════════════════════════

        private Mesh m_quadMesh;
        private Material m_material;
        private Camera m_camera;

        // ══════════════════════════════════════════════════════════════════════════
        // ID 回收
        // ══════════════════════════════════════════════════════════════════════════

        private Stack<int> m_freeIds;
        private int m_count;
        private int m_capacity;

        // ══════════════════════════════════════════════════════════════════════════
        // 属性
        // ══════════════════════════════════════════════════════════════════════════

        /// <summary>当前注册的血条数量 (含不可见的回收槽位)</summary>
        public int RegisteredCount => m_count;

        /// <summary>当前容量</summary>
        public int Capacity => m_capacity;

        // ══════════════════════════════════════════════════════════════════════════
        // MonoBehaviour
        // ══════════════════════════════════════════════════════════════════════════

        private void Awake()
        {
            m_capacity = Mathf.Max(m_initialCapacity, 1);

            m_instanceData = new HealthBarInstance[m_capacity];
            m_targets = new Transform[m_capacity];
            m_headOffsets = new float[m_capacity];
            m_currentHealth = new float[m_capacity];
            m_maxHealth = new float[m_capacity];
            m_displayFill = new float[m_capacity];
            m_freeIds = new Stack<int>(m_capacity / 4);

            m_quadMesh = CreateQuadMesh();

            Shader shader = Shader.Find("AFrameWork/GameUI/HealthBarGPU");
            if (shader == null)
            {
                // Debug.LogError($"[{GetType().Name}] HealthBarGPU shader not found!", this);
                enabled = false;
                return;
            }
            m_material = new Material(shader);

            m_instanceBuffer = new ComputeBuffer(m_capacity, k_instanceStride);

            m_args = new uint[5] { k_quadIndexCount, 0, 0, 0, 0 };
            m_argsBuffer = new ComputeBuffer(1, 5 * sizeof(uint), ComputeBufferType.IndirectArguments);
            m_argsBuffer.SetData(m_args);
        }

        private void Start()
        {
            m_camera = Camera.main;
            if (m_camera == null)
            {
                // Debug.LogWarning($"[{GetType().Name}] Camera.main not found in Start(), will lazy-bind in LateUpdate", this);
            }
        }

        private void OnDestroy()
        {
            m_instanceBuffer?.Release();
            m_argsBuffer?.Release();
            if (m_material != null) Destroy(m_material);
            if (m_quadMesh != null) Destroy(m_quadMesh);
        }

        /// <summary>
        /// 在 LateUpdate 中渲染血条。
        /// 通过 [DefaultExecutionOrder(10000)] 确保在 Cinemachine Brain 之后执行，
        /// 此时相机 transform 已是最终状态，消除 CPU 裁剪与 GPU UNITY_MATRIX_V 的帧时序错位。
        /// </summary>
        private void LateUpdate()
        {
            if (m_count == 0) return;

            if (m_camera == null)
            {
                m_camera = Camera.main;
                if (m_camera == null) return;
            }

            Vector3 camPos = m_camera.transform.position;
            Vector3 camFwd = m_camera.transform.forward;
            float maxDistSq = m_maxRenderDistance * m_maxRenderDistance;
            float dt = Time.deltaTime;

            for (int i = 0; i < m_count; i++)
            {
                // 跳过回收槽位
                if (m_targets[i] == null)
                {
                    m_instanceData[i].size.w = 0f;
                    continue;
                }

                Vector3 tpos = m_targets[i].position;

                // 视锥裁剪 (相机背面)
                Vector3 toTarget = tpos - camPos;
                if (Vector3.Dot(camFwd, toTarget) < 0f)
                {
                    m_instanceData[i].size.w = 0f;
                    continue;
                }

                // 距离 LOD
                if (toTarget.sqrMagnitude > maxDistSq)
                {
                    m_instanceData[i].size.w = 0f;
                    continue;
                }

                // 更新位置 (含头部偏移)
                m_instanceData[i].position.x = tpos.x;
                m_instanceData[i].position.y = tpos.y + m_headOffsets[i];
                m_instanceData[i].position.z = tpos.z;

                // 平滑填充过渡
                float targetFill = m_maxHealth[i] > 0
                    ? m_currentHealth[i] / m_maxHealth[i]
                    : 0f;
                m_displayFill[i] = Mathf.MoveTowards(
                    m_displayFill[i], targetFill, m_fillSpeed * dt);
                m_instanceData[i].position.w = m_displayFill[i];

                // 标记可见
                m_instanceData[i].size.w = 1f;
            }

            // 上传 GPU
            m_instanceBuffer.SetData(m_instanceData);
            m_args[1] = (uint)m_count;
            m_argsBuffer.SetData(m_args);
            m_material.SetBuffer("_InstanceBuffer", m_instanceBuffer);

            // 单次 Draw Call 渲染所有血条
            Graphics.DrawMeshInstancedIndirect(
                m_quadMesh, 0, m_material, k_largeBounds, m_argsBuffer);
        }

        // ══════════════════════════════════════════════════════════════════════════
        // 公共 API
        // ══════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// 注册血条并绑定到目标对象
        /// </summary>
        /// <param name="target">目标 Transform</param>
        /// <param name="headOffset">头部偏移 (世界坐标 Y 轴)</param>
        /// <returns>血条 ID (用于后续更新/注销), -1 表示失败</returns>
        public int Register(Transform target, float headOffset = 2f)
        {
            if (target == null) return -1;

            int id;
            if (m_freeIds.Count > 0)
            {
                id = m_freeIds.Pop();
            }
            else
            {
                if (m_count >= m_capacity)
                {
                    ExpandCapacity();
                }
                id = m_count++;
            }

            m_targets[id] = target;
            m_headOffsets[id] = headOffset;
            m_currentHealth[id] = 100f;
            m_maxHealth[id] = 100f;
            m_displayFill[id] = 1f;

            m_instanceData[id] = new HealthBarInstance
            {
                position = new Vector4(
                    target.position.x,
                    target.position.y + headOffset,
                    target.position.z,
                    1f),
                color = m_normalColor,
                size = new Vector4(m_barWidth, m_barHeight, 0f, 1f)
            };

            return id;
        }

        /// <summary>
        /// 注销血条 (回收 ID 供复用)
        /// </summary>
        /// <param name="id">血条 ID</param>
        public void Unregister(int id)
        {
            if (id < 0 || id >= m_capacity) return;

            m_targets[id] = null;
            m_instanceData[id].size.w = 0f;
            m_freeIds.Push(id);
        }

        /// <summary>
        /// 更新血量值 (自动切换颜色)
        /// </summary>
        /// <param name="id">血条 ID</param>
        /// <param name="currentHealth">当前血量</param>
        /// <param name="maxHealth">最大血量</param>
        public void UpdateHealth(int id, float currentHealth, float maxHealth)
        {
            if (id < 0 || id >= m_capacity || m_targets[id] == null) return;

            m_currentHealth[id] = currentHealth;
            m_maxHealth[id] = maxHealth;

            float pct = maxHealth > 0 ? currentHealth / maxHealth : 0f;

            if (pct <= m_criticalThreshold)
                m_instanceData[id].color = (Vector4)m_criticalColor;
            else if (pct <= m_lowThreshold)
                m_instanceData[id].color = (Vector4)m_lowColor;
            else
                m_instanceData[id].color = (Vector4)m_normalColor;
        }

        /// <summary>
        /// 设置血条尺寸 (世界单位)
        /// </summary>
        /// <param name="id">血条 ID</param>
        /// <param name="width">宽度</param>
        /// <param name="height">高度</param>
        public void SetSize(int id, float width, float height)
        {
            if (id < 0 || id >= m_capacity) return;
            m_instanceData[id].size.x = width;
            m_instanceData[id].size.y = height;
        }

        /// <summary>
        /// 清除所有血条
        /// </summary>
        public void ClearAll()
        {
            for (int i = 0; i < m_count; i++)
            {
                m_targets[i] = null;
                m_instanceData[i].size.w = 0f;
            }
            m_freeIds.Clear();
            m_count = 0;
        }

        // ══════════════════════════════════════════════════════════════════════════
        // 私有方法
        // ══════════════════════════════════════════════════════════════════════════

        private void ExpandCapacity()
        {
            int newCap = m_capacity * 2;

            Array.Resize(ref m_instanceData, newCap);
            Array.Resize(ref m_targets, newCap);
            Array.Resize(ref m_headOffsets, newCap);
            Array.Resize(ref m_currentHealth, newCap);
            Array.Resize(ref m_maxHealth, newCap);
            Array.Resize(ref m_displayFill, newCap);

            m_instanceBuffer?.Release();
            m_instanceBuffer = new ComputeBuffer(newCap, k_instanceStride);

            m_capacity = newCap;
        }

        private static Mesh CreateQuadMesh()
        {
            Mesh mesh = new Mesh();
            mesh.vertices = new Vector3[]
            {
                new Vector3(-0.5f, -0.5f, 0f),
                new Vector3(0.5f, -0.5f, 0f),
                new Vector3(0.5f, 0.5f, 0f),
                new Vector3(-0.5f, 0.5f, 0f)
            };
            mesh.uv = new Vector2[]
            {
                new Vector2(0f, 0f),
                new Vector2(1f, 0f),
                new Vector2(1f, 1f),
                new Vector2(0f, 1f)
            };
            mesh.triangles = new int[] { 0, 2, 1, 0, 3, 2 };
            mesh.RecalculateBounds();
            return mesh;
        }
    }

    /// <summary>
    /// HealthBarGPUInstanced 使用说明：
    /// ============================================================
    /// GPU 实例化血条管理器 — 使用 DrawMeshInstancedIndirect 在单次 Draw Call 中渲染上千个血条。
    /// 适用于大规模战斗场景 (1000+ 角色)，与现有 HealthBarController (UI Toolkit) 可共存。
    ///
    /// ════════════════════════════════════════════════════════════
    /// 【核心特性】
    /// ════════════════════════════════════════════════════════════
    ///   - 单次 Draw Call 渲染所有血条（不论数量）
    ///   - GPU 实例化数据通过 ComputeBuffer 上传（48 bytes/instance）
    ///   - 视锥裁剪 + 距离 LOD 自动隐藏远处血条
    ///   - 平滑填充过渡（Mathf.MoveTowards）
    ///   - 自动颜色切换（正常/低血量/危急）
    ///   - ID 回收机制，支持动态注册/注销
    ///   - [DefaultExecutionOrder(10000)] 确保在 Cinemachine Brain 之后执行
    ///
    /// ════════════════════════════════════════════════════════════
    /// 【HealthBarInstance 数据结构（48 bytes，与 shader 严格对应）】
    /// ════════════════════════════════════════════════════════════
    ///   position : Vector4
    ///     - xyz = 世界坐标(含头部偏移)
    ///     - w = 填充百分比(0-1)
    ///
    ///   color : Vector4
    ///     - rgba 填充颜色
    ///
    ///   size : Vector4
    ///     - x = 宽度, y = 高度, z = 未使用, w = 可见性(1=可见, 0=隐藏)
    ///
    ///   注意：结构体布局必须与 shader 中的 CBUFFER 严格对应，
    ///        否则 GPU 读取数据错位导致血条位置/颜色异常
    ///
    /// ════════════════════════════════════════════════════════════
    /// 【配置字段详解】
    /// ════════════════════════════════════════════════════════════
    ///   m_initialCapacity = 2000
    ///     - 初始容量（预分配数组大小）
    ///     - 超过容量时自动扩容（ExpandCapacity 翻倍）
    ///
    ///   m_barWidth = 5.0f, m_barHeight = 1.0f
    ///     - 血条尺寸（世界单位）
    ///     - 注册时作为默认尺寸，可通过 SetSize 运行时修改
    ///
    ///   m_maxRenderDistance = 200f
    ///     - 最大渲染距离（超出此距离的血条自动隐藏）
    ///     - 使用 sqrMagnitude 比较，避免开方
    ///
    ///   m_fillSpeed = 5f
    ///     - 填充条平滑过渡速度
    ///     - 使用 Mathf.MoveTowards 实现，非线性插值
    ///
    ///   m_normalColor / m_lowColor / m_criticalColor
    ///     - 血量颜色配置（正常红/低血量橙/危急深红）
    ///     - UpdateHealth 时根据阈值自动切换
    ///
    ///   m_lowThreshold = 0.3f, m_criticalThreshold = 0.1f
    ///     - 颜色切换阈值（百分比 0-1）
    ///     - pct &lt;= critical → 危急色，pct &lt;= low → 低血量色，否则正常色
    ///
    /// ════════════════════════════════════════════════════════════
    /// 【GPU 实例数据】
    /// ════════════════════════════════════════════════════════════
    ///   m_instanceBuffer : ComputeBuffer
    ///     - 实例数据缓冲区，容量 = m_capacity，步幅 = 48 bytes
    ///     - LateUpdate 中 SetData 上传所有实例数据
    ///     - 扩容时释放重建
    ///
    ///   m_argsBuffer : ComputeBuffer（IndirectArguments）
    ///     - 间接绘制参数缓冲区，5 个 uint
    ///     - args[0] = 索引数(6，四边形 2 三角形)
    ///     - args[1] = 实例数(m_count)
    ///     - 其余为 0
    ///
    ///   m_instanceData : HealthBarInstance[]
    ///     - CPU 侧实例数据数组
    ///     - LateUpdate 中更新位置/填充/可见性后上传 GPU
    ///
    /// ════════════════════════════════════════════════════════════
    /// 【CPU 侧追踪数据】
    /// ════════════════════════════════════════════════════════════
    ///   m_targets : Transform[]
    ///     - 每个血条绑定的目标 Transform
    ///     - null 表示回收槽位
    ///
    ///   m_headOffsets : float[]
    ///     - 头部偏移（世界坐标 Y 轴）
    ///     - 注册时指定，血条显示在目标头顶
    ///
    ///   m_currentHealth / m_maxHealth : float[]
    ///     - 当前/最大血量
    ///     - UpdateHealth 时更新，用于计算填充百分比
    ///
    ///   m_displayFill : float[]
    ///     - 显示的填充值（平滑过渡用）
    ///     - LateUpdate 中 Mathf.MoveTowards 趋近目标值
    ///
    /// ════════════════════════════════════════════════════════════
    /// 【ID 回收机制】
    /// ════════════════════════════════════════════════════════════
    ///   m_freeIds : Stack&lt;int&gt;
    ///     - 回收的 ID 栈，Unregister 时 push，Register 时 pop
    ///     - 避免频繁扩容，初始容量 = m_capacity / 4
    ///
    ///   m_count : int
    ///     - 已分配的 ID 数量（含已回收的）
    ///     - m_count &gt;= m_capacity 时触发扩容
    ///
    ///   m_capacity : int
    ///     - 当前容量（数组长度）
    ///     - 扩容时翻倍并重建 ComputeBuffer
    ///
    /// ════════════════════════════════════════════════════════════
    /// 【Unity 生命周期】
    /// ════════════════════════════════════════════════════════════
    ///   Awake：
    ///     - 初始化容量（Mathf.Max(m_initialCapacity, 1)）
    ///     - 分配所有数组和 ComputeBuffer
    ///     - 创建四边形 Mesh（CreateQuadMesh）
    ///     - 加载 HealthBarGPU shader 并创建 Material
    ///     - 初始化 args buffer
    ///
    ///   Start：
    ///     - 缓存 Camera.main
    ///     - 为 null 时在 LateUpdate 中延迟绑定
    ///
    ///   LateUpdate（[DefaultExecutionOrder(10000)]）：
    ///     - 通过 [DefaultExecutionOrder(10000)] 确保在 Cinemachine Brain 之后执行
    ///     - 此时相机 transform 已是最终状态，消除 CPU 裁剪与 GPU UNITY_MATRIX_V 的帧时序错位
    ///     - 遍历所有实例：
    ///       1. 跳过回收槽位（m_targets[i] == null）
    ///       2. 视锥裁剪（相机背面隐藏）
    ///       3. 距离 LOD（超出 maxRenderDistance 隐藏）
    ///       4. 更新位置（含头部偏移）
    ///       5. 平滑填充过渡（Mathf.MoveTowards）
    ///       6. 标记可见
    ///     - 上传 GPU 数据（SetData）
    ///     - 单次 DrawMeshInstancedIndirect 渲染
    ///
    ///   OnDestroy：
    ///     - 释放 ComputeBuffer（instanceBuffer + argsBuffer）
    ///     - 销毁 Material 和 Mesh
    ///
    /// ════════════════════════════════════════════════════════════
    /// 【公共 API】
    /// ════════════════════════════════════════════════════════════
    ///   Register(target, headOffset)：
    ///     - 注册血条并绑定到目标对象
    ///     - 返回血条 ID（用于后续更新/注销），-1 表示失败
    ///     - 优先复用回收的 ID，无可用时分配新 ID（必要时扩容）
    ///     - 初始化血量为 100/100，填充为 1
    ///
    ///   Unregister(id)：
    ///     - 注销血条（回收 ID 供复用）
    ///     - 设置 m_targets[id] = null 和 size.w = 0（隐藏）
    ///     - ID push 到 m_freeIds 栈
    ///
    ///   UpdateHealth(id, currentHealth, maxHealth)：
    ///     - 更新血量值（自动切换颜色）
    ///     - 根据 pct 自动设置 normal/low/critical 颜色
    ///     - 平滑过渡在 LateUpdate 中处理
    ///
    ///   SetSize(id, width, height)：
    ///     - 设置血条尺寸（世界单位）
    ///     - 可用于 Boss 血条放大
    ///
    ///   ClearAll()：
    ///     - 清除所有血条，重置计数和 freeIds
    ///
    ///   RegisteredCount / Capacity：查询属性
    ///
    /// ════════════════════════════════════════════════════════════
    /// 【视锥裁剪与距离 LOD】
    /// ════════════════════════════════════════════════════════════
    ///   视锥裁剪（相机背面）：
    ///     - Vector3.Dot(camFwd, toTarget) &lt; 0 → 目标在相机背面
    ///     - 设置 size.w = 0 隐藏（shader 中根据 size.w 丢弃）
    ///
    ///   距离 LOD：
    ///     - toTarget.sqrMagnitude &gt; maxRenderDistance² → 超出距离
    ///     - 设置 size.w = 0 隐藏
    ///     - 使用 sqrMagnitude 避免开方
    ///
    /// ════════════════════════════════════════════════════════════
    /// 【平滑填充过渡】
    /// ════════════════════════════════════════════════════════════
    ///   - targetFill = currentHealth / maxHealth（目标填充值）
    ///   - m_displayFill = Mathf.MoveTowards(m_displayFill, targetFill, fillSpeed × dt)
    ///   - 使用 MoveTowards 而非 Lerp，确保过渡时间可预测
    ///   - 过渡结果写入 position.w（shader 中作为填充百分比）
    ///
    /// ════════════════════════════════════════════════════════════
    /// 【扩容机制】
    /// ════════════════════════════════════════════════════════════
    ///   ExpandCapacity()：
    ///     - 新容量 = m_capacity × 2
    ///     - Array.Resize 扩展所有 CPU 侧数组
    ///     - 释放旧 ComputeBuffer，创建新 ComputeBuffer
    ///     - 更新 m_capacity
    ///     - 注意：扩容时 GPU 数据会丢失，但 LateUpdate 会重新上传
    ///
    /// ════════════════════════════════════════════════════════════
    /// 【四边形 Mesh 创建】
    /// ════════════════════════════════════════════════════════════
    ///   CreateQuadMesh()：
    ///     - 创建 1×1 四边形（4 顶点，2 三角形，6 索引）
    ///     - 顶点：(-0.5,-0.5), (0.5,-0.5), (0.5,0.5), (-0.5,0.5)
    ///     - UV：(0,0), (1,0), (1,1), (0,1)
    ///     - 三角形：0,2,1, 0,3,2（逆时针）
    ///     - RecalculateBounds 确保包围盒正确
    ///
    /// ════════════════════════════════════════════════════════════
    /// 【性能优化】
    /// ════════════════════════════════════════════════════════════
    ///   - 单次 Draw Call 渲染所有血条（DrawMeshInstancedIndirect）
    ///   - ComputeBuffer 批量上传，避免逐个 SetVector
    ///   - 视锥裁剪和距离 LOD 减少不必要的实例渲染
    ///   - 使用 sqrMagnitude 避免开方
    ///   - [DefaultExecutionOrder(10000)] 消除相机移动时的帧时序错位
    ///   - ID 回收机制避免频繁扩容
    ///   - 与 HealthBarController (UI Toolkit) 可共存，按需选择
    ///
    /// ════════════════════════════════════════════════════════════
    /// 【与 HealthBarController 的对比】
    /// ════════════════════════════════════════════════════════════
    ///   HealthBarGPUInstanced（本类）：
    ///     - 渲染方式：DrawMeshInstancedIndirect（GPU 实例化）
    ///     - 适用场景：1000+ 角色的大规模战斗
    ///     - 性能：单 Draw Call，CPU 开销低
    ///     - 限制：无 UI 交互（无法点击），需要自定义 shader
    ///
    ///   HealthBarController（UI Toolkit）：
    ///     - 渲染方式：UI Toolkit VisualElement
    ///     - 适用场景：少量角色（&lt;100），需要 UI 交互
    ///     - 性能：每个血条一个 VisualElement，1000+ 时性能下降
    ///     - 优势：支持 USS 样式、动画、交互
    ///
    /// ────────────────────────────────────────────────────────────
    /// 示例 1：基本设置
    /// ────────────────────────────────────────────────────────────
    /// <code>
    /// // 1. 场景中创建 GameObject 挂载 HealthBarGPUInstanced
    /// // 2. Inspector 中配置参数（容量、尺寸、颜色等）
    /// // 3. 确保有 "AFrameWork/GameUI/HealthBarGPU" shader
    /// // 4. 注册血条：
    /// HealthBarGPUInstanced manager = FindObjectOfType&lt;HealthBarGPUInstanced&gt;();
    /// int id = manager.Register(targetTransform, headOffset: 2f);
    /// </code>
    ///
    /// ────────────────────────────────────────────────────────────
    /// 示例 2：在 Monster 中集成
    /// ────────────────────────────────────────────────────────────
    /// <code>
    /// public class Monster : ObjectBase
    /// {
    ///     private HealthBarGPUInstanced m_gpuHealthBarManager;
    ///     private int m_gpuHealthBarId = -1;
    ///
    ///     private void InitializeHealthBar()
    ///     {
    ///         m_gpuHealthBarManager = FindObjectOfType&lt;HealthBarGPUInstanced&gt;();
    ///         if (m_gpuHealthBarManager == null) return;
    ///
    ///         m_gpuHealthBarId = m_gpuHealthBarManager.Register(transform, 3.0f);
    ///         if (m_gpuHealthBarId &gt;= 0)
    ///         {
    ///             m_gpuHealthBarManager.UpdateHealth(m_gpuHealthBarId, GetCurrentHealth(), GetMaxHealth());
    ///         }
    ///     }
    ///
    ///     protected override void OnDamaged(float damage)
    ///     {
    ///         if (m_gpuHealthBarId &gt;= 0 &amp;&amp; m_gpuHealthBarManager != null)
    ///         {
    ///             m_gpuHealthBarManager.UpdateHealth(m_gpuHealthBarId, GetCurrentHealth(), GetMaxHealth());
    ///         }
    ///     }
    ///
    ///     protected override void OnDeath()
    ///     {
    ///         if (m_gpuHealthBarId &gt;= 0 &amp;&amp; m_gpuHealthBarManager != null)
    ///         {
    ///             m_gpuHealthBarManager.Unregister(m_gpuHealthBarId);
    ///             m_gpuHealthBarId = -1;
    ///         }
    ///     }
    /// }
    /// </code>
    ///
    /// ────────────────────────────────────────────────────────────
    /// 示例 3：Boss 血条放大
    /// ────────────────────────────────────────────────────────────
    /// <code>
    /// // Boss 使用更大的血条
    /// int bossId = manager.Register(bossTransform, headOffset: 5f);
    /// manager.SetSize(bossId, width: 15f, height: 2f);
    /// </code>
    ///
    /// ────────────────────────────────────────────────────────────
    /// 示例 4：场景切换清理
    /// ────────────────────────────────────────────────────────────
    /// <code>
    /// // 场景切换时清除所有血条
    /// HealthBarGPUInstanced manager = FindObjectOfType&lt;HealthBarGPUInstanced&gt;();
    /// if (manager != null)
    /// {
    ///     manager.ClearAll();
    /// }
    /// </code>
    ///
    /// ────────────────────────────────────────────────────────────
    /// 示例 5：查询状态
    /// ────────────────────────────────────────────────────────────
    /// <code>
    /// // 当前注册的血条数量（含回收槽位）
    /// int registeredCount = manager.RegisteredCount;
    ///
    /// // 当前容量
    /// int capacity = manager.Capacity;
    /// </code>
    /// </summary>
}
