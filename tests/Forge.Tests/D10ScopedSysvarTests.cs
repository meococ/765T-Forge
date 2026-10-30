using Forge.Shared;

namespace Forge.Tests;

/// <summary>
/// smoke r2 D10: loading a foreign database (doc_import, xref reload, dgn_import, ...) raises
/// AutoCAD's modal Proxy Information dialog, wedging every later pipe call. The load runs under
/// a scoped PROXYNOTICE=0 that always restores the read-back value — also on exceptions.
/// </summary>
public sealed class D10ScopedSysvarTests
{
    /// <summary>A fake AutoCAD sysvar store: name -> current value.</summary>
    private sealed class FakeSysvars
    {
        private readonly Dictionary<string, object?> _values = new(StringComparer.OrdinalIgnoreCase);
        public readonly List<(string Name, object? Value)> Writes = [];

        public object? Get(string name) => _values.TryGetValue(name, out var v) ? v : null;
        public void Set(string name, object? value)
        {
            Writes.Add((name, value));
            _values[name] = value;
        }

        public object? this[string name]
        {
            get => Get(name);
            set => _values[name] = value;
        }
    }

    private static ScopedSysvar Scope(FakeSysvars vars, int value = 0, string name = "PROXYNOTICE")
        => ScopedSysvar.Set(name, value, vars.Get, vars.Set);

    [Fact]
    public void SetOverridesAndDisposeRestoresTheReadBackValue()
    {
        var vars = new FakeSysvars { ["PROXYNOTICE"] = 1 };
        using (var scope = Scope(vars))
        {
            Assert.True(scope.Applied);
            Assert.Equal(1, scope.Original);
            Assert.Equal(0, vars["PROXYNOTICE"]);
            Assert.False(scope.Restored);
        }

        Assert.Equal(1, vars["PROXYNOTICE"]);
        Assert.Equal(new[] { ("PROXYNOTICE", (object?)0), ("PROXYNOTICE", (object?)1) }, vars.Writes);
    }

    [Fact]
    public void AnAlreadySuppressedValueIsANoOpButReportsRestored()
    {
        var vars = new FakeSysvars { ["PROXYNOTICE"] = 0 };
        using var scope = Scope(vars);
        Assert.False(scope.Applied);
        Assert.True(scope.Restored); // the original value is already in place
        Assert.Empty(vars.Writes);
    }

    [Fact]
    public void EqualityIsNumericNotTypeBound()
    {
        var vars = new FakeSysvars { ["PROXYNOTICE"] = (short)0 };
        using var scope = Scope(vars, value: 0);
        Assert.False(scope.Applied);
    }

    [Fact]
    public void TheOriginalValueIsRestoredEvenWhenTheBodyThrows()
    {
        var vars = new FakeSysvars { ["PROXYNOTICE"] = 2 };
        Action body = () =>
        {
            using var scope = Scope(vars);
            Assert.Equal(0, vars["PROXYNOTICE"]);
            throw new InvalidOperationException("import blew up");
        };
        Assert.Throws<InvalidOperationException>(body);
        Assert.Equal(2, vars["PROXYNOTICE"]);
    }

    [Fact]
    public void RestoredReportsFalseWhenTheRestoreWriteFails()
    {
        var vars = new FakeSysvars { ["PROXYNOTICE"] = 1 };
        var failWrites = false;
        var scope = ScopedSysvar.Set("PROXYNOTICE", 0, vars.Get, (name, value) =>
        {
            if (failWrites)
            {
                throw new AutodeskFreeException();
            }

            vars.Set(name, value);
        });
        Assert.Equal(0, vars["PROXYNOTICE"]);

        failWrites = true;
        scope.Dispose();
        Assert.False(scope.Restored);
        Assert.Equal(0, vars["PROXYNOTICE"]); // the failed restore left the suppressed value
    }

    private sealed class AutodeskFreeException : Exception;

    [Fact]
    public void DoubleDisposeRestoresOnlyOnce()
    {
        var vars = new FakeSysvars { ["PROXYNOTICE"] = 1 };
        var scope = Scope(vars);
        scope.Dispose();
        scope.Dispose();
        Assert.True(scope.Restored);
        Assert.Equal(new[] { ("PROXYNOTICE", (object?)0), ("PROXYNOTICE", (object?)1) }, vars.Writes);
    }

