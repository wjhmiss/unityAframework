using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.AI;
using AFrameWork.Core;
using AFrameWork.GameUI;

namespace AFrameWork.Sample
{
    /// <summary>
    /// 怪物类，继承 ObjectBase，集成骨骼动画、NavMeshAgent 追击、GPU 血条。
    /// 移动方式参考 Enemy：NavMeshAgent.SetDestination 驱动位置，ObjectBase 移动锁定。
    /// 动画系统参考 Fighter：AnimationConfig 统一管理，Addressables 异步加载。
    /// </summary>
    public class Monster : ObjectBase
    {
        #region Addressables 资源键常量

        private const string k_avatarKey = "NPC01_Avatar";

        #endregion

        #region 动画配置

        // 待机动画（循环）
        private static readonly AnimationConfig k_idle = new AnimationConfig
        {
            ClipKey = "NPC01_IDEL"
        };

        // 移动动画（循环）
        private static readonly AnimationConfig k_walk = new AnimationConfig
        {
            ClipKey = "NPC01_WALK"
        };

        // 攻击动画（不循环）
        private static readonly AnimationConfig k_attack = new AnimationConfig
        {
            ClipKey = "NPC01_ATTACK",
            Multiplier = new ObjectStatsConfigMultiplier(physicalAttackMultiplier: 1.0f)
        };

        // 受击动画（不循环）
        private static readonly AnimationConfig k_hurt = new AnimationConfig
        {
            ClipKey = "NPC01_HURT"
        };

        // 死亡动画（不循环）
        private static readonly AnimationConfig k_dead = new AnimationConfig
        {
            ClipKey = "NPC01_DEAD"
        };

        // 实例配置（运行时通过 LoadAnimationConfigAssetsAsync 填充 Clip）
        private AnimationConfig m_idleConfig = k_idle;
        private AnimationConfig m_walkConfig = k_walk;
        private AnimationConfig m_hurtConfig = k_hurt;
        private AnimationConfig m_deadConfig = k_dead;

        #endregion

        #region AI 状态机

        private enum MonsterState
        {
            Idle,
            Chase,
            Attack,
            Hurt,
            Dead
        }

        private MonsterState m_state = MonsterState.Idle;
        private Transform m_target;
        private float m_stateTimer;

        // 受击状态标志（受击动画期间禁止其他输入）
        private bool m_isHurting;

        // 攻击许可标志（false 时停止所有攻击行为）
        [Tooltip("攻击许可：true 可以攻击，false 禁止所有攻击行为")]
        [SerializeField]
        private bool m_canAttack = true;

        /// <summary>
        /// 攻击许可属性：true 可以攻击，false 禁止所有攻击行为
        /// 外部可通过 monster.CanAttack = false 禁止怪物攻击
        /// </summary>
        public bool CanAttack
        {
            get => m_canAttack;
            set => m_canAttack = value;
        }

        #endregion

        #region NavMeshAgent

        private NavMeshAgent m_navMeshAgent;

        // NavMeshAgent 启用开关（Inspector 可配置）
        [Tooltip("NavMeshAgent 启用：true 使用 NavMeshAgent 追击，false 禁用 NavMeshAgent 由 ObjectBase 移动系统控制")]
        [SerializeField]
        private bool m_useNavMeshAgent = false;

        #endregion

        #region 血条系统字段

        private HealthBarGPUInstanced m_gpuHealthBarManager;
        private int m_gpuHealthBarId = -1;

        [Tooltip("血条距离怪物头顶的垂直偏移（世界坐标）")]
        [SerializeField]
        private float m_healthBarHeadOffset = 3.0f;

        #endregion

        #region 配置属性

        protected override MovementConfig MovementConfig => new MovementConfig(
            keepYVelocity: true,
            autoReadInput: false,
            decelerationRate: 50f
        );

        protected override ObjectStatsConfig ObjectStatsConfig => ObjectStatsConfig.CreateMonster();

        #endregion

        #region 初始化方法

