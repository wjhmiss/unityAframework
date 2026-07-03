using UnityEngine;
using AFrameWork.Core;
using AFrameWork.GameUI;

namespace AFrameWork.Sample
{
    /// <summary>
    /// 怪物类，演示如何创建一个基础的怪物物体
    /// 继承自 ObjectBase，使用 SetupComponents + AddObjectComponent 动态添加组件
    /// 集成血条系统，实时显示怪物血量变化
    /// </summary>
    public class Monster : ObjectBase
    {
        #region 血条系统字段

        /// <summary>
        /// GPU 实例化血条管理器引用（场景中的全局管理器）
        /// </summary>
        private HealthBarGPUInstanced m_gpuHealthBarManager;

        /// <summary>
        /// GPU 血条实例 ID（由 Register 返回，用于后续更新/注销）
        /// </summary>
        private int m_gpuHealthBarId = -1;

        /// <summary>
        /// 血条头部偏移（根据怪物模型高度调整）
        /// </summary>
        [Tooltip("血条距离怪物头顶的垂直偏移（世界坐标）")]
        [SerializeField]
        private float m_healthBarHeadOffset = 3.0f;

        #endregion
        #region 配置属性

        /// <summary>
        /// 移动配置属性，控制移动行为
        /// 注意：移动速度由 ObjectStatsConfig.MoveSpeed 提供
        /// </summary>
        protected override MovementConfig MovementConfig => new MovementConfig(
            keepYVelocity: true,
            autoReadInput: false,     // 怪物通常不自动读取玩家输入
            decelerationRate: 5f     // 怪物有中等惯性，停止时稍微滑行
        );

