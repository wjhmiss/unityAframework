using System;
using System.Collections.Generic;
using UnityEngine;

namespace AFrameWork.Core
{
    /// <summary>
    /// 物体类型枚举（原角色职业类型）
    /// </summary>
    public enum ObjectType
    {
        None = 0,
        Warrior = 1,      // 战士
        Mage = 2,         // 法师
        Assassin = 3,     // 刺客
        Tank = 4,         // 坦克
        Marksman = 5,     // 射手
        Support = 6,      // 辅助
        Projectile = 7,   // 投射物（如火球）
        Trap = 8,         // 陷阱
        Building = 9,     // 建筑
        Neutral = 10,      // 中立物体
        Weapon = 11,     // 武器
    }

    /// <summary>
    /// 阵营关系类型（定义阵营之间的关系）
    /// </summary>
    public enum FactionRelationType
    {
        Friendly = 0,    // 友好：可以组队、治疗，不互相伤害
        Neutral = 1,     // 中立：不主动攻击，但可以互相伤害（PVP）
        Hostile = 2,     // 敌对：主动攻击，仇恨系统
        Alliance = 3     // 同盟：组队后的额外友好关系
    }

    /// <summary>
    /// PVP模式（玩家对玩家战斗模式）
    /// </summary>
    public enum PVPMode
    {
        None = 0,        // PVP关闭：不能攻击友好阵营的玩家
        Open = 1,        // 开放PVP：可以攻击任何玩家（自由PVP）
        Arena = 2,       // 竞技场模式：特定区域允许PVP
        Duel = 3         // 决斗模式：双方同意后可以战斗
    }

    /// <summary>
    /// 伤害类型枚举
    /// </summary>
    public enum DamageType
    {
        Physical = 0,   // 物理伤害
        Magic = 1,      // 魔法伤害
        True = 2        // 真实伤害
    }

    /// <summary>
    /// 攻击参数倍率结构体，用于按动画差异化调整 ObjectStatsConfig 属性值。
    /// 每个字段对应 ObjectStatsConfig 中的一个属性，值为该属性的倍率。
    /// 默认值 k_useBase(-1) 表示使用基础 ObjectStatsConfig 值（即倍率 1.0）。
    /// 非 -1 值表示：实际属性 = 基础属性 × 倍率。
    /// 示例：CriticalRateMultiplier = 0.5，基础 CriticalRate = 0.1 → 实际暴击率 = 0.05
    /// </summary>
    [Serializable]
    public struct ObjectStatsConfigMultiplier
    {
        /// <summary>默认标记值，表示使用基础 ObjectStatsConfig 值（倍率 1.0）</summary>
        public const float k_useBase = -1f;

        #region 攻击属性倍率

        /// <summary>物理攻击倍率（-1=使用基础值）</summary>
        public float PhysicalAttackMultiplier;

        /// <summary>魔法攻击倍率（-1=使用基础值）</summary>
        public float MagicAttackMultiplier;

        /// <summary>真实伤害倍率（-1=使用基础值）</summary>
        public float TrueDamageMultiplier;

        #endregion

        #region 暴击属性倍率

        /// <summary>暴击率倍率（-1=使用基础值）</summary>
        public float CriticalRateMultiplier;

        /// <summary>暴击伤害倍率的倍率（-1=使用基础值）</summary>
        public float CriticalDamageMultiplier;

        #endregion

        #region 穿透属性倍率

        /// <summary>护甲穿透倍率（-1=使用基础值）</summary>
        public float ArmorPenetrationMultiplier;

        /// <summary>魔法穿透倍率（-1=使用基础值）</summary>
        public float MagicPenetrationMultiplier;

        #endregion

        #region 命中属性倍率

        /// <summary>命中率倍率（-1=使用基础值）</summary>
        public float HitRateMultiplier;

        /// <summary>攻击范围倍率（-1=使用基础值）</summary>
        public float AttackRangeMultiplier;

        #endregion

        /// <summary>
        /// 构造函数，所有参数默认 k_useBase（使用基础值）
        /// </summary>
        public ObjectStatsConfigMultiplier(
            float physicalAttackMultiplier = k_useBase,
            float magicAttackMultiplier = k_useBase,
            float trueDamageMultiplier = k_useBase,
            float criticalRateMultiplier = k_useBase,
            float criticalDamageMultiplier = k_useBase,
            float armorPenetrationMultiplier = k_useBase,
            float magicPenetrationMultiplier = k_useBase,
            float hitRateMultiplier = k_useBase,
            float attackRangeMultiplier = k_useBase)
        {
            PhysicalAttackMultiplier = physicalAttackMultiplier;
            MagicAttackMultiplier = magicAttackMultiplier;
            TrueDamageMultiplier = trueDamageMultiplier;
            CriticalRateMultiplier = criticalRateMultiplier;
            CriticalDamageMultiplier = criticalDamageMultiplier;
            ArmorPenetrationMultiplier = armorPenetrationMultiplier;
            MagicPenetrationMultiplier = magicPenetrationMultiplier;
            HitRateMultiplier = hitRateMultiplier;
            AttackRangeMultiplier = attackRangeMultiplier;
        }

        /// <summary>
        /// 应用倍率：multiplier 为 k_useBase 时返回 baseValue，否则返回 baseValue × multiplier
        /// </summary>
        public static float Apply(float baseValue, float multiplier)
        {
            return multiplier <= 0f ? baseValue : baseValue * multiplier;
        }
    }

    /// <summary>
    /// 物体状态配置类，定义所有物体的基础属性值
    /// 包含生命值、攻击力、防御力、速度、阵营、伤害配置等核心属性
    /// 适用于角色、投射物、陷阱、建筑等所有游戏物体
    /// </summary>
    [Serializable]
    public class ObjectStatsConfig
    {
        #region 阵营ID范围常量

        // 玩家阵营ID范围（1-10）
        private const int k_playerFactionMinID = 1;
        private const int k_playerFactionMaxID = 10;

        // 怪物阵营ID范围（11-50）
        private const int k_monsterFactionMinID = 11;
        private const int k_monsterFactionMaxID = 50;

        // NPC阵营ID范围（51-99）
        private const int k_npcFactionMinID = 51;
        private const int k_npcFactionMaxID = 99;

        #endregion

        #region 基础属性

        // 物体类型
        [SerializeField]
        [Tooltip("物体类型（角色、投射物、陷阱等）")]
        public ObjectType Type = ObjectType.None;

        // 阵营ID（使用唯一ID标识阵营，而非固定枚举）
        [SerializeField]
        [Tooltip("阵营ID（1=玩家阵营A，2=玩家阵营B，3=怪物阵营，100=中立等）")]
        public int FactionID = 0;

        // 队伍ID（同一队伍的成员互为友方，优先级最高）
        [SerializeField]
        [Tooltip("队伍ID（-1表示无队伍，同一队伍成员互为友方）")]
        public int TeamID = -1;

        // 公会ID（同一公会的成员互为友方，优先级次高）
        [SerializeField]
        [Tooltip("公会ID（-1表示无公会，同一公会成员互为友方）")]
        public int GuildID = -1;

        // 同盟ID（多个公会组成的同盟，优先级第三）
        [SerializeField]
        [Tooltip("同盟ID（-1表示无同盟，同一同盟成员互为友方）")]
        public int AllianceID = -1;

        // PVP模式（控制玩家之间的战斗规则）
        [SerializeField]
        [Tooltip("PVP模式（None=关闭，Open=自由PVP，Arena=竞技场，Duel=决斗）")]
        public PVPMode CurrentPVPMode = PVPMode.None;

        // 最大生命值
        [SerializeField]
        [Tooltip("最大生命值")]
        public float MaxHealth = 0f;

        // 当前生命值
        [SerializeField]
        [Tooltip("当前生命值")]
        public float CurrentHealth = 0f;

        // 物理攻击力
        [SerializeField]
        [Tooltip("物理攻击力")]
        public float PhysicalAttack = 0f;

        // 物理防御力
        [SerializeField]
        [Tooltip("物理防御力")]
        public float PhysicalDefense = 0f;

        // 真实伤害
        [SerializeField]
        [Tooltip("真实伤害（无视防御）")]
        public float TrueDamage = 0f;

        // 魔法攻击力
        [SerializeField]
        [Tooltip("魔法攻击力")]
        public float MagicAttack = 0f;

        // 魔法防御力
        [SerializeField]
        [Tooltip("魔法防御力")]
        public float MagicDefense = 0f;

        #endregion

        #region 速度属性

        // 移动速度
        [SerializeField]
        [Tooltip("移动速度，单位：米/秒")]
        public float MoveSpeed = 0f;

        // 攻击速度
        [SerializeField]
        [Tooltip("攻击速度，每秒攻击次数")]
        public float AttackSpeed = 0f;

        // 施法速度
        [SerializeField]
        [Tooltip("施法速度，每秒施法次数（持续伤害间隔 = 1/CastSpeed）")]
        public float CastSpeed = 0f;

        #endregion

        #region 暴击属性

        // 暴击率
        [SerializeField]
        [Tooltip("暴击率，范围：0-1")]
        [Range(0f, 1f)]
        public float CriticalRate = 0f;

        // 暴击伤害倍率
        [SerializeField]
        [Tooltip("暴击伤害倍率（例如：2.0 表示暴击伤害为200%）")]
        public float CriticalDamageMultiplier = 0f;

        #endregion

        #region 穿透属性

        // 护甲穿透
        [SerializeField]
        [Tooltip("护甲穿透百分比，范围：0-1")]
        [Range(0f, 1f)]
        public float ArmorPenetration = 0f;

