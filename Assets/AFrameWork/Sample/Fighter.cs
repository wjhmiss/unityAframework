using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.VFX;
using AFrameWork.Core;
using AFrameWork.Core.SmallBase;
using AFrameWork.GameUI;

namespace AFrameWork.Sample
{
    /// <summary>
    /// 战斗物体类，演示如何通过 SetupComponents + AddObjectComponent 动态添加组件。
    /// 所有动画（待机、奔跑、攻击、受击、翻滚）统一使用 AnimationConfig 管理，
    /// 动画播放、特效/声音帧事件由 ObjectBase 基类自动处理。
    /// 子类只需配置 AnimationConfig、查找特效子对象、检测输入并调用 TryStartAttack / TryStartRoll。
    /// 受击动画由 OnDamaged 回调自动触发，翻滚期间通过 ObjectBase.SetInvulnerable 实现无敌。
    /// </summary>
    public class Fighter : ObjectBase
    {
        #region Addressables 资源键常量

        // Addressables 组名：fighter，包含骨骼和动画资源
        // 在 Addressables Groups 窗口中配置这些地址键
        private const string k_avatarKey = "player_Avatar";      // 骨骼资源地址
        private const string k_swingSoundKey = "player_SwingSound"; // 挥剑音效地址
        private const string k_bulletPrefabKey = "bullet";           // 子弹预制体地址

        // ===== 翻滚与受击系统参数 =====
        // 这些是 Fighter 特有的游戏行为参数，不属于通用 AnimationConfig 结构。
        // AnimationConfig 仅统一管理动画资源（ClipKey/Clip/帧事件），
        // 行为参数（距离/冷却/无敌时长）由子类按游戏需求定义，保持基类通用性。
        private const float k_rollDistance = 3f;                 // 翻滚位移距离（米）
        private const float k_rollDuration = 0.6f;               // 翻滚持续时间（秒，需与动画长度匹配）
        private const float k_rollInvulnerableDuration = 0.5f;   // 翻滚无敌时长（秒，略短于动画保证安全）
        private const float k_rollCooldown = 1.0f;               // 翻滚冷却时间（秒）
        // 翻滚速度 = 距离 / 时长，编译期常量，避免运行时除法
        private const float k_rollSpeed = k_rollDistance / k_rollDuration;

        #endregion

        #region 字段

        // 当前是否正在移动（用于动画状态切换）
        private bool m_isMoving = false;

        // 移动检测阈值（速度平方，避免开方运算）
        // 0.5f * 0.5f = 0.25f，大于此值认为正在移动
        private const float k_moveSpeedThresholdSqr = 0.25f;

        // 朝向旋转速度（度/秒）
        private const float k_rotationSpeed = 720f;

        // ===== 特效系统 =====
        // 移动脚步特效（VFX Graph）
        private VisualEffect m_footStepVfx;

        // ===== 武器系统 =====
        // 缓存的剑引用（子对象中的 Sword 组件）
        private Sword m_sword;

        // ===== 子弹系统 =====
        // 子弹生成位置（子对象 bulletPos）
        private Transform m_bulletPos;

        // 子弹池预热数量
        private const int k_bulletPrewarm = 10;

        // ===== 翻滚系统状态字段 =====
        // 当前是否正在翻滚（翻滚期间禁止移动/攻击输入）
        private bool m_isRolling = false;
        // 翻滚方向（世界空间，已归一化）
        private Vector3 m_rollDirection;
        // 翻滚开始时间（Time.time，用于检测翻滚完成）
        private float m_rollStartTime = 0f;
        // 上次翻滚触发时间（Time.time，-1 表示从未翻滚过，用于冷却检测）
        private float m_lastRollTime = -1f;
        // 当前是否正在受击（受击动画播放期间禁止其他输入）
        private bool m_isHurting = false;

        // ===== 每帧缓存的输入值 (避免重复调用 Input.GetAxisRaw) =====
        private float m_cachedHorizontal;
        private float m_cachedVertical;

        // ===== 动画配置：待机、奔跑、攻击 统一使用 AnimationConfig =====

        // 待机动画配置（循环播放，无帧事件）
        private static readonly AnimationConfig k_idle = new AnimationConfig
        {
            ClipKey = "player_Idel"
        };

        // 奔跑动画配置（循环播放，无帧事件；脚步特效由 Update 中的状态切换直接控制）
        private static readonly AnimationConfig k_run = new AnimationConfig
        {
            ClipKey = "player_Run"
        };

        // 攻击动画配置（不循环，含属性倍率和帧事件列表）
        // 每个 FrameEventConfig 定义一个帧同步事件：在指定时间触发指定类型的操作
        // Target 字段在 SetupComponents 中查找子对象后赋值；为 null 时自动跳过
        // 新增攻击只需添加一个 k_attackXX 常量并追加到 SetupComponents 中的 m_animationConfigs 数组
        private static readonly AnimationConfig k_attack01 = new AnimationConfig
        {
            ClipKey = "player_Attack01",
            Multiplier = new ObjectStatsConfigMultiplier(baseDamageMultiplier: 1.0f),
            FrameEvents = new FrameEventConfig[]
            {
                // 0.2秒时播放刀光特效（Target 在 SetupComponents 中赋值为 Blade01）
                new FrameEventConfig
                {
                    TriggerTime = 0.2f,
                    TargetType = FrameEventTargetType.PlayParticleSystem
                }
            }
        };
        private static readonly AnimationConfig k_attack02 = new AnimationConfig
        {
            ClipKey = "player_Attack02",
            Multiplier = new ObjectStatsConfigMultiplier(baseDamageMultiplier: 1.2f),
            FrameEvents = new FrameEventConfig[]
            {
                new FrameEventConfig
                {
                    TriggerTime = 0.2f,
                    TargetType = FrameEventTargetType.PlayParticleSystem
                }
            }
        };
        // 攻击03：演示多帧事件组合（音效 + 特效 + 自定义事件）
        // 同一动画可注册多个帧事件，各自独立 TriggerTime 和 TargetType，按时间顺序触发
        private static readonly AnimationConfig k_attack03 = new AnimationConfig
        {
            ClipKey = "player_Attack03",
            Multiplier = new ObjectStatsConfigMultiplier
            (
                baseDamageMultiplier: 1.5f,
                criticalRateMultiplier: 3.0f,
                criticalDamageMultiplier: 1.5f
            ),
            FrameEvents = new FrameEventConfig[]
            {
                // 0.1秒：播放挥剑音效（Target 在 Start 中通过 Addressables 加载 AudioClip 后赋值）
                new FrameEventConfig
                {
                    TriggerTime = 0.1f,
                    TargetType = FrameEventTargetType.PlayAudioClip,
                    FloatParam = 0.8f   // 音量 0~1
                },
                // 0.2秒：播放刀光特效（Target 在 SetupComponents 中查找 Blade03 子对象赋值）
                new FrameEventConfig
                {
                    TriggerTime = 0.2f,
                    TargetType = FrameEventTargetType.PlayParticleSystem
                },
                // 0.4秒：自定义事件（通过 OnAnimationCustomFrameEvent 回调子类处理，如震屏/顿帧）
                new FrameEventConfig
                {
                    TriggerTime = 0.4f,
                    TargetType = FrameEventTargetType.Custom,
                    EventName = "HitImpact"
                }
            }
        };

        // 待机和奔跑的实例配置（运行时通过 LoadAnimationConfigAssetsAsync 填充 Clip）
        private AnimationConfig m_idleConfig = k_idle;
        private AnimationConfig m_runConfig = k_run;

        // 受击动画配置（不循环，无帧事件）
        // 被攻击掉血时立即切换到此动画，打断当前攻击/移动
        private static readonly AnimationConfig k_hurt = new AnimationConfig
        {
            ClipKey = "player_Hurt"
        };

