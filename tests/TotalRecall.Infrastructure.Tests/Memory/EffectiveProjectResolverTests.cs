// tests/TotalRecall.Infrastructure.Tests/Memory/EffectiveProjectResolverTests.cs
//
// Unit tests for EffectiveProjectResolver precedence (Mandantentrennung):
//   1. explicit config/env Project wins;
//   2. git auto-detect fallback;
//   3. null.

using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.FSharp.Core;
using TotalRecall.Infrastructure.Memory;
using Xunit;

namespace TotalRecall.Infrastructure.Tests.Memory;

public sealed class EffectiveProjectResolverTests : IDisposable
{
    private readonly string _tempRoot;

    public EffectiveProjectResolverTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "tr-eff-proj-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempRoot, recursive: true); } catch { }
    }

    private string MakeRepo(string name, string remoteUrl)
    {
        var repo = Path.Combine(_tempRoot, name);
        var git = Path.Combine(repo, ".git");
        Directory.CreateDirectory(git);
        var config = $"[core]\n\trepositoryformatversion = 0\n[remote \"origin\"]\n\turl = {remoteUrl}\n";
        File.WriteAllText(Path.Combine(git, "config"), config);
        return repo;
    }

    [Fact]
    public void Explicit_override_wins_over_git_autodetect()
    {
        // A repo whose git remote resolves to "o/r", but an explicit override
        // is supplied — the override must win.
        var repo = MakeRepo("repo-a", "https://github.com/o/r.git");
        var sut = new EffectiveProjectResolver(new ProjectResolver());

        var result = sut.Resolve("acme/explicit", repo);

        Assert.Equal("acme/explicit", result);
    }

    [Fact]
    public void Falls_back_to_git_autodetect_when_no_explicit_override()
    {
        var repo = MakeRepo("repo-b", "https://github.com/o/r.git");
        var sut = new EffectiveProjectResolver(new ProjectResolver());

        var result = sut.Resolve(explicitProject: (string?)null, cwd: repo);

        Assert.Equal("o/r", result);
    }

    [Fact]
    public void Returns_null_when_no_override_and_no_git()
    {
        var plainDir = Path.Combine(_tempRoot, "not-a-repo");
        Directory.CreateDirectory(plainDir);
        var sut = new EffectiveProjectResolver(new ProjectResolver());

        var result = sut.Resolve(explicitProject: (string?)null, cwd: plainDir);

        Assert.Null(result);
    }

    [Fact]
    public void Whitespace_override_is_treated_as_absent()
    {
        var repo = MakeRepo("repo-c", "https://github.com/o/r.git");
        var sut = new EffectiveProjectResolver(new ProjectResolver());

        var result = sut.Resolve("   ", repo);

        // Blank override → falls back to git auto-detect.
        Assert.Equal("o/r", result);
    }

    [Fact]
    public void Override_is_trimmed()
    {
        var plainDir = Path.Combine(_tempRoot, "plain2");
        Directory.CreateDirectory(plainDir);
        var sut = new EffectiveProjectResolver(new ProjectResolver());

        var result = sut.Resolve("  acme/x  ", plainDir);

        Assert.Equal("acme/x", result);
    }

    [Fact]
    public void FSharpOption_overload_resolves_Some_and_None()
    {
        var repo = MakeRepo("repo-d", "https://github.com/o/r.git");
        var sut = new EffectiveProjectResolver(new ProjectResolver());

        // Some(explicit) wins.
        Assert.Equal("acme/y",
            sut.Resolve(FSharpOption<string>.Some("acme/y"), repo));

        // None → git fallback.
        Assert.Equal("o/r",
            sut.Resolve(FSharpOption<string>.None, repo));
    }

    // --- folder-map + autodetect precedence ---------------------------------

    // Build a folder map with keys canonicalized exactly like the resolver +
    // ConfigLoader do (GetFullPath → '/' → trim → lowercase on Windows).
    private static Dictionary<string, string> Map(params (string dir, string slug)[] entries)
    {
        var d = new Dictionary<string, string>();
        foreach (var (dir, slug) in entries)
            d[EffectiveProjectResolver.Canonicalize(dir)!] = slug;
        return d;
    }

    [Fact]
    public void FolderMap_exact_directory_match_returns_slug()
    {
        var dir = Path.Combine(_tempRoot, "proj-x");
        Directory.CreateDirectory(dir);
        var sut = new EffectiveProjectResolver(new ProjectResolver());
        var map = Map((dir, "acme/x"));

        var result = sut.Resolve(explicitProject: null, cwd: dir, projectMap: map, autodetect: false);

        Assert.Equal("acme/x", result);
    }

    [Fact]
    public void FolderMap_matches_nested_subdirectory()
    {
        var root = Path.Combine(_tempRoot, "proj-y");
        var nested = Path.Combine(root, "src", "deep");
        Directory.CreateDirectory(nested);
        var sut = new EffectiveProjectResolver(new ProjectResolver());
        var map = Map((root, "acme/y"));

        var result = sut.Resolve(null, nested, map, autodetect: false);

        Assert.Equal("acme/y", result);
    }

    [Fact]
    public void FolderMap_longest_of_two_prefixes_wins()
    {
        var outer = Path.Combine(_tempRoot, "mono");
        var inner = Path.Combine(outer, "packages", "widgets");
        Directory.CreateDirectory(inner);
        var sut = new EffectiveProjectResolver(new ProjectResolver());
        var map = Map((outer, "acme/mono"), (inner, "acme/widgets"));

        var result = sut.Resolve(null, inner, map, autodetect: false);

        Assert.Equal("acme/widgets", result);
    }

    [Fact]
    public void FolderMap_boundary_non_match_tr_vs_tr_other()
    {
        // c:/dev/tr must NOT match c:/dev/tr-other — path-boundary prefix only.
        var trOther = Path.Combine(_tempRoot, "tr-other");
        Directory.CreateDirectory(trOther);
        var sut = new EffectiveProjectResolver(new ProjectResolver());
        var map = Map((Path.Combine(_tempRoot, "tr"), "acme/tr"));

        var result = sut.Resolve(null, trOther, map, autodetect: false);

        Assert.Null(result);
    }

    [Fact]
    public void FolderMap_no_match_returns_null()
    {
        var dir = Path.Combine(_tempRoot, "unmapped");
        Directory.CreateDirectory(dir);
        var sut = new EffectiveProjectResolver(new ProjectResolver());
        var map = Map((Path.Combine(_tempRoot, "somewhere-else"), "acme/z"));

        var result = sut.Resolve(null, dir, map, autodetect: false);

        Assert.Null(result);
    }

    [Fact]
    public void FolderMap_is_case_insensitive_on_windows()
    {
        if (!OperatingSystem.IsWindows())
            return; // case-insensitivity is Windows-only behavior

        var dir = Path.Combine(_tempRoot, "CaseProj");
        Directory.CreateDirectory(dir);
        var sut = new EffectiveProjectResolver(new ProjectResolver());
        // Map key canonicalized (lowercased on Windows). Resolve against an
        // upper-cased cwd; must still match.
        var map = Map((dir.ToUpperInvariant(), "acme/case"));

        var result = sut.Resolve(null, dir.ToLowerInvariant(), map, autodetect: false);

        Assert.Equal("acme/case", result);
    }

    [Fact]
    public void Explicit_override_wins_over_folder_map()
    {
        var dir = Path.Combine(_tempRoot, "mapped");
        Directory.CreateDirectory(dir);
        var sut = new EffectiveProjectResolver(new ProjectResolver());
        var map = Map((dir, "acme/mapped"));

        var result = sut.Resolve("acme/explicit", dir, map, autodetect: false);

        Assert.Equal("acme/explicit", result);
    }

    [Fact]
    public void FolderMap_wins_over_git_autodetect()
    {
        // A real git repo whose remote resolves to "o/r", but its dir is in the
        // folder map — the map must win over autodetect.
        var repo = MakeRepo("mapped-repo", "https://github.com/o/r.git");
        var sut = new EffectiveProjectResolver(new ProjectResolver());
        var map = Map((repo, "acme/from-map"));

        var result = sut.Resolve(null, repo, map, autodetect: true);

        Assert.Equal("acme/from-map", result);
    }

    [Fact]
    public void Autodetect_false_and_unmapped_returns_null()
    {
        // A git repo, but autodetect is OFF and no folder-map entry → null.
        var repo = MakeRepo("unmapped-repo", "https://github.com/o/r.git");
        var sut = new EffectiveProjectResolver(new ProjectResolver());

        var result = sut.Resolve(null, repo, projectMap: null, autodetect: false);

        Assert.Null(result);
    }

    [Fact]
    public void Autodetect_true_and_unmapped_falls_back_to_git()
    {
        var repo = MakeRepo("git-fallback-repo", "https://github.com/o/r.git");
        var sut = new EffectiveProjectResolver(new ProjectResolver());

        var result = sut.Resolve(null, repo, projectMap: null, autodetect: true);

        Assert.Equal("o/r", result);
    }
}