        // 魔法穿透
        [SerializeField]
        [Tooltip("魔法穿透百分比，范围：0-1")]
        [Range(0f, 1f)]
        public float MagicPenetration = 0f;

        #endregion

        #region 恢复属性

        // 生命恢复速度
        [SerializeField]
        [Tooltip("生命恢复速度，每秒恢复的生命值")]
        public float HealthRegeneration = 0f;

        // 魔法恢复速度
        [SerializeField]
        [Tooltip("魔法恢复速度，每秒恢复的魔法值")]
        public float ManaRegeneration = 0f;

        #endregion

        #region 特殊属性

        // 最大魔法值
        [SerializeField]
        [Tooltip("最大魔法值")]
        public float MaxMana = 0f;

        // 当前魔法值
        [SerializeField]
        [Tooltip("当前魔法值")]
        public float CurrentMana = 0f;

        // 冷却缩减
        [SerializeField]
        [Tooltip("冷却缩减百分比，范围：0-1")]
        [Range(0f, 1f)]
        public float CooldownReduction = 0f;

        // 闪避率
        [SerializeField]
        [Tooltip("闪避率，范围：0-1")]
        [Range(0f, 1f)]
        public float EvasionRate = 0f;

        // 命中率
        [SerializeField]
        [Tooltip("命中率，范围：0-1")]
        [Range(0f, 1f)]
        public float HitRate = 0.95f;

        // 攻击范围
        [SerializeField]
        [Tooltip("攻击范围，单位：米")]
        public float AttackRange = 0f;

        // 视野范围
        [SerializeField]
        [Tooltip("视野范围，单位：米")]
        public float VisionRange = 0f;

        // 经验值
        [SerializeField]
        [Tooltip("当前经验值")]
        public float Experience = 0f;

        // 等级
        [SerializeField]
        [Tooltip("当前等级")]
        public int Level = 0;

        // 金币
        [SerializeField]
        [Tooltip("当前金币")]
        public int Gold = 0;

        #endregion

        #region 伤害配置属性（从 DamageConfig 合并）

        // 伤害范围
        [SerializeField]
        [Tooltip("范围伤害半径，单位：米")]
        public float DamageRadius = 0f;

        // 是否持续伤害
        [SerializeField]
        [Tooltip("是否持续伤害（ OnTriggerStay 中持续造成伤害）")]
        public bool IsContinuousDamage = false;

        // 伤害持续时间（秒）
        [SerializeField]
        [Tooltip("伤害持续时间，单位：秒（持续伤害的总时长，-1或0 表示永久存活（无时间限制）））")]
        public float DamageDuration = 0f;

        // 是否可以造成伤害
        [SerializeField]
        [Tooltip("是否可以造成伤害（用于控制物体是否启用伤害功能）")]
        public bool CanDealDamage = false;

        // 减速倍率（0.3 = 降至原速度30%，0 = 无减速效果）
        [SerializeField]
        [Tooltip("减速倍率（0.3=降至原速度30%，0=无减速效果）")]
        public float SlowFactor = 0f;

        #endregion

        /// <summary>
        /// 默认配置构造函数（创建空配置，由 CopyTo/Clone 填充）
        /// </summary>
        public ObjectStatsConfig() { }

        /// <summary>
        /// 将当前实例的全部字段复制到目标实例（用于创建运行时快照，避免修改原始配置）。
        /// 字段列表与 ObjectBase.CopyObjectStatsConfig 保持一致，集中维护避免重复。
        /// </summary>
        /// <param name="target">被填充的目标实例（通常为 new ObjectStatsConfig()）</param>
        public void CopyTo(ObjectStatsConfig target)
        {
            if (target == null) return;

            target.Type = Type;
            target.FactionID = FactionID;
            target.TeamID = TeamID;
            target.GuildID = GuildID;
            target.AllianceID = AllianceID;
            target.CurrentPVPMode = CurrentPVPMode;
            target.MaxHealth = MaxHealth;
            target.CurrentHealth = CurrentHealth;
            target.PhysicalAttack = PhysicalAttack;
            target.PhysicalDefense = PhysicalDefense;
            target.TrueDamage = TrueDamage;
            target.MagicAttack = MagicAttack;
            target.MagicDefense = MagicDefense;
            target.MoveSpeed = MoveSpeed;
            target.AttackSpeed = AttackSpeed;
            target.CastSpeed = CastSpeed;
            target.CriticalRate = CriticalRate;
            target.CriticalDamageMultiplier = CriticalDamageMultiplier;
            target.ArmorPenetration = ArmorPenetration;
            target.MagicPenetration = MagicPenetration;
            target.HealthRegeneration = HealthRegeneration;
            target.ManaRegeneration = ManaRegeneration;
            target.MaxMana = MaxMana;
            target.CurrentMana = CurrentMana;
            target.CooldownReduction = CooldownReduction;
            target.EvasionRate = EvasionRate;
            target.HitRate = HitRate;
            target.AttackRange = AttackRange;
            target.VisionRange = VisionRange;
            target.Experience = Experience;
            target.Level = Level;
            target.Gold = Gold;
            target.DamageRadius = DamageRadius;
            target.IsContinuousDamage = IsContinuousDamage;
            target.DamageDuration = DamageDuration;
            target.CanDealDamage = CanDealDamage;
            target.SlowFactor = SlowFactor;
        }

        /// <summary>
        /// 克隆当前实例（深拷贝字段值到新实例）。
        /// 用于 SimpleObjectBase 等场景在发射时锁定 owner 属性快照，
        /// 避免 owner 后续 buff/销毁影响已发射投射物。
        /// </summary>
        /// <returns>字段值完全相同的新实例</returns>
        public ObjectStatsConfig Clone()
        {
            ObjectStatsConfig copy = new ObjectStatsConfig();
            CopyTo(copy);
            return copy;
        }

        #region 静态工厂方法（统一管理所有子类属性配置）

        /// <summary>
        /// 战士（Fighter）属性配置
        /// </summary>
        public static ObjectStatsConfig CreateFighter()
        {
            return new ObjectStatsConfig
            {
                // 身份/阵营
                Type = ObjectType.Warrior,          // 战士类型
                FactionID = 1,                      // 玩家阵营ID

                // 生命值
                MaxHealth = 100f,                   // 最大生命值
                CurrentHealth = 100f,               // 当前生命值

                // 攻击属性
                PhysicalAttack = 1.2f,              // 物理攻击力
                PhysicalDefense = 15f,              // 物理防御力
                TrueDamage = 0.1f,                  // 真实伤害（无视防御）
                MagicAttack = 1.1f,                 // 魔法攻击力
                MagicDefense = 12f,                 // 魔法防御力

                // 速度属性
                MoveSpeed = 6f,                     // 移动速度（米/秒）
                AttackSpeed = 1.5f,                 // 攻击速度（次/秒）
                CastSpeed = 1.2f,                   // 施法速度（次/秒）

                // 暴击属性
                CriticalRate = 0.2f,                // 暴击率（20%）
                CriticalDamageMultiplier = 2.5f,    // 暴击伤害倍率（2.5倍）

                // 穿透属性
                ArmorPenetration = 0.15f,           // 护甲穿透率（15%）
                MagicPenetration = 0.1f,            // 魔法穿透率（10%）

                // 恢复属性
                HealthRegeneration = 2f,            // 生命恢复（点/秒）
                ManaRegeneration = 3f,              // 魔法恢复（点/秒）

                // 魔法值
                MaxMana = 80f,                      // 最大魔法值
                CurrentMana = 80f,                  // 当前魔法值

                // 特殊属性
                CooldownReduction = 0.1f,           // 冷却缩减（10%）
                EvasionRate = 0.05f,                // 闪避率（5%）
                HitRate = 0.95f,                    // 命中率（95%）
                AttackRange = 3f,                   // 攻击范围（米）
                VisionRange = 12f,                  // 视野范围（米）

                // 成长属性
                Experience = 0f,                    // 经验值
                Level = 1,                          // 等级
                Gold = 0                            // 金币
            };
        }

        /// <summary>
        /// 剑（Sword）属性配置
        /// </summary>
        public static ObjectStatsConfig CreateSword()
        {
            return new ObjectStatsConfig
            {
                // 身份/阵营
                Type = ObjectType.Weapon,           // 武器类型
                FactionID = 0,                      // 阵营ID（由持有者继承，0=默认）

                // 生命值
                MaxHealth = 100f,                   // 最大生命值（武器不消耗生命值，仅占位）
                CurrentHealth = 100f,               // 当前生命值

                // 攻击属性
                PhysicalAttack = 5f,                // 物理攻击力（剑的基础伤害）
                AttackRange = 1.5f,                 // 攻击范围（米）

                // 伤害开关
                CanDealDamage = true                // 启用伤害判定
            };
        }

        /// <summary>
        /// 火球（Fire）属性配置
        /// </summary>
        public static ObjectStatsConfig CreateFire()
        {
            return new ObjectStatsConfig
            {
                // 身份/阵营
                Type = ObjectType.Trap,             // 陷阱类型
                FactionID = 100,                    // 中立阵营（可伤害所有非中立阵营）

                // 生命值
                MaxHealth = 1f,                     // 最大生命值（火球一击即毁）
                CurrentHealth = 1f,                 // 当前生命值

                // 攻击属性
                MagicAttack = 0.25f,                  // 魔法攻击力（火球主要伤害来源）
                MagicPenetration = 0.2f,            // 魔法穿透率（20%，无视部分魔防）

                // 速度属性
                CastSpeed = 0.5f,                     // 施法频率（2次/秒，即每0.5秒一次持续伤害）

                // 伤害配置
                DamageRadius = 5f,                  // 伤害范围（5米，同时作为触发器半径）
                IsContinuousDamage = true,          // 启用持续伤害（OnTriggerStay按间隔触发）
                DamageDuration = 0f,               // 持续时间（10秒后自动销毁）
                CanDealDamage = true,                // 启用伤害判定    

                HitRate = 0.95f,                    // 命中率（95%）
            };
        }

