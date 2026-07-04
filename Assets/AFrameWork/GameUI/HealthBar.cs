using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace AFrameWork.GameUI
{
    /// <summary>
    /// 血条组件 - 实现单个血条的显示、跟随、更新等功能
    /// 使用 Unity UI Toolkit 实现血条 UI 元素
    /// 支持实时位置跟随、平滑过渡、屏幕裁剪等功能
    /// </summary>
    public class HealthBar : MonoBehaviour
    {
        /// <summary>
        /// 遮挡检测共享的 RaycastHit 缓冲区（避免每帧 GC 分配）
        /// </summary>
        private static readonly RaycastHit[] s_occlusionHits = new RaycastHit[16];
        // ══════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// UXML 元素名称常量 - 使用 BEM 命名规范
        /// </summary>
        private const string k_healthBarBlock = "health-bar";
        private const string k_healthBarBackground = "health-bar__background";
        private const string k_healthBarFill = "health-bar__fill";
        private const string k_healthBarText = "health-bar__text";

        /// <summary>
        /// USS 类名常量 - 用于状态切换
        /// </summary>
        private const string k_hiddenClass = "health-bar--hidden";
        private const string k_fillLowClass = "health-bar__fill--low";
        private const string k_fillCriticalClass = "health-bar__fill--critical";
        private const string k_textHiddenClass = "health-bar__text--hidden";
        private const string k_fadeInClass = "health-bar--fade-in";
        private const string k_fadeOutClass = "health-bar--fade-out";
        private const string k_smoothClass = "health-bar__fill--smooth";
        private const string k_instantClass = "health-bar__fill--instant";

        // ══════════════════════════════════════════════════════════════════════════
        // 字段定义
        // ══════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// UIDocument 组件引用
        /// </summary>
        private UIDocument m_uiDocument;

        /// <summary>
        /// 血条控制器引用（用于获取 UXML 模板）
        /// </summary>
        private HealthBarController m_healthBarController;

        /// <summary>
        /// 血条根容器 VisualElement
        /// </summary>
        private VisualElement m_healthBarElement;

        /// <summary>
        /// 血条填充 VisualElement
        /// </summary>
        private VisualElement m_fillElement;

        /// <summary>
        /// 血条文本 Label
        /// </summary>
        private Label m_textElement;

        /// <summary>
        /// 血条配置
        /// </summary>
        private HealthBarConfig m_config;

        /// <summary>
        /// 血条跟随的目标 Transform
        /// </summary>
        private Transform m_targetTransform;

        /// <summary>
        /// 目标头部位置的垂直偏移（世界坐标）
        /// </summary>
        private float m_headOffset;

        /// <summary>
        /// 当前血量值
        /// </summary>
        private float m_currentHealth;

        /// <summary>
        /// 最大血量值
        /// </summary>
        private float m_maxHealth;

        /// <summary>
        /// 显示的目标血量值（用于平滑过渡）
        /// </summary>
        private float m_displayHealth;

        /// <summary>
        /// 上次遮挡检测时间（用于节流，避免每帧 Raycast）
        /// </summary>
        private float m_lastOcclusionTime;

        /// <summary>
        /// 上次更新时间（用于性能优化）
        /// </summary>
        private float m_lastUpdateTime;

        /// <summary>
        /// 是否正在显示
        /// </summary>
        private bool m_isVisible;

        /// <summary>
        /// 是否已初始化
        /// </summary>
        private bool m_isInitialized;

        /// <summary>
        /// 相机引用（用于世界坐标到屏幕坐标转换）
        /// </summary>
        private Camera m_camera;

        /// <summary>
        /// 当前是否被遮挡
        /// </summary>
        private bool m_isOccluded;

        /// <summary>
        /// 目标自身的碰撞体数组（遮挡检测时排除）
        /// </summary>
        private Collider[] m_targetColliders;

        // /// <summary>
        // /// 遮挡检测中自动跳过的 Tag 列表（地面、可穿透平台等不应算作遮挡物）
        // /// </summary>
        // private static readonly string[] k_nonOccludingTags = { "floor", "Floor" };

        // ══════════════════════════════════════════════════════════════════════════
        // 属性
        // ══════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// 获取当前血量百分比
        /// </summary>
        public float HealthPercentage => m_maxHealth > 0 ? m_currentHealth / m_maxHealth : 0f;

        /// <summary>
        /// 获取血条是否可见
        /// </summary>
        public bool IsVisible => m_isVisible;

        // ══════════════════════════════════════════════════════════════════════════
        // MonoBehaviour 方法
        // ══════════════════════════════════════════════════════════════════════════

        private void Awake()
        {
            // 获取主相机引用
            m_camera = Camera.main;

            // 初始化时间记录
            m_lastUpdateTime = Time.time;
            m_lastOcclusionTime = 0f;

            // 设置默认配置
            m_config = HealthBarConfig.CreateDefault();
        }

        private void OnEnable()
        {
            // 仅在控制器已就绪时初始化（池化对象在 SetTarget 中初始化）
            if (m_healthBarController != null)
            {
                InitializeUIElements();
            }
        }

        private void OnDisable()
        {
            // 清理 UI 元素引用
            CleanupUIElements();
        }

        // LateUpdate 已集中到 HealthBarController.LateUpdate 中批量调用，
        // 消除 N 个 MonoBehaviour 回调的 native→managed 开销

        /// <summary>
        /// 集中更新入口（由 HealthBarController.LateUpdate 批量调用）。
        /// 位置每帧更新（轻量），遮挡检测和平滑过渡按间隔节流。
        /// </summary>
        internal void UpdateBar(Camera cam, float time, float deltaTime)
        {
            if (!m_isInitialized) return;

            // 确保相机引用
            if (m_camera == null) m_camera = cam;
            if (m_camera == null) return;

            // 位置更新每帧执行（WorldToScreenPoint + style 赋值，轻量）
            UpdatePosition(time);

            // 遮挡检测节流（每 0.15 秒，RaycastNonAlloc 开销大）
            if (m_config.EnableOcclusionCheck && time - m_lastOcclusionTime >= 0.15f)
            {
                m_lastOcclusionTime = time;
                Vector3 worldPosition = m_targetTransform.position + Vector3.up * m_headOffset;
                UpdateOcclusion(m_camera.transform.position, worldPosition);
            }

            // 平滑过渡节流
            if (time - m_lastUpdateTime >= m_config.UpdateInterval)
            {
                m_lastUpdateTime = time;
                UpdateSmoothTransition();
            }
        }

        // ══════════════════════════════════════════════════════════════════════════
        // 公共方法
        // ══════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// 设置血条配置
        /// </summary>
        /// <param name="config">血条配置</param>
        public void SetConfig(HealthBarConfig config)
        {
            m_config = config;
            ApplyConfig();
        }

        /// <summary>
        /// 设置血条跟随目标
        /// </summary>
        /// <param name="target">目标 Transform</param>
        /// <param name="headOffset">头部偏移（世界坐标）</param>
        /// <param name="controller">血条控制器引用（可选）</param>
        public void SetTarget(Transform target, float headOffset = 2.0f, HealthBarController controller = null)
        {
            m_targetTransform = target;
            m_headOffset = headOffset;
            m_healthBarController = controller;

            // 缓存目标自身的碰撞体，遮挡检测时排除
            m_targetColliders = target.GetComponentsInChildren<Collider>();

            // 初始化 UI 元素（如果还没有初始化）
            InitializeUIElements();
        }

        /// <summary>
        /// 初始化血量值（不触发动画）
        /// </summary>
        /// <param name="currentHealth">当前血量</param>
        /// <param name="maxHealth">最大血量</param>
        public void InitializeHealth(float currentHealth, float maxHealth)
        {
            m_currentHealth = currentHealth;
            m_maxHealth = maxHealth;
            m_displayHealth = currentHealth;

            // 立即更新显示（无动画）
            UpdateFillWidth(true);
            UpdateText();
            UpdateHealthColor();
        }

        /// <summary>
        /// 更新血量值（带平滑过渡动画）
        /// </summary>
        /// <param name="currentHealth">当前血量</param>
        /// <param name="maxHealth">最大血量</param>
        public void UpdateHealth(float currentHealth, float maxHealth)
        {
            m_currentHealth = currentHealth;
            m_maxHealth = maxHealth;

            // 启用平滑过渡动画
            EnableSmoothTransition();
        }

        /// <summary>
        /// 显示血条（带淡入动画）
        /// </summary>
        public void Show()
        {
            if (m_isVisible)
            {
                return;
            }

            // 检查 UI 元素是否已初始化
            if (m_healthBarElement == null)
            {
                return;
            }

            m_isVisible = true;

            // 移除隐藏类
            m_healthBarElement.RemoveFromClassList(k_hiddenClass);

            // 添加淡入动画类
            m_healthBarElement.AddToClassList(k_fadeInClass);

            // 确保元素可见（HideImmediate 可能设过 display:none）
            m_healthBarElement.style.display = DisplayStyle.Flex;

            // 延迟移除淡入类（模拟动画完成）
            Invoke(nameof(CompleteFadeIn), m_config.FadeDuration);
        }

        /// <summary>
        /// 隐藏血条（带淡出动画）
        /// </summary>
        public void Hide()
        {
            if (!m_isVisible)
            {
                return;
            }

            // 检查 UI 元素是否已初始化
            if (m_healthBarElement == null)
            {
                return;
            }

            // 添加淡出动画类
            m_healthBarElement.AddToClassList(k_fadeOutClass);

            // 延迟添加隐藏类（模拟动画完成）
            Invoke(nameof(CompleteFadeOut), m_config.FadeDuration);
        }

        /// <summary>
        /// 立即显示血条（无动画）
        /// </summary>
        public void ShowImmediate()
        {
            m_isVisible = true;

            // 检查 UI 元素是否已初始化
            if (m_healthBarElement == null)
            {
                return;
            }

            // 移除所有动画和隐藏类
            m_healthBarElement.RemoveFromClassList(k_hiddenClass);
            m_healthBarElement.RemoveFromClassList(k_fadeInClass);
            m_healthBarElement.RemoveFromClassList(k_fadeOutClass);

            // 恢复显示
            m_healthBarElement.style.display = DisplayStyle.Flex;

            // 恢复透明度（考虑遮挡状态）
            m_healthBarElement.style.opacity = m_isOccluded ? m_config.OccludedAlpha : 1f;
        }

        /// <summary>
        /// 立即隐藏血条（无动画）
        /// </summary>
        public void HideImmediate()
        {
            m_isVisible = false;

            // 检查 UI 元素是否已初始化
            if (m_healthBarElement == null)
            {
                return;
            }

            // 移除所有动画类，添加隐藏类
            m_healthBarElement.RemoveFromClassList(k_fadeInClass);
            m_healthBarElement.RemoveFromClassList(k_fadeOutClass);
            m_healthBarElement.AddToClassList(k_hiddenClass);

            // 直接隐藏元素（不使用 opacity，避免与遮挡检测的 inline opacity 冲突）
            m_healthBarElement.style.display = DisplayStyle.None;
        }

        // ══════════════════════════════════════════════════════════════════════════
        // 私有方法
        // ══════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// 初始化 UI 元素引用
        /// 从 HealthBarController 的 UIDocument 获取 UXML 模板，
        /// 实例化一个血条 VisualElement 并添加到控制器共享的 rootVisualElement
        /// </summary>
        private void InitializeUIElements()
        {
            if (m_isInitialized)
            {
                return;  // 已经初始化，无需重复
            }

            // 始终使用控制器的 UIDocument（共享面板）
            if (m_healthBarController != null)
            {
                m_uiDocument = m_healthBarController.GetComponent<UIDocument>();
            }

            if (m_uiDocument == null)
            {
                // 独立使用场景：尝试自身 UIDocument
                m_uiDocument = GetComponent<UIDocument>();
            }

            if (m_uiDocument == null)
            {
#if UNITY_EDITOR
                // Debug.LogError($"[{GetType().Name}] UIDocument component is null!", this);
#endif
                return;
            }

            var root = m_uiDocument.rootVisualElement;
            if (root == null)
            {
#if UNITY_EDITOR
                // Debug.LogError($"[{GetType().Name}] UIDocument.rootVisualElement is null!", this);
#endif
                return;
            }

            // 从 HealthBarController 获取 UXML 模板
            VisualTreeAsset uxmlTemplate = null;
            if (m_healthBarController != null)
            {
                uxmlTemplate = m_healthBarController.HealthBarUxml;
            }

            if (uxmlTemplate == null)
            {
#if UNITY_EDITOR
                // Debug.LogError($"[{GetType().Name}] HealthBar UXML template is null! Cannot create health bar element.", this);
                // Debug.LogError($"[{GetType().Name}] Solution: Configure 'Health Bar Uxml' in HealthBarController Inspector.", this);
#endif
                return;
            }

            // 从 UXML 模板实例化（Instantiate 返回 TemplateContainer 包装器）
            var templateContainer = uxmlTemplate.Instantiate();

            if (templateContainer == null)
            {
#if UNITY_EDITOR
                // Debug.LogError($"[{GetType().Name}] Failed to instantiate health bar from UXML template!", this);
#endif
                return;
            }

            // 查询实际的 health-bar 元素（TemplateContainer 内部）
            m_healthBarElement = templateContainer.Q<VisualElement>(k_healthBarBlock);

            if (m_healthBarElement == null)
            {
                // 回退：直接使用 TemplateContainer
                m_healthBarElement = templateContainer;
            }
            else
            {
                // 将 health-bar 元素从 TemplateContainer 中移出，直接挂到 root
                templateContainer.Remove(m_healthBarElement);
                // 销毁空的 TemplateContainer
                templateContainer.RemoveFromHierarchy();
            }

            // 设置绝对定位模式（关键！使 left/top 可以自由定位）
            m_healthBarElement.style.position = Position.Absolute;
            m_healthBarElement.style.left = 0;
            m_healthBarElement.style.top = 0;

            // 添加到控制器的 rootVisualElement
            root.Add(m_healthBarElement);

#if UNITY_EDITOR
            // Debug.Log($"[{GetType().Name}] Health bar element created and added to UIDocument.rootVisualElement");
#endif

            // 查询子元素引用
            m_fillElement = m_healthBarElement.Q<VisualElement>(k_healthBarFill);
            m_textElement = m_healthBarElement.Q<Label>(k_healthBarText);

            // 验证元素引用
            if (m_fillElement == null)
            {
#if UNITY_EDITOR
                // Debug.LogError($"[{GetType().Name}] Failed to find fill element!", this);
#endif
                return;
            }

            m_isInitialized = true;

#if UNITY_EDITOR
            // Debug.Log($"[{GetType().Name}] UI elements initialized successfully");
#endif

            // 应用初始配置
            ApplyConfig();

            // 默认隐藏血条
            HideImmediate();
        }

        /// <summary>
        /// 清理 UI 元素引用，从父级移除 VisualElement
        /// </summary>
        private void CleanupUIElements()
        {
            // 从控制器的 rootVisualElement 中移除血条元素
            if (m_healthBarElement != null && m_healthBarElement.parent != null)
            {
                m_healthBarElement.parent.Remove(m_healthBarElement);
            }

            m_healthBarElement = null;
            m_fillElement = null;
            m_textElement = null;
            m_isInitialized = false;
        }

        /// <summary>
        /// 应用配置到 UI 元素
        /// </summary>
        private void ApplyConfig()
        {
            if (!m_isInitialized || m_healthBarElement == null)
            {
                return;
            }

            // 应用尺寸
            m_healthBarElement.style.width = m_config.Width;
            m_healthBarElement.style.height = m_config.Height;

            // 应用文本显示设置
            if (m_textElement != null)
            {
                if (m_config.ShowText)
                {
                    m_textElement.RemoveFromClassList(k_textHiddenClass);
                }
                else
                {
                    m_textElement.AddToClassList(k_textHiddenClass);
                }
            }
        }

        /// <summary>
        /// 更新血条位置（跟随目标对象）
        /// 遮挡检测已移至 UpdateBar 中节流调用
        /// </summary>
        private void UpdatePosition(float time)
        {
            if (!m_isInitialized || m_healthBarElement == null || m_targetTransform == null)
            {
                return;
            }

            // 检查相机引用
            if (m_camera == null)
            {
                m_camera = Camera.main;
                if (m_camera == null)
                {
                    return;
                }
            }

            // 计算目标头部位置（世界坐标）
            Vector3 worldPosition = m_targetTransform.position + Vector3.up * m_headOffset;

            // 遮挡检测未启用时，确保透明度恢复
            if (!m_config.EnableOcclusionCheck && m_isOccluded)
            {
                m_isOccluded = false;
                m_healthBarElement.style.opacity = 1f;
            }

            // 转换为屏幕坐标
            Vector3 screenPosition = m_camera.WorldToScreenPoint(worldPosition);

            // 检查目标是否在相机后方
            if (screenPosition.z < 0)
            {
                // 目标在相机后方，隐藏血条
                HideImmediate();
                return;
            }

            // 检查目标是否在屏幕可视范围内
            if (screenPosition.x < 0 || screenPosition.x > Screen.width ||
                screenPosition.y < 0 || screenPosition.y > Screen.height)
            {
                // 目标超出屏幕边界，隐藏血条
                HideImmediate();
                return;
            }

            // 转换为 UI Toolkit 坐标（左上角为原点）
            Vector2 uiPosition = new Vector2(screenPosition.x, Screen.height - screenPosition.y);

            // 应用偏移（使血条居中）
            uiPosition.x -= m_config.Width * 0.5f;
            uiPosition.y += m_config.OffsetY;

            // 屏幕边缘裁剪
            if (m_config.EnableScreenClipping)
            {
                uiPosition = ApplyScreenClipping(uiPosition);
            }

            // 设置 UI 元素位置
            m_healthBarElement.style.left = uiPosition.x;
            m_healthBarElement.style.top = uiPosition.y;

            // 如果目标在相机前方且血条隐藏，则显示
            if (!m_isVisible && screenPosition.z > 0)
            {
                ShowImmediate();
            }
        }

        /// <summary>
        /// 应用屏幕边缘裁剪
        /// </summary>
        /// <param name="position">原始位置</param>
        /// <returns>裁剪后的位置</returns>
        private Vector2 ApplyScreenClipping(Vector2 position)
        {
            float margin = m_config.ScreenClipMargin;

            // 左边界裁剪
            position.x = Mathf.Max(position.x, margin);

            // 右边界裁剪
            position.x = Mathf.Min(position.x, Screen.width - m_config.Width - margin);

            // 上边界裁剪
            position.y = Mathf.Max(position.y, margin);

            // 下边界裁剪
            position.y = Mathf.Min(position.y, Screen.height - m_config.Height - margin);

            return position;
        }

        /// <summary>
        /// 遮挡检测：从相机向目标方向发射射线，排除目标自身的 Collider。
        /// 基于射线是否被阻隔判断可见性，不按 Tag 跳过任何物体。
        /// </summary>
        private void UpdateOcclusion(Vector3 cameraPos, Vector3 targetPos)
        {
            if (!m_isInitialized || m_healthBarElement == null)
            {
                return;
            }

            Vector3 direction = targetPos - cameraPos;
            float distance = direction.magnitude;
            if (distance < 0.01f) return;

            Vector3 dir = direction / distance;  // 归一化（复用已计算的 distance）

            int mask = m_config.OcclusionLayerMask == 0 ? ~0 : m_config.OcclusionLayerMask;
            bool wasOccluded = m_isOccluded;

            // RaycastAll 获取路径上所有命中点，排除目标自身碰撞体后判断是否有遮挡
            int hitCount = Physics.RaycastNonAlloc(
                cameraPos, dir, s_occlusionHits, distance, mask,
                QueryTriggerInteraction.Ignore);

            bool foundOccluder = false;
            for (int i = 0; i < hitCount; i++)
            {
                Collider hitCollider = s_occlusionHits[i].collider;

                // 跳过目标自身的碰撞体
                if (m_targetColliders != null)
                {
                    bool isOwnCollider = false;
                    for (int j = 0; j < m_targetColliders.Length; j++)
                    {
                        if (m_targetColliders[j] == hitCollider)
                        {
                            isOwnCollider = true;
                            break;
                        }
                    }
                    if (isOwnCollider) continue;
                }

                // // 跳过地面等非遮挡物（角色站立面不应算作遮挡）
                // bool isNonOccluder = false;
                // for (int j = 0; j < k_nonOccludingTags.Length; j++)
                // {
                //     if (hitCollider.CompareTag(k_nonOccludingTags[j]))
                //     {
                //         isNonOccluder = true;
                //         break;
                //     }
                // }
                // if (isNonOccluder) continue;

                foundOccluder = true;
                break;
            }

            m_isOccluded = foundOccluder;

            // 状态变化时才更新 opacity（避免每帧重复赋值）
            if (m_isOccluded != wasOccluded)
            {
                m_healthBarElement.style.opacity = m_isOccluded ? m_config.OccludedAlpha : 1f;
            }
        }

        /// <summary>
        /// 更新血条平滑过渡
        /// </summary>
        private void UpdateSmoothTransition()
        {
            if (!m_isInitialized || m_fillElement == null)
            {
                return;
            }

            // 检查是否需要平滑过渡
            float difference = Mathf.Abs(m_displayHealth - m_currentHealth);
            if (difference < 0.1f)
            {
                // 过渡完成，更新到最终值
                m_displayHealth = m_currentHealth;
                UpdateFillWidth(false);
                UpdateText();
                UpdateHealthColor();
                return;
            }

            // 平滑过渡
            float transitionSpeed = Mathf.Abs(m_currentHealth - m_displayHealth) / m_config.SmoothTransitionDuration;
            m_displayHealth = Mathf.MoveTowards(m_displayHealth, m_currentHealth, transitionSpeed * m_config.UpdateInterval);

            // 更新显示
            UpdateFillWidth(false);
            UpdateText();
            UpdateHealthColor();
        }

        /// <summary>
        /// 更新填充条宽度
        /// </summary>
        /// <param name="instant">是否立即更新（无动画）</param>
        private void UpdateFillWidth(bool instant)
        {
            if (!m_isInitialized || m_fillElement == null)
            {
                return;
            }

            // 计算填充百分比
            float percentage = m_maxHealth > 0 ? m_displayHealth / m_maxHealth : 0f;
            percentage = Mathf.Clamp01(percentage);

            // 设置宽度（百分比）
            m_fillElement.style.width = Length.Percent(percentage * 100f);

            // 切换动画类
            if (instant)
            {
                m_fillElement.RemoveFromClassList(k_smoothClass);
                m_fillElement.AddToClassList(k_instantClass);
            }
            else
            {
                m_fillElement.RemoveFromClassList(k_instantClass);
                m_fillElement.AddToClassList(k_smoothClass);
            }
        }

        /// <summary>
        /// 更新血条文本
        /// </summary>
        private void UpdateText()
        {
            if (!m_isInitialized || m_textElement == null || !m_config.ShowText)
            {
                return;
            }

            // 更新文本内容
            m_textElement.text = $"{Mathf.RoundToInt(m_displayHealth)}/{Mathf.RoundToInt(m_maxHealth)}";
        }

        /// <summary>
        /// 根据血量百分比更新颜色
        /// </summary>
        private void UpdateHealthColor()
        {
            if (!m_isInitialized || m_fillElement == null)
            {
                return;
            }

            float percentage = HealthPercentage;

            // 移除所有状态类
            m_fillElement.RemoveFromClassList(k_fillLowClass);
            m_fillElement.RemoveFromClassList(k_fillCriticalClass);

            // 根据阈值添加状态类
            if (percentage <= m_config.CriticalHealthThreshold)
            {
                m_fillElement.AddToClassList(k_fillCriticalClass);
            }
            else if (percentage <= m_config.LowHealthThreshold)
            {
                m_fillElement.AddToClassList(k_fillLowClass);
            }
        }

        /// <summary>
        /// 启用平滑过渡动画
        /// </summary>
        private void EnableSmoothTransition()
        {
            if (!m_isInitialized || m_fillElement == null)
            {
                return;
            }

            // 移除即时更新类，添加平滑动画类
            m_fillElement.RemoveFromClassList(k_instantClass);
            m_fillElement.AddToClassList(k_smoothClass);
        }

        /// <summary>
        /// 完成淡入动画
        /// </summary>
        private void CompleteFadeIn()
        {
            if (!m_isInitialized || m_healthBarElement == null)
            {
                return;
            }

            m_healthBarElement.RemoveFromClassList(k_fadeInClass);
        }

        /// <summary>
        /// 完成淡出动画
        /// </summary>
        private void CompleteFadeOut()
        {
            if (!m_isInitialized || m_healthBarElement == null)
            {
                return;
            }

            m_isVisible = false;

            // 移除淡出类，添加隐藏类
            m_healthBarElement.RemoveFromClassList(k_fadeOutClass);
            m_healthBarElement.AddToClassList(k_hiddenClass);
        }

        /* ══════════════════════════════════════════════════════════════════════════
           【使用说明】

           1. 基本设置：
              // 获取或添加 HealthBar 组件
              HealthBar healthBar = gameObject.GetComponent<HealthBar>();
              if (healthBar == null)
              {
                  healthBar = gameObject.AddComponent<HealthBar>();
              }

              // 设置血条配置
              HealthBarConfig config = HealthBarConfig.CreateDefault();
              healthBar.SetConfig(config);

              // 设置跟随目标
              healthBar.SetTarget(targetTransform, 2.0f);

              // 初始化血量
              healthBar.InitializeHealth(100f, 100f);

           2. 血量更新：
              // 更新血量（带平滑过渡动画）
              healthBar.UpdateHealth(80f, 100f);

              // 初始化血量（立即显示，无动画）
              healthBar.InitializeHealth(100f, 100f);

           3. 显示/隐藏控制：
              // 显示血条（带淡入动画）
              healthBar.Show();

              // 隐藏血条（带淡出动画）
              healthBar.Hide();

              // 立即显示（无动画）
              healthBar.ShowImmediate();

              // 立即隐藏（无动画）
              healthBar.HideImmediate();

           4. 配置调整：
              // 创建自定义配置
              HealthBarConfig config = new HealthBarConfig(120f, 15f, -25f, true);
              config.LowHealthThreshold = 0.3f;
              config.CriticalHealthThreshold = 0.1f;
              config.SmoothTransitionDuration = 0.3f;
              config.FadeDuration = 0.5f;
              config.UpdateInterval = 0.1f;
              config.EnableScreenClipping = true;
              config.ScreenClipMargin = 15f;

              // 应用配置
              healthBar.SetConfig(config);

           5. 预设配置使用：
              // 紧凑型血条（适合小目标）
              healthBar.SetConfig(HealthBarConfig.CreateCompact());

              // 大型血条（适合Boss）
              healthBar.SetConfig(HealthBarConfig.CreateLarge());

           6. 头部偏移设置：
              // 根据目标对象类型调整头部偏移
              // 小型敌人：headOffset = 1.5f
              // 中型敌人：headOffset = 2.0f
              // 大型敌人/Boss：headOffset = 3.0f
              healthBar.SetTarget(enemyTransform, 2.5f);

           7. 性能优化建议：
              - UpdateInterval: 控制血条更新频率，建议值 0.05-0.1秒
              - 避免在每帧更新血条位置，使用 UpdateInterval 进行限制
              - 当目标在相机后方时自动隐藏血条
              - 使用 LateUpdate 确保位置更新在其他更新之后

           8. 注意事项：
              - 确保 UIDocument 组件已添加并正确配置 UXML 和 USS
              - 确保场景中存在主相机（Camera.main）
              - 血条默认隐藏，需要手动调用 Show() 或 ShowImmediate()
              - 血条位置在 LateUpdate 中更新，确保跟随准确性
              - 血条会自动处理屏幕边缘裁剪，防止超出屏幕范围
              - 血量颜色根据阈值自动变化：正常（红色）-> 低血量（橙色）-> 危急（深红色）

           9. 与 HealthBarController 配合使用：
              // 通常建议使用 HealthBarController 管理多个血条
              // HealthBar 作为单独组件主要用于简单场景或测试
              HealthBarController controller = GetComponent<HealthBarController>();
              controller.CreateHealthBar(target, config);

           ══════════════════════════════════════════════════════════════════════════ */
    }
}