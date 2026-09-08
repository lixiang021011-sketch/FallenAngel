using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Security.Cryptography;
using FallenAngel.Data;
using UnityEngine;

namespace FallenAngel.Core
{
    /// <summary>
    /// 每档独立JSON；写完整临时文件后原子替换，并保留上版备份。
    /// 损坏时停止读取，不静默回滚，以免重复发奖。此实现面向当前桌面Demo。
    /// </summary>
    public sealed class PortfolioProfileStore : IPortfolioProfileStore
    {
        private readonly string directory;

        public PortfolioProfileStore(string directory)
        {
            this.directory = Path.GetFullPath(directory ?? throw new ArgumentNullException(nameof(directory)));
        }

        /// <summary>正式存档目录独立于现有主游戏配置，不修改PlayerPrefs。</summary>
        public static PortfolioProfileStore CreateDefault() => new PortfolioProfileStore(
            Path.Combine(Application.persistentDataPath, "Portfolio", "profiles_v1"));

        private string ProfilePath(string id)
        {
            if (!Guid.TryParseExact(id, "N", out _)) throw new ArgumentException("Invalid profile ID.");
            return Path.Combine(directory, id + ".json");
        }

        /// <summary>读取指定档案，文件不存在或解析失败将显式抛出错误。</summary>
        public PortfolioProfileData Load(string profileId)
        {
            string raw = File.ReadAllText(ProfilePath(profileId), Encoding.UTF8);
            // 包裹记录使缺失整个payload不能被JsonUtility默认字段掩盖。
            var envelope = JsonUtility.FromJson<ProfileEnvelope>(raw);
            if (envelope == null || (envelope.format != "FallenAngel.Portfolio.Profile.v1" && envelope.format != "FallenAngel.Portfolio.Profile.v2") || envelope.profile == null)
                throw new InvalidDataException("Invalid portfolio profile envelope.");
            var profile = envelope.profile;
            string checksum = envelope.format == "FallenAngel.Portfolio.Profile.v1"
                ? Hash(JsonUtility.ToJson(JsonUtility.FromJson<LegacyEnvelope>(raw).profile)) : Checksum(profile);
            if (envelope.format == "FallenAngel.Portfolio.Profile.v1" && !string.IsNullOrEmpty(profile.growthRunJson))
                throw new InvalidDataException("Unexpected run data in legacy profile.");
            if (string.IsNullOrEmpty(envelope.checksum) || envelope.checksum != checksum)
                throw new InvalidDataException("Portfolio profile checksum mismatch; manual recovery required.");
            if (profile.profileId != profileId) throw new InvalidDataException("Profile ID mismatch.");
            new PortfolioTalentService(this).ValidateProfile(profile);
            if (!string.IsNullOrEmpty(profile.growthRunJson)) new PortfolioGrowthService(this).ReadRun(profile);
            return profile;
        }

        /// <summary>提交完整副本。磁盘版本不匹配时拒绝覆盖，保存异常不产生内存中的虚假成功。</summary>
        public void Commit(PortfolioProfileData profile, int expectedRevision)
        {
            if (profile == null) throw new ArgumentNullException(nameof(profile));
            new PortfolioTalentService(this).ValidateProfile(profile);
            if (!string.IsNullOrEmpty(profile.growthRunJson)) new PortfolioGrowthService(this).ReadRun(profile);
            if (profile.revision != checked(expectedRevision + 1)) throw new InvalidOperationException("Invalid next revision.");
            string target = ProfilePath(profile.profileId);
            Directory.CreateDirectory(directory);
            // 多个窗口/实例不得同时读旧版本再覆盖。锁失败显式报错，可重新读取后重试。
            using (new FileStream(target + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
            {
                int diskRevision = File.Exists(target) ? Load(profile.profileId).revision : -1;
                if (diskRevision != expectedRevision) throw new InvalidOperationException("Profile changed; reload before confirming.");
                string temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try
                {
                    byte[] bytes = Encoding.UTF8.GetBytes(JsonUtility.ToJson(new ProfileEnvelope
                        { profile = profile, checksum = Checksum(profile) }, true));
                    using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                        4096, FileOptions.WriteThrough))
                    {
                        stream.Write(bytes, 0, bytes.Length);
                        stream.Flush(true);
                    }
                    if (File.Exists(target)) File.Replace(temporary, target, target + ".bak");
                    else File.Move(temporary, target);
                }
                finally
                {
                    if (File.Exists(temporary)) File.Delete(temporary);
                }
            }
            Debug.Log("[PortfolioProfileStore] Saved profile " + profile.profileId + " revision " + profile.revision);
        }

        /// <summary>按ID列出存档；损坏文件不被自动丢弃或覆盖。</summary>
        public IReadOnlyList<string> ListProfileIds()
        {
            if (!Directory.Exists(directory)) return Array.Empty<string>();
            return Directory.GetFiles(directory, "*.json").Select(Path.GetFileNameWithoutExtension)
                .Where(id => Guid.TryParseExact(id, "N", out _)).OrderBy(id => id, StringComparer.Ordinal).ToList().AsReadOnly();
        }

        /// <summary>删除指定档案的全部文件残留；不存在时静默。仅用于显式重建（如损坏档经用户确认后重置），不提供UI删除功能。</summary>
        public void Delete(string profileId)
        {
            string target = ProfilePath(profileId);
            // 与 Commit 使用同一把锁，避免与并发提交竞争。
            using (new FileStream(target + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
            {
                if (File.Exists(target)) File.Delete(target);
                if (File.Exists(target + ".bak")) File.Delete(target + ".bak");
                if (Directory.Exists(directory))
                {
                    // 清理 {id}.json.{guid}.tmp 等临时残留（File.Delete 对不存在文件静默）。
                    foreach (string leftover in Directory.GetFiles(directory, profileId + ".*"))
                        File.Delete(leftover);
                }
            }
            Debug.Log("[PortfolioProfileStore] Deleted profile " + profileId);
        }

        [Serializable]
        private sealed class ProfileEnvelope
        {
            public string format = "FallenAngel.Portfolio.Profile.v2";
            public PortfolioProfileData profile;
            public string checksum;
        }

        private static string Checksum(PortfolioProfileData profile)
            => Hash(JsonUtility.ToJson(profile));

        private static string Hash(string json)
        {
            using (var hash = SHA256.Create())
                return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(json)))
                    .Replace("-", "").ToLowerInvariant();
        }

        [Serializable] private sealed class LegacyEnvelope { public LegacyProfile profile; }
        [Serializable] private sealed class LegacyProfile
        {
            public int version;
            public string profileId;
            public string displayName;
            public int revision;
            public int growthPoints;
            public List<string> unlockedNodeIds;
            public string activeRunId;
        }
    }
}