        /// <summary>
        /// 怪物（Monster）属性配置
        /// </summary>
        public static ObjectStatsConfig CreateMonster()
        {
            return new ObjectStatsConfig
            {
                // 身份/阵营
                Type = ObjectType.Tank,             // 坦克类型
                FactionID = 11,                     // 怪物阵营ID

                // 生命值
                MaxHealth = 100f,                   // 最大生命值
                CurrentHealth = 100f,               // 当前生命值

                // 攻击属性
                PhysicalAttack = 15f,               // 物理攻击力
                PhysicalDefense = 10f,              // 物理防御力
                TrueDamage = 3f,                    // 真实伤害（无视防御）
                MagicAttack = 5f,                   // 魔法攻击力
                MagicDefense = 8f,                  // 魔法防御力

                // 速度属性
                MoveSpeed = 4f,                     // 移动速度（米/秒）
                AttackSpeed = 1.0f,                 // 攻击速度（次/秒）
                CastSpeed = 1.0f,                   // 施法速度（次/秒）

                // 暴击属性
                CriticalRate = 0.1f,                // 暴击率（10%）
                CriticalDamageMultiplier = 2.0f,    // 暴击伤害倍率（2倍）

                // 穿透属性
                ArmorPenetration = 0.1f,            // 护甲穿透率（10%）
                MagicPenetration = 0.05f,           // 魔法穿透率（5%）

                // 恢复属性
                HealthRegeneration = 1f,            // 生命恢复（点/秒）
                ManaRegeneration = 1f,              // 魔法恢复（点/秒）

                // 魔法值
                MaxMana = 50f,                      // 最大魔法值
                CurrentMana = 50f,                  // 当前魔法值

                // 特殊属性
                CooldownReduction = 0.05f,          // 冷却缩减（5%）
                EvasionRate = 0.03f,                // 闪避率（3%）
                HitRate = 0.9f,                     // 命中率（90%）
                AttackRange = 2f,                   // 攻击范围（米）
                VisionRange = 10f,                  // 视野范围（米）

                // 成长属性
                Level = 1                           // 等级
            };
        }

        /// <summary>
        /// 冰雹（HailStorm）属性配置
        /// </summary>
        public static ObjectStatsConfig CreateHailStorm()
        {
            return new ObjectStatsConfig
            {
                // 身份/阵营
                Type = ObjectType.Trap,             // 陷阱类型
                FactionID = 100,                    // 中立阵营（可伤害所有非中立阵营）

                // 生命值
                MaxHealth = 1f,                     // 最大生命值（冰雹一击即毁）
                CurrentHealth = 1f,                 // 当前生命值

                // 攻击属性
                PhysicalAttack = 0.1f,               // 物理攻击力（冰雹主要伤害来源：物理打击）
                MagicAttack = 0.11f,                   // 魔法攻击力（附加冰霜魔法伤害）
                ArmorPenetration = 0.15f,           // 护甲穿透率（15%，冰雹冲击力部分无视护甲）

                // 速度属性
                CastSpeed = 0.5f,                   // 施法频率（1.5次/秒，即每0.67秒一次持续伤害）

                // 伤害配置
                DamageRadius = 6f,                  // 伤害范围（6米，冰雹覆盖范围比火焰略大）
                IsContinuousDamage = true,           // 启用持续伤害（OnTriggerStay按间隔触发）
                DamageDuration = -1f,                // 持续时间（8秒后自动销毁）
                CanDealDamage = true,               // 启用伤害判定

                // 减速配置
                SlowFactor = 0.4f                   // 减速倍率（降至原速度40%，即减60%移速）
            };
        }

        #endregion

        /// <summary>
        /// 自定义配置构造函数（基础属性）
        /// </summary>
        public ObjectStatsConfig(ObjectType type, float maxHealth, float physicalAttack, float physicalDefense,
            float magicAttack, float magicDefense, float moveSpeed)
        {
            Type = type;    
            MaxHealth = maxHealth;
            CurrentHealth = maxHealth;
            PhysicalAttack = physicalAttack;
            PhysicalDefense = physicalDefense;
            MagicAttack = magicAttack;
            MagicDefense = magicDefense;
            MoveSpeed = moveSpeed;
        }

        #region 伤害计算方法

        /// <summary>
        /// 记录最后一次 CalculateAttack 调用的完整信息，供 UI 面板展示真实数据。
        /// 包含所有攻击方/目标快照（克隆，避免活引用变化）及每一步中间计算结果。
        /// </summary>
        public struct AttackRecord
        {
            public bool IsValid;
            public float Timestamp;

            // 输入快照（克隆，避免活引用变化影响 UI 展示）
            /// <summary>所有攻击方的属性快照（克隆数组，AttackRecord 自包含）</summary>
            public ObjectStatsConfig[] AttackerSnapshots;
            /// <summary>目标属性快照（克隆）</summary>
            public ObjectStatsConfig TargetSnapshot;
            /// <summary>攻击参数倍率</summary>
            public ObjectStatsConfigMultiplier Multiplier;

            // 活引用（供 UI 实时读取生命值/魔法值，可能为 null 或被销毁）
            // 与 AttackerSnapshots/TargetSnapshot 分离：快照用于公式展示（静态），
            // 活引用用于实时数据展示（动态）。某些攻击方可能无 ObjectBase（如子弹共享属性），对应元素为 null。
            /// <summary>目标的 ObjectBase 活引用（供 UI 实时读取生命值/魔法值，可能为 null 或已销毁）</summary>
            public ObjectBase TargetRef;
            /// <summary>攻击方的 ObjectBase 活引用数组（与 AttackerSnapshots 对应，某些元素可能为 null）</summary>
            public ObjectBase[] AttackerRefs;

            // 步骤 1：累加所有攻击方属性
            public float SumPhysicalAttack;
            public float SumMagicAttack;
            public float SumTrueDamage;
            public float SumArmorPenetration;
            public float SumMagicPenetration;
            public float SumCriticalRate;
            public float SumCriticalDamageMultiplier;
            public float SumHitRate;

            // 步骤 2：应用倍率后的有效值
            public float EffectivePhysAtk;
            public float EffectiveMagicAtk;
            public float EffectiveTrueDmg;
            public float EffectiveArmorPen;
            public float EffectiveMagicPen;
            public float EffectiveCritRate;
            public float EffectiveCritDmg;

            // 步骤 3：闪避判定
            public bool IsEvaded;
            public float EffectiveEvasion;

            // 步骤 4：防御减免
            public float EffectivePhysDef;
            public float EffectiveMagicDef;
            public float PhysicalDamage;
            public float MagicDamage;
            public float TrueDamageApplied;
            public float BaseDamage;

            // 步骤 5：暴击判定
            public bool IsCritical;
            public float FinalDamage;
        }

        /// <summary>
        /// 最后一次 CalculateAttack 调用的完整记录，供 UI 面板展示真实数据。
        /// 每次成功调用 CalculateAttack（含被闪避）都会更新此字段。
        /// </summary>
        public static AttackRecord LastAttackRecord { get; private set; }

