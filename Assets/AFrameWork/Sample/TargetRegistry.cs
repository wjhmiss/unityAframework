using System.Collections.Generic;
using UnityEngine;
using AFrameWork.Core;

namespace AFrameWork.Sample
{
    /// <summary>
    /// 目标注册表（方案 D）：消除 Fighter.FireBullet 中的 FindObjectOfType&lt;Monster&gt; O(N) 场景扫描。
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
}
