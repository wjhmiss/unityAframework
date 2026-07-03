using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Animations;
using UnityEngine.Audio;
using UnityEngine.VFX;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace AFrameWork.Core
{
    /// <summary>
    /// 帧事件目标类型：定义帧事件触发时对不同类型对象执行的操作。
    /// </summary>
    public enum FrameEventTargetType
    {
        None = 0,
        /// <summary>播放 ParticleSystem（Target 须为 ParticleSystem）</summary>
        PlayParticleSystem,
        /// <summary>停止 ParticleSystem</summary>
        StopParticleSystem,
        /// <summary>播放 AudioClip（Target 须为 AudioClip，FloatParam 为音量）</summary>
        PlayAudioClip,
        /// <summary>播放 VisualEffect（Target 须为 VisualEffect）</summary>
        PlayVisualEffect,
        /// <summary>停止 VisualEffect</summary>
        StopVisualEffect,
        /// <summary>自定义事件，通过 OnAnimationCustomFrameEvent(eventName) 回调子类处理</summary>
        Custom
    }

    /// <summary>
    /// 帧事件配置：定义动画播放过程中某个时间点触发的动作。
    /// 支持多种目标类型（ParticleSystem、AudioClip、VisualEffect、自定义回调）。
    /// TriggerTime/TargetType/EventName/FloatParam 在编译时确定，
    /// Target 在运行时赋值（ParticleSystem/VisualEffect 在 SetupComponents 中查找子对象，
    /// AudioClip 在 Start 中通过 LoadAssetAsync 加载）。
    /// Target 为 null 时该事件自动跳过。
    /// </summary>
    [Serializable]
    public struct FrameEventConfig
    {
        /// <summary>触发时间（秒，按动画长度换算为归一化时间注册到帧事件系统）</summary>
        public float TriggerTime;

        /// <summary>目标类型（决定 Target 的预期类型和触发行为）</summary>
        public FrameEventTargetType TargetType;

        /// <summary>目标对象（ParticleSystem / AudioClip / VisualEffect，运行时赋值）</summary>
        public UnityEngine.Object Target;

        /// <summary>自定义事件名称（仅 TargetType == Custom 时使用，传递给 OnAnimationCustomFrameEvent）</summary>
        public string EventName;

        /// <summary>浮点参数（如 AudioClip 的音量 0~1，0 表示使用默认值 1.0f）</summary>
        public float FloatParam;
    }

    /// <summary>
    /// 动画配置结构体：将动画剪辑、属性倍率、帧事件列表 统一绑定在同一个实体中。
    /// 适用于所有动画类型（待机、移动、攻击、技能等），杜绝分开管理的匹配错误。
    /// ClipKey/Multiplier/FrameEvents 在编译时确定，
    /// Clip 在运行时通过 LoadAnimationClipsAsync / LoadAnimationConfigAssetsAsync 赋值，
    /// FrameEvents 中各事件的 Target 在 SetupComponents / Start 中赋值。
    /// FrameEvents 为 null 或空数组时表示该动画无帧事件。
    /// </summary>
    [Serializable]
    public struct AnimationConfig
    {
        /// <summary>动画资源的 Addressables 地址键</summary>
        public string ClipKey;

        /// <summary>加载完成后的动画剪辑引用（运行时通过 LoadAnimationClipsAsync 赋值）</summary>
        public AnimationClip Clip;

        /// <summary>该动画对应的属性倍率（仅攻击动画使用，非攻击动画默认即可）</summary>
        public ObjectStatsConfigMultiplier Multiplier;

        /// <summary>
        /// 帧事件列表（多个事件按各自 TriggerTime 帧同步触发）。
        /// 数组为引用类型，子类需在 SetupComponents 中调用 CloneFrameEvents 克隆独立副本后赋值 Target。
        /// </summary>
        public FrameEventConfig[] FrameEvents;
    }

    /// <summary>
    /// 游戏对象基类，提供动态组件创建和管理功能。
    /// 子类重写 SetupComponents() 方法，使用 AddObjectComponent&lt;T&gt;() 添加组件。
    ///
    /// 【性能优化设计】（针对大型多人在线项目的热路径优化）
    ///
    /// 1. MovementConfig 缓存：
    ///    - 子类通过 override MovementConfig 属性返回 new 实例提供配置
    ///    - Awake 时一次性缓存到 m_movementConfig 字段，避免 Update/FixedUpdate 每帧访问属性产生 GC
    ///    - 子类无需修改，基类自动完成缓存；如需运行时改变配置，直接修改 m_movementConfig
    ///
    /// 2. 移动方向偏移旋转预计算：
    ///    - MovementAngleOffset 对应的 Quaternion.Euler 在 Awake 中预计算到 m_movementAngleRotation
    ///    - 通过 m_hasMovementAngleOffset 布尔标志避免每帧比较 Quaternion
    ///
    /// 3. 减速计算消除重复开方：
    ///    - HandleMovement 中复用 currentSpeed 计算 m_horizontalVelocity * (newSpeed / currentSpeed)
    ///    - 避免原 m_horizontalVelocity.normalized * newSpeed 中 .normalized 的第二次 Mathf.Sqrt
    ///
    /// 4. CalculateObjectBounds 零分配：
    ///    - 使用静态缓冲区 s_rendererBuffer 替代 GetComponentsInChildren&lt;Renderer&gt;() 数组分配
    ///    - 缓存 transform.worldToLocalMatrix 避免循环内重复计算
    ///    - 使用 TryGetComponent&lt;MeshFilter&gt; 替代 GetComponent + null 检查
    ///
    /// 5. Debug.Log 剥离：
    ///    - AddCapsuleCollider / AddBoxCollider 中的调试日志包裹 #if UNITY_EDITOR
    ///    - 生产构建中完全剥离，避免字符串插值的 GC 分配
    ///    - Debug.LogError（如 Addressables 加载失败）保留在构建中用于错误上报
    /// </summary>
    public class ObjectBase : MonoBehaviour
    {
        #region 字段

        // 组件缓存字典
        private Dictionary<Type, Component> m_componentCache = new Dictionary<Type, Component>();

        // 标记是否已完成组件初始化
        private bool m_isComponentsInitialized = false;

        // 缓存的 Rigidbody 组件引用（子类在 SetupComponents 中赋值）
        protected Rigidbody m_rigidbody;

        // 缓存的移动配置（Awake 时一次性缓存，避免热路径中每帧访问属性创建新实例）
        // 原因：MovementConfig 属性在子类中返回 new MovementConfig(...)，每帧分配会产生严重 GC 压力
        private MovementConfig m_movementConfig;

        // 缓存的移动方向偏移旋转（避免每帧调用 Quaternion.Euler）
        private Quaternion m_movementAngleRotation = Quaternion.identity;

        // 是否存在移动方向偏移（避免每帧比较 Quaternion）
        private bool m_hasMovementAngleOffset = false;

        // 移动输入
        private Vector3 m_movementInput;

        // 缓存的水平速度（独立于物理摩擦，用于精确控制减速）
        private Vector3 m_horizontalVelocity = Vector3.zero;

        // 物体属性配置实例（运行时数据）
        private ObjectStatsConfig m_objectStats;

        // 静态复用的 Renderer 缓冲区，避免 CalculateObjectBounds 每次调用分配新数组
        // 安全性：Awake/SetupComponents 由 Unity 主线程顺序执行，静态缓冲区不会并发访问
        private static readonly List<Renderer> s_rendererBuffer = new List<Renderer>(16);

        // ===== Playable API 相关字段 =====

        // Playable 图，管理所有动画和声音的 Playable
        private PlayableGraph m_playableGraph;

        // 动画输出，连接到 Animator
        private AnimationPlayableOutput m_animationOutput;

        // 音频输出，连接到 AudioSource
        private AudioPlayableOutput m_audioOutput;

        // 音频混合器，支持同时播放多个声音
        private AudioMixerPlayable m_audioMixer;

        // 动画混合器，通过权重切换实现动画过渡，避免每切换一次就 Create/Destroy
        private AnimationMixerPlayable m_animationMixer;

        // 预分配的动画 Playable 槽位数组，按 slotIndex 管理动画剪辑
        private AnimationClipPlayable[] m_animationSlots;

        // 当前激活的动画槽位索引（-1 表示无动画播放）
        private int m_activeAnimationSlot = -1;

        // 动画混合器的最大输入端口数量（稳定拓扑，避免运行时扩展导致内部分配）
        private const int k_animationMixerMaxInputs = 8;

        // 缓存当前播放的 AnimationClip 引用，避免 CheckFrameEvents 每帧调用 GetAnimationClip()
        private AnimationClip m_currentAnimationClip;

        // 缓存的 Animator 组件
        private Animator m_animator;

        // 缓存的 AudioSource 组件
        private AudioSource m_audioSource;

        // Playable 图是否已初始化
        private bool m_isPlayableGraphValid = false;

        // 当前动画是否正在播放
        private bool m_isAnimationPlaying = false;

        // 当前动画是否循环
        private bool m_isAnimationLoop = false;

        // 上一帧的动画归一化时间，用于检测循环
        private float m_lastAnimationNormalizedTime = 0f;

        // ===== 动画过渡（CrossFade）状态字段 =====
        // 性能影响：过渡期间每帧更新 2 个权重值（轻量 float 设置），MMO 场景可接受
        // 默认 crossFadeTime=0（立即切换），仅在需要平滑过渡时手动启用

        // 是否正在进行动画过渡
        private bool m_isCrossFading = false;

        // 过渡源槽位索引（权重从 1 → 0）
        private int m_crossFadeSourceSlot = -1;

        // 过渡目标槽位索引（权重从 0 → 1）
        private int m_crossFadeTargetSlot = -1;

        // 过渡总时长（秒）
        private float m_crossFadeDuration = 0f;

        // 已过渡时间（秒）
        private float m_crossFadeElapsed = 0f;

        // 音频 slot 是否正在使用
        private bool[] m_audioSlotUsed = new bool[k_audioMixerMaxInputs];

        // 各 slot 对应的音频 Playable
        private AudioClipPlayable[] m_audioPlayables = new AudioClipPlayable[k_audioMixerMaxInputs];

        // 音频混合器的最大 input 数量
        private const int k_audioMixerMaxInputs = 8;

        // 帧事件条目
        private struct FrameEventEntry
        {
            public string Name;
            public float NormalizedTime;
            public bool Triggered;
        }

        // 注册的帧事件列表（预设容量 4，避免首次 Add 时扩容）
        private List<FrameEventEntry> m_frameEvents = new List<FrameEventEntry>(4);

        // ===== Addressables 资源加载相关字段 =====

        // 本对象加载的 Addressables 资源句柄字典（key = Addressables 地址键）
        // 避免同一对象重复加载同一资源，并在 OnDestroy 时统一释放
        private Dictionary<string, AsyncOperationHandle> m_loadedAssetHandles = new Dictionary<string, AsyncOperationHandle>();

        // ===== 攻击系统字段 =====

        // 动画配置数组（子类在 SetupComponents 中赋值，包含动画、倍率、特效、声音）
        protected AnimationConfig[] m_animationConfigs = null;

        // 当前连击索引（循环递增）
        protected int m_comboIndex = 0;

        // 上次攻击触发的时间戳
        protected float m_lastAttackTime = 0f;

        // 是否正在播放攻击动画
        protected bool m_isAttacking = false;

        // 当前播放动画的帧事件配置数组（PlayAnimation 时缓存，供帧事件回调查找目标和参数）
        private FrameEventConfig[] m_currentFrameEvents;

        // 帧事件名称前缀（基类内部使用，格式 "FE_0"、"FE_1" 等，索引对应 m_currentFrameEvents）
        private const string k_frameEventPrefix = "FE_";

        // ===== 无敌状态字段 =====
        // 当前是否处于无敌状态（翻滚/闪避/无敌技能等触发，期间 TakeDamage 直接返回）
        private bool m_isInvulnerable = false;

        // 无敌状态结束时间（Time.time）；<=0f 表示需要手动解除（永久无敌直到调用 ClearInvulnerable）
        private float m_invulnerableEndTime = 0f;

        // ===== 移动锁定字段 =====
        // 子类自定义位移期间（如翻滚、击退、冲刺）设置为 true，HandleMovement 会跳过
        // 避免基类移动逻辑覆盖子类直接设置的 Rigidbody.velocity
        protected bool m_isMovementLocked = false;

        #endregion

        #region 配置属性

        /// <summary>
        /// 移动配置属性，子类重写此属性来提供移动控制参数。
        /// 如果返回 null，则不会启用移动控制功能。
        /// </summary>
        protected virtual MovementConfig MovementConfig => null;

        /// <summary>
        /// 物体属性配置属性，子类重写此属性来提供初始属性配置。
        /// ObjectBase 会根据此配置创建运行时的属性实例。
        /// </summary>
        protected virtual ObjectStatsConfig ObjectStatsConfig => null;

        /// <summary>
        /// 每次攻击后的冷却时间（秒）。子类可重写以调整。
        /// </summary>
        protected virtual float AttackCooldown => 0.5f;

        /// <summary>
        /// 连击超时重置时间（秒）。超过此时间未攻击则连击归零。子类可重写以调整。
        /// </summary>
        protected virtual float ComboResetTime => 1.5f;

        #endregion

        #region MonoBehaviour 方法

        protected virtual void Awake()
        {
            // 缓存移动配置：属性在子类中返回 new 实例，每帧访问会产生 GC 压力
            m_movementConfig = MovementConfig;

            // 预计算移动方向偏移旋转，避免每帧调用 Quaternion.Euler
            if (m_movementConfig != null && m_movementConfig.MovementAngleOffset != 0f)
            {
                m_movementAngleRotation = Quaternion.Euler(0f, m_movementConfig.MovementAngleOffset, 0f);
                m_hasMovementAngleOffset = true;
            }

            SetupComponents();
            SetupObjectStats();
            m_isComponentsInitialized = true;
        }

        protected virtual void Update()
        {
            // 组件未初始化完成时跳过输入处理，避免访问未就绪的状态
            if (!m_isComponentsInitialized)
            {
                return;
            }

            // 处理输入
            HandleInput();

            // 更新动画过渡权重（CrossFade）
            UpdateAnimationCrossFade();

            // 检查动画帧事件
            CheckFrameEvents();

            // 清理已完成的音频 Playable
            CleanupFinishedAudioPlayables();
        }

        private void FixedUpdate()
        {
            // 组件未初始化完成时跳过物理处理，避免访问未就绪的 Rigidbody
            if (!m_isComponentsInitialized)
            {
                return;
            }

            // 处理物理移动
            HandleMovement();
        }

        protected virtual void OnDestroy()
        {
            ClearComponentCache();
            ShutdownPlayableGraph();
            ReleaseAllLoadedAssets();
        }

        #endregion

        #region 初始化方法

        /// <summary>
        /// 子类重写此方法来动态创建和配置所需的组件。
        /// 在 Awake 中自动调用，子类应使用 AddObjectComponent&lt;T&gt;() 添加组件。
        /// <example>
        /// <code>
        /// protected override void SetupComponents()
        /// {
        ///     base.SetupComponents();
        ///
        ///     // 添加 Rigidbody 并配置参数
        ///     m_rigidbody = AddObjectComponent&lt;Rigidbody&gt;(rb =>
        ///     {
        ///         rb.mass = 1f;
        ///         rb.useGravity = true;
        ///         rb.constraints = RigidbodyConstraints.FreezeRotation;
        ///     });
        ///
        ///     // 添加 CapsuleCollider 并配置参数
        ///     AddObjectComponent&lt;CapsuleCollider&gt;(c =>
        ///     {
        ///         c.radius = 0.5f;
        ///         c.height = 2f;
        ///         c.center = new Vector3(0, 1, 0);
        ///     });
        /// }
        /// </code>
        /// </example>
        /// </summary>
        protected virtual void SetupComponents() { }

        /// <summary>
        /// 初始化物体属性配置
        /// </summary>
        private void SetupObjectStats()
        {
            ObjectStatsConfig config = ObjectStatsConfig;
            if (config == null)
            {
                return;
            }

            // 创建运行时的物体属性实例（克隆配置，避免修改原始配置）
            m_objectStats = new ObjectStatsConfig();
            CopyObjectStatsConfig(config, m_objectStats);
        }

        /// <summary>
        /// 复制物体属性配置（用于创建运行时实例）
        /// </summary>
        private void CopyObjectStatsConfig(ObjectStatsConfig source, ObjectStatsConfig target)
        {
            target.Type = source.Type;
            target.FactionID = source.FactionID;
            target.TeamID = source.TeamID;
            target.GuildID = source.GuildID;
            target.AllianceID = source.AllianceID;
            target.CurrentPVPMode = source.CurrentPVPMode;
            target.MaxHealth = source.MaxHealth;
            target.CurrentHealth = source.CurrentHealth;
            target.PhysicalAttack = source.PhysicalAttack;
            target.PhysicalDefense = source.PhysicalDefense;
            target.TrueDamage = source.TrueDamage;
            target.MagicAttack = source.MagicAttack;
            target.MagicDefense = source.MagicDefense;
            target.MoveSpeed = source.MoveSpeed;
            target.AttackSpeed = source.AttackSpeed;
            target.CastSpeed = source.CastSpeed;
            target.CriticalRate = source.CriticalRate;
            target.CriticalDamageMultiplier = source.CriticalDamageMultiplier;
            target.ArmorPenetration = source.ArmorPenetration;
            target.MagicPenetration = source.MagicPenetration;
            target.HealthRegeneration = source.HealthRegeneration;
            target.ManaRegeneration = source.ManaRegeneration;
            target.MaxMana = source.MaxMana;
            target.CurrentMana = source.CurrentMana;
            target.CooldownReduction = source.CooldownReduction;
            target.EvasionRate = source.EvasionRate;
            target.HitRate = source.HitRate;
            target.AttackRange = source.AttackRange;
            target.VisionRange = source.VisionRange;
            target.Experience = source.Experience;
            target.Level = source.Level;
            target.Gold = source.Gold;
            target.BaseDamage = source.BaseDamage;
            target.DamageType = source.DamageType;
            target.DamageRadius = source.DamageRadius;
            target.DamageInterval = source.DamageInterval;
            target.IsContinuousDamage = source.IsContinuousDamage;
            target.DamageDuration = source.DamageDuration;
            target.CanDealDamage = source.CanDealDamage;
        }

        #endregion

        #region 移动控制方法

        /// <summary>
        /// 处理输入
        /// </summary>
        private void HandleInput()
        {
            MovementConfig config = m_movementConfig;
            if (config == null || !config.IsAutoReadInput)
            {
                return;
            }

            // 读取移动输入（使用 GetAxisRaw 避免平滑插值带来的延迟感）
            float horizontal = Input.GetAxisRaw("Horizontal");
            float vertical = Input.GetAxisRaw("Vertical");

            // 构建移动向量，先检查长度再归一化（避免极小值被 normalized 变成方向向量）
            Vector3 rawInput = new Vector3(horizontal, 0f, vertical);
            if (rawInput.sqrMagnitude > 0.01f)
            {
                m_movementInput = rawInput.normalized;

                // 应用预计算的移动方向偏移旋转（避免每帧调用 Quaternion.Euler）
                if (m_hasMovementAngleOffset)
                {
                    m_movementInput = m_movementAngleRotation * m_movementInput;
                }
            }
            else
            {
                m_movementInput = Vector3.zero;
            }
        }

        /// <summary>
        /// 处理物理移动
        /// </summary>
        private void HandleMovement()
        {
            // 子类锁定移动时跳过（翻滚/击退/冲刺等自定义位移期间）
            // 子类负责直接设置 Rigidbody.velocity，基类不干预
            if (m_isMovementLocked)
            {
                return;
            }

            MovementConfig config = m_movementConfig;
            if (config == null || m_rigidbody == null)
            {
                return;
            }

            // 如果没有移动输入，根据 DecelerationRate 控制惯性减速
            if (m_movementInput == Vector3.zero)
            {
                // 获取 Rigidbody 当前水平速度
                Vector3 rbHorizontal = new Vector3(m_rigidbody.velocity.x, 0f, m_rigidbody.velocity.z);

                // 检测外力：如果 Rigidbody 速度明显大于缓存速度，说明有外力加速，同步缓存
                // （缓存速度较小或为零时，任何外力都应该被接受）
                if (rbHorizontal.sqrMagnitude > m_horizontalVelocity.sqrMagnitude + 0.1f)
                {
                    m_horizontalVelocity = rbHorizontal;
                }

                // 缓存的水平速度几乎为零，无需处理
                if (m_horizontalVelocity.sqrMagnitude <= 0.0001f)
                {
                    m_horizontalVelocity = Vector3.zero;
                    return;
                }

                // DecelerationRate <= 0 表示无减速（保持当前速度滑行，覆盖物理摩擦）
                if (config.DecelerationRate <= 0f)
                {
                    // 不修改缓存速度，重新应用到 Rigidbody 以抵消摩擦力
                    ApplyHorizontalVelocity(config);
                    return;
                }

                // 基于缓存速度计算减速（不受物理摩擦影响）
                float deceleration = config.DecelerationRate * Time.fixedDeltaTime;
                float currentSpeed = m_horizontalVelocity.magnitude;
                float newSpeed = Mathf.Max(0f, currentSpeed - deceleration);

                if (newSpeed <= 0.001f)
                {
                    m_horizontalVelocity = Vector3.zero;
                }
                else if (currentSpeed > 0.0001f)
                {
                    // 复用 currentSpeed 计算缩放比例，避免 .normalized 内部再次调用 Mathf.Sqrt
                    m_horizontalVelocity = m_horizontalVelocity * (newSpeed / currentSpeed);
                }

                ApplyHorizontalVelocity(config);
                return;
            }

            // 从 ObjectStatsConfig 获取移动速度
            float moveSpeed = GetMoveSpeed();
            if (moveSpeed <= 0f)
            {
                return;
            }

            // 有输入时，更新缓存水平速度并应用到 Rigidbody
            m_horizontalVelocity = m_movementInput * moveSpeed;
            ApplyHorizontalVelocity(config);
        }

        /// <summary>
        /// 将缓存的水平速度应用到 Rigidbody，保留 Y 轴速度（根据配置）
        /// </summary>
        private void ApplyHorizontalVelocity(MovementConfig config)
        {
            if (config.IsKeepYVelocity)
            {
                m_rigidbody.velocity = new Vector3(
                    m_horizontalVelocity.x,
                    m_rigidbody.velocity.y,
                    m_horizontalVelocity.z);
            }
            else
            {
                m_rigidbody.velocity = m_horizontalVelocity;
            }
        }

        /// <summary>
        /// 设置移动输入（供外部调用）
        /// </summary>
        /// <param name="input">移动输入向量</param>
        public void SetMovementInput(Vector3 input)
        {
            if (input.sqrMagnitude > 0.01f)
            {
                m_movementInput = input.normalized;
            }
            else
            {
                m_movementInput = Vector3.zero;
            }
        }

        /// <summary>
        /// 停止移动
        /// </summary>
        public void StopMovement()
        {
            MovementConfig config = m_movementConfig;
            if (config == null || m_rigidbody == null)
            {
                return;
            }

            m_movementInput = Vector3.zero;
            m_horizontalVelocity = Vector3.zero;
            if (config.IsKeepYVelocity)
            {
                m_rigidbody.velocity = new Vector3(0f, m_rigidbody.velocity.y, 0f);
            }
            else
            {
                m_rigidbody.velocity = Vector3.zero;
            }
        }

        #endregion

        #region 物体属性管理方法

        /// <summary>
        /// 获取物体属性配置实例（运行时数据）
        /// </summary>
        /// <returns>物体属性配置实例</returns>
        public ObjectStatsConfig GetObjectStats()
        {
            return m_objectStats;
        }

        /// <summary>
        /// 检查是否有物体属性配置
        /// </summary>
        public bool HasObjectStats()
        {
            return m_objectStats != null;
        }

        #endregion

        #region 物体属性访问方法（便捷访问）

        /// <summary>
        /// 获取当前生命值
        /// </summary>
        public float GetCurrentHealth()
        {
            return m_objectStats?.CurrentHealth ?? 0f;
        }

        /// <summary>
        /// 获取最大生命值
        /// </summary>
        public float GetMaxHealth()
        {
            return m_objectStats?.MaxHealth ?? 0f;
        }

        /// <summary>
        /// 设置当前生命值
        /// </summary>
        public void SetCurrentHealth(float health)
        {
            if (m_objectStats != null)
            {
                m_objectStats.CurrentHealth = Mathf.Clamp(health, 0f, m_objectStats.MaxHealth);
            }
        }

        /// <summary>
        /// 获取生命值百分比
        /// </summary>
        public float GetHealthPercentage()
        {
            return m_objectStats?.GetHealthPercentage() ?? 0f;
        }

        /// <summary>
        /// 获取当前魔法值
        /// </summary>
        public float GetCurrentMana()
        {
            return m_objectStats?.CurrentMana ?? 0f;
        }

        /// <summary>
        /// 获取最大魔法值
        /// </summary>
        public float GetMaxMana()
        {
            return m_objectStats?.MaxMana ?? 0f;
        }

        /// <summary>
        /// 设置当前魔法值
        /// </summary>
        public void SetCurrentMana(float mana)
        {
            if (m_objectStats != null)
            {
                m_objectStats.CurrentMana = Mathf.Clamp(mana, 0f, m_objectStats.MaxMana);
            }
        }

        /// <summary>
        /// 获取魔法值百分比
        /// </summary>
        public float GetManaPercentage()
        {
            return m_objectStats?.GetManaPercentage() ?? 0f;
        }

        /// <summary>
        /// 获取物体等级
        /// </summary>
        public int GetLevel()
        {
            return m_objectStats?.Level ?? 0;
        }

        /// <summary>
        /// 设置物体等级
        /// </summary>
        public void SetLevel(int level)
        {
            if (m_objectStats != null)
            {
                m_objectStats.Level = level;
            }
        }

        /// <summary>
        /// 获取金币数量
        /// </summary>
        public int GetGold()
        {
            return m_objectStats?.Gold ?? 0;
        }

        /// <summary>
        /// 设置金币数量
        /// </summary>
        public void SetGold(int gold)
        {
            if (m_objectStats != null)
            {
                m_objectStats.Gold = gold;
            }
        }

        /// <summary>
        /// 获取物理攻击力
        /// </summary>
        public float GetPhysicalAttack()
        {
            return m_objectStats?.PhysicalAttack ?? 0f;
        }

        /// <summary>
        /// 设置物理攻击力
        /// </summary>
        public void SetPhysicalAttack(float attack)
        {
            if (m_objectStats != null)
            {
                m_objectStats.PhysicalAttack = attack;
            }
        }

        /// <summary>
        /// 获取魔法攻击力
        /// </summary>
        public float GetMagicAttack()
        {
            return m_objectStats?.MagicAttack ?? 0f;
        }

        /// <summary>
        /// 设置魔法攻击力
        /// </summary>
        public void SetMagicAttack(float attack)
        {
            if (m_objectStats != null)
            {
                m_objectStats.MagicAttack = attack;
            }
        }

        /// <summary>
        /// 获取移动速度
        /// </summary>
        public float GetMoveSpeed()
        {
            return m_objectStats?.MoveSpeed ?? 0f;
        }

        /// <summary>
        /// 设置移动速度
        /// </summary>
        public void SetMoveSpeed(float speed)
        {
            if (m_objectStats != null)
            {
                m_objectStats.MoveSpeed = speed;
            }
        }

        #endregion

        #region 物体属性操作方法

        /// <summary>
        /// 受到伤害。
        /// 无敌状态下（IsInvulnerable=true）直接返回，不扣血不触发回调。
        /// </summary>
        public void TakeDamage(float damage)
        {
            // 无敌状态（翻滚/闪避/无敌技能）：完全免疫伤害
            if (IsInvulnerable)
            {
                return;
            }

            if (m_objectStats != null)
            {
                m_objectStats.TakeDamage(damage);
                OnDamaged(damage);

                if (m_objectStats.IsDead())
                {
                    OnDeath();
                }
            }
        }

        /// <summary>
        /// 恢复生命值
        /// </summary>
        public void Heal(float amount)
        {
            if (m_objectStats != null)
            {
                m_objectStats.Heal(amount);
                OnHealed(amount);
            }
        }

        /// <summary>
        /// 消耗魔法值
        /// </summary>
        public bool ConsumeMana(float amount)
        {
            if (m_objectStats != null)
            {
                bool success = m_objectStats.ConsumeMana(amount);
                if (success)
                {
                    OnManaConsumed(amount);
                }
                return success;
            }
            return false;
        }

        /// <summary>
        /// 恢复魔法值
        /// </summary>
        public void RestoreMana(float amount)
        {
            if (m_objectStats != null)
            {
                m_objectStats.RestoreMana(amount);
                OnManaRestored(amount);
            }
        }

        /// <summary>
        /// 增加经验值
        /// </summary>
        public void AddExperience(float amount)
        {
            if (m_objectStats != null)
            {
                m_objectStats.AddExperience(amount);
                OnExperienceAdded(amount);
            }
        }

        /// <summary>
        /// 增加金币
        /// </summary>
        public void AddGold(int amount)
        {
            if (m_objectStats != null)
            {
                m_objectStats.AddGold(amount);
                OnGoldAdded(amount);
            }
        }

        /// <summary>
        /// 检查是否死亡
        /// </summary>
        public bool IsDead()
        {
            return m_objectStats?.IsDead() ?? false;
        }

        /// <summary>
        /// 检查是否存活
        /// </summary>
        public bool IsAlive()
        {
            return m_objectStats?.IsAlive() ?? false;
        }

        /// <summary>
        /// 当前是否处于无敌状态。访问时自动检查并解除已过期的无敌状态。
        /// 无敌期间 TakeDamage 直接返回（不扣血、不触发 OnDamaged/OnDeath）。
        /// 典型用途：翻滚闪避、技能无敌帧、复活保护、GM 调试无敌。
        /// </summary>
        public bool IsInvulnerable
        {
            get
            {
                // 永久无敌（m_invulnerableEndTime <= 0f）需手动 ClearInvulnerable 解除
                if (m_isInvulnerable && m_invulnerableEndTime > 0f && Time.time >= m_invulnerableEndTime)
                {
                    m_isInvulnerable = false;
                    m_invulnerableEndTime = 0f;
                }
                return m_isInvulnerable;
            }
        }

        /// <summary>
        /// 设置无敌状态。duration &lt;= 0 表示永久无敌，需手动调用 ClearInvulnerable 解除。
        /// 重复调用会刷新结束时间（取较晚的）。
        /// </summary>
        /// <param name="duration">无敌持续时间（秒）；&lt;=0 表示永久无敌</param>
        public void SetInvulnerable(float duration)
        {
            m_isInvulnerable = true;
            if (duration > 0f)
            {
                float newEndTime = Time.time + duration;
                // 取较晚的结束时间，避免短暂无敌覆盖正在进行的较长无敌
                if (newEndTime > m_invulnerableEndTime || m_invulnerableEndTime <= 0f)
                {
                    m_invulnerableEndTime = newEndTime;
                }
            }
            else
            {
                // 永久无敌：标记为 <=0，需手动解除
                m_invulnerableEndTime = 0f;
            }
        }

        /// <summary>
        /// 立即解除无敌状态。
        /// </summary>
        public void ClearInvulnerable()
        {
            m_isInvulnerable = false;
            m_invulnerableEndTime = 0f;
        }

        /// <summary>
        /// 重置物体属性
        /// </summary>
        public void ResetObjectStats()
        {
            if (m_objectStats != null)
            {
                m_objectStats.ResetStats();
                OnStatsReset();
            }
        }

        #endregion

        #region 物体属性事件回调（子类可重写）

        /// <summary>
        /// 受到伤害时的回调，子类可重写此方法实现自定义逻辑
        /// </summary>
        protected virtual void OnDamaged(float damage) { }

        /// <summary>
        /// 恢复生命值时的回调，子类可重写此方法实现自定义逻辑
        /// </summary>
        protected virtual void OnHealed(float amount) { }

        /// <summary>
        /// 消耗魔法值时的回调，子类可重写此方法实现自定义逻辑
        /// </summary>
        protected virtual void OnManaConsumed(float amount) { }

        /// <summary>
        /// 恢复魔法值时的回调，子类可重写此方法实现自定义逻辑
        /// </summary>
        protected virtual void OnManaRestored(float amount) { }

        /// <summary>
        /// 增加经验值时的回调，子类可重写此方法实现自定义逻辑
        /// </summary>
        protected virtual void OnExperienceAdded(float amount) { }

        /// <summary>
        /// 增加金币时的回调，子类可重写此方法实现自定义逻辑
        /// </summary>
        protected virtual void OnGoldAdded(int amount) { }

        /// <summary>
        /// 物体死亡时的回调，子类可重写此方法实现自定义逻辑
        /// </summary>
        protected virtual void OnDeath() { }

        /// <summary>
        /// 重置属性时的回调，子类可重写此方法实现自定义逻辑
        /// </summary>
        protected virtual void OnStatsReset() { }

        #endregion

        #region 组件管理方法

        /// <summary>
        /// 动态添加组件到当前游戏对象，并缓存引用。
        /// 如果组件已存在，则返回现有组件。
        /// </summary>
        /// <typeparam name="T">组件类型</typeparam>
        /// <returns>组件实例</returns>
        protected T AddObjectComponent<T>() where T : Component
        {
            Type type = typeof(T);

            // 检查缓存中是否已存在
            if (m_componentCache.TryGetValue(type, out Component cachedComponent))
            {
                return cachedComponent as T;
            }

            // 尝试获取现有组件
            T component = GetComponent<T>();
            if (component == null)
            {
                // 不存在则创建新组件
                component = gameObject.AddComponent<T>();
            }

            // 缓存组件引用
            m_componentCache[type] = component;
            return component;
        }

        /// <summary>
        /// 动态添加组件，并执行初始化配置。
        /// 如果组件已存在，则返回现有组件（不会重新初始化）。
        /// </summary>
        /// <typeparam name="T">组件类型</typeparam>
        /// <param name="initializer">初始化配置的回调函数</param>
        /// <returns>组件实例</returns>
        protected T AddObjectComponent<T>(Action<T> initializer) where T : Component
        {
            T component = AddObjectComponent<T>();
            initializer?.Invoke(component);
            return component;
        }

        /// <summary>
        /// 获取缓存的组件引用。
        /// 如果缓存中没有，则尝试从 GameObject 上获取。
        /// </summary>
        /// <typeparam name="T">组件类型</typeparam>
        /// <returns>组件实例，如果不存在则返回 null</returns>
        public T GetObjectComponent<T>() where T : Component
        {
            Type type = typeof(T);

            // 检查缓存
            if (m_componentCache.TryGetValue(type, out Component cachedComponent))
            {
                return cachedComponent as T;
            }

            // 尝试从 GameObject 获取
            T component = GetComponent<T>();
            if (component != null)
            {
                m_componentCache[type] = component;
            }

            return component;
        }

        /// <summary>
        /// 检查是否包含指定类型的组件（从缓存或 GameObject）。
        /// </summary>
        /// <typeparam name="T">组件类型</typeparam>
        /// <returns>如果包含则返回 true</returns>
        public bool HasObjectComponent<T>() where T : Component
        {
            return GetObjectComponent<T>() != null;
        }

        /// <summary>
        /// 移除缓存的组件引用并销毁组件。
        /// </summary>
        /// <typeparam name="T">组件类型</typeparam>
        protected void RemoveObjectComponent<T>() where T : Component
        {
            Type type = typeof(T);

            if (m_componentCache.TryGetValue(type, out Component cachedComponent))
            {
                m_componentCache.Remove(type);
                if (cachedComponent != null)
                {
                    Destroy(cachedComponent);
                }
            }
        }

        /// <summary>
        /// 清空所有缓存的组件引用（不销毁组件）。
        /// </summary>
        protected void ClearComponentCache()
        {
            m_componentCache.Clear();
        }

        /// <summary>
        /// 获取所有已缓存的组件。
        /// </summary>
        /// <returns>组件缓存字典</returns>
        protected Dictionary<Type, Component> GetAllCachedComponents()
        {
            return new Dictionary<Type, Component>(m_componentCache);
        }

        /// <summary>
        /// 根据对象所有 Renderer 的 Mesh 本地包围盒，计算本对象本地空间的 Bounds。
        /// 使用 Mesh.bounds / SkinnedMeshRenderer.localBounds（本地空间）而非 Renderer.bounds（世界空间 AABB），
        /// 通过矩阵变换将各子 Renderer 的本地包围盒合并到本对象本地空间，对旋转对象计算更精确。
        /// 包含当前对象及所有子对象的 Renderer，子类可调用此方法获取包围盒后调整大小和位置。
        /// </summary>
        /// <returns>本地空间的包围盒；如果没有 Renderer 则返回默认 Bounds</returns>
        protected Bounds CalculateObjectBounds()
        {
            // 使用静态缓冲区避免每次调用分配 Renderer[] 数组
            s_rendererBuffer.Clear();
            GetComponentsInChildren(true, s_rendererBuffer);
            int rendererCount = s_rendererBuffer.Count;

            if (rendererCount == 0)
            {
                // 没有 Renderer 时返回默认 Bounds（单位立方体）
                return new Bounds(Vector3.zero, Vector3.one);
            }

            // 收集子层级中带有 ObjectBase 的 GameObject 及其所有子对象，
            // 这些属于独立实体（如武器、宠物），不应参与本对象的包围盒计算
            HashSet<int> excludedInstanceIDs = null;
            ObjectBase[] childObjectBases = GetComponentsInChildren<ObjectBase>(true);
            for (int i = 0; i < childObjectBases.Length; i++)
            {
                // 跳过自身
                if (childObjectBases[i] == this)
                {
                    continue;
                }

                if (excludedInstanceIDs == null)
                {
                    excludedInstanceIDs = new HashSet<int>();
                }

                // 标记该 ObjectBase 及其所有子 Renderer 的 instanceID
                excludedInstanceIDs.Add(childObjectBases[i].GetInstanceID());
                Renderer[] childRenderers = childObjectBases[i].GetComponentsInChildren<Renderer>(true);
                for (int j = 0; j < childRenderers.Length; j++)
                {
                    excludedInstanceIDs.Add(childRenderers[j].GetInstanceID());
                }
            }

            // 使用 Mesh 的本地空间 bounds，通过矩阵变换合并到本对象本地空间
            // 相比 Renderer.bounds（世界空间 AABB），此方法对旋转对象计算更精确
            bool hasBounds = false;
            Bounds resultBounds = new Bounds(Vector3.zero, Vector3.zero);

            // 缓存 worldToLocalMatrix 避免每次循环重复计算
            Matrix4x4 worldToLocal = transform.worldToLocalMatrix;

            for (int r = 0; r < rendererCount; r++)
            {
                Renderer currentRenderer = s_rendererBuffer[r];

                // 跳过属于子 ObjectBase 的 Renderer（武器、宠物等独立实体）
                if (excludedInstanceIDs != null && excludedInstanceIDs.Contains(currentRenderer.GetInstanceID()))
                {
                    continue;
                }

                // 跳过特效类 Renderer（VFXRenderer / ParticleSystemRenderer），
                // 它们的 bounds 通常远大于角色实际体型，会导致 Collider 范围偏大
                string rendererTypeName = currentRenderer.GetType().Name;
                if (rendererTypeName == "VFXRenderer"
                    || currentRenderer is ParticleSystemRenderer)
                {
                    continue;
                }

                // 初始化为 default 避免 CS0165（编译器无法推断 else 分支内嵌套 if 的赋值路径）
                Bounds meshLocalBounds = default;
                bool valid = false;

                // SkinnedMeshRenderer.localBounds 不反映蒙皮后实际顶点位置（骨骼驱动顶点可能远超原始 mesh bounds），
                // 使用 Renderer.bounds（世界空间 AABB）替代，通过 worldToLocal 变换到本对象本地空间
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

                // 跳过退化 bounds（空 Mesh）
                if (meshLocalBounds.size.sqrMagnitude < 0.0001f)
                {
                    continue;
                }

                // 世界空间 bounds 直接用 worldToLocal；本地空间 bounds 需要 renderer→local 变换
                Matrix4x4 rendererToLocal = useWorldSpaceBounds
                    ? worldToLocal
                    : worldToLocal * currentRenderer.transform.localToWorldMatrix;

                // 将 Mesh 本地包围盒的 8 个角点变换到本对象本地空间，合并得到精确的本地 AABB
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
                // 没有 Mesh 数据（如 SpriteRenderer/ParticleSystemRenderer），回退到世界空间 bounds
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
        /// 添加 CapsuleCollider 并根据传入的包围盒设置大小和位置。
        /// 自动选择最长的轴作为胶囊方向，并计算合适的半径和高度。
        /// 子类可先调用 CalculateObjectBounds() 获取包围盒，调整后再传入。
        /// </summary>
        /// <param name="bounds">本地空间的包围盒，用于设置 Collider 的大小和位置</param>
        /// <param name="sizeMultiplier">XYZ 轴分别的缩放因子，默认 (1,1,1) 不缩放</param>
        /// <param name="centerOffset">中心点的偏移量（世界空间，单位=米），默认 Vector3.zero 不偏移。
        /// 内部会除以 transform.lossyScale 转换到本地空间，因此无论父级缩放多大，
        /// (0, 1, 0) 始终表示在世界空间中向上偏移 1 米。</param>
        /// <param name="extraConfig">额外的配置回调，用于设置 isTrigger、material 等属性。
        /// 例如：extraConfig = c => { c.isTrigger = false; c.material = physicsMaterial; }</param>
        /// <returns>配置好的 CapsuleCollider 实例</returns>
        protected CapsuleCollider AddCapsuleCollider(
            Bounds bounds,
            Vector3 sizeMultiplier,
            Vector3 centerOffset = default,
            Action<CapsuleCollider> extraConfig = null)
        {
            // 计算缩放后的尺寸和偏移后的中心点
            Vector3 scaledSize = Vector3.Scale(bounds.size, sizeMultiplier);
            // 将世界空间偏移转换为本地空间偏移，抵消父级缩放的影响
            Vector3 lossyScale = transform.lossyScale;
            Vector3 localOffset = new Vector3(
                centerOffset.x / (lossyScale.x != 0f ? lossyScale.x : 1f),
                centerOffset.y / (lossyScale.y != 0f ? lossyScale.y : 1f),
                centerOffset.z / (lossyScale.z != 0f ? lossyScale.z : 1f));
            Vector3 adjustedCenter = bounds.center + localOffset;

#if UNITY_EDITOR
            // Debug.Log($"[AddCapsuleCollider] {name}: " +
            //     $"传入bounds(center={bounds.center}, size={bounds.size}), " +
            //     $"sizeMultiplier={sizeMultiplier}, centerOffset={centerOffset}, " +
            //     $"调整后center={adjustedCenter}, scaledSize={scaledSize}", this);
#endif

            return AddObjectComponent<CapsuleCollider>(c =>
            {
                // 找出最长的轴，用作胶囊方向
                int direction = 1; // 默认 Y 轴
                float maxExtent = scaledSize.y;

                if (scaledSize.x > maxExtent)
                {
                    direction = 0; // X 轴
                    maxExtent = scaledSize.x;
                }

                if (scaledSize.z > maxExtent)
                {
                    direction = 2; // Z 轴
                    maxExtent = scaledSize.z;
                }

                // 根据胶囊方向计算半径和高度
                // 半径取垂直于胶囊方向的两个轴中较长者的一半，确保完全包裹对象宽度
                // 高度取胶囊方向的轴长（CapsuleCollider.height 包含两端半球）
                switch (direction)
                {
                    case 0: // X 轴
                        c.radius = Mathf.Max(scaledSize.y, scaledSize.z) * 0.5f;
                        c.height = scaledSize.x;
                        break;
                    case 1: // Y 轴
                        c.radius = Mathf.Max(scaledSize.x, scaledSize.z) * 0.5f;
                        c.height = scaledSize.y;
                        break;
                    case 2: // Z 轴
                        c.radius = Mathf.Max(scaledSize.x, scaledSize.y) * 0.5f;
                        c.height = scaledSize.z;
                        break;
                }

                c.center = adjustedCenter;
                c.direction = direction;

                // 执行额外配置（如设置 isTrigger、material 等）
                extraConfig?.Invoke(c);

#if UNITY_EDITOR
                // Debug.Log($"[AddCapsuleCollider-完成] {name}: 创建 CapsuleCollider " +
                //     $"(radius={c.radius}, height={c.height}, direction={c.direction}, center={c.center})", this);
#endif
            });
        }

        /// <summary>
        /// 添加 BoxCollider 并根据传入的包围盒设置大小和位置。
        /// 子类可先调用 CalculateObjectBounds() 获取包围盒，调整后再传入。
        /// </summary>
        /// <param name="bounds">本地空间的包围盒，用于设置 Collider 的大小和位置</param>
        /// <param name="sizeMultiplier">XYZ 轴分别的缩放因子，默认 (1,1,1) 不缩放</param>
        /// <param name="centerOffset">中心点的偏移量（世界空间，单位=米），默认 Vector3.zero 不偏移。
        /// 内部会除以 transform.lossyScale 转换到本地空间，因此无论父级缩放多大，
        /// (0, 1, 0) 始终表示在世界空间中向上偏移 1 米。</param>
        /// <param name="extraConfig">额外的配置回调，用于设置 isTrigger、material 等属性。
        /// 例如：extraConfig = c => { c.isTrigger = true; c.material = physicsMaterial; }</param>
        /// <returns>配置好的 BoxCollider 实例</returns>
        protected BoxCollider AddBoxCollider(
            Bounds bounds,
            Vector3 sizeMultiplier,
            Vector3 centerOffset = default,
            Action<BoxCollider> extraConfig = null)
        {
            // 计算缩放后的尺寸和偏移后的中心点
            Vector3 scaledSize = Vector3.Scale(bounds.size, sizeMultiplier);
            // 将世界空间偏移转换为本地空间偏移，抵消父级缩放的影响
            Vector3 lossyScale = transform.lossyScale;
            Vector3 localOffset = new Vector3(
                centerOffset.x / (lossyScale.x != 0f ? lossyScale.x : 1f),
                centerOffset.y / (lossyScale.y != 0f ? lossyScale.y : 1f),
                centerOffset.z / (lossyScale.z != 0f ? lossyScale.z : 1f));
            Vector3 adjustedCenter = bounds.center + localOffset;

#if UNITY_EDITOR
            // Debug.Log($"[AddBoxCollider] {name}: " +
            //     $"传入bounds(center={bounds.center}, size={bounds.size}), " +
            //     $"sizeMultiplier={sizeMultiplier}, centerOffset={centerOffset}, " +
            //     $"调整后center={adjustedCenter}, scaledSize={scaledSize}", this);
#endif

            return AddObjectComponent<BoxCollider>(c =>
            {
                c.center = adjustedCenter;
                c.size = scaledSize;

                // 执行额外配置（如设置 isTrigger、material 等）
                extraConfig?.Invoke(c);

#if UNITY_EDITOR
                // Debug.Log($"[AddBoxCollider-完成] {name}: 创建 BoxCollider " +
                //     $"(size={c.size}, center={c.center})", this);
#endif
            });
        }

        #endregion

        #region 动画与声音控制方法（Playable API）

        /// <summary>
        /// 创建 Animator 并初始化 PlayableGraph，用于动画播放。
        /// 子类应在 SetupComponents() 中调用此方法来启用动画功能。
        /// </summary>
        /// <returns>Animator 组件实例</returns>
        protected Animator SetupAnimator()
        {
            m_animator = AddObjectComponent<Animator>();

            // 创建 PlayableGraph（如果尚未创建）
            EnsurePlayableGraph();

            // 创建动画输出并连接到 Animator
            m_animationOutput = AnimationPlayableOutput.Create(m_playableGraph, "Animation", m_animator);

            // 初始化动画混合器和槽位数组（稳定拓扑，避免运行时扩展）
            InitializeAnimationMixer();

            return m_animator;
        }

        /// <summary>
        /// 异步创建 Animator 并通过 Addressables 加载骨骼文件（Avatar），初始化 PlayableGraph。
        /// 子类应在 SetupComponents 中调用此方法来启用基于骨骼的动画播放功能。
        /// Animator 组件会立即创建，Avatar 和 PlayableGraph 在加载完成后赋值/创建。
        /// </summary>
        /// <param name="avatarKey">Avatar 资源的 Addressables 地址键（骨骼文件），传 null/空字符串则不加载 Avatar</param>
        /// <returns>Animator 组件实例；对象在加载期间被销毁则返回 null</returns>
        protected async Task<Animator> SetupAnimatorAsync(string avatarKey)
        {
            m_animator = AddObjectComponent<Animator>();

            // 通过 Addressables 加载骨骼文件（Avatar）
            if (!string.IsNullOrEmpty(avatarKey))
            {
                Avatar avatar = await LoadAssetAsync<Avatar>(avatarKey);
                // 加载期间对象可能被销毁，需检查 this 是否仍然有效
                if (this == null)
                {
                    return null;
                }
                if (avatar != null)
                {
                    m_animator.avatar = avatar;
                }
            }

            // 创建 PlayableGraph（如果尚未创建）
            EnsurePlayableGraph();

            // 创建动画输出并连接到 Animator
            m_animationOutput = AnimationPlayableOutput.Create(m_playableGraph, "Animation", m_animator);

            // 初始化动画混合器和槽位数组（稳定拓扑，避免运行时扩展）
            InitializeAnimationMixer();

            return m_animator;
        }

        /// <summary>
        /// 创建 AudioSource 并初始化音频输出，用于声音播放。
        /// 子类应在 SetupComponents() 中调用此方法来启用声音功能。
        /// 必须在 SetupAnimator() 之后调用（共享同一个 PlayableGraph）。
        /// </summary>
        /// <param name="initializer">AudioSource 初始化回调（可选）</param>
        /// <returns>AudioSource 组件实例</returns>
        protected AudioSource SetupAudioSource(Action<AudioSource> initializer = null)
        {
            m_audioSource = AddObjectComponent<AudioSource>(src =>
            {
                src.playOnAwake = false;
                src.spatialBlend = 0f; // 默认 2D
                initializer?.Invoke(src);
            });

            // 创建 PlayableGraph（如果尚未创建）
            EnsurePlayableGraph();

            // 创建音频输出并连接到 AudioSource
            m_audioOutput = AudioPlayableOutput.Create(m_playableGraph, "Audio", m_audioSource);

            // 创建音频混合器（支持同时播放多个声音）
            m_audioMixer = AudioMixerPlayable.Create(m_playableGraph, k_audioMixerMaxInputs, true);
            // Unity 2022.3: AudioPlayableOutput 通过 PlayableOutputExtensions.SetSourcePlayable 设置源
            // Mixer 作为 source，混合多个 AudioClipPlayable 的输出
            m_audioOutput.SetSourcePlayable(m_audioMixer);

            // 初始化所有 slot 权重为 0（静音未使用的 slot）
            for (int i = 0; i < k_audioMixerMaxInputs; i++)
            {
                m_audioMixer.SetInputWeight(i, 0f);
            }

            return m_audioSource;
        }

        /// <summary>
        /// 播放动画。统一入口，通过 AnimationConfig 管理剪辑和帧事件。
        /// 使用 AnimationMixerPlayable 权重切换，避免 Create/Destroy 开销。
        /// 同一 AnimationClip 复用已有槽位；不同 clip 查找空闲槽位或复用非活跃槽位。
        /// AnimationConfig 中的 FrameEvents 按各自 TriggerTime 自动注册到帧事件系统。
        /// </summary>
        /// <param name="config">动画配置（含剪辑、帧事件列表）；Clip 为空时跳过</param>
        /// <param name="speed">播放速度（1=正常，0.5=慢速，2=快速）</param>
        /// <param name="loop">是否循环播放</param>
        /// <param name="crossFadeTime">混合过渡时间（秒）。0=立即切换（默认），>0=平滑过渡。
        ///     性能影响：过渡期间每帧更新 2 个权重值（轻量 float 设置），MMO 场景可接受。</param>
        protected void PlayAnimation(AnimationConfig config, float speed = 1f, bool loop = false, float crossFadeTime = 0f)
        {
            AnimationClip clip = config.Clip;
            if (!m_isPlayableGraphValid || clip == null || !m_animationMixer.IsValid())
            {
                return;
            }

            // 缓存当前动画的帧事件配置，供 OnAnimationFrameEvent 查找
            m_currentFrameEvents = config.FrameEvents;

            // 清除上一轮残留的帧事件
            ClearFrameEvents();

            // 查找目标槽位：优先复用同 clip 的已有槽位，否则找空闲槽位或复用非活跃槽位
            int targetSlot = FindOrAllocateAnimationSlot(clip);

            // 创建或重置 AnimationClipPlayable
            if (m_animationSlots[targetSlot].IsValid())
            {
                m_animationSlots[targetSlot].Destroy();
            }

            AnimationClipPlayable playable = AnimationClipPlayable.Create(m_playableGraph, clip);
            playable.SetSpeed(speed);
            playable.SetTime(0d);

            // 连接到混合器
            if (targetSlot < m_animationMixer.GetInputCount()
                && m_animationMixer.GetInput(targetSlot).IsValid())
            {
                m_playableGraph.Disconnect(m_animationMixer, targetSlot);
            }
            m_playableGraph.Connect(playable, 0, m_animationMixer, targetSlot);
            m_animationSlots[targetSlot] = playable;

            // 根据 crossFadeTime 参数决定切换方式
            if (crossFadeTime > 0f && m_activeAnimationSlot >= 0 && m_activeAnimationSlot != targetSlot)
            {
                // 启动平滑过渡：源槽位权重 1→0，目标槽位权重 0→1
                m_isCrossFading = true;
                m_crossFadeSourceSlot = m_activeAnimationSlot;
                m_crossFadeTargetSlot = targetSlot;
                m_crossFadeDuration = crossFadeTime;
                m_crossFadeElapsed = 0f;

                // 初始权重设置：源=1，目标=0（其他=0）
                for (int i = 0; i < m_animationMixer.GetInputCount(); i++)
                {
                    if (i == m_crossFadeSourceSlot)
                    {
                        m_animationMixer.SetInputWeight(i, 1f);
                    }
                    else if (i == targetSlot)
                    {
                        m_animationMixer.SetInputWeight(i, 0f);
                    }
                    else
                    {
                        m_animationMixer.SetInputWeight(i, 0f);
                    }
                }
            }
            else
            {
                // 立即切换（crossFadeTime=0 或无前一个动画）：目标权重=1，其他=0
                m_isCrossFading = false;
                m_crossFadeSourceSlot = -1;
                m_crossFadeTargetSlot = -1;

                for (int i = 0; i < m_animationMixer.GetInputCount(); i++)
                {
                    m_animationMixer.SetInputWeight(i, (i == targetSlot) ? 1f : 0f);
                }
            }

            m_activeAnimationSlot = targetSlot;
            m_currentAnimationClip = clip;
            m_isAnimationPlaying = true;
            m_isAnimationLoop = loop;
            m_lastAnimationNormalizedTime = 0f;

            // 重置所有帧事件的触发状态
            ResetFrameEventTriggeredFlags();

            // 注册所有帧事件：每个事件按 TriggerTime 换算为归一化时间
            if (m_currentFrameEvents != null && m_currentFrameEvents.Length > 0)
            {
                for (int i = 0; i < m_currentFrameEvents.Length; i++)
                {
                    if (m_currentFrameEvents[i].TriggerTime > 0f)
                    {
                        float normalizedTime = Mathf.Clamp01(
                            m_currentFrameEvents[i].TriggerTime / clip.length);
                        RegisterFrameEvent(k_frameEventPrefix + i, normalizedTime);
                    }
                }
            }
        }

        /// <summary>
        /// 停止当前动画播放。将所有混合器权重设为 0 而不销毁 Playable，便于复用。
        /// </summary>
        protected void StopAnimation()
        {
            if (m_animationMixer.IsValid())
            {
                for (int i = 0; i < m_animationMixer.GetInputCount(); i++)
                {
                    m_animationMixer.SetInputWeight(i, 0f);
                }
            }

            m_activeAnimationSlot = -1;
            m_currentAnimationClip = null;
            m_isAnimationPlaying = false;
            m_lastAnimationNormalizedTime = 0f;
        }

        /// <summary>
        /// 设置当前动画的播放速度。
        /// </summary>
        /// <param name="speed">播放速度</param>
        protected void SetAnimationSpeed(float speed)
        {
            if (m_activeAnimationSlot >= 0 && m_activeAnimationSlot < m_animationSlots.Length
                && m_animationSlots[m_activeAnimationSlot].IsValid())
            {
                m_animationSlots[m_activeAnimationSlot].SetSpeed(speed);
            }
        }

        /// <summary>
        /// 获取当前动画的归一化播放时间（0~1）。
        /// </summary>
        /// <returns>归一化时间，如果没有动画在播放则返回 -1</returns>
        protected float GetAnimationNormalizedTime()
        {
            if (!m_isAnimationPlaying || m_activeAnimationSlot < 0
                || m_activeAnimationSlot >= m_animationSlots.Length
                || !m_animationSlots[m_activeAnimationSlot].IsValid())
            {
                return -1f;
            }

            // 使用缓存的 clip 引用，避免每帧调用 GetAnimationClip()
            AnimationClip clip = m_currentAnimationClip;
            if (clip == null || clip.length <= 0f)
            {
                return -1f;
            }

            return (float)(m_animationSlots[m_activeAnimationSlot].GetTime() / clip.length);
        }

        /// <summary>
        /// 播放声音。支持同时播放多个声音（最多 k_audioMixerMaxInputs 个）。
        /// 如果所有 slot 都在使用中，会先清理已完成的声音再尝试播放。
        /// </summary>
        /// <param name="clip">音频剪辑</param>
        /// <param name="volume">音量（0~1）</param>
        protected void PlaySound(AudioClip clip, float volume = 1f)
        {
            if (!m_isPlayableGraphValid || !m_audioMixer.IsValid() || clip == null)
            {
                return;
            }

            // 如果 slot 全满，先清理已完成的
            if (IsAllAudioSlotsUsed())
            {
                CleanupFinishedAudioPlayables();
            }

            // 找一个空闲 slot
            int slotIndex = FindFreeAudioSlot();
            if (slotIndex < 0)
            {
                // 所有 slot 都在使用中，跳过
                return;
            }

            // 创建音频 Playable 并连接到混合器
            AudioClipPlayable audioPlayable = AudioClipPlayable.Create(m_playableGraph, clip, false);
            // Unity 2022.3: AudioClipPlayable 没有 SetVolume，音量通过 Mixer 的 input weight 控制
            // weight = volume，实现音量控制

            m_playableGraph.Connect(audioPlayable, 0, m_audioMixer, slotIndex);
            m_audioMixer.SetInputWeight(slotIndex, volume);
            m_audioSlotUsed[slotIndex] = true;
            m_audioPlayables[slotIndex] = audioPlayable;
        }

        /// <summary>
        /// 异步加载音频剪辑并播放。使用 Addressables 加载 AudioClip。
        /// 支持同时播放多个声音（最多 k_audioMixerMaxInputs 个）。
        /// </summary>
        /// <param name="clipKey">AudioClip 资源的 Addressables 地址键</param>
        /// <param name="volume">音量（0~1）</param>
        protected async Task PlaySoundAsync(string clipKey, float volume = 1f)
        {
            AudioClip clip = await LoadAssetAsync<AudioClip>(clipKey);
            // 加载期间对象可能被销毁
            if (this == null)
            {
                return;
            }
            if (clip != null)
            {
                PlaySound(clip, volume);
            }
        }

        /// <summary>
        /// 停止所有正在播放的声音。
        /// </summary>
        protected void StopAllSounds()
        {
            if (!m_isPlayableGraphValid || !m_audioMixer.IsValid())
            {
                return;
            }

            for (int i = 0; i < k_audioMixerMaxInputs; i++)
            {
                if (m_audioSlotUsed[i])
                {
                    m_audioMixer.DisconnectInput(i);
                    if (m_audioPlayables[i].IsValid())
                    {
                        m_audioPlayables[i].Destroy();
                    }
                    m_audioSlotUsed[i] = false;
                    m_audioPlayables[i] = default(AudioClipPlayable);
                    m_audioMixer.SetInputWeight(i, 0f);
                }
            }
        }

        /// <summary>
        /// 注册一个帧事件。当动画播放到指定归一化时间时触发 OnAnimationFrameEvent 回调。
        /// 应在 PlayAnimation 之前调用。循环动画中每轮都会重新触发。
        /// </summary>
        /// <param name="eventName">事件名称，传递给 OnAnimationFrameEvent 回调</param>
        /// <param name="normalizedTime">触发时间点（0~1，0=动画开始，1=动画结束）</param>
        protected void RegisterFrameEvent(string eventName, float normalizedTime)
        {
            m_frameEvents.Add(new FrameEventEntry
            {
                Name = eventName,
                NormalizedTime = Mathf.Clamp01(normalizedTime),
                Triggered = false
            });
        }

        /// <summary>
        /// 清除所有已注册的帧事件。
        /// </summary>
        protected void ClearFrameEvents()
        {
            m_frameEvents.Clear();
        }

        /// <summary>
        /// 帧事件触发时的回调。基类自动解析帧事件索引并按 TargetType 执行操作，
        /// 子类重写时需调用 base 以保留内置帧事件处理，或重写 OnAnimationCustomFrameEvent 处理自定义事件。
        /// </summary>
        /// <param name="eventName">通过 RegisterFrameEvent 注册的事件名称</param>
        protected virtual void OnAnimationFrameEvent(string eventName)
        {
            // 基类处理内置帧事件（格式 "FE_索引"）
            if (m_currentFrameEvents != null
                && eventName != null
                && eventName.StartsWith(k_frameEventPrefix))
            {
                // 解析索引（k_frameEventPrefix 长度为 3，如 "FE_0" → "0"）
                if (int.TryParse(eventName.Substring(k_frameEventPrefix.Length), out int index)
                    && index >= 0 && index < m_currentFrameEvents.Length)
                {
                    ExecuteFrameEvent(m_currentFrameEvents[index]);
                }
            }
        }

        /// <summary>
        /// 执行单个帧事件：根据 TargetType 对 Target 执行对应操作。
        /// Target 为 null 时自动跳过。
        /// </summary>
        private void ExecuteFrameEvent(FrameEventConfig evt)
        {
            if (evt.Target == null && evt.TargetType != FrameEventTargetType.Custom)
            {
                return;
            }

            switch (evt.TargetType)
            {
                case FrameEventTargetType.PlayParticleSystem:
                    if (evt.Target is ParticleSystem ps)
                    {
                        ps.Play();
                    }
                    break;

                case FrameEventTargetType.StopParticleSystem:
                    if (evt.Target is ParticleSystem stopPs)
                    {
                        stopPs.Stop();
                    }
                    break;

                case FrameEventTargetType.PlayAudioClip:
                    if (evt.Target is AudioClip audio)
                    {
                        float volume = evt.FloatParam > 0f ? evt.FloatParam : 1f;
                        PlaySound(audio, volume);
                    }
                    break;

                case FrameEventTargetType.PlayVisualEffect:
                    if (evt.Target is VisualEffect vfx)
                    {
                        vfx.Play();
                    }
                    break;

                case FrameEventTargetType.StopVisualEffect:
                    if (evt.Target is VisualEffect stopVfx)
                    {
                        stopVfx.Stop();
                    }
                    break;

                case FrameEventTargetType.Custom:
                    OnAnimationCustomFrameEvent(evt.EventName);
                    break;
            }
        }

        /// <summary>
        /// 自定义帧事件回调。子类重写此方法处理 TargetType == Custom 的帧事件。
        /// </summary>
        /// <param name="eventName">FrameEventConfig.EventName 中定义的事件名称</param>
        protected virtual void OnAnimationCustomFrameEvent(string eventName) { }

        /// <summary>
        /// 非循环动画播放完成时的回调。基类自动处理攻击状态结束，子类重写时需调用 base。
        /// </summary>
        protected virtual void OnAnimationComplete()
        {
            // 基类处理攻击动画结束：重置攻击状态并通知子类
            if (m_isAttacking)
            {
                m_isAttacking = false;
                ClearFrameEvents();
                OnAttackEnded();
            }
        }

        /// <summary>
        /// 通过 Addressables 异步加载资源。同一对象对同一 key 的重复加载会返回缓存句柄的结果。
        /// 加载的句柄会在 OnDestroy 时通过 ReleaseAllLoadedAssets 统一释放。
        /// </summary>
        /// <param name="key">Addressables 资源地址键</param>
        /// <returns>加载的资源，加载失败返回 null</returns>
        protected async Task<T> LoadAssetAsync<T>(string key) where T : UnityEngine.Object
        {
            // 命中缓存：同一对象已加载过该资源，直接返回结果
            if (m_loadedAssetHandles.TryGetValue(key, out AsyncOperationHandle cachedHandle))
            {
                return cachedHandle.Result as T;
            }

            AsyncOperationHandle<T> handle = Addressables.LoadAssetAsync<T>(key);
            try
            {
                // 加载失败时 Task 可能以异常完成，需捕获以保证后续状态检查可达
                await handle.Task;
            }
            catch (Exception ex)
            {
                // Debug.LogError($"[{GetType().Name}] Addressables 加载异常: key='{key}', 错误: {ex.Message}", this);
                Addressables.Release(handle);
                return null;
            }

            if (handle.Status == AsyncOperationStatus.Succeeded)
            {
                // 存储为非泛型句柄，统一管理释放
                m_loadedAssetHandles[key] = handle;
                return handle.Result;
            }

            // Debug.LogError($"[{GetType().Name}] Addressables 加载失败: key='{key}', 错误: {handle.OperationException?.Message}", this);
            Addressables.Release(handle);
            return null;
        }

        /// <summary>
        /// 确保 PlayableGraph 已创建。多次调用安全。
        /// </summary>
        private void EnsurePlayableGraph()
        {
            if (m_isPlayableGraphValid)
            {
                return;
            }

            m_playableGraph = PlayableGraph.Create($"{name}_PlayableGraph");
            m_playableGraph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
            m_playableGraph.Play();
            m_isPlayableGraphValid = true;
        }

        /// <summary>
        /// 初始化动画混合器和预分配槽位数组。稳定拓扑，避免运行时扩展导致内部分配。
        /// </summary>
        private void InitializeAnimationMixer()
        {
            // 创建动画混合器，预分配固定数量的输入端口
            m_animationMixer = AnimationMixerPlayable.Create(m_playableGraph, k_animationMixerMaxInputs, true);

            // 初始化所有 slot 权重为 0（无动画输出）
            for (int i = 0; i < k_animationMixerMaxInputs; i++)
            {
                m_animationMixer.SetInputWeight(i, 0f);
            }

            // 连接到动画输出
            m_animationOutput.SetSourcePlayable(m_animationMixer);

            // 预分配槽位数组
            m_animationSlots = new AnimationClipPlayable[k_animationMixerMaxInputs];
        }

        /// <summary>
        /// 查找或分配动画槽位。优先复用同 clip 的已有槽位，否则找空闲槽位或复用非活跃槽位。
        /// </summary>
        /// <param name="clip">目标动画剪辑</param>
        /// <returns>槽位索引</returns>
        private int FindOrAllocateAnimationSlot(AnimationClip clip)
        {
            int freeSlot = -1;
            int inactiveSlot = -1;

            for (int i = 0; i < m_animationSlots.Length; i++)
            {
                if (!m_animationSlots[i].IsValid())
                {
                    // 记录第一个空闲槽位
                    if (freeSlot < 0)
                    {
                        freeSlot = i;
                    }
                }
                else if (m_animationSlots[i].GetAnimationClip() == clip)
                {
                    // 同 clip 直接复用此槽位
                    return i;
                }
                else if (i != m_activeAnimationSlot && inactiveSlot < 0)
                {
                    // 记录第一个非活跃槽位（可安全覆盖）
                    inactiveSlot = i;
                }
            }

            // 优先使用空闲槽位，否则复用非活跃槽位
            return (freeSlot >= 0) ? freeSlot : inactiveSlot;
        }

        /// <summary>
        /// 更新动画过渡权重（CrossFade），在 Update 中调用。
        /// 将源槽位权重从 1 渐变到 0，目标槽位权重从 0 渐变到 1。
        /// 性能影响：过渡期间每帧 2 次 SetInputWeight（轻量 float 设置），MMO 场景可接受。
        /// </summary>
        private void UpdateAnimationCrossFade()
        {
            if (!m_isCrossFading || !m_animationMixer.IsValid())
            {
                return;
            }

            m_crossFadeElapsed += Time.deltaTime;
            float t = Mathf.Clamp01(m_crossFadeElapsed / m_crossFadeDuration);

            // 线性插值权重：源=1-t, 目标=t
            m_animationMixer.SetInputWeight(m_crossFadeSourceSlot, 1f - t);
            m_animationMixer.SetInputWeight(m_crossFadeTargetSlot, t);

            // 过渡完成：清理状态
            if (t >= 1f)
            {
                m_animationMixer.SetInputWeight(m_crossFadeSourceSlot, 0f);
                m_isCrossFading = false;
                m_crossFadeSourceSlot = -1;
                m_crossFadeTargetSlot = -1;
            }
        }

        /// <summary>
        /// 检查动画帧事件，在 Update 中调用。
        /// 检测动画播放时间，触发到达时间点的帧事件。
        /// </summary>
        private void CheckFrameEvents()
        {
            if (!m_isAnimationPlaying || m_activeAnimationSlot < 0
                || m_activeAnimationSlot >= m_animationSlots.Length
                || !m_animationSlots[m_activeAnimationSlot].IsValid())
            {
                return;
            }

            // 使用缓存的 clip 引用，避免每帧调用 GetAnimationClip()（热路径零分配原则）
            AnimationClip clip = m_currentAnimationClip;
            if (clip == null || clip.length <= 0f)
            {
                return;
            }

            float normalizedTime = (float)(m_animationSlots[m_activeAnimationSlot].GetTime() / clip.length);

            // 检测循环：归一化时间从大变小（从接近 1 回到接近 0）
            if (m_isAnimationLoop && normalizedTime < m_lastAnimationNormalizedTime)
            {
                // 重置帧事件触发状态，允许下一轮循环再次触发
                ResetFrameEventTriggeredFlags();
            }

            m_lastAnimationNormalizedTime = normalizedTime;

            // 检查并触发帧事件
            for (int i = 0; i < m_frameEvents.Count; i++)
            {
                if (!m_frameEvents[i].Triggered && normalizedTime >= m_frameEvents[i].NormalizedTime)
                {
                    m_frameEvents[i] = new FrameEventEntry
                    {
                        Name = m_frameEvents[i].Name,
                        NormalizedTime = m_frameEvents[i].NormalizedTime,
                        Triggered = true
                    };
                    OnAnimationFrameEvent(m_frameEvents[i].Name);
                }
            }

            // 非循环动画完成检测
            if (!m_isAnimationLoop && normalizedTime >= 1f)
            {
                m_isAnimationPlaying = false;
                OnAnimationComplete();
            }
        }

        /// <summary>
        /// 重置所有帧事件的触发状态。
        /// </summary>
        private void ResetFrameEventTriggeredFlags()
        {
            for (int i = 0; i < m_frameEvents.Count; i++)
            {
                m_frameEvents[i] = new FrameEventEntry
                {
                    Name = m_frameEvents[i].Name,
                    NormalizedTime = m_frameEvents[i].NormalizedTime,
                    Triggered = false
                };
            }
        }

        /// <summary>
        /// 清理已完成的音频 Playable，释放 slot 供后续使用。
        /// </summary>
        private void CleanupFinishedAudioPlayables()
        {
            if (!m_isPlayableGraphValid || !m_audioMixer.IsValid())
            {
                return;
            }

            for (int i = 0; i < k_audioMixerMaxInputs; i++)
            {
                if (!m_audioSlotUsed[i])
                {
                    continue;
                }

                AudioClipPlayable audioPlayable = m_audioPlayables[i];
                if (!audioPlayable.IsValid() || audioPlayable.IsDone())
                {
                    m_audioMixer.DisconnectInput(i);
                    if (audioPlayable.IsValid())
                    {
                        audioPlayable.Destroy();
                    }
                    m_audioSlotUsed[i] = false;
                    m_audioPlayables[i] = default(AudioClipPlayable);
                    m_audioMixer.SetInputWeight(i, 0f);
                }
            }
        }

        /// <summary>
        /// 检查所有音频 slot 是否都在使用中。
        /// </summary>
        private bool IsAllAudioSlotsUsed()
        {
            for (int i = 0; i < k_audioMixerMaxInputs; i++)
            {
                if (!m_audioSlotUsed[i])
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// 查找空闲的音频 slot 索引。
        /// </summary>
        /// <returns>空闲 slot 索引，如果没有则返回 -1</returns>
        private int FindFreeAudioSlot()
        {
            for (int i = 0; i < k_audioMixerMaxInputs; i++)
            {
                if (!m_audioSlotUsed[i])
                {
                    return i;
                }
            }
            return -1;
        }

        /// <summary>
        /// 关闭并销毁 PlayableGraph，释放资源。在 OnDestroy 中调用。
        /// </summary>
        private void ShutdownPlayableGraph()
        {
            if (!m_isPlayableGraphValid)
            {
                return;
            }

            m_frameEvents.Clear();
            m_isAnimationPlaying = false;
            m_activeAnimationSlot = -1;
            m_currentAnimationClip = null;

            if (m_playableGraph.IsValid())
            {
                m_playableGraph.Stop();
                m_playableGraph.Destroy();
            }

            m_isPlayableGraphValid = false;
        }

        /// <summary>
        /// 释放本对象加载的所有 Addressables 资源句柄。在 OnDestroy 中调用。
        /// </summary>
        private void ReleaseAllLoadedAssets()
        {
            if (m_loadedAssetHandles.Count == 0)
            {
                return;
            }

            foreach (var kvp in m_loadedAssetHandles)
            {
                if (kvp.Value.IsValid())
                {
                    Addressables.Release(kvp.Value);
                }
            }
            m_loadedAssetHandles.Clear();
        }

        #endregion

        #region 动画系统方法

        /// <summary>
        /// 克隆 AnimationConfig 的 FrameEvents 数组，使每个实例拥有独立的副本。
        /// 子类在 SetupComponents 中调用此方法后，才能为各 FrameEventConfig.Target 赋值（特效、声音等），
        /// 避免修改 static readonly 模板中的共享数组。
        /// </summary>
        protected static void CloneFrameEvents(ref AnimationConfig config)
        {
            if (config.FrameEvents != null && config.FrameEvents.Length > 0)
            {
                config.FrameEvents = (FrameEventConfig[])config.FrameEvents.Clone();
            }
        }

        /// <summary>
        /// 异步加载单个 AnimationConfig 的动画剪辑。
        /// 子类在 Start 中调用此方法加载待机、移动等非攻击动画配置。
        /// </summary>
        /// <param name="config">动画配置（需已设置 ClipKey）</param>
        /// <returns>加载完成后的 AnimationConfig（Clip 已填充）</returns>
        protected async Task<AnimationConfig> LoadAnimationConfigAssetsAsync(AnimationConfig config)
        {
            if (!string.IsNullOrEmpty(config.ClipKey))
            {
                config.Clip = await LoadAssetAsync<AnimationClip>(config.ClipKey);
            }
            return config;
        }

        /// <summary>
        /// 异步加载 m_animationConfigs 中所有动画剪辑。
        /// 加载完成后将剪辑回填到对应的 AnimationConfig.Clip 中。
        /// 子类应在 Start 中调用此方法加载攻击动画配置。
        /// </summary>
        protected async Task LoadAnimationClipsAsync()
        {
            if (m_animationConfigs == null || m_animationConfigs.Length == 0)
            {
                return;
            }

            int count = m_animationConfigs.Length;
            Task<AnimationClip>[] clipTasks = new Task<AnimationClip>[count];

            for (int i = 0; i < count; i++)
            {
                clipTasks[i] = LoadAssetAsync<AnimationClip>(m_animationConfigs[i].ClipKey);
            }

            await Task.WhenAll(clipTasks);

            if (this == null)
            {
                return;
            }

            for (int i = 0; i < count; i++)
            {
                m_animationConfigs[i].Clip = clipTasks[i].Result;
            }
        }

        /// <summary>
        /// 尝试触发一次攻击。处理连击超时重置、冷却检测、动画播放、特效/声音帧事件注册。
        /// 子类在输入检测中调用此方法（如鼠标点击时）。
        /// </summary>
        /// <returns>true=攻击已触发，false=冷却中或配置无效</returns>
        protected bool TryStartAttack()
        {
            if (m_animationConfigs == null || m_animationConfigs.Length == 0)
            {
                return false;
            }

            // 连击超时重置：超过 ComboResetTime 未攻击，连击归零
            if (m_comboIndex > 0 && Time.time - m_lastAttackTime >= ComboResetTime)
            {
                m_comboIndex = 0;
            }

            // 攻击冷却：上次攻击后 AttackCooldown 内不可再次触发
            if (Time.time - m_lastAttackTime < AttackCooldown)
            {
                return false;
            }

            // 从 AnimationConfig 中获取当前连击的动画和参数
            ref AnimationConfig config = ref m_animationConfigs[m_comboIndex];
            if (config.Clip == null || config.Clip.length <= 0f)
            {
                return false;
            }

            // 通过 PlayAnimation(AnimationConfig) 统一处理动画播放、特效/声音帧事件注册
            // 攻击动画不循环（loop: false 是默认值，显式标注提高可读性）
            PlayAnimation(config, loop: false);
            m_isAttacking = true;
            m_lastAttackTime = Time.time;

            // 通知子类攻击开始（子类可在此启用武器等）
            OnAttackStarted(config);

            // 攻击开始时立即停止移动
            StopMovement();

            // 推进连击索引（循环）
            m_comboIndex = (m_comboIndex + 1) % m_animationConfigs.Length;

            return true;
        }

        /// <summary>
        /// 攻击开始时的回调。子类重写此方法来启用武器、播放声音等。
        /// </summary>
        /// <param name="config">当前攻击的动画配置（含动画、倍率、特效、声音）</param>
        protected virtual void OnAttackStarted(AnimationConfig config) { }

        /// <summary>
        /// 攻击结束时的回调。子类重写此方法来禁用武器等。
        /// 由基类 OnAnimationComplete 在攻击动画播放完成时自动调用。
        /// </summary>
        protected virtual void OnAttackEnded() { }

        #endregion
    }

    /// <summary>
    /// ObjectBase 使用说明：
    /// ============================================================
    /// 游戏对象基类（MonoBehaviour），提供组件动态管理、移动控制、属性系统、伤害系统、
    /// 动画/声音控制（Playable API）、帧事件系统、攻击连击系统、Addressables 资源管理。
    /// 子类通过重写 SetupComponents() 添加组件，重写 ObjectStatsConfig 配置属性，重写回调方法实现自定义逻辑。
    ///
    /// ════════════════════════════════════════════════════════════
    /// 【核心功能总览】
    /// ════════════════════════════════════════════════════════════
    ///   1. 组件管理：AddObjectComponent&lt;T&gt;() 动态添加并缓存组件，避免重复 GetComponent
    ///   2. 移动控制：基于 Rigidbody 的物理移动（FixedUpdate），支持自动输入或手动输入，可调节惯性
    ///   3. 属性系统：生命值、魔法值、攻击力、防御力、阵营、暴击、穿透等完整 RPG 属性
    ///   4. 伤害系统：TakeDamage/Heal/ConsumeMana 等，含阵营判定与暴击/闪避
    ///   5. Collider 自动设置：根据 Renderer 包围盒自动计算大小，支持 Capsule/Box 与 XYZ 缩放
    ///   6. 动画控制：基于 Playable API 的动画播放，支持速度调节、循环模式、CrossFade 过渡、帧事件
    ///   7. 声音控制：基于 Playable API 的音频播放，支持最多 8 个声音同时播放、音量控制
    ///   8. 帧事件系统：在动画特定时间点触发特效/声音/自定义逻辑，循环动画每轮重新触发
    ///   9. 攻击连击系统：TryStartAttack 自动处理连击递增、冷却、超时重置、动画播放、帧事件注册
    ///   10. 资源加载：基于 Addressables 异步加载 Avatar/AnimationClip/AudioClip，句柄自动管理
    ///
    /// ════════════════════════════════════════════════════════════
    /// 【配置属性（子类重写）】
    /// ════════════════════════════════════════════════════════════
    ///   - MovementConfig：移动配置（返回 null 则禁用移动系统）
    ///       子类返回 new MovementConfig(...)，Awake 时一次性缓存到 m_movementConfig
    ///       如需运行时改变配置，直接修改 m_movementConfig 字段（属性本身每帧调用会 GC）
    ///   - ObjectStatsConfig：物体属性配置（返回 null 则不启用属性系统）
    ///       子类返回配置实例，Awake 时克隆到 m_objectStats（运行时数据，修改不影响原配置）
    ///   - AttackCooldown：单次攻击冷却时间（秒），默认 0.5f，子类可重写调整
    ///   - ComboResetTime：连击超时重置时间（秒），默认 1.5f，超过此时间未攻击连击归零
    ///
    /// ════════════════════════════════════════════════════════════
    /// 【生命周期（MonoBehaviour 方法执行顺序）】
    /// ════════════════════════════════════════════════════════════
    ///   Awake（同步，基类已实现）：
    ///     1. 缓存 MovementConfig 到 m_movementConfig（避免子类属性每帧 new 产生 GC）
    ///     2. 预计算 MovementAngleOffset 对应的 Quaternion 到 m_movementAngleRotation
    ///     3. 调用 SetupComponents()（子类重写：添加组件、初始化动画/声音系统）
    ///     4. 调用 SetupObjectStats()（克隆 ObjectStatsConfig 到运行时实例 m_objectStats）
    ///     5. 标记 m_isComponentsInitialized = true
    ///
    ///   Update（基类已实现，子类重写时需调用 base.Update()）：
    ///     1. 检查 m_isComponentsInitialized，未就绪则跳过
    ///     2. HandleInput()：读取移动输入（若 IsAutoReadInput=true）
    ///     3. UpdateAnimationCrossFade()：更新动画过渡权重
    ///     4. CheckFrameEvents()：检测并触发动画帧事件
    ///     5. CleanupFinishedAudioPlayables()：清理已完成的音频 Playable
    ///
    ///   FixedUpdate（基类已实现）：
    ///     1. 检查 m_isComponentsInitialized，未就绪则跳过
    ///     2. HandleMovement()：处理物理移动（基于 Rigidbody.velocity）
    ///
    ///   OnDestroy（基类已实现）：
    ///     1. ClearComponentCache()：清空组件缓存
    ///     2. ShutdownPlayableGraph()：停止并销毁 PlayableGraph
    ///     3. ReleaseAllLoadedAssets()：释放所有 Addressables 句柄
    ///
    /// ════════════════════════════════════════════════════════════
    /// 【组件管理 API】
    /// ════════════════════════════════════════════════════════════
    ///   - AddObjectComponent&lt;T&gt;()：添加组件并缓存（已存在则返回现有组件）
    ///   - AddObjectComponent&lt;T&gt;(Action&lt;T&gt; initializer)：添加组件并通过回调初始化配置
    ///   - GetObjectComponent&lt;T&gt;()：获取缓存的组件（缓存未命中则从 GameObject 获取并缓存）
    ///   - HasObjectComponent&lt;T&gt;()：检查是否包含指定类型组件
    ///   - RemoveObjectComponent&lt;T&gt;()：移除缓存并销毁组件
    ///   - ClearComponentCache()：清空所有缓存引用（不销毁组件，OnDestroy 自动调用）
    ///   - GetAllCachedComponents()：获取所有已缓存组件的副本
    ///
    ///   设计要点：所有组件通过 AddObjectComponent 添加，统一缓存到 m_componentCache 字典，
    ///   避免热路径中重复 GetComponent 调用。子类在 SetupComponents 中使用 lambda 初始化。
    ///
    /// ════════════════════════════════════════════════════════════
    /// 【移动控制系统】
    /// ════════════════════════════════════════════════════════════
    ///   基于 Rigidbody.velocity 的物理移动，在 FixedUpdate 中应用。
    ///   使用缓存速度 m_horizontalVelocity 独立计算减速，不受物理摩擦力影响。
    ///
    ///   公共 API：
    ///     SetMovementInput(Vector3 input)：手动设置移动输入（用于 AI 控制）
    ///         输入长度 &gt; 0.1 时自动归一化，否则清零（停止移动）
    ///     StopMovement()：立即停止移动，清零缓存速度和 Rigidbody 速度（保留 Y 轴可选）
    ///
    ///   移动速度来源：ObjectStatsConfig.MoveSpeed（通过 GetMoveSpeed() 读取）
    ///       注意：MovementConfig 不包含速度参数，速度由属性系统提供
    ///
    ///   移动惯性说明（通过 MovementConfig.DecelerationRate 控制）：
    ///     0f    = 无减速，角色保持当前速度滑行不止（冰面效果）
    ///     5f    = 较大惯性，滑行明显（约 1.2 秒从 6m/s 停止）
    ///     20f   = 默认值，快速停止但有轻微惯性
    ///     200f  = 近乎立即停止（约 0.04 秒内停止）
    ///     1000f = 几乎瞬时停止（1 帧内停止）
    ///
    ///   移动方向偏移（通过 MovementConfig.MovementAngleOffset 控制，度，绕 Y 轴）：
    ///     0f    = 无偏移，WASD 按标准方向移动（W=+Z, S=-Z, A=-X, D=+X）
    ///     -45f  = 移动方向左偏 45°（补偿斜视角，W 实际朝 -X+Z 方向移动）
    ///     45f   = 移动方向右偏 45°（补偿斜视角，W 实际朝 +X+Z 方向移动）
    ///     用于等距视角（Isometric）、2.5D 等非标准朝向场景的输入方向修正。
    ///     原理：HandleInput 读取输入后用 Quaternion.Euler(0, offset, 0) 旋转移动向量。
    ///
    ///   外力响应机制：
    ///     - 检测 Rigidbody.velocity 大于缓存速度时，自动同步外力速度到缓存
    ///     - 然后按 DecelerationRate 减速（不受摩擦力影响）
    ///     - 适用场景：被爆炸推动、被其他玩家撞击等
    ///
    /// ════════════════════════════════════════════════════════════
    /// 【属性系统 API】
    /// ════════════════════════════════════════════════════════════
    ///   属性访问（返回 0 如果未配置 ObjectStatsConfig）：
    ///     GetObjectStats()：获取运行时属性实例 ObjectStatsConfig
    ///     HasObjectStats()：检查是否已配置属性
    ///
    ///   生命值：
    ///     GetCurrentHealth() / GetMaxHealth() / GetHealthPercentage()
    ///     SetCurrentHealth(float)：自动 Clamp 到 [0, MaxHealth]
    ///   魔法值：
    ///     GetCurrentMana() / GetMaxMana() / GetManaPercentage()
    ///     SetCurrentMana(float)：自动 Clamp 到 [0, MaxMana]
    ///   等级与金币：
    ///     GetLevel() / SetLevel(int) / GetGold() / SetGold(int)
    ///   攻击与速度：
    ///     GetPhysicalAttack() / SetPhysicalAttack(float)
    ///     GetMagicAttack() / SetMagicAttack(float)
    ///     GetMoveSpeed() / SetMoveSpeed(float)
    ///
    ///   属性操作（触发对应回调）：
    ///     TakeDamage(float)：受到伤害，触发 OnDamaged，死亡时触发 OnDeath
    ///     Heal(float)：恢复生命，触发 OnHealed
    ///     ConsumeMana(float)：消耗魔法（返回 bool 表示是否成功），触发 OnManaConsumed
    ///     RestoreMana(float)：恢复魔法，触发 OnManaRestored
    ///     AddExperience(float)：增加经验，触发 OnExperienceAdded
    ///     AddGold(int)：增加金币，触发 OnGoldAdded
    ///     IsDead() / IsAlive()：检查存活状态
    ///     ResetObjectStats()：重置生命和魔法到满值，触发 OnStatsReset
    ///
    ///   属性回调（子类可重写，默认空实现）：
    ///     OnDamaged(float damage) / OnHealed(float amount)
    ///     OnManaConsumed(float amount) / OnManaRestored(float amount)
    ///     OnExperienceAdded(float amount) / OnGoldAdded(int amount)
    ///     OnDeath() / OnStatsReset()
    ///
    /// ════════════════════════════════════════════════════════════
    /// 【Collider 自动设置】
    /// ════════════════════════════════════════════════════════════
    ///   - CalculateObjectBounds()：根据所有 Renderer 的 Mesh 本地包围盒计算本地空间 Bounds
    ///       使用 Mesh.bounds / SkinnedMeshRenderer.localBounds（本地空间）而非 Renderer.bounds（世界空间 AABB）
    ///       通过矩阵变换将各子 Renderer 的本地包围盒合并到本对象本地空间，对旋转对象计算更精确
    ///       没有 Renderer 时返回默认 Bounds（单位立方体）
    ///       没有 Mesh 数据时回退到世界空间 bounds
    ///
    ///   - AddCapsuleCollider(bounds, sizeMultiplier, centerOffset)：
    ///       自动选择最长的轴作为胶囊方向，计算合适的半径和高度
    ///       sizeMultiplier：XYZ 轴分别缩放因子（默认 Vector3.one 不缩放）
    ///       centerOffset：中心点偏移量（本地空间，默认 Vector3.zero 不偏移）
    ///
    ///   - AddBoxCollider(bounds, sizeMultiplier, centerOffset)：
    ///       根据包围盒设置 BoxCollider 的 size 和 center
    ///       参数同 AddCapsuleCollider
    ///
    /// ════════════════════════════════════════════════════════════
    /// 【动画与声音系统（Playable API）】
    /// ════════════════════════════════════════════════════════════
    ///   通过 Playable API 实现动画和声音播放，子类决定何时触发动作及帧事件。
    ///
    ///   动画系统架构（遵循官方最佳实践：稳定拓扑 + 节点复用）：
    ///     AnimationClipPlayable → AnimationMixerPlayable → AnimationPlayableOutput → Animator
    ///     预分配 k_animationMixerMaxInputs(8) 个槽位，切换动画时通过权重切换而非 Create/Destroy
    ///     槽位管理策略：
    ///       - 优先复用同 clip 的已有槽位（避免重复创建）
    ///       - 其次使用空闲槽位
    ///       - 最后复用非活跃槽位（覆盖旧 Playable）
    ///
    ///   音频系统架构：
    ///     AudioClipPlayable → AudioMixerPlayable → AudioPlayableOutput → AudioSource
    ///     预分配 k_audioMixerMaxInputs(8) 个 slot，支持同时播放 8 个声音
    ///     音量通过 AudioMixerPlayable.SetInputWeight() 控制（Unity 2022.3 无 AudioClipPlayable.SetVolume）
    ///     已完成的音频 Playable 自动清理（CleanupFinishedAudioPlayables 在 Update 中调用）
    ///
    ///   初始化流程（在 SetupComponents 中调用，两者共享同一 PlayableGraph，顺序无要求）：
    ///     1. SetupAnimator()     — 创建 Animator，初始化 PlayableGraph、动画混合器和动画输出
    ///     2. SetupAudioSource()  — 创建 AudioSource，初始化音频输出和混合器
    ///
    ///   动画控制 API：
    ///     PlayAnimation(config, speed, loop, crossFadeTime) — 播放动画（自动分配/复用槽位，权重切换）
    ///       config: AnimationConfig（含 ClipKey/Clip/Multiplier/FrameEvents），帧事件自动注册
    ///       speed/loop/crossFadeTime: 播放参数，默认 speed=1, loop=false, crossFadeTime=0
    ///       crossFadeTime=0: 立即切换（默认，极致性能）
    ///       crossFadeTime&gt;0: 平滑过渡（源动画权重 1→0，目标动画权重 0→1）
    ///     StopAnimation()                   — 停止当前动画（权重归零，不销毁 Playable）
    ///     SetAnimationSpeed(speed)          — 实时调整播放速度
    ///     GetAnimationNormalizedTime()      — 获取归一化播放时间（0~1），使用缓存 clip 避免每帧跨域调用
    ///
    ///   声音控制 API：
    ///     PlaySound(clip, volume)           — 播放声音（支持最多 8 个同时播放，slot 满时自动清理已完成）
    ///     PlaySoundAsync(clipKey, volume)   — 异步加载 AudioClip 并播放
    ///     StopAllSounds()                   — 停止所有声音（销毁所有 AudioClipPlayable）
    ///
    /// ════════════════════════════════════════════════════════════
    /// 【帧事件系统】
    /// ════════════════════════════════════════════════════════════
    ///   在动画播放到指定归一化时间（0~1）时自动触发回调，用于同步播放特效/声音/自定义逻辑。
    ///
    ///   帧 API：
    ///     RegisterFrameEvent(name, time)    — 注册帧事件（time 为 0~1 归一化时间）
    ///     ClearFrameEvents()                — 清除所有帧事件
    ///     OnAnimationFrameEvent(name)       — 子类重写：帧事件触发时的回调（基类已实现内置处理）
    ///     OnAnimationCustomFrameEvent(name) — 子类重写：处理 TargetType==Custom 的帧事件
    ///     OnAnimationComplete()             — 子类重写：非循环动画播放完成时的回调
    ///
    ///   内置帧事件类型（FrameEventTargetType，基类自动执行）：
    ///     PlayParticleSystem  — Target 须为 ParticleSystem，触发时调用 Play()
    ///     StopParticleSystem  — Target 须为 ParticleSystem，触发时调用 Stop()
    ///     PlayAudioClip       — Target 须为 AudioClip，FloatParam 为音量（0=默认 1.0f）
    ///     PlayVisualEffect    — Target 须为 VisualEffect，触发时调用 Play()
    ///     StopVisualEffect    — Target 须为 VisualEffect，触发时调用 Stop()
    ///     Custom              — 通过 OnAnimationCustomFrameEvent(eventName) 回调子类处理
    ///     注意：Target 为 null 时自动跳过该事件
    ///
    ///   通过 AnimationConfig 注册多帧事件（推荐方式）：
    ///     config.FrameEvents = new FrameEventConfig[]
    ///     {
    ///         new FrameEventConfig { TriggerTime = 0.2f, TargetType = PlayParticleSystem },
    ///         new FrameEventConfig { TriggerTime = 0.4f, TargetType = Custom, EventName = "HitImpact" }
    ///     };
    ///     PlayAnimation(config);  // 自动按 TriggerTime 换算为归一化时间并注册（loop/speed 等通过参数传入）
    ///
    ///   帧事件特性：
    ///     - 循环动画中每轮都会重新触发已注册的帧事件（自动重置 Triggered 标志）
    ///     - 非循环动画在播放完成时触发 OnAnimationComplete
    ///     - 帧事件检查在 Update 中进行，与渲染帧同步
    ///     - 内置事件基类自动处理，自定义事件通过 OnAnimationCustomFrameEvent 回调
    ///
    /// ════════════════════════════════════════════════════════════
    /// 【攻击连击系统】
    /// ════════════════════════════════════════════════════════════
    ///   通过 TryStartAttack() 触发攻击，自动处理连击递增、冷却、超时重置、动画播放、帧事件注册。
    ///
    ///   攻击 API：
    ///     TryStartAttack() — 尝试触发一次攻击，返回 bool 表示是否成功
    ///     OnAttackStarted(config) — 子类重写：攻击开始回调（启用武器等）
    ///     OnAttackEnded() — 子类重写：攻击结束回调（禁用武器等）
    ///
    ///   TryStartAttack 内部流程：
    ///     1. 检查 m_animationConfigs 是否已初始化
    ///     2. 连击超时重置：超过 ComboResetTime 未攻击，连击归零
    ///     3. 攻击冷却检测：上次攻击后 AttackCooldown 内不可再次触发
    ///     4. 通过 PlayAnimation(AnimationConfig) 播放当前连击动画（含帧事件注册）
    ///     5. 标记 m_isAttacking = true，记录 m_lastAttackTime
    ///     6. 调用 OnAttackStarted(config) 通知子类
    ///     7. 调用 StopMovement() 停止移动
    ///     8. 推进连击索引：m_comboIndex = (m_comboIndex + 1) % m_animationConfigs.Length
    ///
    ///   攻击动画完成流程：
    ///     1. 基类 OnAnimationComplete 检测到 m_isAttacking == true
    ///     2. 重置 m_isAttacking = false
    ///     3. 清除帧事件 ClearFrameEvents()
    ///     4. 调用 OnAttackEnded() 通知子类
    ///     子类重写 OnAnimationComplete 时需调用 base.OnAnimationComplete() 保留上述逻辑
    ///
    ///   AnimationConfig 配置（推荐用于攻击动画）：
    ///     ClipKey      — Addressables 地址键
    ///     Clip         — 加载后的 AnimationClip（LoadAnimationClipsAsync 赋值）
    ///     Multiplier   — 属性倍率（差异化每个攻击的伤害/暴击等，详见 ObjectStatsConfigMultiplier）
    ///     FrameEvents  — 帧事件列表（特效/声音/自定义事件，需 CloneFrameEvents 后赋值 Target）
    ///   播放参数通过 PlayAnimation 方法参数传入（speed/loop/crossFadeTime），不属于 AnimationConfig
    ///
    ///   动画系统辅助 API：
    ///     CloneFrameEvents(ref config)            — 克隆 FrameEvents 数组（实例独立副本）
    ///     LoadAnimationConfigAssetsAsync(config)  — 异步加载单个配置的 Clip
    ///     LoadAnimationClipsAsync()               — 批量加载 m_animationConfigs 中所有 Clip
    ///
    /// ════════════════════════════════════════════════════════════
    /// 【Addressables 资源管理】
    /// ════════════════════════════════════════════════════════════
    ///   基于 Addressables 异步加载资源，句柄由 ObjectBase 自动管理。
    ///
    ///   异步加载 API：
    ///     SetupAnimatorAsync(avatarKey)     — 异步加载 Avatar（骨骼文件）并创建 Animator
    ///     PlaySoundAsync(clipKey, volume)   — 异步加载 AudioClip 并播放
    ///     LoadAssetAsync&lt;T&gt;(key)            — 通用异步加载（返回 T 资源，自动缓存句柄）
    ///     LoadAnimationConfigAssetsAsync(config) — 异步加载 AnimationConfig 中的 Clip（推荐模式）
    ///
    ///   资源管理特性：
    ///     - 同一对象对同一 key 的重复加载会命中缓存（m_loadedAssetHandles 字典）
    ///     - 加载的句柄在 OnDestroy 时通过 ReleaseAllLoadedAssets() 统一释放
    ///     - 加载失败时自动释放句柄并返回 null（不会泄漏）
    ///     - 加载期间对象若被销毁，异步方法通过 this == null 检查安全退出
    ///     - Addressables 内部有引用计数，多对象加载同一资源底层只加载一次
    ///
    ///   推荐加载模式：
    ///     - SetupComponents 中同步创建 Rigidbody/Collider/AudioSource（组件无需异步加载）
    ///     - 在 async void Start 中 await 异步加载（Animator/动画/声音资源）
    ///     - 使用 Task.WhenAll 并行加载多个资源优化加载速度
    ///
    /// ════════════════════════════════════════════════════════════
    /// 【性能优化设计（针对 MMO 热路径）】
    /// ════════════════════════════════════════════════════════════
    ///   1. MovementConfig 缓存：
    ///      - 子类属性返回 new 实例，Awake 时一次性缓存到 m_movementConfig
    ///      - Update/FixedUpdate 使用缓存字段，避免每帧 new 产生 GC
    ///
    ///   2. 移动方向偏移旋转预计算：
    ///      - MovementAngleOffset 对应的 Quaternion.Euler 在 Awake 预计算到 m_movementAngleRotation
    ///      - 通过 m_hasMovementAngleOffset 布尔标志避免每帧比较 Quaternion
    ///
    ///   3. 减速计算消除重复开方：
    ///      - HandleMovement 中复用 currentSpeed：m_horizontalVelocity * (newSpeed / currentSpeed)
    ///      - 避免原 .normalized 内部的第二次 Mathf.Sqrt
    ///
    ///   4. CalculateObjectBounds 零分配：
    ///      - 使用静态缓冲区 s_rendererBuffer 替代 GetComponentsInChildren&lt;Renderer&gt;() 数组分配
    ///      - 缓存 transform.worldToLocalMatrix 避免循环内重复计算
    ///      - 使用 TryGetComponent&lt;MeshFilter&gt; 替代 GetComponent + null 检查
    ///
    ///   5. 动画系统稳定拓扑：
    ///      - 预分配 k_animationMixerMaxInputs(8) 个槽位，运行时不扩展
    ///      - 切换动画通过权重切换而非 Create/Destroy，减少 GC
    ///      - 缓存 m_currentAnimationClip 避免 CheckFrameEvents 每帧调用 GetAnimationClip()
    ///
    ///   6. Debug.Log 剥离：
    ///      - AddCapsuleCollider / AddBoxCollider 中的调试日志包裹 #if UNITY_EDITOR
    ///      - 生产构建中完全剥离，避免字符串插值的 GC 分配
    ///      - Debug.LogError（如 Addressables 加载失败）保留在构建中用于错误上报
    ///
    /// ════════════════════════════════════════════════════════════
    /// 【子类需要重写的方法/属性】
    /// ════════════════════════════════════════════════════════════
    ///   必须重写：
    ///     - SetupComponents()：添加 Rigidbody、Collider 等组件，调用 SetupAnimator/SetupAudioSource
    ///     - ObjectStatsConfig：配置物体的所有数值属性
    ///
    ///   可选重写：
    ///     - MovementConfig：配置移动行为（返回 null 则禁用移动）
    ///     - AttackCooldown / ComboResetTime：调整攻击冷却和连击超时
    ///     - OnDamaged/OnHealed/OnDeath 等属性回调：自定义事件响应逻辑
    ///     - OnAnimationFrameEvent/OnAnimationCustomFrameEvent：动画帧事件回调
    ///     - OnAnimationComplete：非循环动画完成回调（攻击结束自动处理）
    ///     - OnAttackStarted/OnAttackEnded：攻击开始/结束回调
    ///     - Update()：子类重写时需调用 base.Update() 保留基类逻辑
    ///
    /// ────────────────────────────────────────────────────────────
    /// 示例 1：完整的角色子类（玩家控制 + 近乎立即停止）
    /// ────────────────────────────────────────────────────────────
    /// <code>
    /// public class Warrior : ObjectBase
    /// {
    ///     // 配置移动：保持重力，自动读取键盘输入，近乎立即停止，视角偏移
    ///     protected override MovementConfig MovementConfig =&gt; new MovementConfig(
    ///         keepYVelocity: true,
    ///         autoReadInput: true,
    ///         decelerationRate: 200f,    // 近乎立即停止（约 0.04 秒内停止）
    ///         movementAngleOffset: -45f  // 移动方向左偏45度，补偿视角偏差
    ///     );
    ///
    ///     // 配置属性
    ///     protected override ObjectStatsConfig ObjectStatsConfig =&gt; new ObjectStatsConfig
    ///     {
    ///         Type = ObjectType.Warrior,
    ///         FactionID = 1,
    ///         MaxHealth = 150f,
    ///         CurrentHealth = 150f,
    ///         MoveSpeed = 6f
    ///     };
    ///
    ///     protected override void SetupComponents()
    ///     {
    ///         base.SetupComponents();
    ///
    ///         m_rigidbody = AddObjectComponent&lt;Rigidbody&gt;(rb =&gt;
    ///         {
    ///             rb.mass = 1f;
    ///             rb.useGravity = true;
    ///             rb.constraints = RigidbodyConstraints.FreezeRotation;
    ///         });
    ///
    ///         // CapsuleCollider 自动匹配对象大小
    ///         AddCapsuleCollider(CalculateObjectBounds(), Vector3.one);
    ///     }
    ///
    ///     protected override void OnDamaged(float damage)
    ///     {
    ///         Debug.Log($"受到 {damage} 点伤害！");
    ///     }
    ///
    ///     protected override void OnDeath()
    ///     {
    ///         StopMovement();
    ///     }
    /// }
    /// </code>
    ///
    /// ────────────────────────────────────────────────────────────
    /// 示例 2：Collider 的各种使用方式
    /// ────────────────────────────────────────────────────────────
    /// <code>
    /// protected override void SetupComponents()
    /// {
    ///     base.SetupComponents();
    ///
    ///     // 方式 1：CapsuleCollider 精确包裹对象
    ///     AddCapsuleCollider(CalculateObjectBounds(), Vector3.one);
    ///
    ///     // 方式 2：CapsuleCollider XYZ 分别缩放（怪物碰撞体 XZ 放大 10% 便于命中）
    ///     AddCapsuleCollider(CalculateObjectBounds(), new Vector3(1.1f, 1f, 1.1f));
    ///
    ///     // 方式 3：CapsuleCollider 整体缩小 20%
    ///     AddCapsuleCollider(CalculateObjectBounds(), new Vector3(0.8f, 0.8f, 0.8f));
    ///
    ///     // 方式 4：CapsuleCollider 中心偏移 - 单轴移动
    ///     AddCapsuleCollider(CalculateObjectBounds(), Vector3.one, Vector3.up * 0.1f);      // 上移 0.1 单位
    ///     AddCapsuleCollider(CalculateObjectBounds(), Vector3.one, Vector3.down * 0.1f);    // 下移 0.1 单位
    ///     AddCapsuleCollider(CalculateObjectBounds(), Vector3.one, Vector3.left * 0.1f);    // 左移 0.1 单位
    ///     AddCapsuleCollider(CalculateObjectBounds(), Vector3.one, Vector3.right * 0.1f);   // 右移 0.1 单位
    ///     AddCapsuleCollider(CalculateObjectBounds(), Vector3.one, Vector3.forward * 0.1f); // 前移 0.1 单位
    ///     AddCapsuleCollider(CalculateObjectBounds(), Vector3.one, Vector3.back * 0.1f);    // 后移 0.1 单位
    ///     // 方式 5：CapsuleCollider 中心偏移 - 多轴组合（同时上移和前移）
    ///     AddCapsuleCollider(CalculateObjectBounds(), Vector3.one, new Vector3(0f, 0.1f, 0.05f));
    //      AddCapsuleCollider(CalculateObjectBounds(), Vector3.one, new Vector3(-1f, 1f, 2f));  // 向左移动1，向前移动2，向上移动1`
    ///
    ///     // 方式 6：CapsuleCollider 缩放 + 中心偏移组合使用
    ///     AddCapsuleCollider(CalculateObjectBounds(), new Vector3(0.9f, 1f, 0.9f), Vector3.up * 0.15f);
    ///
    ///     // 方式 7：BoxCollider 精确包裹（适合箱子、建筑等方形物体）
    ///     AddBoxCollider(CalculateObjectBounds(), Vector3.one);
    ///
    ///     // 方式 8：BoxCollider Y 轴放大（适合需要更高触发范围的陷阱）
    ///     AddBoxCollider(CalculateObjectBounds(), new Vector3(1f, 2f, 1f));
    ///
    ///     // 方式 9：BoxCollider 中心偏移 - 单轴移动
    ///     AddBoxCollider(CalculateObjectBounds(), Vector3.one, Vector3.up * 0.1f);      // 上移
    ///     AddBoxCollider(CalculateObjectBounds(), Vector3.one, Vector3.down * 0.05f);   // 下移（底部对齐地面）
    ///     AddBoxCollider(CalculateObjectBounds(), Vector3.one, Vector3.left * 0.1f);    // 左移
    ///     AddBoxCollider(CalculateObjectBounds(), Vector3.one, Vector3.right * 0.1f);   // 右移
    ///
    ///     // 方式 10：BoxCollider 中心偏移 - 多轴组合
    ///     AddBoxCollider(CalculateObjectBounds(), Vector3.one, new Vector3(0.1f, 0.1f, 0f)); // 右移+上移
    ///
    ///     // 方式 11：使用原始 AddObjectComponent 手动配置（完全自定义）
    ///     AddObjectComponent&lt;SphereCollider&gt;(c =&gt;
    ///     {
    ///         c.isTrigger = true;
    ///         c.radius = 5f;
    ///         c.center = Vector3.zero;
    ///     });
    /// }
    /// </code>
    /// ────────────────────────────────────────────────────────────
    /// 示例 3：移动控制（手动输入 + 惯性配置）
    /// ────────────────────────────────────────────────────────────
    /// <code>
    /// public class Monster : ObjectBase
    /// {
    ///     // 关闭自动输入，由 AI 脚本控制，设置中等惯性
    ///     protected override MovementConfig MovementConfig =&gt; new MovementConfig(
    ///         keepYVelocity: true,
    ///         autoReadInput: false,
    ///         decelerationRate: 10f     // 怪物有中等惯性，停止时稍微滑行
    ///     );
    ///
    ///     protected override ObjectStatsConfig ObjectStatsConfig =&gt; new ObjectStatsConfig
    ///     {
    ///         Type = ObjectType.Tank,
    ///         FactionID = 2,
    ///         MoveSpeed = 4f,
    ///         MaxHealth = 100f,
    ///         CurrentHealth = 100f
    ///     };
    /// }
    ///
    /// // 在 AI 脚本中控制怪物移动
    /// public class MonsterAI : MonoBehaviour
    /// {
    ///     [SerializeField] private Monster m_monster;
    ///
    ///     private void Update()
    ///     {
    ///         // 朝玩家方向移动
    ///         Vector3 direction = (player.position - m_monster.transform.position).normalized;
    ///         m_monster.SetMovementInput(direction);
    ///     }
    ///
    ///     private void FixedUpdate()
    ///     {
    ///         // 到达目标后停止
    ///         if (Vector3.Distance(transform.position, player.position) &lt; 2f)
    ///         {
    ///             m_monster.StopMovement();
    ///         }
    ///     }
    /// }
    /// </code>
    ///
    /// ────────────────────────────────────────────────────────────
    /// 示例 4：投射物/陷阱（禁用移动，使用触发器）
    /// ────────────────────────────────────────────────────────────
    /// <code>
    /// public class FireTrap : ObjectBase
    /// {
    ///     // 返回 null 禁用移动控制
    ///     protected override MovementConfig MovementConfig =&gt; null;
    ///
    ///     protected override ObjectStatsConfig ObjectStatsConfig =&gt; new ObjectStatsConfig
    ///     {
    ///         Type = ObjectType.Trap,
    ///         FactionID = 100,
    ///         BaseDamage = 10f,
    ///         DamageType = DamageType.Magic,
    ///         DamageRadius = 5f,
    ///         IsContinuousDamage = true,
    ///         DamageInterval = 0.5f
    ///     };
    ///
    ///     protected override void SetupComponents()
    ///     {
    ///         base.SetupComponents();
    ///
    ///         // 投射物/陷阱通常用触发器而非物理碰撞
    ///         AddObjectComponent&lt;SphereCollider&gt;(c =&gt;
    ///         {
    ///             c.isTrigger = true;
    ///             c.radius = ObjectStatsConfig.DamageRadius;
    ///         });
    ///     }
    /// }
    /// </code>
    ///
    /// ────────────────────────────────────────────────────────────
    /// 示例 5：外部调用（伤害、属性、组件访问）
    /// ────────────────────────────────────────────────────────────
    /// <code>
    /// // 伤害系统
    /// warrior.TakeDamage(20f);               // 造成 20 点伤害
    /// warrior.Heal(10f);                     // 恢复 10 点生命
    /// warrior.ConsumeMana(15f);              // 消耗 15 点魔法（返回 bool 表示是否成功）
    /// warrior.RestoreMana(5f);               // 恢复 5 点魔法
    ///
    /// // 属性查询
    /// float hp = warrior.GetCurrentHealth();
    /// float maxHp = warrior.GetMaxHealth();
    /// float hpPercent = warrior.GetHealthPercentage();  // 0-1
    /// float speed = warrior.GetMoveSpeed();
    /// bool isDead = warrior.IsDead();
    /// bool isAlive = warrior.IsAlive();
    ///
    /// // 属性设置
    /// warrior.SetMoveSpeed(8f);              // 修改移动速度
    /// warrior.SetCurrentHealth(100f);        // 直接设置生命值
    /// warrior.AddExperience(50f);            // 增加经验
    /// warrior.AddGold(100);                  // 增加金币
    ///
    /// // 组件访问
    /// Rigidbody rb = warrior.GetObjectComponent&lt;Rigidbody&gt;();
    /// bool hasCollider = warrior.HasObjectComponent&lt;CapsuleCollider&gt;();
    ///
    /// // 重置属性
    /// warrior.ResetObjectStats();            // 重置生命和魔法到满值
    /// </code>
    ///
    /// ────────────────────────────────────────────────────────────
    /// 示例 6：完整回调方法重写
    /// ────────────────────────────────────────────────────────────
    /// <code>
    /// public class Boss : ObjectBase
    /// {
    ///     protected override void OnDamaged(float damage)
    ///     {
    ///         Debug.Log($"Boss 受到 {damage} 伤害，剩余 {GetCurrentHealth()}");
    ///         // 可以触发受击动画、音效等
    ///     }
    ///
    ///     protected override void OnHealed(float amount)
    ///     {
    ///         Debug.Log($"Boss 恢复 {amount} 生命");
    ///     }
    ///
    ///     protected override void OnManaConsumed(float amount)
    ///     {
    ///         Debug.Log($"Boss 消耗 {amount} 魔法");
    ///     }
    ///
    ///     protected override void OnManaRestored(float amount)
    ///     {
    ///         Debug.Log($"Boss 恢复 {amount} 魔法");
    ///     }
    ///
    ///     protected override void OnExperienceAdded(float amount)
    ///     {
    ///         Debug.Log($"Boss 获得 {amount} 经验");
    ///     }
    ///
    ///     protected override void OnGoldAdded(int amount)
    ///     {
    ///         Debug.Log($"Boss 获得 {amount} 金币");
    ///     }
    ///
    ///     protected override void OnDeath()
    ///     {
    ///         Debug.Log("Boss 死亡！");
    ///         StopMovement();
    ///         // 触发死亡动画、掉落物品等
    ///     }
    ///
    ///     protected override void OnStatsReset()
    ///     {
    ///         Debug.Log("Boss 属性已重置");
    ///     }
    /// }
    /// </code>
    ///
    /// ────────────────────────────────────────────────────────────
    /// 示例 7：外力响应与减速行为说明
    /// ────────────────────────────────────────────────────────────
    /// 【外力响应机制】
    ///   当角色被其他物体推动（碰撞、爆炸、推力等）时：
    ///   1. 检测到 Rigidbody.velocity 大于缓存速度 → 自动同步外力速度到缓存
    ///   2. 然后按照 DecelerationRate 逐渐减速（不受摩擦力影响）
    ///
    /// 【行为示例】
    /// <code>
    /// // 场景：角色以 6m/s 移动，突然被爆炸推动到 15m/s
    /// // 1. 爆炸瞬间：Rigidbody.velocity = 15m/s，缓存自动同步到 15m/s
    /// // 2. 松开按键后：基于缓存 15m/s，按 DecelerationRate 减速
    /// // 3. 如果 DecelerationRate = 20f：约 0.75 秒后完全停止
    ///
    /// // 场景：角色站立不动（缓存 0m/s），被其他玩家推到 3m/s
    /// // 1. 推动瞬间：Rigidbody.velocity = 3m/s，缓存自动同步到 3m/s
    /// // 2. 无输入：基于缓存 3m/s，按 DecelerationRate 减速
    /// // 3. 如果 DecelerationRate = 20f：约 0.15 秒后完全停止
    ///
    /// // 场景：角色移动中，地面摩擦力让 Rigidbody.velocity 从 6m/s 降到 5m/s
    /// // 1. 检测：Rigidbody.velocity (5m/s) 小于 缓存 (6m/s)
    /// // 2. 不同步缓存（忽略摩擦力干扰）
    /// // 3. 基于缓存 6m/s，按 DecelerationRate 减速，精确匹配配置
    /// </code>
    ///
    /// ────────────────────────────────────────────────────────────
    /// 示例 8：动画与声音基础使用（Playable API）
    /// ────────────────────────────────────────────────────────────
    /// <code>
    /// public class AnimatedCharacter : ObjectBase
    /// {
    ///     private static readonly AnimationConfig k_idle = new AnimationConfig { ClipKey = "Anim/Idle" };
    ///     private static readonly AnimationConfig k_walk = new AnimationConfig { ClipKey = "Anim/Walk" };
    ///     private AnimationConfig m_idleConfig = k_idle;
    ///     private AnimationConfig m_walkConfig = k_walk;
    ///     [SerializeField] private AudioClip m_footstepClip;
    ///
    ///     protected override void SetupComponents()
    ///     {
    ///         base.SetupComponents();
    ///         m_rigidbody = AddObjectComponent&lt;Rigidbody&gt;(rb =&gt;
    ///         {
    ///             rb.useGravity = true;
    ///             rb.constraints = RigidbodyConstraints.FreezeRotation;
    ///         });
    ///         AddCapsuleCollider(CalculateObjectBounds(), Vector3.one);
    ///         CloneFrameEvents(ref m_idleConfig);
    ///         CloneFrameEvents(ref m_walkConfig);
    ///     }
    ///
    ///     private async void Start()
    ///     {
    ///         await SetupAnimatorAsync("PlayerAvatar");
    ///         var idleTask = LoadAnimationConfigAssetsAsync(m_idleConfig);
    ///         var walkTask = LoadAnimationConfigAssetsAsync(m_walkConfig);
    ///         await Task.WhenAll(idleTask, walkTask);
    ///         if (this == null) return;
    ///         m_idleConfig = idleTask.Result;
    ///         m_walkConfig = walkTask.Result;
    ///         PlayAnimation(m_idleConfig, loop: true);
    ///     }
    ///
    ///     private void Update()
    ///     {
    ///         float h = Input.GetAxisRaw("Horizontal");
    ///         float v = Input.GetAxisRaw("Vertical");
    ///         if (h != 0f || v != 0f)
    ///             PlayAnimation(m_walkConfig, loop: true, crossFadeTime: 0.2f);
    ///         else
    ///             PlayAnimation(m_idleConfig, loop: true, crossFadeTime: 0.2f);
    ///     }
    ///
    ///     public void PlayFootstep() { PlaySound(m_footstepClip, 0.5f); }
    /// }
    /// </code>
    ///
    /// 【CrossFade 说明】
    ///   crossFadeTime=0（默认）: 立即切换，极致性能，适合高频战斗动画
    ///   crossFadeTime>0: 平滑过渡，源动画权重从 1 渐变到 0，目标动画权重从 0 渐变到 1
    ///   性能影响：过渡期间每帧 2 次 SetInputWeight（轻量 float 设置），MMO 场景可接受
    ///   建议值：0.1~0.3 秒用于普通动画切换（Idle/Run），0 用于高频战斗动画（Attack）
    ///
    /// ────────────────────────────────────────────────────────────
    /// 示例 9：帧事件（通过 AnimationConfig.FrameEvents 配置）
    /// ────────────────────────────────────────────────────────────
    /// 【原理】
    ///   帧事件通过 AnimationConfig.FrameEvents 配置，PlayAnimation 时自动注册。
    ///   动画播放到指定归一化时间（0~1）时自动触发 OnAnimationFrameEvent 回调。
    ///   循环动画中每轮都会重新触发。
    ///
    /// <code>
    /// public class AttackingCharacter : ObjectBase
    /// {
    ///     private static readonly AnimationConfig k_idle = new AnimationConfig { ClipKey = "Anim/Idle" };
    ///     private static readonly AnimationConfig k_attack = new AnimationConfig
    ///     {
    ///         ClipKey = "Anim/Attack",
    ///         FrameEvents = new FrameEventConfig[]
    ///         {
    ///             // 0.3 = 挥剑到最高点，播放挥剑音效
    ///             new FrameEventConfig { TriggerTime = 0.3f, TargetType = FrameEventTargetType.PlayAudioClip },
    ///             // 0.5 = 剑挥到前方，生成命中特效
    ///             new FrameEventConfig { TriggerTime = 0.5f, TargetType = FrameEventTargetType.PlayParticleSystem },
    ///             // 0.8 = 收剑，自定义事件
    ///             new FrameEventConfig { TriggerTime = 0.8f, TargetType = FrameEventTargetType.Custom, EventName = "Recover" }
    ///         }
    ///     };
    ///     private AnimationConfig m_idleConfig = k_idle;
    ///     private AnimationConfig m_attackConfig = k_attack;
    ///
    ///     protected override void SetupComponents()
    ///     {
    ///         base.SetupComponents();
    ///         m_rigidbody = AddObjectComponent&lt;Rigidbody&gt;();
    ///         AddCapsuleCollider(CalculateObjectBounds(), Vector3.one);
    ///         CloneFrameEvents(ref m_idleConfig);
    ///         CloneFrameEvents(ref m_attackConfig);
    ///     }
    ///
    ///     private async void Start()
    ///     {
    ///         await SetupAnimatorAsync("PlayerAvatar");
    ///         var idleTask = LoadAnimationConfigAssetsAsync(m_idleConfig);
    ///         var attackTask = LoadAnimationConfigAssetsAsync(m_attackConfig);
    ///         await Task.WhenAll(idleTask, attackTask);
    ///         if (this == null) return;
    ///         m_idleConfig = idleTask.Result;
    ///         m_attackConfig = attackTask.Result;
    ///         PlayAnimation(m_idleConfig, loop: true);
    ///     }
    ///
    ///     public void Attack()
    ///     {
    ///         // 播放攻击动画，帧事件自动注册
    ///         PlayAnimation(m_attackConfig, loop: false);
    ///     }
    ///
    ///     // 帧事件回调
    ///     protected override void OnAnimationFrameEvent(FrameEventConfig evt)
    ///     {
    ///         // TargetType 决定帧事件类型，Target 在 SetupComponents/Start 中赋值
    ///     }
    ///
    ///     protected override void OnAnimationComplete()
    ///     {
    ///         base.OnAnimationComplete();
    ///         PlayAnimation(m_idleConfig, loop: true);
    ///     }
    /// }
    /// </code>
    ///
    /// ────────────────────────────────────────────────────────────
    /// 示例 10：动画速度控制与多声音混合
    /// ────────────────────────────────────────────────────────────
    /// <code>
    /// // 根据攻击速度属性加快动画播放
    /// float attackSpeed = GetAttackSpeed(); // 从属性系统获取
    /// PlayAnimation(m_attackConfig, speed: attackSpeed, loop: false);
    ///
    /// // 同时播放多个声音（最多 8 个）
    /// PlaySound(m_backgroundMusic, 0.3f);  // 背景音乐
    /// PlaySound(m_voiceLine, 1f);          // 角色语音
    /// PlaySound(m_swingClip, 0.7f);        // 挥剑音效
    ///
    /// // 停止所有声音
    /// StopAllSounds();
    ///
    /// // 实时调整动画速度（如慢动作效果）
    /// SetAnimationSpeed(0.3f);  // 慢动作
    /// SetAnimationSpeed(1f);    // 恢复正常
    /// </code>
    ///
    /// ────────────────────────────────────────────────────────────
    /// 示例 11：使用 Addressables 异步加载资源（骨骼、动画、声音）
    /// ────────────────────────────────────────────────────────────
    /// 【说明】
    ///   使用 Addressables 官方推荐方式异步加载资源，子类通过 key 加载：
    ///     - SetupAnimatorAsync(avatarKey)  — 加载 Avatar（骨骼文件）并创建 Animator
    ///     - LoadAnimationConfigAssetsAsync(config) — 加载 AnimationConfig 中的 Clip（推荐）
    ///     - LoadAnimationClipsAsync()      — 加载 m_animationConfigs 数组中所有 Clip
    ///     - PlaySoundAsync(clipKey)        — 加载 AudioClip 并播放
    ///     - LoadAssetAsync&lt;T&gt;(key)         — 通用异步加载
    ///   加载的句柄由 ObjectBase 自动管理，OnDestroy 时统一释放，无需子类手动释放。
    ///
    ///   推荐模式：SetupComponents 中同步创建 Rigidbody/Collider/AudioSource，
    ///   在 async void Start 中并行 await 异步加载（Animator/动画/声音资源），
    ///   加载完成后通过 PlayAnimation(AnimationConfig) 播放动画——零延迟。
    ///
    ///   ⚠️ 不推荐在播放时才加载动画（PlayAnimationAsync 已删除）：
    ///     - 播放时加载会产生延迟，用户体验差
    ///     - MMO 场景下战斗频繁，首次播放延迟会破坏节奏
    ///     - 正确做法是预加载所有可能用到的动画，播放时直接切换
    ///
    /// <code>
    /// public class AddressablesCharacter : ObjectBase
    /// {
    ///     // Addressables 资源键（在 Addressables Groups 窗口中配置地址）
    ///     private const string k_avatarKey = "PlayerAvatar";
    ///     private const string k_footstepKey = "Audio/Footstep";
    ///
    ///     // 动画配置模板（static readonly，Clip 在 Start 中加载填充）
    ///     private static readonly AnimationConfig k_idle = new AnimationConfig
    ///     {
    ///         ClipKey = "Anim/Idle",
    ///         Loop = true
    ///     };
    ///     private static readonly AnimationConfig k_walk = new AnimationConfig
    ///     {
    ///         ClipKey = "Anim/Walk",
    ///         Loop = true
    ///     };
    ///
    ///     // 实例配置（运行时填充 Clip）
    ///     private AnimationConfig m_idleConfig = k_idle;
    ///     private AnimationConfig m_walkConfig = k_walk;
    ///
    ///     protected override void SetupComponents()
    ///     {
    ///         base.SetupComponents();
    ///
    ///         m_rigidbody = AddObjectComponent&lt;Rigidbody&gt;(rb =&gt;
    ///         {
    ///             rb.useGravity = true;
    ///             rb.constraints = RigidbodyConstraints.FreezeRotation;
    ///         });
    ///         AddCapsuleCollider(CalculateObjectBounds(), Vector3.one);
    ///
    ///         // 音频源同步创建（组件无需异步加载）
    ///         SetupAudioSource(src =&gt; src.spatialBlend = 1f);
    ///
    ///         // 克隆 FrameEvents 数组（实例独立副本）
    ///         CloneFrameEvents(ref m_idleConfig);
    ///         CloneFrameEvents(ref m_walkConfig);
    ///     }
    ///
    ///     // 在 Start 中并行执行异步加载（Awake/SetupComponents 是同步的）
    ///     private async void Start()
    ///     {
    ///         // 并行加载：骨骼 + 动画配置
    ///         Task animatorTask = SetupAnimatorAsync(k_avatarKey);
    ///         Task&lt;AnimationConfig&gt; idleTask = LoadAnimationConfigAssetsAsync(m_idleConfig);
    ///         Task&lt;AnimationConfig&gt; walkTask = LoadAnimationConfigAssetsAsync(m_walkConfig);
    ///
    ///         await Task.WhenAll(animatorTask, idleTask, walkTask);
    ///
    ///         // 再次安全检查（加载期间可能销毁）
    ///         if (this == null) return;
    ///
    ///         // 回填加载完成的配置
    ///         m_idleConfig = idleTask.Result;
    ///         m_walkConfig = walkTask.Result;
    ///
    ///         // 播放待机动画——零延迟，Clip 已预加载
    ///         PlayAnimation(m_idleConfig);
    ///     }
    ///
    ///     // 外部调用：切换到行走动画——零延迟，Clip 已预加载
    ///     public void Walk()
    ///     {
    ///         PlayAnimation(m_walkConfig);
    ///     }
    ///
    ///     // 外部调用：播放脚步声
    ///     public async Task PlayFootstepAsync()
    ///     {
    ///         await PlaySoundAsync(k_footstepKey, volume: 0.5f);
    ///     }
    /// }
    /// </code>
    ///
    /// 【资源释放说明】
    ///   - ObjectBase 在 OnDestroy 中自动调用 ReleaseAllLoadedAssets() 释放所有句柄
    ///   - 同一对象对同一 key 的重复加载会命中缓存，避免重复请求
    ///   - Addressables 内部有引用计数，多个对象加载同一资源时底层只加载一次
    ///   - 加载期间对象若被销毁，异步方法会通过 this == null 检查安全退出
    /// </summary>
}