        /// <summary>
        /// 计算攻击伤害（不应用）：累加所有攻击方属性 → 闪避判定 → 伤害计算（防御减免+穿透）→ 暴击判定。
        ///
        /// 此方法是唯一的伤害计算入口，不调用 target.TakeDamage —— 由调用方决定如何应用：
        ///   float damage = CalculateAttack(multiplier, targetStats, attackerStats);
        ///   target.TakeDamage(damage);  // 经 ObjectBase.TakeDamage 保留无敌检查/OnDamaged/OnDeath
        ///
        /// 这样分离计算与应用，避免 AttackTarget 那种"计算+扣血一体"导致的重复扣血风险。
        /// </summary>
        /// <param name="multiplier">攻击参数倍率（无倍率传 new ObjectStatsConfigMultiplier()）</param>
        /// <param name="target">目标属性配置（仅读取防御/闪避，不修改）</param>
        /// <param name="attackers">攻击方属性（可变参数，1 个或多个，会被累加。如武器+持有者）</param>
        /// <returns>计算出的伤害值（被闪避返回 0）</returns>
        public static float CalculateAttack(
            ObjectStatsConfigMultiplier multiplier,
            ObjectStatsConfig target,
            params ObjectStatsConfig[] attackers)
        {
            if (target == null || attackers == null || attackers.Length == 0)
            {
                return 0f;
            }

            // 1. 累加所有攻击方的战斗属性
            float totalPhysicalAttack = 0f;
            float totalMagicAttack = 0f;
            float totalTrueDamage = 0f;
            float totalArmorPenetration = 0f;
            float totalMagicPenetration = 0f;
            float totalCriticalRate = 0f;
            float totalCriticalDamageMultiplier = 0f;
            float totalHitRate = 0f;

            int count = attackers.Length;
            for (int i = 0; i < count; i++)
            {
                ObjectStatsConfig attacker = attackers[i];
                if (attacker == null)
                {
                    continue;
                }

                totalPhysicalAttack += attacker.PhysicalAttack;
                totalMagicAttack += attacker.MagicAttack;
                totalTrueDamage += attacker.TrueDamage;
                totalArmorPenetration += attacker.ArmorPenetration;
                totalMagicPenetration += attacker.MagicPenetration;
                totalCriticalRate += attacker.CriticalRate;
                totalCriticalDamageMultiplier += attacker.CriticalDamageMultiplier;
                totalHitRate += attacker.HitRate;
            }

            // 2. 应用攻击参数倍率
            float effectivePhysAtk = ObjectStatsConfigMultiplier.Apply(totalPhysicalAttack, multiplier.PhysicalAttackMultiplier);
            float effectiveMagicAtk = ObjectStatsConfigMultiplier.Apply(totalMagicAttack, multiplier.MagicAttackMultiplier);
            float effectiveTrueDmg = ObjectStatsConfigMultiplier.Apply(totalTrueDamage, multiplier.TrueDamageMultiplier);
            float effectiveArmorPen = ObjectStatsConfigMultiplier.Apply(totalArmorPenetration, multiplier.ArmorPenetrationMultiplier);
            float effectiveMagicPen = ObjectStatsConfigMultiplier.Apply(totalMagicPenetration, multiplier.MagicPenetrationMultiplier);
            float effectiveCritRate = ObjectStatsConfigMultiplier.Apply(totalCriticalRate, multiplier.CriticalRateMultiplier);
            float effectiveCritDmg = ObjectStatsConfigMultiplier.Apply(totalCriticalDamageMultiplier, multiplier.CriticalDamageMultiplier);

            // 3. 闪避判定（累加命中率 vs 目标闪避率）
            // effectiveEvasion = EvasionRate - (HitRate - 1)，与 IsEvaded 内部公式一致
            float effectiveEvasion = target.EvasionRate - (totalHitRate - 1f);
            bool isEvaded = target.IsEvaded(totalHitRate);

            if (isEvaded)
            {
                // 记录被闪避的攻击（供 UI 面板展示真实数据）
                LastAttackRecord = new AttackRecord
                {
                    IsValid = true,
                    Timestamp = Time.time,
                    AttackerSnapshots = CloneAttackers(attackers),
                    TargetSnapshot = target.Clone(),
                    Multiplier = multiplier,
                    SumPhysicalAttack = totalPhysicalAttack,
                    SumMagicAttack = totalMagicAttack,
                    SumTrueDamage = totalTrueDamage,
                    SumArmorPenetration = totalArmorPenetration,
                    SumMagicPenetration = totalMagicPenetration,
                    SumCriticalRate = totalCriticalRate,
                    SumCriticalDamageMultiplier = totalCriticalDamageMultiplier,
                    SumHitRate = totalHitRate,
                    EffectivePhysAtk = effectivePhysAtk,
                    EffectiveMagicAtk = effectiveMagicAtk,
                    EffectiveTrueDmg = effectiveTrueDmg,
                    EffectiveArmorPen = effectiveArmorPen,
                    EffectiveMagicPen = effectiveMagicPen,
                    EffectiveCritRate = effectiveCritRate,
                    EffectiveCritDmg = effectiveCritDmg,
                    IsEvaded = true,
                    EffectiveEvasion = effectiveEvasion,
                    FinalDamage = 0f,
                };
                return 0f;
            }

            // 4. 计算伤害（累加所有非零攻击类型，含防御减免）
            float damage = 0f;
            float effectivePhysDef = 0f;
            float effectiveMagicDef = 0f;
            float physicalDamage = 0f;
            float magicDamage = 0f;

            if (effectivePhysAtk > 0f)
            {
                effectivePhysDef = target.PhysicalDefense * (1f - effectiveArmorPen);
                physicalDamage = effectivePhysAtk * (100f / (100f + effectivePhysDef));
                damage += physicalDamage;
            }

            if (effectiveMagicAtk > 0f)
            {
                effectiveMagicDef = target.MagicDefense * (1f - effectiveMagicPen);
                magicDamage = effectiveMagicAtk * (100f / (100f + effectiveMagicDef));
                damage += magicDamage;
            }

            damage += effectiveTrueDmg;
            float baseDamage = damage;

            // 5. 暴击判定
            bool isCritical = UnityEngine.Random.value < effectiveCritRate;
            if (isCritical)
            {
                damage *= effectiveCritDmg;
            }

            // 记录最后一次攻击的完整信息（供 UI 面板展示真实数据）
            LastAttackRecord = new AttackRecord
            {
                IsValid = true,
                Timestamp = Time.time,
                AttackerSnapshots = CloneAttackers(attackers),
                TargetSnapshot = target.Clone(),
                Multiplier = multiplier,
                SumPhysicalAttack = totalPhysicalAttack,
                SumMagicAttack = totalMagicAttack,
                SumTrueDamage = totalTrueDamage,
                SumArmorPenetration = totalArmorPenetration,
                SumMagicPenetration = totalMagicPenetration,
                SumCriticalRate = totalCriticalRate,
                SumCriticalDamageMultiplier = totalCriticalDamageMultiplier,
                SumHitRate = totalHitRate,
                EffectivePhysAtk = effectivePhysAtk,
                EffectiveMagicAtk = effectiveMagicAtk,
                EffectiveTrueDmg = effectiveTrueDmg,
                EffectiveArmorPen = effectiveArmorPen,
                EffectiveMagicPen = effectiveMagicPen,
                EffectiveCritRate = effectiveCritRate,
                EffectiveCritDmg = effectiveCritDmg,
                IsEvaded = false,
                EffectiveEvasion = effectiveEvasion,
                EffectivePhysDef = effectivePhysDef,
                EffectiveMagicDef = effectiveMagicDef,
                PhysicalDamage = physicalDamage,
                MagicDamage = magicDamage,
                TrueDamageApplied = effectiveTrueDmg,
                BaseDamage = baseDamage,
                IsCritical = isCritical,
                FinalDamage = damage,
            };

            return damage;
        }

        /// <summary>
        /// 克隆攻击方数组到新数组（attackers 可能是复用缓冲如 s_twoAttackerBuffer，必须克隆）。
        /// 供 AttackRecord 记录使用，确保快照自包含且不受后续属性变化影响。
        /// </summary>
        private static ObjectStatsConfig[] CloneAttackers(ObjectStatsConfig[] attackers)
        {
            int count = attackers.Length;
            ObjectStatsConfig[] snapshots = new ObjectStatsConfig[count];
            for (int i = 0; i < count; i++)
            {
                ObjectStatsConfig a = attackers[i];
                if (a != null)
                {
                    snapshots[i] = a.Clone();
                }
            }
            return snapshots;
        }

        /// <summary>
        /// 为最近一次 CalculateAttack 记录补充 ObjectBase 活引用，供 UI 实时读取生命值/魔法值。
        /// 必须在 CalculateAttack 成功调用后、TakeDamage 之前/之后均可调用。
        /// 若 AttackRecord 无效（CalculateAttack 未成功）则忽略。
        ///
        /// 调用约定：
        ///   attackerRefs 的顺序和数量应与传入 CalculateAttack 的 attackers 数组一致；
        ///   某些攻击方无 ObjectBase（如子弹共享属性），对应位置传 null。
        /// </summary>
        /// <param name="target">目标的 ObjectBase 活引用（可能为 null）</param>
        /// <param name="attackerRefs">攻击方的 ObjectBase 活引用数组（与 attackers 对应，某些元素可为 null）</param>
        public static void SetLastAttackRefs(ObjectBase target, params ObjectBase[] attackerRefs)
        {
            if (!LastAttackRecord.IsValid) return;

            AttackRecord record = LastAttackRecord;
            record.TargetRef = target;
            record.AttackerRefs = attackerRefs;
            LastAttackRecord = record;
        }

        #endregion

        #region 暴击和闪避方法

        /// <summary>
        /// 检查是否暴击
        /// </summary>
        /// <returns>是否触发暴击</returns>
        public bool IsCriticalHit()
        {
            return UnityEngine.Random.value < CriticalRate;
        }

        /// <summary>
        /// 获取暴击后的伤害值
        /// </summary>
        /// <param name="baseDamage">基础伤害</param>
        /// <returns>暴击伤害</returns>
        public float GetCriticalDamage(float baseDamage)
        {
            return baseDamage * CriticalDamageMultiplier;
        }

        /// <summary>
        /// 检查是否闪避
        /// </summary>
        /// <param name="attackerHitRate">攻击者命中率</param>
        /// <returns>是否闪避成功</returns>
        public bool IsEvaded(float attackerHitRate)
        {
            // 闪避成功率 = 闪避率 - (攻击者命中率 - 1)
            float effectiveEvasion = EvasionRate - (attackerHitRate - 1f);
            return UnityEngine.Random.value < Mathf.Max(0f, effectiveEvasion);
        }

        #endregion

        #region 生命值和魔法值管理方法

        /// <summary>
        /// 受到伤害
        /// </summary>
        /// <param name="damage">伤害值</param>
        public void TakeDamage(float damage)
        {
            CurrentHealth = Mathf.Max(0f, CurrentHealth - damage);
        }

        /// <summary>
        /// 恢复生命值
        /// </summary>
        /// <param name="amount">恢复量</param>
        public void Heal(float amount)
        {
            CurrentHealth = Mathf.Min(MaxHealth, CurrentHealth + amount);
        }

        /// <summary>
        /// 消耗魔法值
        /// </summary>
        /// <param name="amount">消耗量</param>
        /// <returns>是否成功消耗</returns>
        public bool ConsumeMana(float amount)
        {
            if (CurrentMana < amount)
            {
                return false;
            }

            CurrentMana -= amount;
            return true;
        }