    [Fact]
    public void AFailingReadPropagatesAndNothingIsWritten()
    {
        var vars = new FakeSysvars();
        Func<ScopedSysvar> body = () =>
            ScopedSysvar.Set("PROXYNOTICE", 0, _ => throw new AutodeskFreeException(), vars.Set);
        Assert.Throws<AutodeskFreeException>(body);
        Assert.Empty(vars.Writes);
    }

    [Fact]
    public void ProxyNoticeIsNotADeniedSysvar()
    {
        // D10 note: PROXYNOTICE must stay off the trust/startup deny list or the scoped
        // suppression (and forge_system_setvar) could never use it.
        Assert.Null(SysvarPolicy.Reject("cmd", "PROXYNOTICE"));
        Assert.False(SysvarPolicy.ContainsDeniedSysvar("(setvar \"PROXYNOTICE\" 0)", out _));
    }

    [Fact]
    public void SourceGuardEveryForeignDatabaseLoadFailsClosed()
    {
        // review-wp2-r1 finding 5 (D10): no foreign load without suppression — every
        // load-bearing call site goes through EnterOrFail and returns proxynotice_scope_failed
        // instead of loading unguarded when the sysvar cannot be read/set.
        var pluginDir = PluginDispatchSyncTests.RepoPath("src", "Forge.Plugin");
        var import = File.ReadAllText(Path.Combine(pluginDir, "PluginCommandProcessor.Import.cs"));
        var processor = File.ReadAllText(Path.Combine(pluginDir, "PluginCommandProcessor.cs"));
        var dgn = File.ReadAllText(Path.Combine(pluginDir, "PluginCommandProcessor.Dgn.cs"));
        var standards = File.ReadAllText(Path.Combine(pluginDir, "PluginCommandProcessor.Standards.cs"));
        var sheets = File.ReadAllText(Path.Combine(pluginDir, "PluginCommandProcessor.Sheets.cs"));
        var qa = File.ReadAllText(Path.Combine(pluginDir, "PluginCommandProcessor.QaAndPublish.cs"));
        var bulk = File.ReadAllText(Path.Combine(pluginDir, "PluginCommandProcessor.BulkInsert.cs"));

        // The single suppression point emits the typed failure.
        Assert.Contains("ScopedSysvar.Set(", processor, StringComparison.Ordinal);
        Assert.Contains("\"PROXYNOTICE\"", processor, StringComparison.Ordinal);
        Assert.Contains("proxynotice_scope_failed", processor, StringComparison.Ordinal);
        // The permissive Enter() is gone — nothing may enter a scope it cannot verify.
        foreach (var (name, source) in new[]
        {
            ("processor", processor), ("import", import), ("dgn", dgn),
            ("standards", standards), ("sheets", sheets), ("qa", qa), ("bulk", bulk)
        })
        {
            Assert.DoesNotContain("ProxyNoticeScope.Enter()", source, StringComparison.Ordinal);
            Assert.Contains("ProxyNoticeScope.EnterOrFail(", source, StringComparison.Ordinal);
        }

        // The 11 load-bearing sites: doc_open, xref_reload, repath reload + page-setup import
        // (processor); doc_import (import); -DGNIMPORT (dgn); standards import (standards);
        // doc_new — unconditional, template or not (sheets); xref normalize reload +
        // pack-and-go (qa); library-DWG read (bulk).
        Assert.True(processor.Split("EnterOrFail(", StringSplitOptions.None).Length - 1 >= 5,
            "doc_open, xref_reload, repath reload and page-setup import must each enter the fail-closed scope");
        Assert.Contains("Documents.Add(args.TemplatePath ?? \"\")", sheets, StringComparison.Ordinal);
        Assert.DoesNotContain("args.TemplatePath is null ? null", sheets, StringComparison.Ordinal);
        Assert.True(qa.Split("EnterOrFail(", StringSplitOptions.None).Length - 1 >= 2,
            "xref normalize reload and pack-and-go must each enter the fail-closed scope");
    }
}
