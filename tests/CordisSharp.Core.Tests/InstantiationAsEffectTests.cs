using CordisSharp.Core;
using Xunit;

namespace CordisSharp.Core.Tests;

public sealed class InstantiationAsEffectTests
{
    private interface IDb
    {
    }

    private sealed class Db : IDb
    {
    }

    private sealed class Log
    {
        public List<string> Lines { get; } = [];
    }

    /// <summary>A component that records itself and optionally instantiates a child.</summary>
    private sealed class Node(string name, Log log, params Type[] declared) : IComponent
    {
        public IReadOnlySet<Type> Declared { get; } = new HashSet<Type>(declared);

        public IComponent? ChildSpec { get; set; }

        public int Instantiations { get; private set; }

        public async IAsyncEnumerable<Func<ValueTask<Func<ValueTask>>>> Effect(IComponentContext ctx)
        {
            yield return () =>
            {
                Instantiations++;
                log.Lines.Add($"do:{name}");
                return new ValueTask<Func<ValueTask>>(() =>
                {
                    log.Lines.Add($"undo:{name}");
                    return default;
                });
            };

            if (ChildSpec is { } child)
            {
                // Instantiation as an effect: the child's existence is one of our inverses.
                await ctx.Add(child);
            }
        }
    }

    private static (Node Parent, Node Child, Node Grandchild) Tree(Log log)
    {
        var grandchild = new Node("G", log);
        var child = new Node("C", log) { ChildSpec = grandchild };
        var parent = new Node("P", log) { ChildSpec = child };
        return (parent, child, grandchild);
    }

    [Fact]
    public async Task Unloading_a_parent_retires_children_leaves_first()
    {
        var log = new Log();
        var host = new CompositionHost();
        var (parent, child, grandchild) = Tree(log);

        var added = await host.Add(parent);
        Assert.Equal(["do:P", "do:C", "do:G"], log.Lines);

        await added.DisposeAsync();

        // Leaves first, one level at a time, no manager code anywhere.
        Assert.Equal(
            ["do:P", "do:C", "do:G", "undo:G", "undo:C", "undo:P"],
            log.Lines);
        Assert.Equal(FiberState.Absent, host.StateOf(parent));
        Assert.Equal(FiberState.Absent, host.StateOf(child));
    }

    [Fact]
    public async Task Retiring_a_parent_retires_children_leaves_first()
    {
        var log = new Log();
        var host = new CompositionHost();
        var (parent, child, _) = Tree(log);
        await host.Add(parent);

        await host.Retire(parent);

        Assert.Equal(
            ["do:P", "do:C", "do:G", "undo:G", "undo:C", "undo:P"],
            log.Lines);
        // The retired parent stays registered; the children it created are gone.
        Assert.Equal(FiberState.Inactive, host.StateOf(parent));
        Assert.Equal(FiberState.Absent, host.StateOf(child));
    }

    [Fact]
    public async Task Reactivating_a_parent_reinstantiates_children()
    {
        var log = new Log();
        var host = new CompositionHost();
        var child = new Node("C", log);
        var parent = new Node("P", log, typeof(IDb)) { ChildSpec = child };

        await host.Provide<IDb>(new Db());
        await host.Add(parent);
        Assert.Equal(["do:P", "do:C"], log.Lines);

        // A provider swap deactivates the parent, retiring its children, and
        // reactivates it against the new binding, instantiating them fresh.
        await host.Provide<IDb>(new Db());

        Assert.Equal(
            ["do:P", "do:C", "undo:C", "undo:P", "do:P", "do:C"],
            log.Lines);
        Assert.Equal(2, child.Instantiations);
    }
}