        protected override void SetupComponents()
        {
            base.SetupComponents();

            // 初始化攻击配置数组
            m_animationConfigs = new AnimationConfig[] { k_attack };
            CloneFrameEvents(ref m_idleConfig);
            CloneFrameEvents(ref m_walkConfig);
            CloneFrameEvents(ref m_hurtConfig);
            CloneFrameEvents(ref m_deadConfig);
            for (int i = 0; i < m_animationConfigs.Length; i++)
            {
                CloneFrameEvents(ref m_animationConfigs[i]);
            }

            // Rigidbody 设为 kinematic — NavMeshAgent 驱动位置，避免物理冲突
            m_rigidbody = AddObjectComponent<Rigidbody>(rb =>
            {
                rb.isKinematic = true;
                rb.useGravity = false;
                rb.constraints = RigidbodyConstraints.FreezeRotationX
                    | RigidbodyConstraints.FreezeRotationZ;
            });

            // CapsuleCollider — 硬编码尺寸，避免 Awake 时 CalculateObjectBounds 返回空包围盒
            AddObjectComponent<CapsuleCollider>(cc =>
            {
                cc.radius = 0.5f;
                cc.height = 2f;
                cc.center = new Vector3(0f, 1f, 0f);
                cc.isTrigger = false;
            });

            // NavMeshAgent — 参考 Enemy 模式，基于导航网格的AI追击系统
            // 设计原则：参数来源统一（从ObjectStatsConfig获取）、物理尺寸一致（与CapsuleCollider匹配）、追击流畅（高转向速度+自动刹车）
            if (m_useNavMeshAgent)
            {
                m_navMeshAgent = AddObjectComponent<NavMeshAgent>(agent =>
                {
                    // 移动速度（米/秒）：从配置获取，便于统一管理怪物速度属性
                    // 当前值=4f，与VisionRange(10f)配合确保追击速度合理
                    agent.speed = ObjectStatsConfig.MoveSpeed;

                    // 转向速度（度/秒）：720度/秒意味着2秒内可完成360度转向
                    // 高转向速度确保怪物能快速调整方向追击移动中的玩家
                    agent.angularSpeed = 720f;

                    // 加速度（米/秒²）：40f是较高的加速度，确保从静止快速进入追击状态
                    // 与Enemy.cs保持一致，EnterIdle→EnterChase切换时能快速加速到最大速度
                    agent.acceleration = 40f;

                    // 停止距离（米）：AttackRange*0.8f = 2f*0.8f = 1.6米
                    // 0.8倍攻击距离确保停止后仍处于攻击范围内，防止过于接近玩家导致物理碰撞重叠
                    // Update检测距离时使用 sqrDist<=attackRange²，配合停止距离实现流畅追击→攻击切换
                    agent.stoppingDistance = ObjectStatsConfig.AttackRange * 0.8f;

                    // 避障半径（米）：0.4f小于CapsuleCollider的radius(0.5f)
                    // 避免导航网格碰撞体大于实际碰撞体，确保能穿过稍窄通道（门框、走廊）
                    // 多个Monster追击时，避障系统根据此值计算彼此避让距离
                    agent.radius = 0.4f;

                    // 导航高度（米）：与CapsuleCollider高度一致(height=2f)
                    // 确保无法通过低于2米的障碍物（矮墙、管道），NavMesh烘焙时自动生成适合此高度的路径
                    agent.height = 2f;

                    // 自动刹车：true确保接近目标点时平滑减速而非急停
                    // 配合stoppingDistance=1.6f实现自然停止过渡，防止位置抖动
                    // EnterIdle时调用isStopped=true，autoBraking确保停止过程平滑
                    agent.autoBraking = true;

                    // 避障优先级（0-99，数值越小优先级越高）：50为中等优先级，适合普通怪物群体
                    // 高优先级怪物（数值小）会优先避让低优先级怪物
                    // 如有Boss级怪物可设置更小优先级(如10)确保不被阻挡
                    // 多个Monster同时追击时，避障系统根据此值计算彼此避让路径避免碰撞拥堵
                    agent.avoidancePriority = 50;
                });

                // 锁定 ObjectBase 移动 — 由 NavMeshAgent 全权控制位置
                m_isMovementLocked = true;
            }
        }

        /// <summary>
        /// 注册到 TargetRegistry，供 SimpleObjectPool 发射子弹时查找目标（方案 D）。
        /// 必须在 OnDisable 中注销，避免发射器遍历到已禁用的 Monster。
        /// </summary>
        private void OnEnable()
        {
            TargetRegistry.Register(this);
        }

        /// <summary>从 TargetRegistry 注销。</summary>
        private void OnDisable()
        {
            TargetRegistry.Unregister(this);
        }

        private async void Start()
        {
            // 异步加载骨骼并初始化 Animator
            await SetupAnimatorAsync(k_avatarKey);
            if (this == null) return;

            // 并行加载所有动画
            Task<AnimationConfig> idleTask = LoadAnimationConfigAssetsAsync(m_idleConfig);
            Task<AnimationConfig> walkTask = LoadAnimationConfigAssetsAsync(m_walkConfig);
            Task<AnimationConfig> hurtTask = LoadAnimationConfigAssetsAsync(m_hurtConfig);
            Task<AnimationConfig> deadTask = LoadAnimationConfigAssetsAsync(m_deadConfig);
            Task loadAttacksTask = LoadAnimationClipsAsync();

            await Task.WhenAll(idleTask, walkTask, hurtTask, deadTask);
            await loadAttacksTask;

            if (this == null) return;

            m_idleConfig = idleTask.Result;
            m_walkConfig = walkTask.Result;
            m_hurtConfig = hurtTask.Result;
            m_deadConfig = deadTask.Result;

            // 播放待机动画
            if (m_idleConfig.Clip != null)
            {
                PlayAnimation(m_idleConfig, loop: true);
            }

            // 查找玩家目标
            var fighter = FindObjectOfType<Fighter>();
            if (fighter != null)
            {
                m_target = fighter.transform;
            }

            // 初始化血条
            InitializeHealthBar();
        }

        private void InitializeHealthBar()
        {
            m_gpuHealthBarManager = FindObjectOfType<HealthBarGPUInstanced>();
            if (m_gpuHealthBarManager == null) return;

            m_gpuHealthBarId = m_gpuHealthBarManager.Register(transform, m_healthBarHeadOffset);
            if (m_gpuHealthBarId >= 0)
            {
                m_gpuHealthBarManager.UpdateHealth(m_gpuHealthBarId, GetCurrentHealth(), GetMaxHealth());
            }
        }