        /// <summary>
        /// 恢复魔法值
        /// </summary>
        /// <param name="amount">恢复量</param>
        public void RestoreMana(float amount)
        {
            CurrentMana = Mathf.Min(MaxMana, CurrentMana + amount);
        }

        /// <summary>
        /// 增加经验值
        /// </summary>
        /// <param name="amount">经验值</param>
        public void AddExperience(float amount)
        {
            Experience += amount;
            // 这里可以添加升级逻辑
        }

        /// <summary>
        /// 增加金币
        /// </summary>
        /// <param name="amount">金币数量</param>
        public void AddGold(int amount)
        {
            Gold += amount;
        }

        /// <summary>
        /// 检查是否死亡
        /// </summary>
        /// <returns>是否死亡</returns>
        public bool IsDead()
        {
            return CurrentHealth <= 0f;
        }

        /// <summary>
        /// 检查是否存活
        /// </summary>
        /// <returns>是否存活</returns>
        public bool IsAlive()
        {
            return CurrentHealth > 0f;
        }

        /// <summary>
        /// 获取生命值百分比
        /// </summary>
        /// <returns>生命值百分比（0-1）</returns>
        public float GetHealthPercentage()
        {
            return CurrentHealth / MaxHealth;
        }

        /// <summary>
        /// 获取魔法值百分比
        /// </summary>
        /// <returns>魔法值百分比（0-1）</returns>
        public float GetManaPercentage()
        {
            return CurrentMana / MaxMana;
        }

        /// <summary>
        /// 重置属性到初始状态
        /// </summary>
        public void ResetStats()
        {
            CurrentHealth = MaxHealth;
            CurrentMana = MaxMana;
        }

        /// <summary>
        /// 将另一个 ObjectStatsConfig 的所有属性与自身相加，返回新的合并实例
        /// 用于武器 + 持有者 + Buff 等多个对象属性叠加：数值属性全部相加，布尔属性取或，枚举/ID 取首个有效值
        /// 可链式调用：a.MergeWith(b).MergeWith(c)
        /// </summary>
        /// <param name="other">叠加来源（null 时返回自身的拷贝，不影响结果）</param>
        /// <returns>叠加后的新 ObjectStatsConfig 实例</returns>
        public ObjectStatsConfig MergeWith(ObjectStatsConfig other)
        {
            // null 视为全零配置，相加无效果
            return new ObjectStatsConfig
            {
                // 身份/阵营 — 取首个有效值（0/None/-1 视为未设置）
                Type = Type != ObjectType.None ? Type : (other?.Type ?? ObjectType.None),
                FactionID = FactionID != 0 ? FactionID : (other?.FactionID ?? 0),
                TeamID = TeamID != -1 ? TeamID : (other?.TeamID ?? -1),
                GuildID = GuildID != -1 ? GuildID : (other?.GuildID ?? -1),
                AllianceID = AllianceID != -1 ? AllianceID : (other?.AllianceID ?? -1),
                CurrentPVPMode = CurrentPVPMode != PVPMode.None ? CurrentPVPMode : (other?.CurrentPVPMode ?? PVPMode.None),

                // 基础属性 — 全部相加
                MaxHealth = MaxHealth + (other?.MaxHealth ?? 0f),
                CurrentHealth = CurrentHealth + (other?.CurrentHealth ?? 0f),
                PhysicalAttack = PhysicalAttack + (other?.PhysicalAttack ?? 0f),
                PhysicalDefense = PhysicalDefense + (other?.PhysicalDefense ?? 0f),
                TrueDamage = TrueDamage + (other?.TrueDamage ?? 0f),
                MagicAttack = MagicAttack + (other?.MagicAttack ?? 0f),
                MagicDefense = MagicDefense + (other?.MagicDefense ?? 0f),

                // 速度属性 — 全部相加
                MoveSpeed = MoveSpeed + (other?.MoveSpeed ?? 0f),
                AttackSpeed = AttackSpeed + (other?.AttackSpeed ?? 0f),
                CastSpeed = CastSpeed + (other?.CastSpeed ?? 0f),

                // 暴击属性 — 全部相加
                CriticalRate = CriticalRate + (other?.CriticalRate ?? 0f),
                CriticalDamageMultiplier = CriticalDamageMultiplier + (other?.CriticalDamageMultiplier ?? 0f),

                // 穿透属性 — 全部相加
                ArmorPenetration = ArmorPenetration + (other?.ArmorPenetration ?? 0f),
                MagicPenetration = MagicPenetration + (other?.MagicPenetration ?? 0f),

                // 恢复属性 — 全部相加
                HealthRegeneration = HealthRegeneration + (other?.HealthRegeneration ?? 0f),
                ManaRegeneration = ManaRegeneration + (other?.ManaRegeneration ?? 0f),

                // 特殊属性 — 全部相加
                MaxMana = MaxMana + (other?.MaxMana ?? 0f),
                CurrentMana = CurrentMana + (other?.CurrentMana ?? 0f),
                CooldownReduction = CooldownReduction + (other?.CooldownReduction ?? 0f),
                EvasionRate = EvasionRate + (other?.EvasionRate ?? 0f),
                HitRate = HitRate + (other?.HitRate ?? 0f),
                AttackRange = AttackRange + (other?.AttackRange ?? 0f),
                VisionRange = VisionRange + (other?.VisionRange ?? 0f),
                Experience = Experience + (other?.Experience ?? 0f),
                Level = Level + (other?.Level ?? 0),
                Gold = Gold + (other?.Gold ?? 0),

                // 伤害配置 — 数值相加，布尔取或
                DamageRadius = DamageRadius + (other?.DamageRadius ?? 0f),
                IsContinuousDamage = IsContinuousDamage || (other?.IsContinuousDamage ?? false),
                DamageDuration = DamageDuration + (other?.DamageDuration ?? 0f),
                CanDealDamage = CanDealDamage || (other?.CanDealDamage ?? false),

                // 减速配置 — 取较大值（减速效果不叠加，取最强）
                SlowFactor = Mathf.Max(SlowFactor, other?.SlowFactor ?? 0f)
            };
        }

        /// <summary>
        /// 将多个 ObjectStatsConfig 的所有属性叠加，返回合并后的新实例
        /// 用于不固定个数的攻击方属性合并（如 武器 + 持有者 + Buff1 + Buff2）
        /// </summary>
        /// <param name="configs">需要叠加的属性配置数组（null 元素会被跳过）</param>
        /// <returns>叠加后的新 ObjectStatsConfig 实例</returns>
        public static ObjectStatsConfig Merge(params ObjectStatsConfig[] configs)
        {
            ObjectStatsConfig result = new ObjectStatsConfig();

            if (configs == null)
            {
                return result;
            }

            for (int i = 0; i < configs.Length; i++)
            {
                if (configs[i] != null)
                {
                    result = result.MergeWith(configs[i]);
                }
            }

            return result;
        }

        /// <summary>
        /// 设置当前生命值（自动限制在 [0, MaxHealth] 范围内）
        /// </summary>
        /// <param name="health">目标生命值</param>
        public void SetCurrentHealth(float health)
        {
            CurrentHealth = Mathf.Clamp(health, 0f, MaxHealth);
        }

        /// <summary>
        /// 设置当前魔法值（自动限制在 [0, MaxMana] 范围内）
        /// </summary>
        /// <param name="mana">目标魔法值</param>
        public void SetCurrentMana(float mana)
        {
            CurrentMana = Mathf.Clamp(mana, 0f, MaxMana);
        }

        /// <summary>
        /// 设置物理攻击力
        /// </summary>
        /// <param name="attack">物理攻击力</param>
        public void SetPhysicalAttack(float attack)
        {
            PhysicalAttack = attack;
        }

        /// <summary>
        /// 设置魔法攻击力
        /// </summary>
        /// <param name="attack">魔法攻击力</param>
        public void SetMagicAttack(float attack)
        {
            MagicAttack = attack;
        }

        /// <summary>
        /// 从源物体继承阵营信息（用于武器/投射物从持有者继承阵营）
        /// 复制 FactionID/TeamID/GuildID/AllianceID/CurrentPVPMode 五个字段
        /// </summary>
        /// <param name="source">阵营信息来源（持有者属性配置）</param>
        public void InheritFactionFrom(ObjectStatsConfig source)
        {
            if (source == null)
            {
                return;
            }

            FactionID = source.FactionID;
            TeamID = source.TeamID;
            GuildID = source.GuildID;
            AllianceID = source.AllianceID;
            CurrentPVPMode = source.CurrentPVPMode;
        }

        /// <summary>
        /// 设置伤害范围（运行时修改 DamageRadius）
        /// </summary>
        /// <param name="radius">新的伤害半径（米）</param>
        public void SetDamageRadius(float radius)
        {
            DamageRadius = radius;
        }

        /// <summary>
        /// 设置伤害持续时间（运行时修改 DamageDuration）
        /// </summary>
        /// <param name="duration">持续时间（秒）</param>
        public void SetDamageDuration(float duration)
        {
            DamageDuration = duration;
        }

        /// <summary>
        /// 应用减速效果：将 MoveSpeed 降低到原速度的 slowFactor 比例。
        /// slowFactor=0.3 表示降至原速度30%（即减70%），0 表示不减速。
        /// </summary>
        /// <param name="slowFactor">减速后的速度保留比例（0~1）</param>
        public void ApplySlow(float slowFactor)
        {
            if (slowFactor <= 0f || MoveSpeed <= 0f)
            {
                return;
            }

            MoveSpeed *= slowFactor;
        }

