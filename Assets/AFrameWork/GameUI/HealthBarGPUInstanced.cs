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
                Debug.LogError($"[{GetType().Name}] HealthBarGPU shader not found!", this);
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
                Debug.LogWarning($"[{GetType().Name}] Camera.main not found in Start(), will lazy-bind in LateUpdate", this);
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
}
