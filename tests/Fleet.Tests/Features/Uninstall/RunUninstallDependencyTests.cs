using Fleet.Features.Uninstall.RunUninstall;
using Fleet.Features.Uninstall.RunUninstall.Models;
using Fleet.Ports.Uninstall;
using Fleet.Ports.Uninstall.Enums;
using Fleet.Ports.Uninstall.Models;

namespace Fleet.Tests.Features.Uninstall;

public class RunUninstallDependencyTests
{
    private readonly List<string> _events = [];

    private sealed class Targets(List<string> events, params UninstallItem[] items) : IUninstallTargets
    {
        public IReadOnlyList<UninstallItem> Discover() => items;

        public IReadOnlyList<string> Refusals() => [];

        public void Remove(UninstallItem item) => events.Add($"item:{item.Path}");
    }

    private sealed class Manifest(params InstalledDependency[] deps) : IDependencyManifest
    {
        public IReadOnlyList<InstalledDependency> Read() => deps;
    }

    private sealed class Remover(List<string> events) : IPackageRemover
    {
        public Dictionary<string, string> Failing { get; } = [];

        public DependencyRemoval Remove(InstalledDependency dependency)
        {
            if (Failing.TryGetValue(dependency.Package, out var reason))
            {
                return new(false, reason);
            }

            events.Add($"dep:{dependency.Package}");
            return new(true, dependency.Package);
        }
    }

    private static readonly UninstallItem Config = new(UninstallKind.Directory, "/home/me/.config/fleet", "the fleet config home");

    private static readonly UninstallItem Binary = new(UninstallKind.Binary, "/usr/local/bin/fleet", "the fleet binary");

    private static readonly InstalledDependency Nvim =
        new(DependencyTool.Nvim, PackageManager.Pacman, "neovim", true, "2026-10-23T05:00:00Z");

    private static readonly InstalledDependency Yazi =
        new(DependencyTool.Yazi, PackageManager.Pacman, "yazi", true, "2026-10-23T05:01:00Z");

    private sealed record Outcome(int Exit, List<string> Output, int MainAsked, List<string> DepQuestions);

    private Outcome Run(
        RunUninstallCommand command,
        InstalledDependency[] deps,
        Func<string, bool>? answerDep = null,
        Remover? remover = null)
    {
        var output = new List<string>();
        var mainAsked = 0;
        var questions = new List<string>();

        var exit = new RunUninstallHandler(new Targets(_events, Config, Binary), new Manifest(deps), remover ?? new Remover(_events))
            .Handle(
                command,
                () =>
                {
                    mainAsked++;
                    return true;
                },
                output.Add,
                q =>
                {
                    questions.Add(q);
                    return answerDep?.Invoke(q) ?? false;
                });

        return new(exit, output, mainAsked, questions);
    }

    [Fact]
    public void The_plan_lists_the_dependencies_after_the_fleet_items_as_installed_by_fleets_installer()
    {
        var (_, output, _, _) = Run(new(Yes: true, InteractiveInput: false), [Nvim]);

        var heading = output.FindIndex(l => l.Contains("fleet's installer", StringComparison.Ordinal));
        Assert.True(heading > output.FindIndex(l => l.Contains(Binary.Path, StringComparison.Ordinal)));
        Assert.Contains(output.Skip(heading), l => l.Contains("nvim", StringComparison.Ordinal) && l.Contains("neovim", StringComparison.Ordinal) && l.Contains("pacman", StringComparison.Ordinal));
    }

    [Fact]
    public void Yes_alone_keeps_every_dependency_and_says_so()
    {
        var (exit, output, _, questions) = Run(new(Yes: true, InteractiveInput: false), [Nvim, Yazi]);

        Assert.Equal(0, exit);
        Assert.DoesNotContain(_events, e => e.StartsWith("dep:", StringComparison.Ordinal));
        Assert.Empty(questions);
        Assert.Contains(output, l => l.Contains("keep", StringComparison.Ordinal) && l.Contains("--remove-deps", StringComparison.Ordinal));
    }

