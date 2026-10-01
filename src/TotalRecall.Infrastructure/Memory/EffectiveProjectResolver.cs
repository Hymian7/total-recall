// src/TotalRecall.Infrastructure/Memory/EffectiveProjectResolver.cs
//
// Resolves the "effective project" used by retrieval-side project scoping
// (Mandantentrennung) and by memory_store's scope:"project" mapping.
//
// Resolution runs through ONE folder-map path so both the Kiro IDE (process
// cwd) and KiroCrew (caller-passed [PROJECT] path per call) map a directory to
// a project slug the same way. Precedence:
//
//   1. explicit config/env Project, if non-empty (Retrieval.Project — the env
//      override TOTAL_RECALL_PROJECT is already folded into this value by
//      ConfigLoader) — WINS;
//   2. longest-prefix folder-map match on cwd;
//   3. git auto-detect (ProjectResolver.Resolve(cwd)) — ONLY when
//      autodetect==true (default off);
//   4. null (globals only).
//
// Git auto-detect is UNRELIABLE (a session's cwd is often not the repo, or the
// repo has no useful remote), which is why it is opt-in and last. Pure/fail-soft:
// any resolution failure returns null.

using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.FSharp.Core;

namespace TotalRecall.Infrastructure.Memory;

public sealed class EffectiveProjectResolver
{
    private readonly ProjectResolver _gitResolver;

    public EffectiveProjectResolver(ProjectResolver? gitResolver = null)
    {
        _gitResolver = gitResolver ?? new ProjectResolver();
    }

    /// <summary>
    /// Resolve the effective project. <paramref name="explicitProject"/> is
    /// the config/env override (already env-folded by ConfigLoader); when it
    /// is non-null and non-whitespace it WINS. Otherwise a longest-prefix
    /// folder-map match on <paramref name="cwd"/> is tried, then git
    /// auto-detect (only when <paramref name="autodetect"/> is true), then null.
    /// </summary>
    /// <param name="explicitProject">Config/env project override.</param>
    /// <param name="cwd">Directory to resolve against (process cwd in the IDE,
    /// caller-passed [PROJECT] path in KiroCrew).</param>
    /// <param name="projectMap">Folder map (canonical dir path -> slug); may be
    /// null/empty. Keys must be canonicalized the same way this class does.</param>
    /// <param name="autodetect">When true, fall back to git auto-detect after
    /// the folder map misses.</param>
    public string? Resolve(
        string? explicitProject,
        string cwd,
        IReadOnlyDictionary<string, string>? projectMap,
        bool autodetect)
    {
        if (!string.IsNullOrWhiteSpace(explicitProject))
            return explicitProject!.Trim();

        var mapped = MatchLongestPrefix(cwd, projectMap);
        if (mapped is not null)
            return mapped;

        if (autodetect)
            return _gitResolver.Resolve(cwd);

        return null;
    }

    /// <summary>
    /// Back-compat overload for callers that predate the folder-map feature.
    /// Delegates with no folder map and autodetect ON — preserving the original
    /// "explicit override → git auto-detect → null" behavior exactly.
    /// </summary>
    public string? Resolve(string? explicitProject, string cwd)
        => Resolve(explicitProject, cwd, projectMap: null, autodetect: true);

    /// <summary>
    /// Convenience overload that reads the explicit override out of an
    /// F# <c>string option</c> (as it lives on <c>RetrievalConfig.Project</c>).
    /// Back-compat: no folder map, autodetect ON.
    /// </summary>
    public string? Resolve(FSharpOption<string> explicitProject, string cwd)
    {
        var value = FSharpOption<string>.get_IsSome(explicitProject)
            ? explicitProject.Value
            : null;
        return Resolve(value, cwd, projectMap: null, autodetect: true);
    }

    /// <summary>
    /// Canonicalize a path identically to the ConfigLoader folder-map key
    /// normalization: <c>Path.GetFullPath</c> → separators to '/' → trim a
    /// trailing '/' → lowercase on Windows. Returns null on any error.
    /// </summary>
    internal static string? Canonicalize(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;
        try
        {
            var full = Path.GetFullPath(path);
            full = full.Replace('\\', '/');
            if (full.Length > 1 && full.EndsWith('/'))
                full = full.TrimEnd('/');
            if (OperatingSystem.IsWindows())
                full = full.ToLowerInvariant();
            return full;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Longest path-boundary-prefix match of <paramref name="cwd"/> against the
    /// folder map keys. A key matches only when it is either equal to the
    /// canonical cwd or a prefix followed by a '/' boundary — so
    /// <c>c:/dev/tr</c> does NOT match <c>c:/dev/tr-other</c>. Returns the value
    /// of the longest matching key, or null when nothing matches / on error.
    /// Case-insensitive on Windows, case-sensitive elsewhere.
    /// </summary>
    internal static string? MatchLongestPrefix(string cwd, IReadOnlyDictionary<string, string>? map)
    {
        if (map is null || map.Count == 0)
            return null;
        try
        {
            var canonical = Canonicalize(cwd);
            if (canonical is null)
                return null;

            var cmp = OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;

            string? bestKey = null;
            string? bestValue = null;
            foreach (var kv in map)
            {
                var key = kv.Key;
                if (key.Length == 0)
                    continue;
                if (!canonical.StartsWith(key, cmp))
                    continue;
                // Path-boundary check: equal length, or next char is '/'.
                if (canonical.Length != key.Length && canonical[key.Length] != '/')
                    continue;
                if (bestKey is null || key.Length > bestKey.Length)
                {
                    bestKey = key;
                    bestValue = kv.Value;
                }
            }
            return bestValue;
        }
        catch
        {
            return null;
        }
    }
}