        #endregion

        #region Update — AI 状态机

        protected override void Update()
        {
            base.Update();

            if (m_state == MonsterState.Dead) return;
            if (m_isHurting) return;

            // 正在攻击时检查攻击许可，被取消时立即中断攻击
            if (m_isAttacking)
            {
                if (!m_canAttack)
                {
                    // 立即中断攻击
                    m_isAttacking = false;
                    m_comboIndex = 0;
                    if (m_idleConfig.Clip != null)
                    {
                        PlayAnimation(m_idleConfig, loop: true);
                    }
                    EnterIdle();
                }
                return;
            }

            // 动画未加载完成
            if (m_idleConfig.Clip == null) return;

            // 目标已死亡 → 停止追击/攻击，回到待机
            if (m_target != null && m_target.TryGetComponent<ObjectBase>(out var targetObj) && targetObj.IsDead())
            {
                EnterIdle();
                return;
            }

            // 无目标 → 待机
            if (m_target == null)
            {
                if (m_state != MonsterState.Idle)
                {
                    EnterIdle();
                }
                return;
            }

            float sqrDist = (m_target.position - transform.position).sqrMagnitude;
            float attackRange = ObjectStatsConfig.AttackRange;
            float visionRange = ObjectStatsConfig.VisionRange;

            if (sqrDist <= attackRange * attackRange && m_canAttack)
            {
                // 在攻击范围内 + 攻击许可 → 攻击
                if (m_state != MonsterState.Attack)
                {
                    EnterAttack();
                }
            }
            else if (sqrDist <= visionRange * visionRange)
            {
                // 在视野范围内 → 追击
                if (m_state != MonsterState.Chase)
                {
                    EnterChase();
                }

                // 持续更新追击目标
                if (m_navMeshAgent != null && m_navMeshAgent.isOnNavMesh)
                {
                    m_navMeshAgent.SetDestination(m_target.position);
                }
            }
            else
            {
                // 超出视野 → 待机
                if (m_state != MonsterState.Idle)
                {
                    EnterIdle();
                }
            }
        }

        private void EnterIdle()
        {
            m_state = MonsterState.Idle;
            if (m_navMeshAgent != null && m_navMeshAgent.isOnNavMesh)
            {
                m_navMeshAgent.isStopped = true;
            }
            if (m_idleConfig.Clip != null)
            {
                PlayAnimation(m_idleConfig, loop: true);
            }
        }

        private void EnterChase()
        {
            m_state = MonsterState.Chase;
            if (m_navMeshAgent != null && m_navMeshAgent.isOnNavMesh)
            {
                m_navMeshAgent.isStopped = false;
            }
            if (m_walkConfig.Clip != null)
            {
                PlayAnimation(m_walkConfig, loop: true);
            }
        }

        private void EnterAttack()
        {
            // 攻击许可检查
            if (!m_canAttack)
            {
                EnterIdle();
                return;
            }

            m_state = MonsterState.Attack;
            if (m_navMeshAgent != null && m_navMeshAgent.isOnNavMesh)
            {
                m_navMeshAgent.isStopped = true;
            }

            // 朝向目标
            if (m_target != null)
            {
                Vector3 lookPos = m_target.position;
                lookPos.y = transform.position.y;
                transform.rotation = Quaternion.LookRotation(lookPos - transform.position);
            }

            TryStartAttack();
        }

        #endregion

        #region 动画回调

        /// <summary>
        /// 攻击开始回调：直接对目标造成伤害。
        /// Monster 没有武器碰撞体，采用直接伤害模式（参考 Enemy.cs）。
        /// </summary>
        protected override void OnAttackStarted(AnimationConfig config)
        {
            if (m_target == null) return;

            // 距离校验 — 攻击动画播放期间目标可能移动超出范围
            float sqrDist = (m_target.position - transform.position).sqrMagnitude;
            float maxRange = ObjectStatsConfig.AttackRange * 1.5f;
            if (sqrDist > maxRange * maxRange) return;

            // 获取目标 ObjectBase 并造成伤害
            // CalculateAttack 计算伤害（不扣血），target.TakeDamage 应用（保留无敌/回调/死亡）
            if (m_target.TryGetComponent<ObjectBase>(out ObjectBase target) && target.HasObjectStats())
            {
                float damage = ObjectStatsConfig.CalculateAttack(
                    new ObjectStatsConfigMultiplier(), target.GetObjectStats(), ObjectStatsConfig);
                target.TakeDamage(damage);

#if UNITY_EDITOR
                // Debug.Log($"[{GetType().Name}] 攻击 {target.name}", this);
#endif
            }
        }

        /// <summary>
        /// 动画剪辑内置的 AnimationEvent 接收器。
        /// </summary>
        private void AttackVFX()
        {
        }