        /// <summary>
        /// 恢复原始移动速度（离开减速区域或减速结束时调用）
        /// </summary>
        /// <param name="originalSpeed">减速前的原始速度值</param>
        public void RestoreSpeed(float originalSpeed)
        {
            MoveSpeed = originalSpeed;
        }

        #endregion

        #region 阵营判断方法（RPG游戏标准逻辑）

        /// <summary>
        /// 检查是否可以造成伤害（综合考虑PVP模式、队伍、公会、同盟、阵营关系）
        /// 判定优先级：TeamID > GuildID > AllianceID > FactionID + FactionRelation + PVPMode
        /// </summary>
        /// <param name="target">目标物体</param>
        /// <returns>是否可以造成伤害</returns>
        public bool CanDealDamageTo(ObjectStatsConfig target)
        {
            if (target == null)
            {
                return false;
            }

            // 优先级1：同一队伍（TeamID > -1）不能互相伤害
            if (TeamID > -1 && target.TeamID > -1 && TeamID == target.TeamID)
            {
                return false; // 同队伍成员互为友方，不能伤害
            }

            // 优先级2：同一公会（GuildID > -1）不能互相伤害（除非PVP开放）
            if (GuildID > -1 && target.GuildID > -1 && GuildID == target.GuildID)
            {
                // 公会成员之间，如果开启自由PVP，可以互相伤害
                if (CurrentPVPMode == PVPMode.Open && target.CurrentPVPMode == PVPMode.Open)
                {
                    return true; // 双方都开启自由PVP，可以伤害
                }
                return false; // 同公会成员默认不能伤害
            }

            // 优先级3：同一同盟（AllianceID > -1）不能互相伤害（除非PVP开放）
            if (AllianceID > -1 && target.AllianceID > -1 && AllianceID == target.AllianceID)
            {
                // 同盟成员之间，如果开启自由PVP，可以互相伤害
                if (CurrentPVPMode == PVPMode.Open && target.CurrentPVPMode == PVPMode.Open)
                {
                    return true; // 双方都开启自由PVP，可以伤害
                }
                return false; // 同同盟成员默认不能伤害
            }

            // 优先级4：阵营关系判定（需要查询阵营关系表）
            FactionRelationType relation = GetFactionRelation(target.FactionID);

            // 根据阵营关系判定
            switch (relation)
            {
                case FactionRelationType.Friendly:
                    // 友好阵营：默认不能伤害，除非双方都开启PVP
                    if (CurrentPVPMode == PVPMode.Open && target.CurrentPVPMode == PVPMode.Open)
                    {
                        return true; // 双方都开启自由PVP，可以伤害
                    }
                    return false; // 友好阵营默认不能伤害

                case FactionRelationType.Alliance:
                    // 同盟阵营：默认不能伤害，除非双方都开启PVP
                    if (CurrentPVPMode == PVPMode.Open && target.CurrentPVPMode == PVPMode.Open)
                    {
                        return true; // 双方都开启自由PVP，可以伤害
                    }
                    return false; // 同盟阵营默认不能伤害

                case FactionRelationType.Neutral:
                    // 中立阵营：可以互相伤害（PVP区域或自由PVP）
                    return true; // 中立阵营可以伤害

                case FactionRelationType.Hostile:
                    // 敌对阵营：可以直接伤害
                    return true; // 敌对阵营可以伤害

                default:
                    return true; // 默认可以伤害
            }
        }

        /// <summary>
        /// 检查是否是同一阵营（基于FactionID）
        /// </summary>
        /// <param name="other">另一个物体</param>
        /// <returns>是否是同一阵营</returns>
        public bool IsSameFaction(ObjectStatsConfig other)
        {
            if (other == null)
            {
                return false;
            }

            return FactionID == other.FactionID;
        }

        /// <summary>
        /// 检查是否是同一队伍（基于TeamID）
        /// </summary>
        /// <param name="other">另一个物体</param>
        /// <returns>是否是同一队伍</returns>
        public bool IsSameTeam(ObjectStatsConfig other)
        {
            if (other == null || TeamID <= -1 || other.TeamID <= -1)
            {
                return false;
            }

            return TeamID == other.TeamID;
        }

        /// <summary>
        /// 检查是否是同一公会（基于GuildID）
        /// </summary>
        /// <param name="other">另一个物体</param>
        /// <returns>是否是同一公会</returns>
        public bool IsSameGuild(ObjectStatsConfig other)
        {
            if (other == null || GuildID <= -1 || other.GuildID <= -1)
            {
                return false;
            }

            return GuildID == other.GuildID;
        }

        /// <summary>
        /// 检查是否是同一同盟（基于AllianceID）
        /// </summary>
        /// <param name="other">另一个物体</param>
        /// <returns>是否是同一同盟</returns>
        public bool IsSameAlliance(ObjectStatsConfig other)
        {
            if (other == null || AllianceID <= -1 || other.AllianceID <= -1)
            {
                return false;
            }

            return AllianceID == other.AllianceID;
        }

        /// <summary>
        /// 检查是否是友方（综合考虑队伍、公会、同盟、阵营关系）
        /// </summary>
        /// <param name="other">另一个物体</param>
        /// <returns>是否是友方</returns>
        public bool IsFriendly(ObjectStatsConfig other)
        {
            if (other == null)
            {
                return false;
            }

            // 同队伍是友方
            if (IsSameTeam(other))
            {
                return true;
            }

            // 同公会是友方（除非PVP开启）
            if (IsSameGuild(other))
            {
                if (CurrentPVPMode == PVPMode.Open && other.CurrentPVPMode == PVPMode.Open)
                {
                    return false; // 双方开启自由PVP，不再是友方
                }
                return true;
            }

            // 同同盟是友方（除非PVP开启）
            if (IsSameAlliance(other))
            {
                if (CurrentPVPMode == PVPMode.Open && other.CurrentPVPMode == PVPMode.Open)
                {
                    return false; // 双方开启自由PVP，不再是友方
                }
                return true;
            }

            // 阵营关系判定
            FactionRelationType relation = GetFactionRelation(other.FactionID);
            if (relation == FactionRelationType.Friendly || relation == FactionRelationType.Alliance)
            {
                // 友好或同盟阵营，如果双方都开启PVP，则不再是友方
                if (CurrentPVPMode == PVPMode.Open && other.CurrentPVPMode == PVPMode.Open)
                {
                    return false;
                }
                return true;
            }

            return false;
        }

        /// <summary>
        /// 检查是否是敌方（综合考虑队伍、公会、同盟、阵营关系）
        /// </summary>
        /// <param name="other">另一个物体</param>
        /// <returns>是否是敌方</returns>
        public bool IsHostile(ObjectStatsConfig other)
        {
            if (other == null)
            {
                return false;
            }

            // 如果不能造成伤害，则不是敌方
            if (!CanDealDamageTo(other))
            {
                return false;
            }

            // 可以造成伤害的，即为敌方
            return true;
        }

        /// <summary>
        /// 获取阵营关系（从阵营关系表中查询）
        /// 注意：这是一个示例实现，实际项目中应该从全局阵营关系表中查询
        /// </summary>
        /// <param name="targetFactionID">目标阵营ID</param>
        /// <returns>阵营关系类型</returns>
        protected virtual FactionRelationType GetFactionRelation(int targetFactionID)
        {
            // 示例实现：同阵营为友好，不同阵营为敌对
            if (FactionID == targetFactionID)
            {
                return FactionRelationType.Friendly;
            }

            // 示例阵营关系规则：
            // 玩家阵营（1-10）、怪物阵营（11-50，敌对）、NPC阵营（51-99，中立）、100+ 中立阵营

            // 玩家阵营之间：友好
            if (FactionID >= k_playerFactionMinID && FactionID <= k_playerFactionMaxID
                && targetFactionID >= k_playerFactionMinID && targetFactionID <= k_playerFactionMaxID)
            {
                return FactionRelationType.Friendly;
            }

            // 玩家对怪物：敌对
            if (FactionID >= k_playerFactionMinID && FactionID <= k_playerFactionMaxID
                && targetFactionID >= k_monsterFactionMinID && targetFactionID <= k_monsterFactionMaxID)
            {
                return FactionRelationType.Hostile;
            }

            // 怪物对玩家：敌对
            if (FactionID >= k_monsterFactionMinID && FactionID <= k_monsterFactionMaxID
                && targetFactionID >= k_playerFactionMinID && targetFactionID <= k_playerFactionMaxID)
            {
                return FactionRelationType.Hostile;
            }

            // NPC阵营：中立
            if (targetFactionID >= k_npcFactionMinID && targetFactionID <= k_npcFactionMaxID)
            {
                return FactionRelationType.Neutral;
            }

            // 其他情况：中立
            return FactionRelationType.Neutral;
        }

        #endregion
    }