        // 翻滚动画配置（不循环，无帧事件）
        // 按空格触发，播放期间无敌并向前位移 k_rollDistance 米
        private static readonly AnimationConfig k_roll = new AnimationConfig
        {
            ClipKey = "player_Roll"
        };

        // 死亡动画配置（不循环，无帧事件）
        private static readonly AnimationConfig k_dead = new AnimationConfig
        {
            ClipKey = "player_Dead"
        };

        // 受击、翻滚、死亡的实例配置（运行时通过 LoadAnimationConfigAssetsAsync 填充 Clip）
        private AnimationConfig m_hurtConfig = k_hurt;
        private AnimationConfig m_rollConfig = k_roll;
        private AnimationConfig m_deadConfig = k_dead;

        #endregion

        #region 血条系统字段

        /// <summary>
        /// 血条控制器引用（场景中的全局管理器）
        /// </summary>
        private HealthBarController m_healthBarController;

        /// <summary>
        /// 血条组件引用（当前角色的血条）
        /// </summary>
        private HealthBar m_healthBar;

        /// <summary>
        /// 血条头部偏移（根据角色模型高度调整）
        /// </summary>
        [Tooltip("血条距离角色头顶的垂直偏移（世界坐标）")]
        [SerializeField]
        private float m_healthBarHeadOffset = 2.0f;

        #endregion

        #region 配置属性

        /// <summary>
        /// 移动配置属性，控制移动行为
        /// 注意：移动速度由 ObjectStatsConfig.MoveSpeed 提供（6f）
        /// </summary>
        protected override MovementConfig MovementConfig => new MovementConfig(
            keepYVelocity: true,
            autoReadInput: true,
            decelerationRate: 50f,     // 近乎立即停止（约 0.04 秒内停止)
            movementAngleOffset: -45f // 移动方向左偏45度，补偿视角偏差
        );

        /// <summary>
        /// 物体属性配置，包含角色所有数值属性
        /// </summary>
        protected override ObjectStatsConfig ObjectStatsConfig => new ObjectStatsConfig
        {
            // 基础属性
            Type = ObjectType.Warrior,
            FactionID = 1,               // 玩家阵营
            MaxHealth = 100f,
            CurrentHealth = 100f,
            PhysicalAttack = 25f,
            PhysicalDefense = 15f,
            TrueDamage = 5f,
            MagicAttack = 10f,
            MagicDefense = 12f,

            // 速度属性
            MoveSpeed = 6f,
            AttackSpeed = 1.5f,
            CastSpeed = 1.2f,

            // 暴击属性
            CriticalRate = 0.2f,
            CriticalDamageMultiplier = 2.5f,

            // 穿透属性
            ArmorPenetration = 0.15f,
            MagicPenetration = 0.1f,

            // 恢复属性
            HealthRegeneration = 2f,
            ManaRegeneration = 1.5f,

            // 特殊属性
            MaxMana = 80f,
            CurrentMana = 80f,
            CooldownReduction = 0.1f,
            EvasionRate = 0.05f,
            HitRate = 0.95f,
            AttackRange = 3f,
            VisionRange = 12f,
            Experience = 0f,
            Level = 1,
            Gold = 0
        };

        #endregion

        #region 初始化方法

        /// <summary>
        /// 重写 SetupComponents，使用 AddObjectComponent 动态添加组件
        /// 直接传入组件类型和初始化回调，无需额外的配置类
        /// </summary>
        protected override void SetupComponents()
        {
            base.SetupComponents();

            // 初始化攻击配置数组（基类字段，必须在查找特效之前完成）
            m_animationConfigs = new AnimationConfig[] { k_attack01, k_attack02, k_attack03 };

            // 克隆 FrameEvents 数组，使每个实例拥有独立副本，避免修改 static readonly 模板
            CloneFrameEvents(ref m_idleConfig);
            CloneFrameEvents(ref m_runConfig);
            CloneFrameEvents(ref m_hurtConfig);
            CloneFrameEvents(ref m_rollConfig);
            CloneFrameEvents(ref m_deadConfig);
            for (int i = 0; i < m_animationConfigs.Length; i++)
            {
                CloneFrameEvents(ref m_animationConfigs[i]);
            }

            // 添加 Rigidbody 并配置参数
            m_rigidbody = AddObjectComponent<Rigidbody>(rb =>
            {
                rb.mass = 1f;
                rb.drag = 0f;
                rb.angularDrag = 0.05f;
                rb.useGravity = true;
                rb.constraints = RigidbodyConstraints.FreezeRotationX
                    | RigidbodyConstraints.FreezeRotationZ;
                rb.freezeRotation = true;
                rb.isKinematic = false; // 如果为true 则不会响应物理力，不会移动，如果为false 则会响应物理力，会移动
            });
            //Debug.Log($"fighter m_rigidbody {m_rigidbody.isKinematic}", this);

            // CapsuleCollider — 硬编码尺寸，避免 Awake 时 CalculateObjectBounds 返回空包围盒
            // 添加 CapsuleCollider，new Vector3(-1f, 1f, 2f));  // 向左移动1，向上移动1`向前移动2，
            //AddCapsuleCollider(CalculateObjectBounds(), new Vector3(0.3f, 1f, 0.3f), Vector3.zero);
            AddObjectComponent<CapsuleCollider>(cc =>
            {
                cc.radius = 0.2f;
                cc.height = 2.1f;
                cc.center = new Vector3(0f, 1f, 0f);
                cc.isTrigger = false;
            });


            // 查找特效子对象
            Transform vfxRoot = transform.Find("VFX");
            if (vfxRoot != null)
            {
                // 查找脚步特效
                m_footStepVfx = vfxRoot.Find("VFXFootStep")?.GetComponent<VisualEffect>();
                if (m_footStepVfx != null)
                {
                    m_footStepVfx.Stop();
                }

                // 为每个攻击的 FrameEvents 赋值刀光特效 Target
                // 特效子对象命名约定：Blade01 对应 m_animationConfigs[0]，Blade02 对应 [1]，以此类推
                // 未找到的特效 Target 保持 null，基类 ExecuteFrameEvent 自动跳过
                // 支持同一动画含多个帧事件：按 TargetType 匹配赋值，而非固定索引 [0]
                for (int i = 0; i < m_animationConfigs.Length; i++)
                {
                    if (m_animationConfigs[i].FrameEvents == null
                        || m_animationConfigs[i].FrameEvents.Length == 0)
                    {
                        continue;
                    }

                    ParticleSystem bladeVfx = vfxRoot.Find($"Blade{i + 1:00}")?.GetComponent<ParticleSystem>();
                    if (bladeVfx != null)
                    {
                        bladeVfx.Stop();
                        // 遍历帧事件，赋值到所有 PlayParticleSystem / StopParticleSystem 类型的事件
                        FrameEventConfig[] events = m_animationConfigs[i].FrameEvents;
                        for (int j = 0; j < events.Length; j++)
                        {
                            if (events[j].TargetType == FrameEventTargetType.PlayParticleSystem
                                || events[j].TargetType == FrameEventTargetType.StopParticleSystem)
                            {
                                events[j].Target = bladeVfx;
                            }
                        }
                    }
                }
            }

            // 查找武器子对象（Sword 组件）
            m_sword = GetComponentInChildren<Sword>();

            // 查找子弹生成位置
            m_bulletPos = transform.Find("bulletPos");
        }

