using System;
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

        /// <summary>基础伤害倍率（-1=使用基础值）</summary>
        public float BaseDamageMultiplier;

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
            float baseDamageMultiplier = k_useBase,
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
            BaseDamageMultiplier = baseDamageMultiplier;
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
            return multiplier < 0f ? baseValue : baseValue * multiplier;
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
        public ObjectType Type = ObjectType.Warrior;

        // 阵营ID（使用唯一ID标识阵营，而非固定枚举）
        [SerializeField]
        [Tooltip("阵营ID（1=玩家阵营A，2=玩家阵营B，3=怪物阵营，100=中立等）")]
        public int FactionID = 1;

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
        public float MaxHealth = 100f;

        // 当前生命值
        [SerializeField]
        [Tooltip("当前生命值")]
        public float CurrentHealth = 100f;

        // 物理攻击力
        [SerializeField]
        [Tooltip("物理攻击力")]
        public float PhysicalAttack = 10f;

        // 物理防御力
        [SerializeField]
        [Tooltip("物理防御力")]
        public float PhysicalDefense = 5f;

        // 真实伤害
        [SerializeField]
        [Tooltip("真实伤害（无视防御）")]
        public float TrueDamage = 0f;

        // 魔法攻击力
        [SerializeField]
        [Tooltip("魔法攻击力")]
        public float MagicAttack = 10f;

        // 魔法防御力
        [SerializeField]
        [Tooltip("魔法防御力")]
        public float MagicDefense = 5f;

        #endregion

        #region 速度属性

        // 移动速度
        [SerializeField]
        [Tooltip("移动速度，单位：米/秒")]
        public float MoveSpeed = 5f;

        // 攻击速度
        [SerializeField]
        [Tooltip("攻击速度，每秒攻击次数")]
        public float AttackSpeed = 1f;

        // 施法速度
        [SerializeField]
        [Tooltip("施法速度倍率")]
        public float CastSpeed = 1f;

        #endregion

        #region 暴击属性

        // 暴击率
        [SerializeField]
        [Tooltip("暴击率，范围：0-1")]
        [Range(0f, 1f)]
        public float CriticalRate = 0.1f;

        // 暴击伤害倍率
        [SerializeField]
        [Tooltip("暴击伤害倍率（例如：2.0 表示暴击伤害为200%）")]
        public float CriticalDamageMultiplier = 2f;

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
        public float HealthRegeneration = 1f;

        // 魔法恢复速度
        [SerializeField]
        [Tooltip("魔法恢复速度，每秒恢复的魔法值")]
        public float ManaRegeneration = 1f;

        #endregion

        #region 特殊属性

        // 最大魔法值
        [SerializeField]
        [Tooltip("最大魔法值")]
        public float MaxMana = 100f;

        // 当前魔法值
        [SerializeField]
        [Tooltip("当前魔法值")]
        public float CurrentMana = 100f;

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
        public float HitRate = 1f;

        // 攻击范围
        [SerializeField]
        [Tooltip("攻击范围，单位：米")]
        public float AttackRange = 5f;

        // 视野范围
        [SerializeField]
        [Tooltip("视野范围，单位：米")]
        public float VisionRange = 10f;

        // 经验值
        [SerializeField]
        [Tooltip("当前经验值")]
        public float Experience = 0f;

        // 等级
        [SerializeField]
        [Tooltip("当前等级")]
        public int Level = 1;

        // 金币
        [SerializeField]
        [Tooltip("当前金币")]
        public int Gold = 0;

        #endregion

        #region 伤害配置属性（从 DamageConfig 合并）

        // 基础伤害值
        [SerializeField]
        [Tooltip("基础伤害值（用于投射物、陷阱等）")]
        public float BaseDamage = 10f;

        // 伤害类型
        [SerializeField]
        [Tooltip("伤害类型（物理/魔法/真实）")]
        public DamageType DamageType = DamageType.Physical;

        // 伤害范围
        [SerializeField]
        [Tooltip("范围伤害半径，单位：米")]
        public float DamageRadius = 5f;

        // 伤害频率（秒）
        [SerializeField]
        [Tooltip("持续伤害频率，单位：秒（每多少秒造成一次伤害）")]
        public float DamageInterval = 1f;

        // 是否持续伤害
        [SerializeField]
        [Tooltip("是否持续伤害（ OnTriggerStay 中持续造成伤害）")]
        public bool IsContinuousDamage = true;

        // 伤害持续时间（秒）
        [SerializeField]
        [Tooltip("伤害持续时间，单位：秒（持续伤害的总时长）")]
        public float DamageDuration = 5f;

        // 是否可以造成伤害
        [SerializeField]
        [Tooltip("是否可以造成伤害（用于控制物体是否启用伤害功能）")]
        public bool CanDealDamage = true;

        #endregion

        /// <summary>
        /// 默认配置构造函数（创建空配置，由 CopyObjectStatsConfig 填充）
        /// </summary>
        public ObjectStatsConfig() { }

        /// <summary>
        /// 自定义配置构造函数（基础属性）
        /// </summary>
        public ObjectStatsConfig(float maxHealth, float physicalAttack, float physicalDefense,
            float magicAttack, float magicDefense, float moveSpeed)
        {
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
        /// 计算实际物理伤害（考虑防御和穿透）
        /// </summary>
        /// <param name="targetDefense">目标物理防御</param>
        /// <returns>实际造成的物理伤害</returns>
        public float CalculatePhysicalDamage(float targetDefense)
        {
            // 计算穿透后的防御值
            float effectiveDefense = targetDefense * (1f - ArmorPenetration);

            // 物理伤害公式：伤害 = 攻击力 * (100 / (100 + 防御))
            float damage = PhysicalAttack * (100f / (100f + effectiveDefense));

            return damage;
        }

        /// <summary>
        /// 计算实际魔法伤害（考虑防御和穿透）
        /// </summary>
        /// <param name="targetDefense">目标魔法防御</param>
        /// <returns>实际造成的魔法伤害</returns>
        public float CalculateMagicDamage(float targetDefense)
        {
            // 计算穿透后的防御值
            float effectiveDefense = targetDefense * (1f - MagicPenetration);

            // 魔法伤害公式：伤害 = 攻击力 * (100 / (100 + 防御))
            float damage = MagicAttack * (100f / (100f + effectiveDefense));

            return damage;
        }

        /// <summary>
        /// 计算实际伤害（根据伤害类型）
        /// </summary>
        /// <param name="targetStats">目标属性配置</param>
        /// <returns>实际伤害值</returns>
        public float CalculateDamage(ObjectStatsConfig targetStats)
        {
            if (targetStats == null)
            {
                return BaseDamage;
            }

            switch (DamageType)
            {
                case DamageType.Physical:
                    // 物理伤害 = 攻击力 - 防御力计算
                    return CalculatePhysicalDamage(targetStats.PhysicalDefense);

                case DamageType.Magic:
                    // 魔法伤害 = 魔法攻击力 - 魔法防御力计算
                    return CalculateMagicDamage(targetStats.MagicDefense);

                case DamageType.True:
                    // 真实伤害无视防御
                    return TrueDamage + BaseDamage;

                default:
                    return BaseDamage;
            }
        }

        /// <summary>
        /// 计算实际伤害（根据伤害类型，应用攻击参数倍率）
        /// 倍率为 k_useBase(-1) 时使用自身基础属性值，否则 基础值 × 倍率
        /// </summary>
        /// <param name="targetStats">目标属性配置</param>
        /// <param name="multiplier">攻击参数倍率</param>
        /// <returns>实际伤害值</returns>
        public float CalculateDamage(ObjectStatsConfig targetStats, ObjectStatsConfigMultiplier multiplier)
        {
            if (targetStats == null)
            {
                return ObjectStatsConfigMultiplier.Apply(BaseDamage, multiplier.BaseDamageMultiplier);
            }

            switch (DamageType)
            {
                case DamageType.Physical:
                {
                    float effectivePhysAtk = ObjectStatsConfigMultiplier.Apply(PhysicalAttack, multiplier.PhysicalAttackMultiplier);
                    float effectiveArmorPen = ObjectStatsConfigMultiplier.Apply(ArmorPenetration, multiplier.ArmorPenetrationMultiplier);
                    float effectiveDefense = targetStats.PhysicalDefense * (1f - effectiveArmorPen);
                    float effectiveBaseDmg = ObjectStatsConfigMultiplier.Apply(BaseDamage, multiplier.BaseDamageMultiplier);
                    return effectivePhysAtk * (100f / (100f + effectiveDefense)) + effectiveBaseDmg;
                }

                case DamageType.Magic:
                {
                    float effectiveMagicAtk = ObjectStatsConfigMultiplier.Apply(MagicAttack, multiplier.MagicAttackMultiplier);
                    float effectiveMagicPen = ObjectStatsConfigMultiplier.Apply(MagicPenetration, multiplier.MagicPenetrationMultiplier);
                    float effectiveDefense = targetStats.MagicDefense * (1f - effectiveMagicPen);
                    float effectiveBaseDmg = ObjectStatsConfigMultiplier.Apply(BaseDamage, multiplier.BaseDamageMultiplier);
                    return effectiveMagicAtk * (100f / (100f + effectiveDefense)) + effectiveBaseDmg;
                }

                case DamageType.True:
                {
                    float effectiveTrueDmg = ObjectStatsConfigMultiplier.Apply(TrueDamage, multiplier.TrueDamageMultiplier);
                    float effectiveBaseDmg = ObjectStatsConfigMultiplier.Apply(BaseDamage, multiplier.BaseDamageMultiplier);
                    return effectiveTrueDmg + effectiveBaseDmg;
                }

                default:
                    return ObjectStatsConfigMultiplier.Apply(BaseDamage, multiplier.BaseDamageMultiplier);
            }
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
    ///      CastSpeed(施法速度倍率)
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
    ///      BaseDamage(基础伤害值)
    ///      DamageType(伤害类型)
    ///      DamageRadius(范围伤害半径, 米)
    ///      DamageInterval(持续伤害频率, 秒)
    ///      IsContinuousDamage(是否持续伤害)
    ///      DamageDuration(伤害持续时间, 秒)
    ///      CanDealDamage(是否启用伤害功能)
    ///
    /// ════════════════════════════════════════════════════════════
    /// 【核心方法详解】
    /// ════════════════════════════════════════════════════════════
    ///   伤害计算方法：
    ///     CalculatePhysicalDamage(targetDefense) — 物理伤害 = 攻击力 × (100 / (100 + 有效防御))
    ///       有效防御 = 目标防御 × (1 - 护甲穿透)
    ///     CalculateMagicDamage(targetDefense)    — 魔法伤害 = 攻击力 × (100 / (100 + 有效防御))
    ///       有效防御 = 目标防御 × (1 - 魔法穿透)
    ///     CalculateDamage(targetStats)           — 根据 DamageType 自动选择伤害公式
    ///       Physical/Magic 走对应公式，True 直接返回 TrueDamage + BaseDamage
    ///     CalculateDamage(targetStats, multiplier) — 应用 ObjectStatsConfigMultiplier 倍率后计算
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
    ///     BaseDamageMultiplier       — 基础伤害倍率
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
    ///     - CalculateDamage(target, multiplier) 重载自动应用倍率
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
    /// 示例 4：投射物配置（范围持续伤害）
    /// ────────────────────────────────────────────────────────────
    /// <code>
    /// protected override ObjectStatsConfig ObjectStatsConfig =&gt; new ObjectStatsConfig
    /// {
    ///     Type = ObjectType.Projectile,
    ///     FactionID = 100,                  // 中立阵营
    ///     MaxHealth = 1f,                   // 投射物本身只有 1 点生命
    ///     CurrentHealth = 1f,
    ///     MagicAttack = 15f,
    ///     BaseDamage = 10f,
    ///     DamageType = DamageType.Magic,
    ///     DamageRadius = 5f,                // 伤害范围 5 米
    ///     DamageInterval = 0.5f,            // 每 0.5 秒造成一次伤害
    ///     IsContinuousDamage = true,        // 持续伤害
    ///     DamageDuration = 10f,             // 持续 10 秒
    ///     CanDealDamage = true
    /// };
    /// </code>
    ///
    /// ────────────────────────────────────────────────────────────
    /// 示例 5：陷阱配置（单次物理伤害）
    /// ────────────────────────────────────────────────────────────
    /// <code>
    /// protected override ObjectStatsConfig ObjectStatsConfig =&gt; new ObjectStatsConfig
    /// {
    ///     Type = ObjectType.Trap,
    ///     FactionID = 100,
    ///     BaseDamage = 50f,
    ///     DamageType = DamageType.True,     // 真实伤害（无视防御）
    ///     TrueDamage = 50f,
    ///     DamageRadius = 2f,
    ///     IsContinuousDamage = false,       // 单次伤害
    ///     CanDealDamage = true
    /// };
    /// </code>
    ///
    /// ────────────────────────────────────────────────────────────
    /// 示例 6：伤害计算与战斗流程
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
    /// // 2. 闪避判定
    /// if (targetStats.IsEvaded(attackerStats.HitRate))
    /// {
    ///     Debug.Log("攻击被闪避！");
    ///     return;
    /// }
    ///
    /// // 3. 计算基础伤害（考虑防御和穿透）
    /// float damage = attackerStats.CalculateDamage(targetStats);
    ///
    /// // 4. 暴击判定
    /// if (attackerStats.IsCriticalHit())
    /// {
    ///     damage = attackerStats.GetCriticalDamage(damage);
    ///     Debug.Log($"触发暴击！伤害 {damage}");
    /// }
    ///
    /// // 5. 造成伤害
    /// target.TakeDamage(damage);
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