    [Fact]
    public void Yes_with_remove_deps_removes_every_dependency_without_prompting()
    {
        var (exit, _, mainAsked, questions) = Run(new(Yes: true, InteractiveInput: false, RemoveDeps: true), [Nvim, Yazi]);

        Assert.Equal(0, exit);
        Assert.Equal(0, mainAsked);
        Assert.Empty(questions);
        Assert.Contains("dep:neovim", _events);
        Assert.Contains("dep:yazi", _events);
    }

    [Fact]
    public void Remove_deps_without_yes_asks_the_main_question_then_removes_all_without_more_prompts()
    {
        var (_, _, mainAsked, questions) = Run(new(Yes: false, InteractiveInput: true, RemoveDeps: true), [Nvim, Yazi]);

        Assert.Equal(1, mainAsked);
        Assert.Empty(questions);
        Assert.Contains("dep:neovim", _events);
        Assert.Contains("dep:yazi", _events);
    }

    [Fact]
    public void After_the_main_confirm_each_dependency_is_asked_and_only_the_yes_ones_go()
    {
        var (exit, _, mainAsked, questions) = Run(
            new(Yes: false, InteractiveInput: true),
            [Nvim, Yazi],
            q => q.Contains("yazi", StringComparison.Ordinal));

        Assert.Equal(0, exit);
        Assert.Equal(1, mainAsked);
        Assert.Equal(2, questions.Count);
        Assert.Contains(questions, q => q.Contains("nvim", StringComparison.Ordinal) && q.Contains("[y/N]", StringComparison.Ordinal));
        Assert.Contains("dep:yazi", _events);
        Assert.DoesNotContain("dep:neovim", _events);
    }

    [Fact]
    public void Every_dependency_question_comes_before_anything_is_removed()
    {
        var removedWhenAsked = new List<int>();

        Run(new(Yes: false, InteractiveInput: true), [Nvim, Yazi], _ =>
        {
            removedWhenAsked.Add(_events.Count);
            return true;
        });

        Assert.Equal([0, 0], removedWhenAsked);
    }

    [Fact]
    public void A_cancelled_main_prompt_asks_about_no_dependency()
    {
        var questions = new List<string>();

        new RunUninstallHandler(new Targets(_events, Config, Binary), new Manifest(Nvim), new Remover(_events))
            .Handle(new(false, true), () => false, _ => { }, q =>
            {
                questions.Add(q);
                return true;
            });

        Assert.Empty(questions);
        Assert.Empty(_events);
    }

    [Fact]
    public void Dependencies_are_removed_before_the_config_home_and_the_binary()
    {
        Run(new(Yes: true, InteractiveInput: false, RemoveDeps: true), [Nvim, Yazi]);

        Assert.Equal(["dep:neovim", "dep:yazi", $"item:{Config.Path}", $"item:{Binary.Path}"], _events);
    }

    [Fact]
    public void A_failed_dependency_is_reported_the_rest_continue_and_it_exits_1()
    {
        var remover = new Remover(_events);
        remover.Failing.Add("neovim", "pacman exited with code 1");

        var (exit, output, _, _) = Run(new(Yes: true, InteractiveInput: false, RemoveDeps: true), [Nvim, Yazi], remover: remover);

        Assert.Equal(1, exit);
        Assert.Contains("fleet uninstall: could not remove nvim (pacman): pacman exited with code 1", output);
        Assert.Equal(["dep:yazi", $"item:{Config.Path}", $"item:{Binary.Path}"], _events);
    }

    [Fact]
    public void An_empty_manifest_offers_and_removes_no_dependency()
    {
        var (exit, output, _, questions) = Run(new(Yes: false, InteractiveInput: true, RemoveDeps: true), []);

        Assert.Equal(0, exit);
        Assert.Empty(questions);
        Assert.DoesNotContain(output, l => l.Contains("fleet's installer", StringComparison.Ordinal));
        Assert.Equal([$"item:{Config.Path}", $"item:{Binary.Path}"], _events);
    }

    [Fact]
    public void Kept_dependencies_are_named_after_a_successful_run()
    {
        var (_, output, _, _) = Run(new(Yes: false, InteractiveInput: true), [Nvim, Yazi], q => q.Contains("yazi", StringComparison.Ordinal));

        Assert.Contains(output, l => l.Contains("kept", StringComparison.Ordinal) && l.Contains("nvim", StringComparison.Ordinal) && !l.Contains("yazi", StringComparison.Ordinal));
    }
}
