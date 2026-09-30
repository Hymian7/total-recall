// tests/TotalRecall.Infrastructure.Tests/Memory/EffectiveProjectResolverTests.cs
//
// Unit tests for EffectiveProjectResolver precedence (Mandantentrennung):
//   1. explicit config/env Project wins;
//   2. git auto-detect fallback;
//   3. null.

using System;
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
}