        /// <summary>
        /// 物体属性配置，包含怪物所有数值属性
        /// </summary>
        protected override ObjectStatsConfig ObjectStatsConfig => new ObjectStatsConfig
        {
            // 基础属性
            Type = ObjectType.Tank,
            FactionID = 2,               // 敌人阵营
            MaxHealth = 100f,
            CurrentHealth = 100f,
            PhysicalAttack = 15f,
            PhysicalDefense = 10f,
            TrueDamage = 3f,
            MagicAttack = 5f,
            MagicDefense = 8f,

            // 速度属性
            MoveSpeed = 4f,              // 怪物移动速度较慢
            AttackSpeed = 1.0f,
            CastSpeed = 1.0f,

            // 暴击属性
            CriticalRate = 0.1f,
            CriticalDamageMultiplier = 2.0f,

            // 穿透属性
            ArmorPenetration = 0.1f,
            MagicPenetration = 0.05f,

            // 恢复属性
            HealthRegeneration = 1f,
            ManaRegeneration = 1f,

            // 特殊属性
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

        /// <summary>
        /// 重写 SetupComponents，使用 AddObjectComponent 动态添加组件
        /// 直接传入组件类型和初始化回调，无需额外的配置类
        /// </summary>
        protected override void SetupComponents()
        {
            base.SetupComponents();

            // 添加 Rigidbody 并配置参数
            m_rigidbody = AddObjectComponent<Rigidbody>(rb =>
            {
                rb.mass = 1f;
                rb.drag = 0f;
                rb.angularDrag = 0.05f;
                rb.useGravity = true;
                rb.constraints = RigidbodyConstraints.FreezeRotationX
                    | RigidbodyConstraints.FreezeRotationZ;
            });

            // 添加 CapsuleCollider，根据对象包围盒设置大小
            // sizeMultiplier: XYZ 轴分别缩放，怪物碰撞体 XZ 稍大便于命中
            AddCapsuleCollider(CalculateObjectBounds(), new Vector3(0.4f, 1.2f, 0.4f), new Vector3(-0.30f, -0.45f, -0.10f));
        }

        /// <summary>
        /// Unity Start 方法，在组件初始化完成后创建血条
        /// 注意：ObjectBase 的 Awake 已经完成组件初始化，血条创建需要在 Start 中进行
        /// </summary>
        protected virtual void Start()
        {
            // 初始化血条系统
            InitializeHealthBar();
        }

        /// <summary>
        /// 初始化血条系统
        /// 查找场景中的 HealthBarGPUInstanced 并注册 GPU 血条
        /// </summary>
        private void InitializeHealthBar()
        {
#if UNITY_EDITOR
            Debug.Log($"[{GetType().Name}] 开始初始化 GPU 血条系统...");
#endif

            // 查找场景中的 GPU 血条管理器
            m_gpuHealthBarManager = FindObjectOfType<HealthBarGPUInstanced>();

            if (m_gpuHealthBarManager == null)
            {
#if UNITY_EDITOR
                Debug.LogWarning($"[{GetType().Name}] 场景中未找到 HealthBarGPUInstanced，血条系统未启用！", this);
                Debug.LogWarning($"[{GetType().Name}] 解决方案：在 Hierarchy 中创建 GameObject 并添加 HealthBarGPUInstanced 组件", this);
#endif
                return;
            }

            // 注册 GPU 血条
            m_gpuHealthBarId = m_gpuHealthBarManager.Register(transform, m_healthBarHeadOffset);

            if (m_gpuHealthBarId < 0)
            {
#if UNITY_EDITOR
                Debug.LogError($"[{GetType().Name}] GPU 血条注册失败！", this);
#endif
                return;
            }

            // 初始化血量显示
            m_gpuHealthBarManager.UpdateHealth(m_gpuHealthBarId, GetCurrentHealth(), GetMaxHealth());

#if UNITY_EDITOR
            Debug.Log($"[{GetType().Name}] GPU 血条已注册，ID: {m_gpuHealthBarId}，当前血量: {GetCurrentHealth()}/{GetMaxHealth()}", this);
#endif
        }

        #endregion

        #region 物体属性回调方法（重写父类的所有回调）

        // 注意：所有回调中的 Debug.Log 均包裹在 #if UNITY_EDITOR 中
        // MMO 场景下战斗频繁，字符串插值会产生 GC 压力，生产构建中需要完全剥离

        /// <summary>
        /// 受到伤害时的回调
        /// 更新血条显示，血量变化会触发平滑过渡动画
        /// </summary>
        protected override void OnDamaged(float damage)
        {
#if UNITY_EDITOR
            Debug.Log($"怪物受到 {damage} 点伤害！当前生命值：{GetCurrentHealth()}/{GetMaxHealth()}");
#endif

            // 更新 GPU 血条显示
            if (m_gpuHealthBarId >= 0 && m_gpuHealthBarManager != null)
            {
                m_gpuHealthBarManager.UpdateHealth(m_gpuHealthBarId, GetCurrentHealth(), GetMaxHealth());
            }
        }

        /// <summary>
        /// 恢复生命值时的回调
        /// 更新血条显示，血量变化会触发平滑过渡动画
        /// </summary>
        protected override void OnHealed(float amount)
        {
#if UNITY_EDITOR
            Debug.Log($"怪物恢复 {amount} 点生命值！当前生命值：{GetCurrentHealth()}/{GetMaxHealth()}");
#endif

            // 更新 GPU 血条显示
            if (m_gpuHealthBarId >= 0 && m_gpuHealthBarManager != null)
            {
                m_gpuHealthBarManager.UpdateHealth(m_gpuHealthBarId, GetCurrentHealth(), GetMaxHealth());
            }
        }

        /// <summary>
        /// 消耗魔法值时的回调
        /// </summary>
        protected override void OnManaConsumed(float amount)
        {
#if UNITY_EDITOR
            Debug.Log($"怪物消耗 {amount} 点魔法值！当前魔法值：{GetCurrentMana()}/{GetMaxMana()}");
#endif
        }

        /// <summary>
        /// 恢复魔法值时的回调
        /// </summary>
        protected override void OnManaRestored(float amount)
        {
#if UNITY_EDITOR
            Debug.Log($"怪物恢复 {amount} 点魔法值！当前魔法值：{GetCurrentMana()}/{GetMaxMana()}");
#endif
        }

        /// <summary>
        /// 增加经验值时的回调
        /// </summary>
        protected override void OnExperienceAdded(float amount)
        {
#if UNITY_EDITOR
            Debug.Log($"怪物获得 {amount} 点经验值！当前经验值：{GetObjectStats().Experience}");
#endif
        }

        /// <summary>
        /// 增加金币时的回调
        /// </summary>
        protected override void OnGoldAdded(int amount)
        {
#if UNITY_EDITOR
            Debug.Log($"怪物获得 {amount} 金币！当前金币：{GetGold()}");
#endif
        }

        /// <summary>
        /// 物体死亡时的回调
        /// 停止移动并移除血条
        /// </summary>
        protected override void OnDeath()
        {
#if UNITY_EDITOR
            Debug.Log("怪物死亡！");
#endif

            // 停止移动，防止死亡后继续滑行
            StopMovement();

            // 注销 GPU 血条
            if (m_gpuHealthBarId >= 0 && m_gpuHealthBarManager != null)
            {
                m_gpuHealthBarManager.Unregister(m_gpuHealthBarId);
                m_gpuHealthBarId = -1;
            }
        }

        /// <summary>
        /// 重置属性时的回调
        /// </summary>
        protected override void OnStatsReset()
        {
#if UNITY_EDITOR
            Debug.Log("怪物属性已重置！");
#endif
        }

        #endregion
    }

