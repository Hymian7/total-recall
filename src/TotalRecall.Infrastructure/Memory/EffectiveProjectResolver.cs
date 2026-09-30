// src/TotalRecall.Infrastructure/Memory/EffectiveProjectResolver.cs
//
// Resolves the "effective project" used by retrieval-side project scoping
// (Mandantentrennung) and by memory_store's scope:"project" mapping.
//
// Git auto-detect is UNRELIABLE (a session's cwd is often not the repo, or
// the repo has no useful remote), so it is a FALLBACK only: an explicit
// config/env Project always WINS. Precedence:
//
//   1. explicit config/env Project, if non-empty (Retrieval.Project — the env
//      override TOTAL_RECALL_PROJECT is already folded into this value by
//      ConfigLoader);
//   2. ProjectResolver.Resolve(cwd) — the git remote slug / repo folder;
//   3. null (globals only).
//
// Pure/fail-soft: any resolution failure downstream is ProjectResolver's own
// concern (it returns null on error). This class only orders the sources.

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
    /// is non-null and non-whitespace it WINS. Otherwise falls back to git
    /// auto-detect from <paramref name="cwd"/>, then to null.
    /// </summary>
    public string? Resolve(string? explicitProject, string cwd)
    {
        if (!string.IsNullOrWhiteSpace(explicitProject))
            return explicitProject!.Trim();
        return _gitResolver.Resolve(cwd);
    }

    /// <summary>
    /// Convenience overload that reads the explicit override out of an
    /// F# <c>string option</c> (as it lives on <c>RetrievalConfig.Project</c>).
    /// </summary>
    public string? Resolve(FSharpOption<string> explicitProject, string cwd)
    {
        var value = FSharpOption<string>.get_IsSome(explicitProject)
            ? explicitProject.Value
            : null;
        return Resolve(value, cwd);
    }
}