        protected override void OnAnimationComplete()
        {
            bool wasAttacking = m_isAttacking;
            bool wasHurting = m_isHurting;

            base.OnAnimationComplete();

            if (wasHurting)
            {
                m_isHurting = false;
                // 受击结束后恢复待机（Update 会自动切换到 Chase/Attack）
                EnterIdle();
            }
            else if (wasAttacking)
            {
                // 攻击结束后恢复待机（Update 会自动切换到 Chase/Attack）
                EnterIdle();
            }
        }

        #endregion

        #region 物体属性回调方法

        protected override void OnDamaged(float damage)
        {
            // 更新 GPU 血条
            if (m_gpuHealthBarId >= 0 && m_gpuHealthBarManager != null)
            {
                m_gpuHealthBarManager.UpdateHealth(m_gpuHealthBarId, GetCurrentHealth(), GetMaxHealth());
            }

            if (IsDead()) return;
            if (m_hurtConfig.Clip == null) return;

            // 打断攻击
            if (m_isAttacking)
            {
                m_isAttacking = false;
                m_comboIndex = 0;
            }

            // 停止 NavMeshAgent
            if (m_navMeshAgent != null && m_navMeshAgent.isOnNavMesh)
            {
                m_navMeshAgent.isStopped = true;
            }

            m_isHurting = true;
            m_state = MonsterState.Hurt;
            PlayAnimation(m_hurtConfig, loop: false);
        }

        protected override void OnHealed(float amount)
        {
            if (m_gpuHealthBarId >= 0 && m_gpuHealthBarManager != null)
            {
                m_gpuHealthBarManager.UpdateHealth(m_gpuHealthBarId, GetCurrentHealth(), GetMaxHealth());
            }
        }

        protected override void OnDeath()
        {
            m_state = MonsterState.Dead;
            m_isAttacking = false;
            m_isHurting = false;
            m_isMovementLocked = false;

            // 停止 NavMeshAgent
            if (m_navMeshAgent != null)
            {
                m_navMeshAgent.isStopped = true;
                m_navMeshAgent.enabled = false;
            }

            // 播放死亡动画
            if (m_deadConfig.Clip != null)
            {
                PlayAnimation(m_deadConfig, loop: false);
            }

            // 注销 GPU 血条
            if (m_gpuHealthBarId >= 0 && m_gpuHealthBarManager != null)
            {
                m_gpuHealthBarManager.Unregister(m_gpuHealthBarId);
                m_gpuHealthBarId = -1;
            }

            StopMovement();
        }

        protected override void OnManaConsumed(float amount) { }
        protected override void OnManaRestored(float amount) { }
        protected override void OnExperienceAdded(float amount) { }
        protected override void OnGoldAdded(int amount) { }
        protected override void OnStatsReset() { }

        #endregion
    }