        /// <summary>
        /// 异步初始化动画系统。
        /// SetupComponents 在 Awake 中同步调用，异步加载在 Start 中执行。
        /// </summary>
        private async void Start()
        {
            // 异步加载子弹预制体并注册到对象池
            GameObject bulletPrefab = await LoadAssetAsync<GameObject>(k_bulletPrefabKey);
            if (bulletPrefab != null)
            {
                SimpleObjectPool pool = SimpleObjectPool.Instance;
                if (pool != null && !pool.IsRegistered<Bullet>())
                {
                    pool.RegisterPrefab<Bullet>(bulletPrefab, k_bulletPrewarm);
                }
            }
            else
            {
                Debug.LogWarning($"[{GetType().Name}] 无法加载子弹预制体（Addressables key: {k_bulletPrefabKey}），子弹发射将失败。", this);
            }

            // 异步加载骨骼资源并初始化 Animator（含 PlayableGraph）
            await SetupAnimatorAsync(k_avatarKey);

            // 加载期间对象可能被销毁，安全检查
            if (this == null)
            {
                return;
            }

            // 并行加载待机/奔跑/受击/翻滚动画配置和攻击动画配置
            // m_animationConfigs 已在 SetupComponents 中初始化并克隆 FrameEvents
            Task<AnimationConfig> idleTask = LoadAnimationConfigAssetsAsync(m_idleConfig);
            Task<AnimationConfig> runTask = LoadAnimationConfigAssetsAsync(m_runConfig);
            Task<AnimationConfig> hurtTask = LoadAnimationConfigAssetsAsync(m_hurtConfig);
            Task<AnimationConfig> rollTask = LoadAnimationConfigAssetsAsync(m_rollConfig);
            Task<AnimationConfig> deadTask = LoadAnimationConfigAssetsAsync(m_deadConfig);
            Task loadAttacksTask = LoadAnimationClipsAsync();
            // 加载挥剑音效（用于 k_attack03 的 PlayAudioClip 帧事件）
            Task<AudioClip> swingSoundTask = LoadAssetAsync<AudioClip>(k_swingSoundKey);

            // 等待所有加载完成
            await Task.WhenAll(idleTask, runTask, hurtTask, rollTask, deadTask);
            await loadAttacksTask;
            await swingSoundTask;

            // 再次安全检查（加载期间可能销毁）
            if (this == null)
            {
                return;
            }

            // 回填加载完成的配置
            m_idleConfig = idleTask.Result;
            m_runConfig = runTask.Result;
            m_hurtConfig = hurtTask.Result;
            m_rollConfig = rollTask.Result;
            m_deadConfig = deadTask.Result;

            // 将挥剑音效赋值到 k_attack03（m_animationConfigs[2]）的 PlayAudioClip 帧事件
            AudioClip swingSound = swingSoundTask.Result;
            if (swingSound != null
                && m_animationConfigs != null
                && m_animationConfigs.Length > 2
                && m_animationConfigs[2].FrameEvents != null)
            {
                FrameEventConfig[] events = m_animationConfigs[2].FrameEvents;
                for (int i = 0; i < events.Length; i++)
                {
                    if (events[i].TargetType == FrameEventTargetType.PlayAudioClip)
                    {
                        events[i].Target = swingSound;
                    }
                }
            }

            // 加载成功后播放待机动画（通过 AnimationConfig 统一播放）
            if (m_idleConfig.Clip != null)
            {
                PlayAnimation(m_idleConfig, loop: true);
            }

            // 初始化血条系统
            InitializeHealthBar();
        }

        /// <summary>
        /// 初始化血条系统
        /// 查找场景中的 HealthBarController 并创建血条
        /// </summary>
        private void InitializeHealthBar()
        {
#if UNITY_EDITOR
            // Debug.Log($"[{GetType().Name}] 开始初始化血条系统...");
#endif

            // 获取场景中的血条控制器（使用静态缓存，避免 FindObjectOfType 遍历场景）
            m_healthBarController = HealthBarController.Instance;

            if (m_healthBarController == null)
            {
#if UNITY_EDITOR
                // Debug.LogWarning($"[{GetType().Name}] 场景中未找到 HealthBarController，血条系统未启用！", this);
                // Debug.LogWarning($"[{GetType().Name}] 解决方案：在 Hierarchy 中创建 HealthBarController 对象并添加 HealthBarController 组件", this);
#endif
                return;
            }

            // 使用大型血条配置（适合玩家角色，含文本显示）
            m_healthBar = m_healthBarController.CreateHealthBar(
                transform, HealthBarConfig.CreateCompact(), m_healthBarHeadOffset);

            if (m_healthBar == null)
            {
#if UNITY_EDITOR
                // Debug.LogError($"[{GetType().Name}] 血条创建失败！请检查 HealthBarController 的 UXML/USS 资源配置", this);
#endif
                return;
            }

            // 初始化血条显示（当前血量/最大血量）
            m_healthBar.InitializeHealth(GetCurrentHealth(), GetMaxHealth());

            // 显示血条
            m_healthBar.Show();

#if UNITY_EDITOR
            // Debug.Log($"[{GetType().Name}] 血条已初始化并显示，当前血量: {GetCurrentHealth()}/{GetMaxHealth()}", this);
#endif
        }

        /// <summary>
        /// 每帧检测移动状态并切换动画。
        /// 使用 sqrMagnitude 检测速度，避免开方运算。
        /// 攻击输入检测通过 TryStartAttack 委托给基类处理冷却、连击、动画、特效、声音。
        /// 翻滚输入（空格）通过 TryStartRoll 触发，翻滚期间无敌并向前位移。
        /// 受击/翻滚期间禁止其他输入，动画完成由 OnAnimationComplete 恢复待机。
        /// </summary>
        protected override void Update()
        {
            // 调用父类 Update，确保输入处理、帧事件检查（触发 OnAnimationFrameEvent / OnAnimationComplete）等逻辑正常执行
            base.Update();

            // 死亡后禁止一切输入和移动
            if (IsDead())
            {
                SetMovementInput(Vector3.zero);
                return;
            }

            // 缓存移动输入（供 TryStartRoll 复用，避免重复调用 Input.GetAxisRaw）
            m_cachedHorizontal = Input.GetAxisRaw("Horizontal");
            m_cachedVertical = Input.GetAxisRaw("Vertical");

            // 子弹输入：Q 键按下时朝最近敌方发射一颗子弹（通过 SimpleObjectPool 对象池）
            // 不依赖动画系统，放在动画加载检查之前，确保场景加载初期即可发射
            if (Input.GetKeyDown(KeyCode.Q))
            {
                FireBullet();
            }

            // 动画未加载完成时跳过动画切换逻辑
            if (m_idleConfig.Clip == null || m_runConfig.Clip == null)
            {
                return;
            }

            // 翻滚期间：持续向前位移，禁止其他输入
            // 翻滚完成检测放在 FixedUpdate 之后由 OnAnimationComplete 或时长检测处理
            if (m_isRolling)
            {
                HandleRollMovement();
                return;
            }

            // 受击期间：禁止其他输入，等待受击动画完成（OnAnimationComplete 恢复待机）
            if (m_isHurting)
            {
                SetMovementInput(Vector3.zero);
                return;
            }

            // 翻滚输入：空格键按下时尝试触发翻滚（必须在攻击/受击之外才能触发）
            if (Input.GetKeyDown(KeyCode.Space))
            {
                if (TryStartRoll())
                {
                    return;
                }
            }

            // 攻击输入：鼠标左键点击时尝试触发攻击
            // TryStartAttack 内部处理连击超时、冷却检测、动画播放、特效/声音帧事件
            if (Input.GetMouseButtonDown(0))
            {
                TryStartAttack();
            }

            // 攻击期间禁止移动：覆盖 HandleInput 读取的输入，清零水平速度
            // 动画结束由基类 OnAnimationComplete 自动处理
            if (m_isAttacking)
            {
                SetMovementInput(Vector3.zero);
                ZeroHorizontalVelocity();
                return;
            }

            // 使用 Rigidbody 水平速度的平方判断是否移动
            // 忽略 Y 轴速度（跳跃/下落不影响移动动画）
            Vector3 horizontalVelocity = new Vector3(m_rigidbody.velocity.x, 0f, m_rigidbody.velocity.z);
            float speedSqr = horizontalVelocity.sqrMagnitude;

            bool isCurrentlyMoving = speedSqr > k_moveSpeedThresholdSqr;

            // 状态切换时才切换动画，避免每帧重复调用
            if (isCurrentlyMoving != m_isMoving)
            {
                m_isMoving = isCurrentlyMoving;

                if (m_isMoving)
                {
                    // 开始移动：通过 AnimationConfig 播放奔跑动画（循环）
                    PlayAnimation(m_runConfig, loop: true);
                    // 播放脚步特效
                    if (m_footStepVfx != null)
                    {
                        m_footStepVfx.Play();
                    }
                }
                else
                {
                    // 停止移动：通过 AnimationConfig 播放待机动画（循环）
                    PlayAnimation(m_idleConfig, loop: true);
                    // 停止脚步特效
                    if (m_footStepVfx != null)
                    {
                        m_footStepVfx.Stop();
                    }
                }
            }

            // 移动时朝向移动方向（平滑旋转）
            // 复用已计算的 speedSqr 做 normalized，避免 .normalized 内部重复 sqrMagnitude + Sqrt
            if (m_isMoving)
            {
                float speed = Mathf.Sqrt(speedSqr);
                Vector3 moveDir = horizontalVelocity / speed;
                Quaternion targetRotation = Quaternion.LookRotation(moveDir);
                transform.rotation = Quaternion.RotateTowards(
                    transform.rotation, targetRotation, k_rotationSpeed * Time.deltaTime);
            }
        }

