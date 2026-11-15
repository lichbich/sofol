using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Sofol.Core.Models;

namespace Sofol.Core.Services;

public sealed class SmartGroupMatch
{
    public string GroupFolderName { get; init; } = string.Empty;

    public string MatchDescription { get; init; } = string.Empty;

    public int GroupMemberCount { get; init; }
}

/// <summary>
/// Phân tích và gom nhóm các tệp có chung tiền tố gốc hoặc có độ tương đồng cao về tên tệp.
/// Ví dụ: "Book_Exported.docx", "Book_Exported (1).docx", "Book_Exported (2).docx" -> cụm "Book_Exported".
/// </summary>
public sealed partial class SmartNameGroupingService
{
    [GeneratedRegex(@"(\s*\(\d+\)|\s*\[\d+\]|\s*-\s*Copy(\s*\(\d+\))?|\s+copy(\s+\d+)?|_copy(\d+)?|_v\d+(\.\d+)?|\s+v\d+(\.\d+)?)$", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex CopyOrVersionSuffixRegex();

    [GeneratedRegex(@"[\s_\-\.]+", RegexOptions.Compiled)]
    private static partial Regex SeparatorNormalizeRegex();

    public Dictionary<string, SmartGroupMatch> DetectSmartGroups(
        IReadOnlyList<FileInfo> safeFiles,
        SmartGroupingSettings settings)
    {
        var result = new Dictionary<string, SmartGroupMatch>(StringComparer.OrdinalIgnoreCase);

        if (!settings.EnablePrefixSimilarityGrouping || safeFiles.Count < settings.MinFilesForPrefixGroup)
        {
            return result;
        }

        int minGroupSize = Math.Max(2, settings.MinFilesForPrefixGroup);
        double threshold = Math.Clamp(settings.SimilarityThreshold, 0.50, 0.99);

        // Bước 1: Gom theo phần gốc tên tệp đã chuẩn hóa hậu tố (ví dụ Book_Exported, Book_Exported (1), Book_Exported (2))
        var stemBuckets = new Dictionary<string, List<FileInfo>>(StringComparer.OrdinalIgnoreCase);
        var ungroupedAfterStem = new List<(FileInfo File, string BaseStem, string NormalizedStem)>();

        foreach (FileInfo file in safeFiles)
        {
            string baseStem = ExtractBaseStem(Path.GetFileNameWithoutExtension(file.Name));
            if (string.IsNullOrWhiteSpace(baseStem))
            {
                continue;
            }

            if (!stemBuckets.TryGetValue(baseStem, out var bucket))
            {
                bucket = [];
                stemBuckets[baseStem] = bucket;
            }
            bucket.Add(file);
        }

        foreach ((string baseStem, List<FileInfo> bucket) in stemBuckets)
        {
            if (bucket.Count >= minGroupSize)
            {
                string folderName = SanitizeFolderName(baseStem);
                foreach (FileInfo file in bucket)
                {
                    result[file.FullName] = new SmartGroupMatch
                    {
                        GroupFolderName = folderName,
                        MatchDescription = $"Cụm tên gốc ({bucket.Count} tệp)",
                        GroupMemberCount = bucket.Count
                    };
                }
            }
            else
            {
                FileInfo single = bucket[0];
                string normalized = NormalizeForSimilarity(baseStem);
                if (normalized.Length >= 5)
                {
                    ungroupedAfterStem.Add((single, baseStem, normalized));
                }
            }
        }

        // Bước 2: Gom các tệp còn lại dựa trên độ tương đồng chuỗi (Levenshtein / tiền tố token chung)
        int count = ungroupedAfterStem.Count;
        if (count < minGroupSize)
        {
            return result;
        }

        var parent = Enumerable.Range(0, count).ToArray();

        int Find(int x)
        {
            while (parent[x] != x)
            {
                parent[x] = parent[parent[x]];
                x = parent[x];
            }
            return x;
        }

        void Union(int a, int b)
        {
            int ra = Find(a);
            int rb = Find(b);
            if (ra != rb)
            {
                parent[rb] = ra;
            }
        }

        for (int i = 0; i < count; i++)
        {
            for (int j = i + 1; j < count; j++)
            {
                var itemA = ungroupedAfterStem[i];
                var itemB = ungroupedAfterStem[j];

                double sim = ComputeNameSimilarity(itemA.NormalizedStem, itemB.NormalizedStem);
                if (sim >= threshold)
                {
                    Union(i, j);
                }
            }
        }

        var clusters = new Dictionary<int, List<(FileInfo File, string BaseStem, string NormalizedStem)>>();
        for (int i = 0; i < count; i++)
        {
            int root = Find(i);
            if (!clusters.TryGetValue(root, out var list))
            {
                list = [];
                clusters[root] = list;
            }
            list.Add(ungroupedAfterStem[i]);
        }

        foreach (var cluster in clusters.Values)
        {
            if (cluster.Count < minGroupSize)
            {
                continue;
            }

            string commonGroupName = DeriveCommonGroupFolderName(cluster.Select(c => c.BaseStem).ToList());
            if (string.IsNullOrWhiteSpace(commonGroupName) || commonGroupName.Length < 3)
            {
                continue;
            }

            foreach (var member in cluster)
            {
                result[member.File.FullName] = new SmartGroupMatch
                {
                    GroupFolderName = commonGroupName,
                    MatchDescription = $"Tên tương đồng ({cluster.Count} tệp)",
                    GroupMemberCount = cluster.Count
                };
            }
        }

        return result;
    }

    public static string ExtractBaseStem(string fileNameWithoutExtension)
    {
        if (string.IsNullOrWhiteSpace(fileNameWithoutExtension))
        {
            return string.Empty;
        }

        string current = fileNameWithoutExtension.Trim();
        // Lặp tối đa 3 lần để xử lý trường hợp chồng hậu tố như "Report - Copy (1)"
        for (int i = 0; i < 3; i++)
        {
            string stripped = CopyOrVersionSuffixRegex().Replace(current, string.Empty).Trim();
            if (stripped == current || string.IsNullOrWhiteSpace(stripped))
            {
                break;
            }
            current = stripped;
        }

        return current;
    }

    public static string NormalizeForSimilarity(string value)
    {
        string lower = value.Trim().ToLowerInvariant();
        return SeparatorNormalizeRegex().Replace(lower, " ").Trim();
    }

    public static double ComputeNameSimilarity(string normA, string normB)
    {
        if (string.IsNullOrEmpty(normA) || string.IsNullOrEmpty(normB))
        {
            return 0;
        }

        if (string.Equals(normA, normB, StringComparison.Ordinal))
        {
            return 1.0;
        }

        // Tránh so sánh hai chuỗi chênh lệch độ dài quá lớn
        int maxLen = Math.Max(normA.Length, normB.Length);
        int minLen = Math.Min(normA.Length, normB.Length);
        if ((double)minLen / maxLen < 0.50)
        {
            return 0;
        }

        int distance = ComputeLevenshteinDistance(normA, normB);
        double levenshteinSim = 1.0 - (double)distance / maxLen;

        // Kiểm tra thêm tiền tố token chung (ví dụ "ProjectA_Spec_Final" và "ProjectA_Spec_Draft")
        string[] tokensA = normA.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        string[] tokensB = normB.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        int commonTokens = 0;
        int minTokens = Math.Min(tokensA.Length, tokensB.Length);
        for (int i = 0; i < minTokens; i++)
        {
            if (string.Equals(tokensA[i], tokensB[i], StringComparison.Ordinal))
            {
                commonTokens++;
            }
            else
            {
                break;
            }
        }

        double tokenPrefixRatio = minTokens >= 2 && commonTokens >= 2
            ? (double)commonTokens / Math.Max(tokensA.Length, tokensB.Length)
            : 0;

        return Math.Max(levenshteinSim, tokenPrefixRatio);
    }

    private static int ComputeLevenshteinDistance(string s, string t)
    {
        int n = s.Length;
        int m = t.Length;
        var prev = new int[m + 1];
        var curr = new int[m + 1];

        for (int j = 0; j <= m; j++)
        {
            prev[j] = j;
        }

        for (int i = 1; i <= n; i++)
        {
            curr[0] = i;
            for (int j = 1; j <= m; j++)
            {
                int cost = s[i - 1] == t[j - 1] ? 0 : 1;
                curr[j] = Math.Min(
                    Math.Min(curr[j - 1] + 1, prev[j] + 1),
                    prev[j - 1] + cost);
            }
            (prev, curr) = (curr, prev);
        }

        return prev[m];
    }

    private static string DeriveCommonGroupFolderName(IReadOnlyList<string> stems)
    {
        if (stems.Count == 0)
        {
            return string.Empty;
        }

        string prefix = stems[0];
        for (int i = 1; i < stems.Count; i++)
        {
            prefix = CommonPrefix(prefix, stems[i]);
            if (prefix.Length == 0)
            {
                break;
            }
        }

        prefix = prefix.Trim().TrimEnd('_', '-', '.', ' ');
        if (prefix.Length >= 4)
        {
            return SanitizeFolderName(prefix);
        }

        return SanitizeFolderName(stems[0]);
    }

    private static string CommonPrefix(string a, string b)
    {
        int len = Math.Min(a.Length, b.Length);
        int i = 0;
        while (i < len && char.ToLowerInvariant(a[i]) == char.ToLowerInvariant(b[i]))
        {
            i++;
        }
        return a[..i];
    }

    public static string SanitizeFolderName(string raw)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(raw.Length);
        foreach (char c in raw)
        {
            sb.Append(Array.IndexOf(invalid, c) >= 0 ? '_' : c);
        }

        string sanitized = sb.ToString().Trim().TrimEnd('.', ' ');
        if (sanitized.Length > 64)
        {
            sanitized = sanitized[..64].TrimEnd('.', ' ', '_', '-');
        }

        return string.IsNullOrWhiteSpace(sanitized) ? "Nhóm_Tệp" : sanitized;
    }
}
