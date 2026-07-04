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
            Multiplier = new ObjectStatsConfigMultiplier(baseDamageMultiplier: 1.0f)
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

        protected override ObjectStatsConfig ObjectStatsConfig => new ObjectStatsConfig
        {
            Type = ObjectType.Tank,
            FactionID = 11,  // 怪物阵营 (k_monsterFactionMinID=11, k_monsterFactionMaxID=50)
            MaxHealth = 100f,
            CurrentHealth = 100f,
            PhysicalAttack = 15f,
            PhysicalDefense = 10f,
            TrueDamage = 3f,
            MagicAttack = 5f,
            MagicDefense = 8f,
            MoveSpeed = 4f,
            AttackSpeed = 1.0f,
            CastSpeed = 1.0f,
            CriticalRate = 0.1f,
            CriticalDamageMultiplier = 2.0f,
            ArmorPenetration = 0.1f,
            MagicPenetration = 0.05f,
            HealthRegeneration = 1f,
            ManaRegeneration = 1f,
            MaxMana = 50f,
            CurrentMana = 50f,
            CooldownReduction = 0.05f,
            EvasionRate = 0.03f,
            HitRate = 0.9f,
            AttackRange = 2f,
            VisionRange = 10f,
            Experience = 0f,
            Level = 1,
            Gold = 0
        };

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
            if (m_target.TryGetComponent<ObjectBase>(out ObjectBase target))
            {
                float damage = ObjectStatsConfig.PhysicalAttack;
                target.TakeDamage(damage);

#if UNITY_EDITOR
                // Debug.Log($"[{GetType().Name}] 攻击 {target.name}，造成 {damage} 点伤害", this);
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
}