        /// <summary>
        /// 翻滚期间持续向前位移。
        /// 使用 Rigidbody.velocity 直接设置水平速度（保留 Y 轴速度支持重力/下落），
        /// 翻滚速度为常量 k_rollSpeed = k_rollDistance / k_rollDuration。
        /// 翻滚完成检测：基于时长 k_rollDuration（与动画长度匹配）。
        /// </summary>
        private void HandleRollMovement()
        {
            // 持续施加翻滚方向的速度
            m_rigidbody.velocity = new Vector3(
                m_rollDirection.x * k_rollSpeed,
                m_rigidbody.velocity.y,
                m_rollDirection.z * k_rollSpeed);

            // 翻滚时长到达 → 结束翻滚
            if (Time.time - m_rollStartTime >= k_rollDuration)
            {
                EndRoll();
            }
        }

        /// <summary>
        /// 尝试开始翻滚。检测冷却、计算方向、设置无敌、播放动画。
        /// 翻滚方向优先使用当前移动输入方向；无输入时使用角色朝向（向前翻滚）。
        /// </summary>
        /// <returns>true=翻滚已触发，false=冷却中或动画未加载</returns>
        private bool TryStartRoll()
        {
            // 动画未加载完成
            if (m_rollConfig.Clip == null)
            {
                return false;
            }

            // 冷却检测：上次翻滚后 k_rollCooldown 内不可再次触发
            // m_lastRollTime < 0 表示从未翻滚过，跳过冷却检测
            if (m_lastRollTime >= 0f && Time.time - m_lastRollTime < k_rollCooldown)
            {
                return false;
            }

            // 计算翻滚方向：
            // 优先使用当前移动输入方向（玩家正在按方向键时朝该方向翻滚）
            // 无输入时使用角色当前朝向（向前翻滚）
            Vector3 forward = transform.forward;
            // 复用 Update 中缓存的输入值，避免重复调用 Input.GetAxisRaw
            Vector3 inputDir = new Vector3(m_cachedHorizontal, 0f, m_cachedVertical);
            m_rollDirection = inputDir.sqrMagnitude > 0.01f
                ? inputDir.normalized
                : new Vector3(forward.x, 0f, forward.z).normalized;

            // 朝向翻滚方向（立即转向，不使用平滑旋转，保证翻滚方向清晰）
            if (m_rollDirection.sqrMagnitude > 0.01f)
            {
                transform.rotation = Quaternion.LookRotation(m_rollDirection);
            }

            // 设置无敌状态（翻滚期间免疫所有伤害）
            SetInvulnerable(k_rollInvulnerableDuration);

            // 播放翻滚动画（非循环，无帧事件）
            PlayAnimation(m_rollConfig, loop: false);

            // 记录翻滚状态
            m_isRolling = true;
            m_rollStartTime = Time.time;
            m_lastRollTime = Time.time;

            // 锁定基类移动控制：翻滚期间由 HandleRollMovement 直接控制 Rigidbody.velocity，
            // 防止 FixedUpdate 中的 HandleMovement 用移动输入覆盖翻滚速度
            m_isMovementLocked = true;

            // 停止脚步特效（翻滚期间不播放脚步）
            if (m_footStepVfx != null)
            {
                m_footStepVfx.Stop();
            }

            // 重置移动状态（翻滚结束后从待机开始）
            m_isMoving = false;

            return true;
        }

        /// <summary>
        /// 结束翻滚。重置翻滚状态并恢复待机动画。
        /// 由 HandleRollMovement 在翻滚时长到达时调用，
        /// 或由 OnAnimationComplete 在翻滚动画播放完成时调用（取先到达者）。
        /// </summary>
        private void EndRoll()
        {
            if (!m_isRolling)
            {
                return;
            }

            m_isRolling = false;

            // 解除移动锁定，恢复基类移动控制
            m_isMovementLocked = false;

            // 翻滚无敌可能在 EndRoll 时仍未到期（k_rollInvulnerableDuration < k_rollDuration 时已到期，
            // 这里仅作为安全清理；让 IsInvulnerable 属性的自动过期机制处理更稳妥）
            // 不主动 ClearInvulnerable()，避免提前结束较长的无敌窗口

            // 立即停止水平移动（避免翻滚结束后滑行）
            ZeroHorizontalVelocity();

            // 恢复待机动画
            if (m_idleConfig.Clip != null)
            {
                PlayAnimation(m_idleConfig, loop: true);
            }
        }

        /// <summary>
        /// 清零水平速度，保留 Y 轴速度（重力/下落）。
        /// 用于攻击/翻滚结束/受击时停止水平位移。
        /// </summary>
        private void ZeroHorizontalVelocity()
        {
            m_rigidbody.velocity = new Vector3(0f, m_rigidbody.velocity.y, 0f);
        }

        /// <summary>
        /// 攻击开始回调：启用剑的攻击状态，传入当前攻击的属性倍率。
        /// 由基类 TryStartAttack 在动画播放后自动调用。
        /// </summary>
        protected override void OnAttackStarted(AnimationConfig config)
        {
            if (m_sword != null)
            {
                m_sword.BeginSwing(config.Multiplier);
            }
        }

        /// <summary>
        /// 攻击结束回调：禁用剑的攻击状态。
        /// 由基类 OnAnimationComplete 在攻击动画播放完成时自动调用。
        /// </summary>
        protected override void OnAttackEnded()
        {
            if (m_sword != null)
            {
                m_sword.EndSwing();
            }
        }

        /// <summary>
        /// 发射子弹：通过 SimpleObjectPool 从对象池取一颗子弹，朝最近敌方目标飞行。
        /// 目标查找使用 TargetRegistry（方案 D），避免 FindObjectOfType 的 O(N) 场景扫描。
        /// 子弹伤害使用 Fighter 的 PhysicalAttack（方案 A/B/C/E 优化见 SimpleObjectBase/SimpleObjectPool）。
        /// </summary>
        private void FireBullet()
        {
            if (m_bulletPos == null) return;

            // 通过 TargetRegistry 查找最近敌方（替代 FindObjectOfType<Monster>）
            ObjectBase target = TargetRegistry.FindNearest(m_bulletPos.position, this);
            if (target == null) return;

            // 计算水平方向
            Vector3 direction = target.transform.position - m_bulletPos.position;
            direction.y = 0f;
            if (direction.sqrMagnitude < 0.01f) return;

            // 通过 SimpleObjectPool 发射（对象池复用，无 Instantiate/Destroy）
            SimpleObjectPool pool = SimpleObjectPool.Instance;
            if (pool == null)
            {
#if UNITY_EDITOR
                Debug.LogWarning($"[{GetType().Name}] 场景中未找到 SimpleObjectPool，子弹发射失败。", this);
#endif
                return;
            }

            // 伤害值取 Fighter 的物理攻击力
            //float damage = HasObjectStats() ? GetObjectStats().PhysicalAttack : 10f;
            float damage = 1f;
            pool.Launch<Bullet>(m_bulletPos.position, direction, this, damage, DamageType.Physical);
        }