    /// <summary>
    /// Monster 使用说明：
    /// ============================================================
    /// 怪物类，继承 ObjectBase，演示基础的怪物物体实现。
    /// 包含：动态组件创建、属性配置、回调方法、移动系统。
    /// 作为 MMO 项目中怪物物体的最小可运行模板，AI 逻辑可在此基础上扩展。
    ///
    /// ════════════════════════════════════════════════════════════
    /// 【与 ObjectBase 的关系】
    /// ════════════════════════════════════════════════════════════
    ///   - Monster 继承 ObjectBase，复用父类的所有基础能力
    ///   - 未重写 AnimationConfig 相关字段（未启用动画系统），可按需扩展
    ///   - 未重写帧事件和攻击连击系统，AI 攻击逻辑需自行实现
    ///   - 重写所有生命周期回调方法（OnDamaged/OnHealed/OnDeath 等）以响应战斗事件
    ///   - 父类自动处理：组件管理、移动控制、属性系统、阵营判定
    ///
    /// ════════════════════════════════════════════════════════════
    /// 【移动系统】
    /// ════════════════════════════════════════════════════════════
    ///   - 不自动读取玩家输入（autoReadInput: false，怪物由 AI 控制移动）
    ///     AI 通过 SetMovementInput(Vector3) 主动注入移动方向
    ///   - 移动速度由 ObjectStatsConfig.MoveSpeed 提供（4f）
    ///     MovementConfig 不包含 speed 参数，遵循父类设计规范
    ///   - 减速速率 5f（中等惯性，停止时有轻微滑行，模拟生物运动惯性）
    ///   - 保留 Y 轴速度（keepYVelocity: true，支持跳跃/击退等垂直运动）
    ///
    ///   性能优化（由父类 ObjectBase 自动处理，子类无感知）：
    ///     - MovementConfig 在 Awake 时缓存（m_movementConfig），避免每帧 new 产生 GC
    ///     - 减速计算复用 currentSpeed（m_horizontalVelocity * (newSpeed/currentSpeed)），
    ///       避免 .normalized 的重复 Mathf.Sqrt
    ///     - 移动方向偏移旋转在 Awake 预计算（m_movementAngleRotation），
    ///       使用 m_hasMovementAngleOffset 标志进行分支无关检查
    ///
    /// ════════════════════════════════════════════════════════════
    /// 【初始化流程】
    /// ════════════════════════════════════════════════════════════
    ///   1. Awake → SetupComponents()：
    ///      - base.SetupComponents()：父类添加默认组件
    ///      - 添加 Rigidbody（mass=1, drag=0, useGravity=true, 冻结 X/Z 旋转）
    ///        FreezeRotationX|FreezeRotationZ 防止怪物被外力翻倒
    ///      - 添加 CapsuleCollider（自动计算包围盒，使用静态缓冲区零分配）
    ///        sizeMultiplier: (0.4, 1.2, 0.4) — XZ 稍大便于玩家命中
    ///        centerOffset: (-0.30, -0.45, -0.10) — 调整碰撞体中心至角色躯干
    ///   2. 父类 Awake 中预缓存 MovementConfig 和移动方向偏移旋转
    ///
    ///   注意：CapsuleCollider 的 centerOffset 是针对特定模型调整的，
    ///        实际项目中应根据怪物模型重新计算包围盒和偏移
    ///
    /// ════════════════════════════════════════════════════════════
    /// 【属性配置】
    /// ════════════════════════════════════════════════════════════
    ///   通过 ObjectStatsConfig 配置怪物属性：
    ///     基础属性：
    ///       Type = ObjectType.Tank（坦克型怪物，高生命高防御）
    ///       FactionID = 2（敌人阵营，与玩家阵营 1 敌对）
    ///       MaxHealth = CurrentHealth = 100f
    ///       PhysicalAttack = 15f（物理攻击力）
    ///       PhysicalDefense = 10f（物理防御）
    ///       TrueDamage = 3f（真实伤害，无视防御）
    ///       MagicAttack = 5f, MagicDefense = 8f
    ///     速度属性：
    ///       MoveSpeed = 4f（较慢，玩家 6f 可轻松绕背）
    ///       AttackSpeed = 1.0f, CastSpeed = 1.0f
    ///     暴击属性：
    ///       CriticalRate = 0.1f（10% 暴击率）
    ///       CriticalDamageMultiplier = 2.0f（200% 暴击伤害）
    ///     穿透属性：
    ///       ArmorPenetration = 0.1f（10% 护甲穿透）
    ///       MagicPenetration = 0.05f（5% 魔法穿透）
    ///     恢复属性：
    ///       HealthRegeneration = 1f, ManaRegeneration = 1f
    ///     特殊属性：
    ///       MaxMana = CurrentMana = 50f
    ///       CooldownReduction = 0.05f, EvasionRate = 0.03f, HitRate = 0.9f
    ///       AttackRange = 2f, VisionRange = 10f（视野范围，用于 AI 仇恨判定）
    ///       Experience = 0f, Level = 1, Gold = 0
    ///
    /// ════════════════════════════════════════════════════════════
    /// 【回调方法】
    /// ════════════════════════════════════════════════════════════
    ///   重写 ObjectBase 的所有回调方法，当前仅输出日志（包裹 #if UNITY_EDITOR）：
    ///     OnDamaged(float damage)      —— 受到伤害时触发，可在此播放受击动画/音效
    ///     OnHealed(float amount)       —— 恢复生命值时触发，可在此播放治疗特效
    ///     OnManaConsumed(float amount) —— 消耗魔法值时触发
    ///     OnManaRestored(float amount) —— 恢复魔法值时触发
    ///     OnExperienceAdded(float amount) —— 获得经验时触发（怪物一般不用，保留扩展性）
    ///     OnGoldAdded(int amount)      —— 获得金币时触发（怪物一般不用，保留扩展性）
    ///     OnDeath()                    —— 死亡时触发，调用 StopMovement() 停止移动
    ///     OnStatsReset()               —— 属性重置时触发
    ///
    ///   OnDeath 中的特殊处理：
    ///     - 调用 StopMovement() 停止移动，防止死亡后继续滑行
    ///     - 实际项目应在此播放死亡动画、掉落物品、给予玩家经验/金币
    ///
    ///   性能优化：
    ///     - 所有回调中的 Debug.Log 包裹 #if UNITY_EDITOR，生产构建中完全剥离
    ///     - MMO 场景下怪物数量多、战斗频繁，避免字符串插值（$"..."）的 GC 分配
    ///
    /// ════════════════════════════════════════════════════════════
    /// 【与 Fighter 的对比】
    /// ════════════════════════════════════════════════════════════
    ///   维度          | Fighter（玩家）         | Monster（怪物）
    ///   ──────────────┼─────────────────────────┼────────────────────────
    ///   输入控制      | autoReadInput=true      | autoReadInput=false
    ///                 | 自动读取 WASD 输入       | 由 AI 调用 SetMovementInput
    ///   移动速度      | 6f                      | 4f（较慢，玩家可绕背）
    ///   减速速率      | 50f（立即停止）         | 5f（有滑行惯性）
    ///   移动角度偏移  | -45°（斜视角补偿）      | 0°（无偏移）
    ///   攻击系统      | 3 段连击 + 帧事件        | 未启用（需 AI 扩展）
    ///   动画系统      | AnimationConfig 完整配置 | 未启用
    ///   武器系统      | Sword 子对象            | 无
    ///   属性类型      | Warrior（战士）         | Tank（坦克，高生命高防御）
    ///   阵营          | 1（玩家阵营）           | 2（敌人阵营）
    ///   生命值        | 150                     | 100
    ///   视野范围      | 12f                     | 10f
    ///   死亡处理      | （未演示）              | StopMovement()
    ///
    /// ════════════════════════════════════════════════════════════
    /// 【AI 扩展指导】
    /// ════════════════════════════════════════════════════════════
    ///   Monster 当前是基础模板，AI 逻辑需自行实现。推荐扩展方向：
    ///
    ///   1. 状态机 AI（推荐）：
    ///      - 在 Update 中实现简单 FSM：Patrol → Chase → Attack → Flee → Dead
    ///      - Patrol：随机巡逻或路径点巡逻，调用 SetMovementInput
    ///      - Chase：检测玩家进入 VisionRange，朝玩家移动
    ///      - Attack：距离 ≤ AttackRange 时调用 TryStartAttack（需配置 AnimationConfig）
    ///      - Flee：生命值低于阈值时远离玩家
    ///      - Dead：OnDeath 回调中切换状态，播放死亡动画后销毁
    ///
    ///   2. 启用攻击系统：
    ///      - 在 SetupComponents 中初始化 m_animationConfigs 数组
    ///      - 配置攻击动画的 AnimationConfig（参考 Fighter.cs）
    ///      - AI 在 Attack 状态下调用 TryStartAttack() 触发连击
    ///      - 配合帧事件系统实现伤害判定（参考 Sword.cs）
    ///
    ///   3. 仇恨系统：
    ///      - 维护 Dictionary&lt;ObjectBase, float&gt; 记忆每个目标的仇恨值
    ///      - OnDamaged 回调中增加攻击者仇恨值
    ///      - AI 选择仇恨最高的目标作为 Chase/Attack 对象
    ///
    ///   4. 出生/掉落：
    ///      - OnDeath 中通过 Addressables 加载掉落物预制体
    ///      - 调用 ExperienceAdded/OnGoldAdded 给予玩家奖励（需引用玩家 ObjectBase）
    ///
    ///   5. 配置驱动：
    ///      - 将硬编码属性提取为 ScriptableObject（如 MonsterDataSO）
    ///      - 不同怪物类型（哥布林、巨魔、龙）共享同一 Monster 类，仅配置不同
    ///
    /// ════════════════════════════════════════════════════════════
    /// 【MMO 场景下的性能注意事项】
    /// ════════════════════════════════════════════════════════════
    ///   - 怪物数量大（数百到数千），每帧 Update 必须保持轻量
    ///   - 推荐使用对象池管理怪物 GameObject，避免运行时 Instantiate/Destroy
    ///   - 远离玩家的怪物可降低 Update 频率（LOD AI）或休眠
    ///   - OnDamaged 中的日志必须包裹 #if UNITY_EDITOR，避免 GC 压力
    ///   - 仇恨字典应预分配容量，避免运行时扩容
    ///   - 视野检测使用 sqrMagnitude 而非 Vector3.Distance
    /// </summary>
}