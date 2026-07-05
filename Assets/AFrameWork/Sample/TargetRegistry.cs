using System.Collections.Generic;
using UnityEngine;
using AFrameWork.Core;

namespace AFrameWork.Sample
{
    /// <summary>
    /// 目标注册表（方案 D）：消除 Fighter.FireBullet 中的 FindObjectOfType&lt;Monster&gt; O(N) 场景扫描。
    /// 供 SimpleObjectPool.Launch 时查找最近敌方目标。
    ///
    /// 用法：
    ///   - Monster/Fighter 等 ObjectBase 在 OnEnable 调用 Register(this)
    ///   - OnDisable 调用 Unregister(this)
    ///   - 发射子弹时调用 FindNearest(position, attackerFactionID) 获取最近敌方
    ///
    /// 设计要点：
    ///   - 静态 List&lt;ObjectBase&gt;，注册/注销为 O(N) 但只在 Enable/Disable 时发生（低频）
    ///   - FindNearest 为 O(N)，但只在发射时调用一次（不在每颗子弹的 Update 中调用）
    ///   - 已销毁的 Unity 对象（fake-null）会被 == null 比较跳过；
    ///     当 null 数量超过阈值时触发 Compact 清理，避免列表膨胀
    /// </summary>
    public static class TargetRegistry
    {
        // 注册表（初始容量 32，按需扩容）
        private static readonly List<ObjectBase> s_targets = new List<ObjectBase>(32);

        // 触发 Compact 的 null 数量阈值
        private const int k_compactNullThreshold = 8;

        /// <summary>当前注册的目标数量（含可能已被销毁但未注销的）</summary>
        public static int Count => s_targets.Count;

        /// <summary>
        /// 注册目标。在 OnEnable 中调用。
        /// 重复注册安全（Contains 检查）。
        /// </summary>
        public static void Register(ObjectBase target)
        {
            if (target == null) return;
            if (!s_targets.Contains(target))
            {
                s_targets.Add(target);
            }
        }

        /// <summary>
        /// 注销目标。在 OnDisable/OnDestroy 中调用。
        /// 注意：即使 target 已被销毁（fake-null），List.Remove 仍能正确匹配移除，
        /// 因为 UnityEngine.Object 重载的 == 使 destroyed == null 成立，
        /// List.Remove 内部的 Equals 比较会匹配到该条目。
        /// </summary>
        public static void Unregister(ObjectBase target)
        {
            s_targets.Remove(target);
        }

        /// <summary>
        /// 查找距离 position 最近的、与 attackerFactionID 敌对的目标。
        /// 跳过：已销毁（fake-null）、无属性、已死亡、同阵营的目标。
        /// 使用 sqrMagnitude 避免开方运算。
        /// </summary>
        /// <returns>最近的敌方 ObjectBase；无则返回 null</returns>
        public static ObjectBase FindNearest(Vector3 position, int attackerFactionID)
        {
            ObjectBase nearest = null;
            float nearestSqr = float.MaxValue;
            int nullCount = 0;

            for (int i = 0; i < s_targets.Count; i++)
            {
                ObjectBase t = s_targets[i];

                // 跳过已销毁对象（Unity fake-null == 检查）
                if (t == null)
                {
                    nullCount++;
                    continue;
                }

                if (!t.HasObjectStats()) continue;

                ObjectStatsConfig stats = t.GetObjectStats();
                if (stats.IsDead()) continue;

                // 跳过同阵营（简化判定：FactionID 相同视为友方）
                // 完整的阵营关系判定留给子弹命中时的 CanDealDamageTo 处理
                if (stats.FactionID == attackerFactionID) continue;

                float sqr = (t.transform.position - position).sqrMagnitude;
                if (sqr < nearestSqr)
                {
                    nearestSqr = sqr;
                    nearest = t;
                }
            }

            // 累积过多已销毁条目时清理列表，避免长期膨胀
            if (nullCount >= k_compactNullThreshold)
            {
                Compact();
            }

            return nearest;
        }

        /// <summary>
        /// 查找距离 position 最近的、与 owner 敌对的目标。
        /// 便捷重载：直接从 owner 的 ObjectStatsConfig 读取阵营 ID。
        /// </summary>
        public static ObjectBase FindNearest(Vector3 position, ObjectBase owner)
        {
            if (owner == null || !owner.HasObjectStats()) return null;
            return FindNearest(position, owner.GetObjectStats().FactionID);
        }