        /// <summary>
        /// 非循环动画播放完成回调。
        /// 基类已处理攻击状态重置和 OnAttackEnded 回调。
        /// 子类在此处理：
        ///   - 攻击动画完成 → 恢复待机
        ///   - 受击动画完成 → 清除受击状态，恢复待机
        ///   - 翻滚动画完成 → 调用 EndRoll 恢复待机（若时长检测已先触发则跳过）
        /// </summary>
        protected override void OnAnimationComplete()
        {
            // 记录是否为攻击动画完成（base 调用后会重置 m_isAttacking）
            bool wasAttacking = m_isAttacking;
            bool wasHurting = m_isHurting;
            bool wasRolling = m_isRolling;

            // 基类处理：重置攻击状态、清除帧事件、调用 OnAttackEnded
            base.OnAnimationComplete();

            // 攻击/受击/翻滚是互斥状态，使用 else if 跳过后续判断
            if (wasAttacking && m_idleConfig.Clip != null)
            {
                PlayAnimation(m_idleConfig, loop: true);
            }
            else if (wasHurting)
            {
                m_isHurting = false;
                if (m_idleConfig.Clip != null)
                {
                    PlayAnimation(m_idleConfig, loop: true);
                }
            }
            else if (wasRolling && m_isRolling)
            {
                EndRoll();
            }
        }

        /// <summary>
        /// 自定义帧事件回调：处理 TargetType == Custom 的帧事件。
        /// 由基类 OnAnimationFrameEvent 在帧事件触发时自动调用。
        /// 在此实现游戏逻辑：震屏、顿帧、技能特效等无法用内置 TargetType 表达的操作。
        /// </summary>
        /// <param name="eventName">FrameEventConfig.EventName 中定义的事件名称</param>
        protected override void OnAnimationCustomFrameEvent(string eventName)
        {
            switch (eventName)
            {
                case "HitImpact":
                    // 命中冲击：可在此触发震屏、顿帧、命中特效等
#if UNITY_EDITOR
                    // Debug.Log($"[{nameof(Fighter)}] HitImpact 自定义帧事件触发", this);
#endif
                    break;
            }
        }

        #endregion

        #region 物体属性回调方法（重写父类的所有回调）

        // 注意：所有回调中的 Debug.Log 均包裹在 #if UNITY_EDITOR 中
        // MMO 场景下战斗频繁，字符串插值会产生 GC 压力，生产构建中需要完全剥离

        /// <summary>
        /// 受到伤害时的回调。
        /// 立即触发受击动画 player_Hurt，打断当前攻击/移动。
        /// 死亡时不触发受击动画（由 OnDeath 处理死亡流程）。
        /// 翻滚期间无敌，不会进入此回调。
        /// </summary>
        protected override void OnDamaged(float damage)
        {
#if UNITY_EDITOR
            // Debug.Log($"战士受到 {damage} 点伤害！当前生命值：{GetCurrentHealth()}/{GetMaxHealth()}");
#endif

            // 更新血条显示（带平滑过渡动画）
            if (m_healthBar != null)
            {
                m_healthBar.UpdateHealth(GetCurrentHealth(), GetMaxHealth());
            }

            // 死亡时不播放受击动画，让 OnDeath 处理死亡流程
            if (IsDead())
            {
                return;
            }

            // 动画未加载完成时跳过
            if (m_hurtConfig.Clip == null)
            {
                return;
            }

            // 打断当前攻击状态：重置连击索引和攻击标志
            // 注意：不调用 OnAttackEnded，因为攻击被打断时武器可能正在挥动
            // 由基类 OnAnimationComplete 在受击动画完成时处理攻击状态清理
            if (m_isAttacking)
            {
                m_isAttacking = false;
                m_comboIndex = 0;
                // 停用武器攻击状态，防止受击期间武器继续造成伤害
                if (m_sword != null)
                {
                    m_sword.EndSwing();
                }
            }

            // 停止移动（受击期间不可移动）
            StopMovement();

            // 标记受击状态并播放受击动画
            m_isHurting = true;
            m_isMoving = false;

            // 停止脚步特效
            if (m_footStepVfx != null)
            {
                m_footStepVfx.Stop();
            }

            PlayAnimation(m_hurtConfig, loop: false);
        }

        /// <summary>
        /// 恢复生命值时的回调
        /// </summary>
        protected override void OnHealed(float amount)
        {
#if UNITY_EDITOR
            // Debug.Log($"战士恢复 {amount} 点生命值！当前生命值：{GetCurrentHealth()}/{GetMaxHealth()}");
#endif

            // 更新血条显示
            if (m_healthBar != null)
            {
                m_healthBar.UpdateHealth(GetCurrentHealth(), GetMaxHealth());
            }
        }

        /// <summary>
        /// 消耗魔法值时的回调
        /// </summary>
        protected override void OnManaConsumed(float amount)
        {
#if UNITY_EDITOR
            // Debug.Log($"战士消耗 {amount} 点魔法值！当前魔法值：{GetCurrentMana()}/{GetMaxMana()}");
#endif
        }

        /// <summary>
        /// 恢复魔法值时的回调
        /// </summary>
        protected override void OnManaRestored(float amount)
        {
#if UNITY_EDITOR
            // Debug.Log($"战士恢复 {amount} 点魔法值！当前魔法值：{GetCurrentMana()}/{GetMaxMana()}");
#endif
        }

        /// <summary>
        /// 增加经验值时的回调
        /// </summary>
        protected override void OnExperienceAdded(float amount)
        {
#if UNITY_EDITOR
            // Debug.Log($"战士获得 {amount} 点经验值！当前经验值：{GetObjectStats().Experience}");
#endif
        }

        /// <summary>
        /// 增加金币时的回调
        /// </summary>
        protected override void OnGoldAdded(int amount)
        {
#if UNITY_EDITOR
            // Debug.Log($"战士获得 {amount} 金币！当前金币：{GetGold()}");
#endif
        }

        /// <summary>
        /// 物体死亡时的回调。
        /// 清理所有战斗状态（攻击/翻滚/受击），停止移动，停止特效。
        /// </summary>
        protected override void OnDeath()
        {
#if UNITY_EDITOR
            // Debug.Log("战士死亡！");
#endif

            // 清理战斗状态标志，防止 Update 继续执行位移/输入逻辑
            m_isAttacking = false;
            m_isRolling = false;
            m_isHurting = false;
            m_isMoving = false;
            m_comboIndex = 0;
            // 解除移动锁定，恢复基类移动控制（防御性清理）
            m_isMovementLocked = false;

            // 停用武器攻击状态
            if (m_sword != null)
            {
                m_sword.EndSwing();
            }

            // 停止脚步特效
            if (m_footStepVfx != null)
            {
                m_footStepVfx.Stop();
            }

            // 移除血条
            if (m_healthBarController != null)
            {
                m_healthBarController.RemoveHealthBar(transform);
                m_healthBar = null;
            }

            // 播放死亡动画
            if (m_deadConfig.Clip != null)
            {
                PlayAnimation(m_deadConfig, loop: false);
            }

            StopMovement();
        }

        /// <summary>
        /// 重置属性时的回调
        /// </summary>
        protected override void OnStatsReset()
        {
#if UNITY_EDITOR
            // Debug.Log("战士属性已重置！");
#endif
        }

        #endregion
    }

