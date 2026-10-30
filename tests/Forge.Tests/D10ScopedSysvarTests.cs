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
    public void SourceGuardEveryForeignDatabaseLoadRunsUnderTheScope()
    {
        var pluginDir = PluginDispatchSyncTests.RepoPath("src", "Forge.Plugin");
        var import = File.ReadAllText(Path.Combine(pluginDir, "PluginCommandProcessor.Import.cs"));
        var processor = File.ReadAllText(Path.Combine(pluginDir, "PluginCommandProcessor.cs"));
        var dgn = File.ReadAllText(Path.Combine(pluginDir, "PluginCommandProcessor.Dgn.cs"));

        // The single suppression point in the plugin feeds every call site below.
        Assert.Contains("ScopedSysvar.Set(", processor, StringComparison.Ordinal);
        Assert.Contains("\"PROXYNOTICE\"", processor, StringComparison.Ordinal);
        // doc_import covers DxfIn/ReadDwgFile, Insert, AttachXref/OverlayXref in one scope.
        Assert.Contains("ProxyNoticeScope.Enter()", import, StringComparison.Ordinal);
        // xref reload / repath-reload / page-setup / doc-open load foreign databases too.
        var scopeCalls = processor.Split("ProxyNoticeScope.Enter()", StringSplitOptions.None).Length - 1;
        Assert.True(scopeCalls >= 4, $"ReloadXrefs, RepathXref, PageSetupImport and OpenDocument all enter the PROXYNOTICE scope (found {scopeCalls})");
        // -DGNIMPORT converts a foreign file in a temporary document.
        Assert.Contains("ProxyNoticeScope.Enter()", dgn, StringComparison.Ordinal);
    }
}