    /// <summary>
    /// ObjectStatsConfig 使用说明：
    /// ============================================================
    /// 物体属性配置数据类，定义所有游戏物体的基础数值属性。
    /// 适用于角色、投射物、陷阱、建筑等所有 ObjectBase 子类。
    /// 子类通过重写 ObjectBase.ObjectStatsConfig 属性返回配置实例。
    ///
    /// ════════════════════════════════════════════════════════════
    /// 【与 ObjectBase 的关系】
    /// ════════════════════════════════════════════════════════════
    ///   ObjectBase 通过 protected virtual ObjectStatsConfig 属性获取配置：
    ///     - 子类重写属性返回 new ObjectStatsConfig { ... } 提供配置
    ///     - 返回 null 则不启用属性系统（GetObjectStats 返回 null，所有 Get 方法返回 0）
    ///     - ObjectBase.Awake 会克隆配置到运行时实例 m_objectStats（修改不影响原配置）
    ///     - 通过 GetObjectStats() 获取运行时实例进行属性操作
    ///   设计原因：必须存在无参构造函数，以便 ObjectBase 通过 new + CopyObjectStatsConfig 克隆配置
    ///
    /// ════════════════════════════════════════════════════════════
    /// 【枚举详解】
    /// ════════════════════════════════════════════════════════════
    ///   ObjectType（物体类型）：
    ///     None=0, Warrior=1（战士）, Mage=2（法师）, Assassin=3（刺客）
    ///     Tank=4（坦克）, Marksman=5（射手）, Support=6（辅助）
    ///     Projectile=7（投射物）, Trap=8（陷阱）, Building=9（建筑）
    ///     Neutral=10（中立物体）, Weapon=11（武器）
    ///
    ///   FactionRelationType（阵营关系类型）：
    ///     Friendly=0  友好：可以组队、治疗，不互相伤害
    ///     Neutral=1   中立：不主动攻击，但可以互相伤害（PVP）
    ///     Hostile=2   敌对：主动攻击，仇恨系统
    ///     Alliance=3  同盟：组队后的额外友好关系
    ///
    ///   PVPMode（PVP 模式）：
    ///     None=0   PVP 关闭：不能攻击友好阵营的玩家
    ///     Open=1   开放 PVP：可以攻击任何玩家（自由 PVP）
    ///     Arena=2  竞技场模式：特定区域允许 PVP
    ///     Duel=3   决斗模式：双方同意后可以战斗
    ///
    ///   DamageType（伤害类型）：
    ///     Physical=0 物理伤害：受物理防御减免，受护甲穿透影响
    ///     Magic=1    魔法伤害：受魔法防御减免，受魔法穿透影响
    ///     True=2     真实伤害：无视防御，直接扣除
    ///
    /// ════════════════════════════════════════════════════════════
    /// 【阵营 ID 范围常量】
    /// ════════════════════════════════════════════════════════════
    ///   k_playerFactionMinID=1, k_playerFactionMaxID=10   玩家阵营
    ///   k_monsterFactionMinID=11, k_monsterFactionMaxID=50 怪物阵营
    ///   k_npcFactionMinID=51, k_npcFactionMaxID=99        NPC 阵营
    ///   100+                                                中立阵营
    ///   这些常量用于 GetFactionRelation 的默认实现，实际项目可通过 FactionRelationManager 自定义
    ///
    /// ════════════════════════════════════════════════════════════
    /// 【属性分组详解】
    /// ════════════════════════════════════════════════════════════
    ///   1. 基础属性：
    ///      Type(物体类型), FactionID(阵营ID), TeamID(队伍ID, -1=无)
    ///      GuildID(公会ID, -1=无), AllianceID(同盟ID, -1=无), CurrentPVPMode(PVP模式)
    ///      MaxHealth/CurrentHealth(生命值)
    ///      PhysicalAttack/PhysicalDefense(物理攻防)
    ///      TrueDamage(真实伤害), MagicAttack/MagicDefense(魔法攻防)
    ///
    ///   2. 速度属性：
    ///      MoveSpeed(移动速度, 米/秒)
    ///      AttackSpeed(攻击速度, 次/秒)
    ///      CastSpeed(施法速度, 次/秒，持续伤害间隔=1/CastSpeed)
    ///
    ///   3. 暴击属性：
    ///      CriticalRate(暴击率, 0-1)
    ///      CriticalDamageMultiplier(暴击伤害倍率, 如 2.0=200%)
    ///
    ///   4. 穿透属性：
    ///      ArmorPenetration(护甲穿透, 0-1)
    ///      MagicPenetration(魔法穿透, 0-1)
    ///
    ///   5. 恢复属性：
    ///      HealthRegeneration(生命恢复, 每秒)
    ///      ManaRegeneration(魔法恢复, 每秒)
    ///
    ///   6. 特殊属性：
    ///      MaxMana/CurrentMana(魔法值)
    ///      CooldownReduction(冷却缩减, 0-1)
    ///      EvasionRate(闪避率, 0-1)
    ///      HitRate(命中率, 0-1)
    ///      AttackRange(攻击范围, 米)
    ///      VisionRange(视野范围, 米)
    ///      Experience(经验值), Level(等级), Gold(金币)
    ///
    ///   7. 伤害配置属性（用于投射物、陷阱等）：
    ///      DamageRadius(范围伤害半径, 米)
    ///      IsContinuousDamage(是否持续伤害)
    ///      DamageDuration(伤害持续时间, 秒)
    ///      CanDealDamage(是否启用伤害功能)
    ///      持续伤害频率由 AttackSpeed(物理) 或 CastSpeed(魔法) 控制：interval = 1f / Speed
    ///
    /// ════════════════════════════════════════════════════════════
    /// 【核心方法详解】
    /// ════════════════════════════════════════════════════════════
    ///   伤害计算方法（静态，唯一入口，计算与应用分离）：
    ///     CalculateAttack(multiplier, target, params attackers)
    ///       — 累加所有攻击方属性 → 闪避 → 伤害计算 → 暴击，返回伤害值（不扣血）
    ///       物理伤害 = 攻击力 × (100 / (100 + 目标防御 × (1 - 护甲穿透)))
    ///       魔法伤害 = 攻击力 × (100 / (100 + 目标防御 × (1 - 魔法穿透)))
    ///       真实伤害 = TrueDamage（无视防御）
    ///       计算后由调用方调 target.TakeDamage(damage) 应用（保留无敌/回调/死亡）
    ///       无倍率时传 new ObjectStatsConfigMultiplier()
    ///
    ///   暴击和闪避方法：
    ///     IsCriticalHit()             — Random.value &lt; CriticalRate
    ///     GetCriticalDamage(base)     — base × CriticalDamageMultiplier
    ///     IsEvaded(attackerHitRate)   — Random.value &lt; max(0, EvasionRate - (HitRate - 1))
    ///
    ///   阵营判断方法（详见下方阵营判定章节）：
    ///     CanDealDamageTo(target)  — 综合判断能否造成伤害（核心方法）
    ///     IsSameFaction/IsSameTeam/IsSameGuild/IsSameAlliance(other) — 同一阵营判定
    ///     IsFriendly(other)        — 是否是友方
    ///     IsHostile(other)         — 是否是敌方
    ///     GetFactionRelation(targetFactionID) — 查询阵营关系（可重写）
    ///
    ///   属性操作方法：
    ///     TakeDamage(damage)   — CurrentHealth = max(0, CurrentHealth - damage)
    ///     Heal(amount)         — CurrentHealth = min(MaxHealth, CurrentHealth + amount)
    ///     ConsumeMana(amount)  — 消耗魔法（不足返回 false，不消耗）
    ///     RestoreMana(amount)  — 恢复魔法
    ///     AddExperience(amount) — 增加经验值
    ///     AddGold(amount)      — 增加金币
    ///     IsDead() / IsAlive() — CurrentHealth &lt;= 0 / &gt; 0
    ///     GetHealthPercentage() / GetManaPercentage() — 返回 0-1
    ///     ResetStats()         — 重置生命和魔法到满值
    ///
    /// ════════════════════════════════════════════════════════════
    /// 【阵营判定详解】
    /// ════════════════════════════════════════════════════════════
    ///   CanDealDamageTo(target) 判定优先级（从高到低）：
    ///     1. TeamID（同一队伍不能互相伤害，-1 表示无队伍）
    ///     2. GuildID（同一公会默认不能伤害，双方 Open PVP 例外）
    ///     3. AllianceID（同一同盟默认不能伤害，双方 Open PVP 例外）
    ///     4. FactionID + FactionRelation（阵营关系判定）：
    ///        - Friendly/Alliance：默认不能伤害，双方 Open PVP 例外
    ///        - Neutral：可以互相伤害
    ///        - Hostile：可以直接伤害
    ///
    ///   IsFriendly(other) 判定规则：
    ///     - 同队伍 → 友方
    ///     - 同公会（非 Open PVP）→ 友方
    ///     - 同同盟（非 Open PVP）→ 友方
    ///     - 友好/同盟阵营（非 Open PVP）→ 友方
    ///     - 其他 → 非友方
    ///
    ///   IsHostile(other) 判定规则：
    ///     - 等价于 !CanDealDamageTo(other) 的反面：CanDealDamageTo 返回 true 则为敌方
    ///
    ///   GetFactionRelation(targetFactionID) 默认实现：
    ///     - 同 FactionID → Friendly
    ///     - 玩家阵营(1-10) 之间 → Friendly
    ///     - 玩家(1-10) ↔ 怪物(11-50) → Hostile
    ///     - 涉及 NPC(51-99) → Neutral
    ///     - 其他 → Neutral
    ///     注意：这是 protected virtual 方法，子类可重写以接入 FactionRelationManager
    ///
    /// ════════════════════════════════════════════════════════════
    /// 【ObjectStatsConfigMultiplier 详解】
    /// ════════════════════════════════════════════════════════════
    ///   攻击参数倍率结构体，用于按动画差异化调整属性值（每个攻击动画独立配置）。
    ///   设计用途：连击系统中每个攻击动画可设置不同倍率，实现递增伤害、暴击加成等。
    ///
    ///   倍率字段（每个对应 ObjectStatsConfig 中的一个属性）：
    ///     PhysicalAttackMultiplier   — 物理攻击倍率
    ///     MagicAttackMultiplier      — 魔法攻击倍率
    ///     TrueDamageMultiplier       — 真实伤害倍率
    ///     CriticalRateMultiplier     — 暴击率倍率
    ///     CriticalDamageMultiplier   — 暴击伤害倍率的倍率
    ///     ArmorPenetrationMultiplier — 护甲穿透倍率
    ///     MagicPenetrationMultiplier — 魔法穿透倍率
    ///     HitRateMultiplier          — 命中率倍率
    ///     AttackRangeMultiplier      — 攻击范围倍率
    ///
    ///   倍率规则：
    ///     k_useBase(-1) = 使用基础 ObjectStatsConfig 值（倍率 1.0）
    ///     其他值 = 实际属性 = 基础属性 × 倍率
    ///     示例：CriticalRateMultiplier = 0.5，基础 CriticalRate = 0.1 → 实际暴击率 = 0.05
    ///
    ///   静态方法：
    ///     Apply(baseValue, multiplier) — multiplier &lt; 0 返回 baseValue，否则返回 baseValue × multiplier
    ///
    ///   使用场景：
    ///     - 在 AnimationConfig.Multiplier 中配置，TryStartAttack 时传递给 OnAttackStarted
    ///     - 武器系统（如 Sword）通过 BeginSwing(multiplier) 接收，碰撞时用于伤害计算
    ///     - CalculateAttack(multiplier, target, attackers) 静态方法自动应用倍率
    ///
    /// ────────────────────────────────────────────────────────────
    /// 示例 1：战士属性配置（物理输出型）
    /// ────────────────────────────────────────────────────────────
    /// <code>
    /// protected override ObjectStatsConfig ObjectStatsConfig =&gt; new ObjectStatsConfig
    /// {
    ///     Type = ObjectType.Warrior,
    ///     FactionID = 1,                    // 玩家阵营
    ///     MaxHealth = 150f,
    ///     CurrentHealth = 150f,
    ///     PhysicalAttack = 25f,
    ///     PhysicalDefense = 15f,
    ///     TrueDamage = 5f,
    ///     MagicAttack = 10f,
    ///     MagicDefense = 12f,
    ///     MoveSpeed = 6f,
    ///     CriticalRate = 0.2f,              // 20% 暴击率
    ///     CriticalDamageMultiplier = 2.5f,  // 暴击伤害 250%
    ///     ArmorPenetration = 0.15f          // 15% 护甲穿透
    /// };
    /// </code>
    ///
    /// ────────────────────────────────────────────────────────────
    /// 示例 2：法师属性配置（魔法输出型）
    /// ────────────────────────────────────────────────────────────
    /// <code>
    /// protected override ObjectStatsConfig ObjectStatsConfig =&gt; new ObjectStatsConfig
    /// {
    ///     Type = ObjectType.Mage,
    ///     FactionID = 1,
    ///     MaxHealth = 80f,
    ///     CurrentHealth = 80f,
    ///     PhysicalAttack = 5f,
    ///     PhysicalDefense = 5f,
    ///     MagicAttack = 30f,
    ///     MagicDefense = 10f,
    ///     MaxMana = 150f,
    ///     CurrentMana = 150f,
    ///     MagicPenetration = 0.2f,          // 20% 魔法穿透
    ///     CooldownReduction = 0.15f         // 15% 冷却缩减
    /// };
    /// </code>
    ///
    /// ────────────────────────────────────────────────────────────
    /// 示例 3：怪物属性配置（坦克型）
    /// ────────────────────────────────────────────────────────────
    /// <code>
    /// protected override ObjectStatsConfig ObjectStatsConfig =&gt; new ObjectStatsConfig
    /// {
    ///     Type = ObjectType.Tank,
    ///     FactionID = 2,                    // 怪物阵营（与玩家阵营敌对）
    ///     MaxHealth = 300f,                 // 高血量
    ///     CurrentHealth = 300f,
    ///     PhysicalAttack = 15f,
    ///     PhysicalDefense = 30f,            // 高防御
    ///     MagicDefense = 20f,
    ///     MoveSpeed = 3f,                   // 移动缓慢
    ///     EvasionRate = 0.05f,
    ///     HitRate = 0.9f
    /// };
    /// </code>
    ///
    /// ────────────────────────────────────────────────────────────
    /// 示例 4：投射物配置（范围持续魔法伤害）
    /// ────────────────────────────────────────────────────────────
    /// <code>
    /// protected override ObjectStatsConfig ObjectStatsConfig =&gt; new ObjectStatsConfig
    /// {
    ///     Type = ObjectType.Projectile,
    ///     FactionID = 100,                  // 中立阵营
    ///     MaxHealth = 1f,                   // 投射物本身只有 1 点生命
    ///     CurrentHealth = 1f,
    ///     MagicAttack = 25f,                // 魔法攻击力（替代 BaseDamage + DamageType.Magic）
    ///     CastSpeed = 2f,                   // 施法频率 2次/秒，即每 0.5 秒一次（替代 DamageInterval）
    ///     MagicPenetration = 0.2f,
    ///     DamageRadius = 5f,                // 伤害范围 5 米
    ///     IsContinuousDamage = true,        // 持续伤害
    ///     DamageDuration = 10f,             // 持续 10 秒
    ///     CanDealDamage = true
    /// };
    /// </code>
    ///
    /// ────────────────────────────────────────────────────────────
    /// 示例 5：陷阱配置（单次真实伤害）
    /// ────────────────────────────────────────────────────────────
    /// <code>
    /// protected override ObjectStatsConfig ObjectStatsConfig =&gt; new ObjectStatsConfig
    /// {
    ///     Type = ObjectType.Trap,
    ///     FactionID = 100,
    ///     TrueDamage = 50f,                // 真实伤害（无视防御，替代 BaseDamage + DamageType.True）
    ///     DamageRadius = 2f,
    ///     IsContinuousDamage = false,       // 单次伤害
    ///     CanDealDamage = true
    /// };
    /// </code>
    ///
    /// ────────────────────────────────────────────────────────────
    /// 示例 6：伤害计算与战斗流程（CalculateAttack 计算与 TakeDamage 应用分离）
    /// ────────────────────────────────────────────────────────────
    /// <code>
    /// // 获取双方的属性配置
    /// ObjectStatsConfig attackerStats = attacker.GetObjectStats();
    /// ObjectStatsConfig targetStats = target.GetObjectStats();
    ///
    /// // 1. 判断能否攻击（阵营/队伍/PVP 规则）
    /// if (!attackerStats.CanDealDamageTo(targetStats))
    /// {
    ///     return;  // 友方不能伤害
    /// }
    ///
    /// // 2. 计算伤害（静态方法：累加攻击 → 闪避 → 防御减免 → 暴击，不扣血）
    /// float damage = ObjectStatsConfig.CalculateAttack(
    ///     new ObjectStatsConfigMultiplier(), targetStats, attackerStats);
    /// if (damage &lt;= 0f)
    /// {
    ///     Debug.Log("攻击被闪避！");
    ///     return;
    /// }
    ///
    /// // 3. 应用伤害（经 ObjectBase.TakeDamage 保留无敌/回调/死亡）
    /// target.TakeDamage(damage);
    ///
    /// // —— 武器场景：传入武器 + 持有者，params 累加后计算 ——
    /// // float dmg = ObjectStatsConfig.CalculateAttack(multiplier, targetStats, weaponStats, holderStats);
    /// // target.TakeDamage(dmg);
    /// ///
    /// // —— 多对象场景（武器 + 持有者 + Buff）：params 可变参数 ——
    /// // float dmg = ObjectStatsConfig.CalculateAttack(multiplier, targetStats, weaponStats, holderStats, buffStats);
    /// // target.TakeDamage(dmg);
    /// </code>
    ///
    /// ────────────────────────────────────────────────────────────
    /// 示例 7：阵营关系与 PVP 判定
    /// ────────────────────────────────────────────────────────────
    /// <code>
    /// // 配置同队伍成员（TeamID 相同，互为友方，无法伤害）
    /// player1.GetObjectStats().TeamID = 1;
    /// player2.GetObjectStats().TeamID = 1;
    /// // player1.CanDealDamageTo(player2) == false
    ///
    /// // 配置同公会成员（GuildID 相同，默认友方）
    /// player1.GetObjectStats().GuildID = 100;
    /// player2.GetObjectStats().GuildID = 100;
    ///
    /// // 开启自由 PVP 模式后，同公会成员可以互相伤害
    /// player1.GetObjectStats().CurrentPVPMode = PVPMode.Open;
    /// player2.GetObjectStats().CurrentPVPMode = PVPMode.Open;
    /// // player1.CanDealDamageTo(player2) == true
    ///
    /// // 判断敌我关系
    /// bool isFriend = attacker.IsFriendly(target);   // 友方
    /// bool isEnemy = attacker.IsHostile(target);     // 敌方
    /// bool isSameFaction = attacker.IsSameFaction(target);
    /// </code>
    ///
    /// ────────────────────────────────────────────────────────────
    /// 示例 8：运行时属性操作
    /// ────────────────────────────────────────────────────────────
    /// <code>
    /// ObjectStatsConfig stats = warrior.GetObjectStats();
    ///
    /// // 生命值操作
    /// stats.TakeDamage(20f);                     // 扣血
    /// stats.Heal(10f);                           // 回血
    /// float hpPercent = stats.GetHealthPercentage(); // 0-1
    ///
    /// // 魔法值操作
    /// bool success = stats.ConsumeMana(15f);      // 消耗魔法（返回是否成功）
    /// stats.RestoreMana(5f);                      // 恢复魔法
    ///
    /// // 经验和金币
    /// stats.AddExperience(100f);
    /// stats.AddGold(50);
    ///
    /// // 重置到满状态
    /// stats.ResetStats();
    /// </code>
    /// </summary>
}