    /// <summary>
    /// Monster 使用说明：
    /// ============================================================
    /// 怪物类，继承 ObjectBase，集成骨骼动画、NavMeshAgent 追击、GPU 血条。
    /// 移动方式参考 Enemy：NavMeshAgent.SetDestination 驱动位置，ObjectBase 移动锁定。
    /// 动画系统参考 Fighter：AnimationConfig 统一管理，Addressables 异步加载。
    /// 典型应用场景：MMO 野外怪物、副本 BOSS、剧情 NPC 敌人、塔防敌人单位。
    ///
    /// ════════════════════════════════════════════════════════════
    /// 【与 ObjectBase 的关系】
    /// ════════════════════════════════════════════════════════════
    ///   - 继承 ObjectBase，复用父类的属性系统、组件管理、阵营判定、伤害系统、动画系统
    ///   - 重写 MovementConfig：keepYVelocity=true, autoReadInput=false, decelerationRate=50f
    ///     （autoReadInput=false 因为怪物由 AI 控制移动，不读取玩家输入）
    ///   - 重写 ObjectStatsConfig：Type=ObjectType.Tank, FactionID=11（怪物阵营）
    ///   - 重写 SetupComponents：动态添加 Rigidbody（kinematic）+ CapsuleCollider + NavMeshAgent
    ///   - 重写 Update：实现 AI 状态机（Idle/Chase/Attack/Hurt/Dead）
    ///   - 重写 OnDamaged/OnHealed/OnDeath 回调，集成 GPU 血条更新
    ///   - 父类自动处理：组件管理、属性系统、阵营判定、伤害计算、动画播放、帧事件回调
    ///
    /// ════════════════════════════════════════════════════════════
    /// 【AI 状态机】
    /// ════════════════════════════════════════════════════════════
    ///   MonsterState 枚举：5 个互斥状态
    ///
    ///     Idle  — 待机状态：NavMeshAgent.isStopped=true，播放待机动画
    ///     Chase — 追击状态：NavMeshAgent.SetDestination 跟随目标，播放移动动画
    ///     Attack— 攻击状态：NavMeshAgent.isStopped=true，朝向目标并播放攻击动画
    ///     Hurt  — 受击状态：被打断攻击后播放受击动画，期间禁止其他输入
    ///     Dead  — 死亡状态：播放死亡动画，禁用 NavMeshAgent，注销血条
    ///
    ///   状态切换条件（在 Update 中检测）：
    ///     1. m_state == Dead → 直接返回，不再处理任何状态切换
    ///     2. m_isHurting == true → 直接返回，受击动画期间禁止其他输入
    ///     3. m_isAttacking == true：
    ///        - 若 m_canAttack == false → 立即中断攻击，回到 Idle
    ///        - 否则等待攻击动画结束（OnAnimationComplete 触发后回到 Idle）
    ///     4. 目标已死亡（target.IsDead()）→ EnterIdle
    ///     5. 无目标 → EnterIdle
    ///     6. sqrDist <= AttackRange² && m_canAttack → EnterAttack
    ///     7. sqrDist <= VisionRange² → EnterChase + SetDestination
    ///     8. 超出视野 → EnterIdle
    ///
    ///   状态进入方法：
    ///     EnterIdle()    — 停止 NavMeshAgent，播放待机动画（loop=true）
    ///     EnterChase()   — 启动 NavMeshAgent，播放移动动画（loop=true）
    ///     EnterAttack()  — 攻击许可检查 → 停止 NavMeshAgent → 朝向目标 → TryStartAttack()
    ///     （Hurt 状态由 OnDamaged 触发，Dead 状态由 OnDeath 触发，无独立 Enter 方法）
    ///
    ///   ⚠️ 距离比较使用 sqrMagnitude 而非 Vector3.Distance，避免 Mathf.Sqrt 开销
    ///
    /// ════════════════════════════════════════════════════════════
    /// 【NavMeshAgent 配置详解】
    /// ════════════════════════════════════════════════════════════
    ///   m_useNavMeshAgent : bool (Inspector 可配置，默认 false)
    ///     - true：启用 NavMeshAgent 追击系统，锁定 ObjectBase 移动（m_isMovementLocked=true）
    ///     - false：禁用 NavMeshAgent，由 ObjectBase 移动系统控制（当前未实现 AI 移动逻辑）
    ///     - 设计原因：灵活切换移动方式，便于测试不同移动方案
    ///
    ///   NavMeshAgent 参数（在 SetupComponents 中初始化）：
    ///     speed             = MoveSpeed (4f)        从 ObjectStatsConfig 获取，统一管理速度
    ///     angularSpeed      = 720f                  高转向速度，2 秒内完成 360° 转向
    ///     acceleration      = 40f                   高加速度，从静止快速进入追击
    ///     stoppingDistance  = AttackRange × 0.8f   0.8 倍攻击距离，停止后仍处于攻击范围
    ///     radius            = 0.4f                  小于 CapsuleCollider.radius(0.5f)，可穿过稍窄通道
    ///     height            = 2f                    与 CapsuleCollider.height 一致，无法通过低于 2 米障碍
    ///     autoBraking       = true                  接近目标点平滑减速，防止急停抖动
    ///     avoidancePriority = 50                    中等优先级（0-99，数值越小优先级越高）
    ///
    ///   ⚠️ NavMeshAgent 启用前必须烘焙 NavMesh，否则 isOnNavMesh 为 false
    ///   ⚠️ 调用 SetDestination / isStopped 前必须检查 m_navMeshAgent.isOnNavMesh，避免报错
    ///
    /// ════════════════════════════════════════════════════════════
    /// 【动画系统 — Addressables 异步加载】
    /// ════════════════════════════════════════════════════════════
    ///   动画配置（static readonly，实例共享）：
    ///     k_idle  — NPC01_IDEL   待机动画（循环）
    ///     k_walk  — NPC01_WALK   移动动画（循环）
    ///     k_attack— NPC01_ATTACK 攻击动画（不循环，Multiplier=1.0×基础伤害）
    ///     k_hurt  — NPC01_HURT   受击动画（不循环）
    ///     k_dead  — NPC01_DEAD   死亡动画（不循环）
    ///
    ///   实例配置（运行时通过 LoadAnimationConfigAssetsAsync 填充 Clip）：
    ///     m_idleConfig / m_walkConfig / m_hurtConfig / m_deadConfig
    ///     每个实例独立持有配置，避免共享 Clip 引用导致的状态污染
    ///
    ///   加载流程（Start 中执行）：
    ///     1. await SetupAnimatorAsync(k_avatarKey)        异步加载 Avatar 并初始化 Animator
    ///     2. 并行 await LoadAnimationConfigAssetsAsync    加载所有 AnimationClip 资源
    ///     3. await LoadAnimationClipsAsync()              加载攻击动画配置数组
    ///     4. await Task.WhenAll(...)                      等待所有加载完成
    ///     5. 检查 this == null（异步等待期间对象可能被销毁）
    ///     6. 回填 m_idleConfig / m_walkConfig / ...       填充 Clip 引用
    ///     7. 播放待机动画
    ///
    ///   ⚠️ async void Start 中每个 await 后必须检查 this == null
    ///   ⚠️ AnimationConfig 使用 struct，赋值时为值拷贝，修改实例不影响 static 配置
    ///   ⚠️ CloneFrameEvents 在 SetupComponents 中执行，将 static 配置的帧事件深拷贝到实例
    ///
    /// ════════════════════════════════════════════════════════════
    /// 【GPU 血条集成】
    /// ════════════════════════════════════════════════════════════
    ///   m_gpuHealthBarManager : HealthBarGPUInstanced
    ///     - GPU 实例化血条管理器引用（在 InitializeHealthBar 中通过 FindObjectOfType 获取）
    ///     - 单场景只需一个实例，所有 Monster 共享同一管理器
    ///
    ///   m_gpuHealthBarId : int
    ///     - 当前 Monster 在 GPU 血条系统中的实例 ID（&lt; 0 表示未注册）
    ///     - 由 m_gpuHealthBarManager.Register 返回
    ///     - 用于 UpdateHealth / Unregister 调用
    ///
    ///   m_healthBarHeadOffset : float (Inspector 可配置，默认 3.0f)
    ///     - 血条距离怪物头顶的垂直偏移（世界坐标）
    ///     - 用于 Register 时计算血条世界位置
    ///
    ///   血条生命周期：
    ///     Start → InitializeHealthBar() → Register()              注册并初始化血条
    ///     OnDamaged / OnHealed → UpdateHealth()                   血量变化时更新血条
    ///     OnDeath → Unregister()                                   死亡时注销血条
    ///
    ///   ⚠️ 若场景中无 HealthBarGPUInstanced，InitializeHealthBar 直接返回，血条不显示
    ///   ⚠️ Unregister 后必须将 m_gpuHealthBarId 置为 -1，避免重复注销
    ///
    /// ════════════════════════════════════════════════════════════
    /// 【TargetRegistry 注册 / 注销】
    /// ════════════════════════════════════════════════════════════
    ///   OnEnable → TargetRegistry.Register(this)
    ///     - 将自身注册到全局目标注册表
    ///     - 供 SimpleObjectPool 发射子弹时查找目标（方案 D，消除 FindObjectOfType）
    ///     - Bullet 通过 TargetRegistry.GetAllTargets() 获取所有 Monster 实例
    ///
    ///   OnDisable → TargetRegistry.Unregister(this)
    ///     - 从注册表注销，避免发射器遍历到已禁用的 Monster
    ///     - ⚠️ TargetRegistry 内部使用 InstanceID 作为 Key，fake-null 仍可注销
    ///
    ///   ⚠️ 必须在 OnEnable/OnDisable 中成对注册/注销，避免内存泄漏
    ///
    /// ════════════════════════════════════════════════════════════
    /// 【攻击许可机制】
    /// ════════════════════════════════════════════════════════════
    ///   m_canAttack : bool (Inspector 可配置，默认 true)
    ///     - false 时禁止所有攻击行为
    ///     - 可在运行时通过 monster.CanAttack = false 动态禁用
    ///     - 应用场景：剧情过场、和平区域、怪物被眩晕/冰冻、调试观察
    ///
    ///   CanAttack 属性：
    ///     - 公开 get/set，外部可直接修改
    ///     - 不触发任何状态切换，Update 中检测到变化时自然处理
    ///
    ///   攻击许可检查点：
    ///     1. Update：m_isAttacking 期间检查，false 时立即中断攻击
    ///     2. EnterAttack：进入攻击状态前检查，false 时直接 EnterIdle
    ///     3. Update：在攻击范围内检查 m_canAttack，false 时不进入攻击状态
    ///
    /// ════════════════════════════════════════════════════════════
    /// 【字段详解】
    /// ════════════════════════════════════════════════════════════
    ///   m_state : MonsterState
    ///     - 当前 AI 状态（Idle/Chase/Attack/Hurt/Dead）
    ///     - 初始值 Idle
    ///
    ///   m_target : Transform
    ///     - 当前攻击/追击目标（通常是玩家 Fighter）
    ///     - 在 Start 中通过 FindObjectOfType&lt;Fighter&gt;() 获取
    ///     - 为 null 时进入 Idle 状态
    ///
    ///   m_stateTimer : float
    ///     - 状态计时器（保留字段，当前未使用）
    ///
    ///   m_isHurting : bool
    ///     - 受击状态标志（受击动画期间为 true）
    ///     - Update 顶部检查，true 时直接返回，禁止其他输入
    ///     - OnAnimationComplete 中重置为 false
    ///
    ///   m_navMeshAgent : NavMeshAgent
    ///     - 导航代理组件引用（m_useNavMeshAgent=true 时创建）
    ///     - 调用方法前必须检查 isOnNavMesh
    ///
    ///   m_gpuHealthBarManager : HealthBarGPUInstanced
    ///     - GPU 血条管理器引用
    ///
    ///   m_gpuHealthBarId : int
    ///     - GPU 血条实例 ID（&lt; 0 表示未注册）
    ///
    ///   m_healthBarHeadOffset : float
    ///     - 血条头顶偏移（默认 3.0f）
    ///
    /// ════════════════════════════════════════════════════════════
    /// 【生命周期方法】
    /// ════════════════════════════════════════════════════════════
    ///   SetupComponents()（重写）
    ///     - 调用 base.SetupComponents() 添加父类默认组件
    ///     - 初始化攻击动画配置数组 m_animationConfigs = { k_attack }
    ///     - CloneFrameEvents：将 static 配置的帧事件深拷贝到实例
    ///     - 添加 Rigidbody（kinematic=true，useGravity=false，冻结 XZ 旋转）
    ///     - 添加 CapsuleCollider（radius=0.5, height=2, center=(0,1,0)，isTrigger=false）
    ///     - 若 m_useNavMeshAgent=true：添加并配置 NavMeshAgent，锁定移动
    ///
    ///   OnEnable()（重写）
    ///     - TargetRegistry.Register(this) 注册到全局目标表
    ///
    ///   OnDisable()（重写）
    ///     - TargetRegistry.Unregister(this) 从全局目标表注销
    ///
    ///   Start()（async void）
    ///     - 异步加载 Avatar（SetupAnimatorAsync）
    ///     - 并行加载所有动画（LoadAnimationConfigAssetsAsync × 4 + LoadAnimationClipsAsync）
    ///     - 等待所有加载完成（Task.WhenAll）
    ///     - 回填实例配置（m_idleConfig / m_walkConfig / ...）
    ///     - 播放待机动画
    ///     - 查找玩家目标（FindObjectOfType&lt;Fighter&gt;）
    ///     - 初始化血条（InitializeHealthBar）
    ///
    ///   Update()（重写）
    ///     - 调用 base.Update() 处理父类逻辑
    ///     - 实现 AI 状态机（详见上方 AI 状态机章节）
    ///
    ///   OnAttackStarted(AnimationConfig)（重写）
    ///     - 攻击动画开始时回调
    ///     - 距离校验：sqrDist &lt;= (AttackRange × 1.5)²，超出则不造成伤害
    ///     - 直接对目标造成 PhysicalAttack 伤害（无武器碰撞体）
    ///     - 调用 target.TakeDamage(damage)
    ///
    ///   OnAnimationComplete()（重写）
    ///     - 动画播放完成回调
    ///     - 若 wasHurting：清除受击状态，回到 Idle
    ///     - 若 wasAttacking：回到 Idle（Update 会自动切换到 Chase/Attack）
    ///
    ///   OnDamaged(float)（重写）
    ///     - 更新 GPU 血条
    ///     - 若已死亡：直接返回（OnDeath 会处理）
    ///     - 若受伤动画未加载：直接返回
    ///     - 打断攻击：m_isAttacking=false, m_comboIndex=0
    ///     - 停止 NavMeshAgent
    ///     - 设置 m_isHurting=true，切换到 Hurt 状态
    ///     - 播放受击动画（loop=false）
    ///
    ///   OnHealed(float)（重写）
    ///     - 更新 GPU 血条
    ///
    ///   OnDeath()（重写）
    ///     - 切换到 Dead 状态
    ///     - 重置攻击/受击标志
    ///     - 解除移动锁定（m_isMovementLocked=false）
    ///     - 停止并禁用 NavMeshAgent
    ///     - 播放死亡动画（loop=false）
    ///     - 注销 GPU 血条
    ///     - StopMovement()
    ///
    /// ════════════════════════════════════════════════════════════
    /// 【配置属性】
    /// ════════════════════════════════════════════════════════════
    ///   MovementConfig（重写）：
    ///     keepYVelocity     = true     保留 Y 轴速度（用于跳跃/击退）
    ///     autoReadInput     = false    不读取玩家输入（AI 控制移动）
    ///     decelerationRate  = 50f      减速速率（米/秒²）
    ///
    ///   ObjectStatsConfig（重写）：
    ///     Type              = ObjectType.Tank
    ///     FactionID         = 11       怪物阵营（k_monsterFactionMinID=11）
    ///     MaxHealth         = 100f
    ///     PhysicalAttack    = 15f
    ///     PhysicalDefense   = 10f
    ///     TrueDamage        = 3f       真实伤害（无视防御）
    ///     MagicAttack       = 5f
    ///     MagicDefense      = 8f
    ///     MoveSpeed         = 4f       NavMeshAgent.speed 数据源
    ///     AttackSpeed       = 1.0f     攻击速度倍率
    ///     CriticalRate      = 0.1f     10% 暴击率
    ///     CriticalDamageMultiplier = 2.0f    暴击伤害 2 倍
    ///     AttackRange       = 2f       攻击距离（米）
    ///     VisionRange       = 10f      视野范围（米）
    ///     Level             = 1
    ///
    /// ════════════════════════════════════════════════════════════
    /// 【性能优化】
    /// ════════════════════════════════════════════════════════════
    ///   1. 距离比较使用 sqrMagnitude
    ///      - sqrDist = (m_target.position - transform.position).sqrMagnitude
    ///      - 避免 Vector3.Distance 的 Mathf.Sqrt 开销
    ///      - 比较时使用 sqrDist &lt;= range * range
    ///
    ///   2. NavMeshAgent 操作前检查 isOnNavMesh
    ///      - if (m_navMeshAgent != null && m_navMeshAgent.isOnNavMesh)
    ///      - 避免 NavMesh 未烘焙时报错
    ///
    ///   3. TryGetComponent 替代 GetComponent
    ///      - m_target.TryGetComponent&lt;ObjectBase&gt;(out ObjectBase target)
    ///      - 避免空引用检查开销
    ///
    ///   4. Debug.Log 包裹在 #if UNITY_EDITOR 中
    ///      - 战斗频繁，字符串插值会产生 GC 压力
    ///      - 生产构建中完全剥离
    ///
    ///   5. async void Start 中每个 await 后检查 this == null
    ///      - 异步等待期间对象可能被销毁（场景卸载/对象池回收）
    ///      - 避免访问已销毁对象的成员
    ///
    ///   6. 动画配置使用 static readonly 共享
    ///      - 5 个 AnimationConfig 实例所有 Monster 共享
    ///      - CloneFrameEvents 在 SetupComponents 中深拷贝帧事件到实例配置
    ///
    ///   7. TargetRegistry 注册消除 FindObjectOfType
    ///      - Bullet 通过 TargetRegistry.GetAllTargets() 获取所有 Monster
    ///      - 避免每次发射子弹时扫描整个场景
    ///
    ///   8. GPU 实例化血条
    ///      - DrawMeshInstancedIndirect 一次绘制所有血条
    ///      - 与 HealthBarController（UI Toolkit）方案对比，性能提升 10-100 倍
    ///
    /// ════════════════════════════════════════════════════════════
    /// 【使用示例】
    /// ════════════════════════════════════════════════════════════
    ///
    /// ─── 示例 1：场景中放置 Monster ───
    ///   1. 在场景中创建空 GameObject，添加 Monster 组件
    ///   2. 在 Inspector 中配置：
    ///      - m_useNavMeshAgent = true（启用 NavMeshAgent 追击）
    ///      - m_canAttack = true（允许攻击）
    ///      - m_healthBarHeadOffset = 3.0（血条头顶偏移）
    ///   3. 烘焙 NavMesh（Window → AI → Navigation → Bake）
    ///   4. 场景中放置 HealthBarGPUInstanced 管理器
    ///   5. 运行场景，Monster 自动追击 Fighter 并攻击
    ///
    /// ─── 示例 2：运行时禁用攻击 ───
    ///   // 剧情过场中禁止 Monster 攻击
    ///   Monster monster = FindObjectOfType&lt;Monster&gt;();
    ///   monster.CanAttack = false;
    ///
    ///   // 过场结束后恢复攻击
    ///   monster.CanAttack = true;
    ///
    /// ─── 示例 3：动态修改血条偏移 ───
    ///   // Monster 体型较大，需要更高的血条偏移
    ///   [SerializeField] private Monster m_bossMonster;
    ///
    ///   void Start()
    ///   {
    ///       // 注意：此字段未提供公开 setter，需要在 Monster.cs 中添加
    ///       // 或通过反射修改 m_healthBarHeadOffset
    ///       // 当前版本仅支持 Inspector 配置
    ///   }
    ///
    /// ─── 示例 4：自定义怪物属性 ───
    ///   public class EliteMonster : Monster
    ///   {
    ///       protected override ObjectStatsConfig ObjectStatsConfig => new ObjectStatsConfig
    ///       {
    ///           Type = ObjectType.Tank,
    ///           FactionID = 11,
    ///           MaxHealth = 500f,           // 精英怪血量 5 倍
    ///           CurrentHealth = 500f,
    ///           PhysicalAttack = 30f,       // 攻击力 2 倍
    ///           PhysicalDefense = 20f,
    ///           MoveSpeed = 6f,             // 移动速度更快
    ///           AttackRange = 3f,           // 攻击范围更远
    ///           VisionRange = 15f,           // 视野范围更远
    ///           Level = 5
    ///       };
    ///   }
    ///
    /// ─── 示例 5：与 Bullet 协同工作 ───
    ///   // Monster 在 OnEnable 中自动注册到 TargetRegistry
    ///   // Bullet 通过 TargetRegistry.GetAllTargets() 获取所有 Monster
    ///   // Bullet.OnTriggerEnter 中检测 Monster 阵营并造成伤害
    ///
    ///   // 派生类可重写阵营实现阵营差异化
    ///   public class BossMonster : Monster
    ///   {
    ///       protected override ObjectStatsConfig ObjectStatsConfig => new ObjectStatsConfig
    ///       {
    ///           Type = ObjectType.Tank,
    ///           FactionID = 12,             // 不同阵营，可互相攻击
    ///           MaxHealth = 1000f,
    ///           // ...
    ///       };
    ///   }
    ///
    /// ════════════════════════════════════════════════════════════
    /// 【注意事项】
    /// ════════════════════════════════════════════════════════════
    ///   ⚠️ NavMeshAgent 启用前必须烘焙 NavMesh，否则 isOnNavMesh 为 false
    ///   ⚠️ Addressables 资源键必须正确配置（NPC01_Avatar, NPC01_IDEL, NPC01_WALK, ...）
    ///   ⚠️ 场景中必须有 HealthBarGPUInstanced 才能显示血条
    ///   ⚠️ 场景中必须有 Fighter 才能触发追击/攻击行为（否则 Monster 一直 Idle）
    ///   ⚠️ CapsuleCollider 硬编码尺寸（radius=0.5, height=2），派生类可重写 SetupComponents 调整
    ///   ⚠️ 攻击伤害直接调用 target.TakeDamage，未使用武器碰撞体（与 Sword 模式不同）
    ///   ⚠️ OnManaConsumed/OnManaRestored/OnExperienceAdded/OnGoldAdded/OnStatsReset 当前为空实现
    ///      派生类可按需重写实现法力消耗、经验、金币等逻辑
    /// </summary>
}