        /// <summary>清理列表中所有已销毁（fake-null）的条目。</summary>
        public static void Compact()
        {
            s_targets.RemoveAll(t => t == null);
        }

        /// <summary>清空注册表（场景切换时调用）。</summary>
        public static void Clear()
        {
            s_targets.Clear();
        }
    }

    /// <summary>
    /// TargetRegistry 使用说明：
    /// ============================================================
    /// 目标注册表（方案 D）：消除 Fighter.FireBullet 中的 FindObjectOfType&lt;Monster&gt; O(N) 场景扫描。
    /// 供 SimpleObjectPool.Launch 时查找最近敌方目标。
    ///
    /// ════════════════════════════════════════════════════════════
    /// 【设计要点】
    /// ════════════════════════════════════════════════════════════
    ///   - 纯静态类，无需挂载到场景对象
    ///   - 静态 List&lt;ObjectBase&gt;，注册/注销为 O(N) 但只在 Enable/Disable 时发生（低频）
    ///   - FindNearest 为 O(N)，但只在发射时调用一次（不在每颗子弹的 Update 中调用）
    ///   - 已销毁的 Unity 对象（fake-null）会被 == null 比较跳过
    ///   - 当 null 数量超过阈值时触发 Compact 清理，避免列表膨胀
    ///
    /// ════════════════════════════════════════════════════════════
    /// 【字段详解】
    /// ════════════════════════════════════════════════════════════
    ///   s_targets : List&lt;ObjectBase&gt;
    ///     - 注册表，初始容量 32，按需扩容
    ///     - 存储 ObjectBase 引用（角色、怪物等可被攻击的目标）
    ///
    ///   k_compactNullThreshold = 8
    ///     - 触发 Compact 的 null 数量阈值
    ///     - FindNearest 中累计 null 数量，超过此值时清理列表
    ///     - 避免每次 FindNearest 都清理（O(N) 开销），同时防止列表长期膨胀
    ///
    /// ════════════════════════════════════════════════════════════
    /// 【API 详解】
    /// ════════════════════════════════════════════════════════════
    ///   Register(target)：
    ///     - 注册目标，在 OnEnable 中调用
    ///     - 重复注册安全（Contains 检查）
    ///     - target 为 null 时直接返回
    ///
    ///   Unregister(target)：
    ///     - 注销目标，在 OnDisable/OnDestroy 中调用
    ///     - 即使 target 已被销毁（fake-null），List.Remove 仍能正确移除
    ///       原因：UnityEngine.Object 重载的 == 使 destroyed == null 成立，
    ///             List.Remove 内部的 Equals 比较会匹配到该条目
    ///
    ///   FindNearest(position, attackerFactionID)：
    ///     - 查找距离 position 最近的、与 attackerFactionID 敌对的目标
    ///     - 跳过：已销毁（fake-null）、无属性、已死亡、同阵营的目标
    ///     - 使用 sqrMagnitude 避免开方运算
    ///     - 返回最近的敌方 ObjectBase；无则返回 null
    ///     - 累计 null 数量超过阈值时触发 Compact
    ///
    ///   FindNearest(position, owner)：
    ///     - 便捷重载：直接从 owner 的 ObjectStatsConfig 读取阵营 ID
    ///     - owner 为 null 或无属性时返回 null
    ///
    ///   Compact()：
    ///     - 清理列表中所有已销毁（fake-null）的条目
    ///     - 使用 RemoveAll(t =&gt; t == null) 一次性清理
    ///     - 场景切换或定期清理时调用
    ///
    ///   Clear()：
    ///     - 清空注册表（场景切换时调用）
    ///     - 与 Compact 的区别：Clear 清空所有条目，Compact 仅清理已销毁的
    ///
    ///   Count：当前注册的目标数量（含可能已被销毁但未注销的）
    ///
    /// ════════════════════════════════════════════════════════════
    /// 【fake-null 处理机制】
    /// ════════════════════════════════════════════════════════════
    ///   Unity 对象销毁后不会立即从内存移除，而是变为 fake-null 状态：
    ///     - 对象的 C++ 引用已被销毁
    ///     - C# 包装类仍存在，但 == null 返回 true
    ///     - GetHashCode() 仍可用（基于 InstanceID）
    ///
    ///   TargetRegistry 的处理策略：
    ///     - FindNearest 中用 t == null 跳过已销毁条目
    ///     - Unregister 中 List.Remove 仍能匹配 fake-null 条目（依赖 == 重载）
    ///     - 累计 null 超过阈值时 Compact 清理
    ///     - 注意：不要用 ReferenceEquals 检查 Unity 对象，会绕过 == 重载
    ///
    /// ════════════════════════════════════════════════════════════
    /// 【阵营判定简化】
    /// ════════════════════════════════════════════════════════════
    ///   FindNearest 中使用简化判定：FactionID 相同视为友方
    ///   完整的阵营关系判定（TeamID/GuildID/AllianceID/PVPMode）留给子弹命中时的
    ///   CanDealDamageTo 处理，避免在查找阶段执行复杂逻辑
    ///
    ///   设计原因：
    ///     - 查找阶段只需排除明显友方（同阵营）
    ///     - 命中时的完整判定可处理边界情况（如 Open PVP 模式下同阵营可攻击）
    ///     - 简化查找逻辑提升性能
    ///
    /// ════════════════════════════════════════════════════════════
    /// 【性能考虑】
    /// ════════════════════════════════════════════════════════════
    ///   - Register/Unregister 为 O(N)（Contains/Remove），但只在 Enable/Disable 时发生
    ///   - FindNearest 为 O(N)，但只在发射时调用一次（不在每颗子弹的 Tick 中调用）
    ///   - 使用 sqrMagnitude 避免开方
    ///   - Compact 阈值避免每次 FindNearest 都清理
    ///   - 静态列表在场景切换时需手动 Clear，避免跨场景残留
    ///
    /// ────────────────────────────────────────────────────────────
    /// 示例 1：在 ObjectBase 子类中注册
    /// ────────────────────────────────────────────────────────────
    /// <code>
    /// public class Monster : ObjectBase
    /// {
    ///     private void OnEnable()
    ///     {
    ///         TargetRegistry.Register(this);
    ///     }
    ///
    ///     private void OnDisable()
    ///     {
    ///         TargetRegistry.Unregister(this);
    ///     }
    /// }
    /// </code>
    ///
    /// ────────────────────────────────────────────────────────────
    /// 示例 2：发射时查找最近敌方
    /// ────────────────────────────────────────────────────────────
    /// <code>
    /// public class Fighter : ObjectBase
    /// {
    ///     private void FireBullet()
    ///     {
    ///         // 查找最近敌方
    ///         ObjectBase target = TargetRegistry.FindNearest(transform.position, this);
    ///         if (target == null) return;
    ///
    ///         Vector3 dir = (target.transform.position - transform.position).normalized;
    ///         SimpleObjectPool.Instance.Launch&lt;Bullet&gt;(
    ///             position: transform.position,
    ///             direction: dir,
    ///             owner: this,
    ///             damage: GetObjectStats().PhysicalAttack
    ///         );
    ///     }
    /// }
    /// </code>
    ///
    /// ────────────────────────────────────────────────────────────
    /// 示例 3：使用阵营 ID 查找
    /// ────────────────────────────────────────────────────────────
    /// <code>
    /// // 直接指定阵营 ID 查找（无需 owner 对象）
    /// int attackerFactionID = 1;  // 玩家阵营
    /// ObjectBase target = TargetRegistry.FindNearest(position, attackerFactionID);
    /// </code>
    ///
    /// ────────────────────────────────────────────────────────────
    /// 示例 4：场景切换时清理
    /// ────────────────────────────────────────────────────────────
    /// <code>
    /// public class SceneManager : MonoBehaviour
    /// {
    ///     private void OnSceneUnloaded(Scene scene)
    ///     {
    ///         // 清空注册表，避免跨场景残留
    ///         TargetRegistry.Clear();
    ///     }
    /// }
    /// </code>
    ///
    /// ────────────────────────────────────────────────────────────
    /// 示例 5：手动清理已销毁条目
    /// ────────────────────────────────────────────────────────────
    /// <code>
    /// // 定期清理（如每 10 秒）
    /// TargetRegistry.Compact();
    ///
    /// // 查看当前注册数量
    /// int count = TargetRegistry.Count;
    /// </code>
    /// </summary>
}