    /// <summary>
    /// Fighter 使用说明：
    /// ============================================================
    /// 战士角色类，继承 ObjectBase，演示完整的玩家控制角色实现。
    /// 包含：移动控制、视角偏移、朝向旋转、连击攻击系统、特效系统、武器系统集成。
    ///
    /// ════════════════════════════════════════════════════════════
    /// 【字段详解】
    /// ════════════════════════════════════════════════════════════
    ///   Addressables 资源键常量：
    ///     k_avatarKey = "player_Avatar"        — 角色骨骼文件（Avatar）
    ///     k_swingSoundKey = "player_SwingSound" — 挥剑音效（k_attack03 使用）
    ///
    ///   状态字段：
    ///     m_isMoving — 当前是否正在移动（用于动画状态切换，避免每帧重复调用 PlayAnimation）
    ///     k_moveSpeedThresholdSqr = 0.25f — 移动检测阈值（速度平方，0.5f²，避免开方运算）
    ///     k_rotationSpeed = 720f — 朝向旋转速度（度/秒，用于移动时平滑转向）
    ///
    ///   特效与武器：
    ///     m_footStepVfx — 移动脚步特效（VisualEffect，VFX Graph）
    ///     m_sword — 缓存的剑引用（子对象中的 Sword 组件）
    ///
    ///   动画配置（static readonly 模板，运行时克隆独立副本）：
    ///     k_idle — 待机动画（ClipKey="player_Idel"，无帧事件，PlayAnimation 时 loop:true）
    ///     k_run  — 奔跑动画（ClipKey="player_Run"，无帧事件，PlayAnimation 时 loop:true）
    ///     k_attack01 — 攻击1（baseDamageMultiplier=1.0，1 个帧事件：0.2s 播放刀光特效）
    ///     k_attack02 — 攻击2（baseDamageMultiplier=1.2，1 个帧事件：0.2s 播放刀光特效）
    ///     k_attack03 — 攻击3（baseDamage×1.5, 暴击率×3.0, 暴击伤害×1.5，3 个帧事件）
    ///     k_hurt  — 受击动画（ClipKey="player_Hurt"，无帧事件，PlayAnimation 时 loop:false）
    ///     k_roll  — 翻滚动画（ClipKey="player_Roll"，无帧事件，PlayAnimation 时 loop:false）
    ///     m_idleConfig / m_runConfig / m_hurtConfig / m_rollConfig —
    ///       实例配置（运行时通过 LoadAnimationConfigAssetsAsync 填充 Clip）
    ///
    ///   翻滚系统参数（Fighter 特有行为参数，非 AnimationConfig 字段）：
    ///     k_rollDistance = 3f              — 翻滚位移距离（米）
    ///     k_rollDuration = 0.6f            — 翻滚持续时间（秒，需与动画长度匹配）
    ///     k_rollInvulnerableDuration = 0.5f — 翻滚无敌时长（秒，略短于动画保证安全）
    ///     k_rollCooldown = 1.0f            — 翻滚冷却时间（秒）
    ///     k_rollSpeed = k_rollDistance / k_rollDuration — 翻滚速度（编译期常量）
    ///
    ///   翻滚状态字段：
    ///     m_isRolling — 当前是否正在翻滚（翻滚期间禁止移动/攻击输入）
    ///     m_rollDirection — 翻滚方向（世界空间，已归一化）
    ///     m_rollStartTime — 翻滚开始时间（用于检测翻滚完成）
    ///     m_lastRollTime — 上次翻滚时间（-1 表示从未翻滚，用于冷却检测）
    ///     m_isHurting — 当前是否正在受击（受击动画期间禁止其他输入）
    ///
    /// ════════════════════════════════════════════════════════════
    /// 【AnimationConfig 统一动画管理】（定义在 ObjectBase.cs，Core 命名空间）
    /// ════════════════════════════════════════════════════════════
    ///   所有动画（待机、奔跑、攻击）统一使用 AnimationConfig 配置：
    ///     ClipKey      — Addressables 地址键（编译时确定）
    ///     Clip         — 动画剪辑（LoadAnimationClipsAsync 运行时赋值）
    ///     Loop         — （已移除，通过 PlayAnimation 的 loop 参数传入）
    ///     Multiplier   — 属性倍率（仅攻击使用，非攻击默认即可）
    ///     FrameEvents  — 帧事件列表 FrameEventConfig[]（支持多个事件，null=无帧事件）
    ///
    ///   【FrameEventConfig 帧事件配置】每个事件独立 TriggerTime 和 TargetType：
    ///     TriggerTime — 触发时间，秒（按动画长度换算为归一化时间注册到帧事件系统）
    ///     TargetType  — 目标类型：PlayParticleSystem / StopParticleSystem /
    ///                   PlayAudioClip / PlayVisualEffect / StopVisualEffect / Custom
    ///     Target      — 目标对象（ParticleSystem/AudioClip/VisualEffect，运行时赋值，null=跳过）
    ///     EventName   — 自定义事件名称（仅 TargetType==Custom 时使用）
    ///     FloatParam  — 浮点参数（如 AudioClip 音量 0~1，0=默认 1.0f）
    ///
    ///   新增动画只需添加一个 k_xxx 常量：
    ///     - 待机/奔跑：追加到实例字段，Start 中通过 LoadAnimationConfigAssetsAsync 加载
    ///     - 攻击：追加到 m_animationConfigs 数组，LoadAnimationClipsAsync 自动加载
    ///   新增帧事件只需在 FrameEvents 数组中追加一个 FrameEventConfig：
    ///     - ParticleSystem/VisualEffect：SetupComponents 中查找子对象赋值 Target
    ///     - AudioClip：Start 中通过 LoadAssetAsync 加载后赋值 Target
    ///     - Custom：重写 OnAnimationCustomFrameEvent 处理 EventName
    ///
    /// ════════════════════════════════════════════════════════════
    /// 【攻击动画差异化设计】
    /// ════════════════════════════════════════════════════════════
    ///   k_attack01（基础攻击）：
    ///     Multiplier: baseDamageMultiplier=1.0f（基础伤害）
    ///     FrameEvents: [0.2s PlayParticleSystem]（播放 Blade01 刀光特效）
    ///
    ///   k_attack02（强化攻击）：
    ///     Multiplier: baseDamageMultiplier=1.2f（伤害 +20%）
    ///     FrameEvents: [0.2s PlayParticleSystem]（播放 Blade02 刀光特效）
    ///
    ///   k_attack03（终极攻击，演示多帧事件组合）：
    ///     Multiplier: baseDamage×1.5, 暴击率×3.0, 暴击伤害×1.5
    ///     FrameEvents:
    ///       [0.1s PlayAudioClip, volume=0.8] — 播放挥剑音效
    ///       [0.2s PlayParticleSystem]        — 播放 Blade03 刀光特效
    ///       [0.4s Custom, "HitImpact"]       — 自定义事件（震屏/顿帧等）
    ///
    /// ════════════════════════════════════════════════════════════
    /// 【基类 ObjectBase 提供的动画系统方法】
    /// ════════════════════════════════════════════════════════════
    ///   PlayAnimation(AnimationConfig)         — 统一播放：动画+循环+多帧事件注册
    ///   LoadAnimationConfigAssetsAsync(config) — 加载单个配置的 Clip
    ///   LoadAnimationClipsAsync()              — 批量加载 m_animationConfigs 的 Clip
    ///   CloneFrameEvents(ref config)            — 克隆 FrameEvents 数组（实例独立副本）
    ///   TryStartAttack()                        — 攻击触发：连击+冷却+动画+帧事件
    ///   OnAttackStarted(config)                 — 攻击开始回调（子类重写）
    ///   OnAttackEnded()                         — 攻击结束回调（子类重写）
    ///   OnAnimationFrameEvent                   — 基类按 TargetType 自动执行帧事件
    ///   OnAnimationCustomFrameEvent(eventName)  — 自定义帧事件回调（子类重写）
    ///   OnAnimationComplete                     — 基类自动重置攻击状态并调用 OnAttackEnded
    ///
    /// ════════════════════════════════════════════════════════════
    /// 【Addressables 资源配置】
    /// ════════════════════════════════════════════════════════════
    ///   在 Addressables Groups 窗口的 fighter 组中配置以下地址键：
    ///     player_Avatar      — 角色骨骼文件（Avatar），用于 Animator
    ///     player_Idel        — 待机动画（注意拼写为 Idel）
    ///     player_Run         — 奔跑动画
    ///     player_Attack01    — 攻击动画1
    ///     player_Attack02    — 攻击动画2
    ///     player_Attack03    — 攻击动画3
    ///     player_Hurt        — 受击动画（OnDamaged 触发）
    ///     player_Roll        — 翻滚动画（空格键触发，含无敌帧）
    ///     player_SwingSound  — 挥剑音效（k_attack03 的 PlayAudioClip 帧事件使用）
    ///   攻击动画地址键在 AnimationConfig.ClipKey 中定义。
    ///   音效地址键在常量 k_swingSoundKey 中定义，Start 中加载后赋值到 FrameEventConfig.Target。
    ///
    /// ════════════════════════════════════════════════════════════
    /// 【场景层级结构】
    /// ════════════════════════════════════════════════════════════
    ///   Fighter 需要以下子对象层级（手动在场景中搭建）：
    ///   <code>
    ///   fighter (GameObject + Fighter 脚本)
    ///   ├── VFX (空对象，特效根节点)
    ///   │   ├── VFXFootStep (VisualEffect 组件，脚步特效)
    ///   │   ├── Blade01 (ParticleSystem 组件，攻击1刀光特效)
    ///   │   ├── Blade02 (ParticleSystem 组件，攻击2刀光特效)
    ///   │   └── Blade03 (ParticleSystem 组件，攻击3刀光特效)
    ///   └── LittleAdventurerAndie (角色模型，含 SkinnedMeshRenderer 和骨架)
    ///   </code>
    ///   特效子对象命名约定：Blade01 对应 m_animationConfigs[0]，Blade02 对应 [1]，以此类推。
    ///   未找到的特效保持 null，基类自动跳过该动画的特效播放。
    ///
    /// ════════════════════════════════════════════════════════════
    /// 【移动系统】
    /// ════════════════════════════════════════════════════════════
    ///   - 自动读取 WASD/箭头键输入（autoReadInput: true）
    ///   - 移动速度由 ObjectStatsConfig.MoveSpeed 提供（6f）
    ///   - 减速速率 50f（近乎立即停止，约 0.12 秒内停止）
    ///   - 方向偏移 -45°（movementAngleOffset: -45f）
    ///     原因：等距视角下 W 键需要向左前方移动，通过偏移角度修正输入方向
    ///   - 朝向旋转：移动时用 Quaternion.RotateTowards 平滑转向移动方向（720°/秒）
    ///
    /// ════════════════════════════════════════════════════════════
    /// 【状态机说明（Update 方法流程）】
    /// ════════════════════════════════════════════════════════════
    ///   状态：待机（Idle）/ 移动（Run）/ 攻击（Attack）/ 受击（Hurt）/ 翻滚（Roll）/ 死亡（Dead）
    ///
    ///   Update 流程：
    ///     1. base.Update() — 处理输入、帧事件检查、动画过渡（必须调用）
    ///     2. 动画未加载完成时跳过（m_idleConfig.Clip == null 检查）
    ///     3. 翻滚期间（m_isRolling）：HandleRollMovement 持续位移，return
    ///     4. 受击期间（m_isHurting）：清零移动输入，return（等待 OnAnimationComplete）
    ///     5. 空格键按下 → TryStartRoll()（翻滚优先于攻击）
    ///     6. 鼠标左键点击 → TryStartAttack()（基类处理连击、冷却、动画、帧事件）
    ///     7. 攻击期间（m_isAttacking）：清零移动输入和水平速度，return
    ///     8. 移动检测：
    ///        - 使用 Rigidbody 水平速度的 sqrMagnitude 判断（避免开方）
    ///        - 阈值 k_moveSpeedThresholdSqr = 0.25f（速度 0.5m/s 以上为移动）
    ///        - 状态切换时才切换动画（避免每帧重复调用 PlayAnimation）
    ///     9. 移动时朝向移动方向（Quaternion.RotateTowards 平滑旋转）
    ///
    ///   状态切换：
    ///     待机 → 移动：speedSqr &gt; 0.25 → PlayAnimation(runConfig) + 播放脚步特效
    ///     移动 → 待机：speedSqr ≤ 0.25 → PlayAnimation(idleConfig) + 停止脚步特效
    ///     任意 → 攻击：鼠标左键 → TryStartAttack() → StopMovement()
    ///     任意 → 翻滚：空格键 → TryStartRoll() → SetInvulnerable + PlayAnimation(rollConfig)
    ///     任意 → 受击：被攻击掉血 → OnDamaged → PlayAnimation(hurtConfig)（翻滚期间无敌不会触发）
    ///     攻击 → 待机：OnAnimationComplete → PlayAnimation(idleConfig)
    ///     翻滚 → 待机：EndRoll（时长或动画完成触发）→ PlayAnimation(idleConfig)
    ///     受击 → 待机：OnAnimationComplete → PlayAnimation(idleConfig)
    ///     任意 → 死亡：OnDeath（致命伤害）→ 清理所有状态 + StopMovement()
    ///
    ///   状态优先级（Update 中检测顺序）：
    ///     翻滚 &gt; 受击 &gt; 翻滚输入 &gt; 攻击输入 &gt; 攻击中 &gt; 移动检测
    ///
    /// ════════════════════════════════════════════════════════════
    /// 【连击攻击系统】（基于基类 TryStartAttack + 帧事件回调）
    /// ════════════════════════════════════════════════════════════
    ///   输入：鼠标左键点击 → TryStartAttack()
    ///   规则（由基类处理）：
    ///     1. 第1次点击 → 播放 ATTACK01，0.5秒内不可再次触发
    ///     2. 0.5秒后第2次点击 → 播放 ATTACK02，0.5秒内不可再次触发
    ///     3. 0.5秒后第3次点击 → 播放 ATTACK03
    ///     4. 超过1.5秒未点击 → 连击重置，下次点击回到 ATTACK01
    ///   冷却/重置时间可通过重写 AttackCooldown / ComboResetTime 属性调整
    ///
    ///   攻击参数从 AnimationConfig 中直接获取（动画 + 倍率 + 特效 + 声音一体，无法错配）
    ///   特效/声音为 null/空 时自动跳过对应帧事件注册
    ///
    /// ════════════════════════════════════════════════════════════
    /// 【翻滚系统】（空格键触发，基于 ObjectBase 无敌机制）
    /// ════════════════════════════════════════════════════════════
    ///   输入：空格键按下 → TryStartRoll()
    ///   功能：
    ///     1. 播放翻滚动画 player_Roll（不循环）
    ///     2. 翻滚期间无敌（ObjectBase.SetInvulnerable），免疫所有伤害
    ///     3. 向前位移 k_rollDistance 米（3 米），基于 k_rollDuration 时长匀速移动
    ///     4. 翻滚方向：优先使用当前移动输入方向；无输入时使用角色朝向（向前翻滚）
    ///     5. 翻滚期间立即转向翻滚方向（不使用平滑旋转）
    ///
    ///   翻滚流程：
    ///     1. Update 检测空格键 → TryStartRoll()
    ///     2. TryStartRoll 内部：
    ///        - 冷却检测（k_rollCooldown = 1.0s）
    ///        - 计算翻滚方向（输入方向 or 角色朝向）
    ///        - 立即转向翻滚方向（Quaternion.LookRotation）
    ///        - SetInvulnerable(k_rollInvulnerableDuration) 设置无敌
    ///        - PlayAnimation(m_rollConfig, loop: false) 播放翻滚动画
    ///        - 标记 m_isRolling = true，记录 m_rollStartTime
    ///     3. 翻滚期间 Update → HandleRollMovement：
    ///        - 每帧设置 Rigidbody.velocity = m_rollDirection * k_rollSpeed（保留 Y 轴）
    ///        - 检测 Time.time - m_rollStartTime >= k_rollDuration → EndRoll()
    ///     4. EndRoll 内部：
    ///        - m_isRolling = false
    ///        - 清零水平速度（避免滑行）
    ///        - 恢复待机动画
    ///     5. 翻滚动画完成时 OnAnimationComplete 也会调用 EndRoll（双重保险，取先到达者）
    ///
    ///   无敌机制（ObjectBase 基类提供）：
    ///     - SetInvulnerable(duration)：设置无敌状态，duration 秒后自动解除
    ///     - IsInvulnerable 属性：访问时自动检查过期并解除
    ///     - TakeDamage 开头检查 IsInvulnerable，无敌时直接返回（不扣血、不触发 OnDamaged）
    ///     - 翻滚无敌时长 0.5s 略短于翻滚动画 0.6s，保证动画结束前无敌已消失（避免无敌滥用）
    ///
    ///   行为参数设计决策：
    ///     翻滚行为参数（距离/冷却/无敌时长）作为 Fighter 的 const 常量定义，
    ///     而非扩展到 AnimationConfig 结构体。原因：
    ///       1. AnimationConfig 的设计意图是统一管理"动画资源+帧事件"，保持通用性
    ///       2. 翻滚行为参数是 Fighter 特有的游戏逻辑，不同游戏需求差异大
    ///       3. 待机/奔跑动画也是同样模式：AnimationConfig 管动画，行为由基类+MovementConfig 处理
    ///       4. 避免 AnimationConfig 结构体膨胀，保持单一职责原则
    ///
    /// ════════════════════════════════════════════════════════════
    /// 【受击系统】（OnDamaged 回调自动触发）
    /// ════════════════════════════════════════════════════════════
    ///   触发条件：被攻击掉血时（TakeDamage → OnDamaged 回调）
    ///   功能：
    ///     1. 立即播放受击动画 player_Hurt（打断当前攻击/移动）
    ///     2. 受击期间禁止移动和攻击输入
    ///     3. 受击动画完成后自动恢复待机（OnAnimationComplete）
    ///
    ///   受击流程：
    ///     1. 敌方攻击命中 Fighter → target.TakeDamage(damage)（Sword.OnTriggerEnter 或 Fire.ApplyDamageToTarget）
    ///     2. ObjectBase.TakeDamage 内部：
    ///        - 检查 IsInvulnerable：翻滚期间无敌，直接返回（不触发受击）
    ///        - m_objectStats.TakeDamage 扣血
    ///        - 调用 OnDamaged(damage) ← Fighter 重写此方法
    ///        - 若死亡 → 调用 OnDeath()
    ///     3. Fighter.OnDamaged 内部：
    ///        - 死亡时不播放受击动画（让 OnDeath 处理）
    ///        - 打断当前攻击：m_isAttacking = false, m_comboIndex = 0, m_sword.EndSwing()
    ///        - StopMovement() 停止移动
    ///        - m_isHurting = true，PlayAnimation(m_hurtConfig, loop: false)
    ///     4. 受击动画完成 → OnAnimationComplete：
    ///        - m_isHurting = false
    ///        - 恢复待机动画
    ///
    ///   受击打断攻击的细节：
    ///     - 受击会重置连击索引（m_comboIndex = 0），下次攻击从第一段开始
    ///     - 调用 m_sword.EndSwing() 停用武器攻击状态，防止受击期间武器继续造成伤害
    ///     - 不调用 OnAttackEnded（攻击是被打断的，而非正常结束）
    ///
    ///   翻滚 vs 受击：
    ///     - 翻滚期间无敌，不会触发 OnDamaged，因此不会受击
    ///     - 翻滚结束后无敌消失，此时受到伤害会正常触发受击动画
    ///     - 受击期间按空格无法翻滚（Update 中受击状态优先级高于翻滚输入）
    ///
    /// ════════════════════════════════════════════════════════════
    /// 【武器系统集成】
    /// ════════════════════════════════════════════════════════════
    ///   Fighter 通过子对象查找 Sword 组件（GetComponentInChildren&lt;Sword&gt;()）
    ///   攻击流程与 Sword 的交互：
    ///     1. TryStartAttack 触发后，基类调用 OnAttackStarted(config)
    ///     2. Fighter.OnAttackStarted 调用 m_sword.BeginSwing(config.Multiplier)
    ///        传入当前攻击的属性倍率，Sword 启用攻击状态并记录命中
    ///     3. Sword 的 OnTriggerEnter 检测碰撞并造成伤害（使用传入的倍率计算）
    ///     4. 攻击动画完成，基类调用 OnAttackEnded
    ///     5. Fighter.OnAttackEnded 调用 m_sword.EndSwing() 禁用攻击状态
    ///
    /// ════════════════════════════════════════════════════════════
    /// 【初始化流程】
    /// ════════════════════════════════════════════════════════════
    ///   1. Awake → SetupComponents()：
    ///      - 初始化 m_animationConfigs 数组（攻击配置）
    ///      - CloneFrameEvents 克隆所有配置的 FrameEvents（idle/run/hurt/roll/attack）
    ///      - 添加 Rigidbody（冻结 X/Z 旋转）
    ///      - 添加 CapsuleCollider（自动计算包围盒，sizeMultiplier + centerOffset 调整）
    ///      - 查找 VFX 子对象并赋值到 FrameEventConfig.Target（按 TargetType 匹配）
    ///      - 查找 Sword 子对象
    ///   2. Start（异步）：
    ///      - SetupAnimatorAsync 加载 Avatar 并创建 Animator + PlayableGraph
    ///      - 并行加载 idle/run/hurt/roll 配置（LoadAnimationConfigAssetsAsync）
    ///      - 并行加载攻击配置（LoadAnimationClipsAsync）
    ///      - 并行加载挥剑音效（LoadAssetAsync&lt;AudioClip&gt;）
    ///      - 将挥剑音效赋值到 k_attack03 的 PlayAudioClip 帧事件 Target
    ///      - 播放待机动画
    ///
    /// ════════════════════════════════════════════════════════════
    /// 【属性配置】
    /// ════════════════════════════════════════════════════════════
    ///   通过 ObjectStatsConfig 配置角色属性：
    ///     生命值 150, 魔法值 80, 物理攻击 25, 物理防御 15
    ///     移动速度 6, 攻击速度 1.5, 暴击率 20%, 暴击伤害 250%
    ///     护甲穿透 15%, 魔法穿透 10%, 命中率 95%, 闪避率 5%
    ///     攻击范围 3m, 视野范围 12m
    ///
    /// ════════════════════════════════════════════════════════════
    /// 【回调方法】
    /// ════════════════════════════════════════════════════════════
    ///   攻击回调：OnAttackStarted(config)、OnAttackEnded（基类自动调用）
    ///   动画回调：OnAnimationComplete（处理攻击/受击/翻滚动画完成后的状态恢复）
    ///   自定义帧事件：OnAnimationCustomFrameEvent("HitImpact")（震屏/顿帧等）
    ///   受击回调：OnDamaged（触发受击动画，打断攻击/移动，重置连击）
    ///   死亡回调：OnDeath（清理所有战斗状态，停止移动和特效）
    ///   属性回调：OnHealed, OnManaConsumed, OnManaRestored,
    ///             OnExperienceAdded, OnGoldAdded, OnStatsReset
    ///
    ///   性能优化：
    ///     - 所有回调中的 Debug.Log 包裹 #if UNITY_EDITOR，生产构建中完全剥离
    ///     - MMO 场景下战斗频繁，避免字符串插值（$"..."）的 GC 分配
    /// </summary>
}
