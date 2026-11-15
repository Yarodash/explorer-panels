using ExplorerPanel.Core;

namespace ExplorerPanel.Tests;

public class TemplateTests
{
    static Func<string, string?> Values(params (string Name, string Value)[] pairs) =>
        name => pairs.Where(p => p.Name == name).Select(p => p.Value).FirstOrDefault();

    [Fact]
    public void Names_lists_each_placeholder_once_in_order()
    {
        Assert.Equal(new[] { "x", "y-1" }, Template.Names("a {x} b {y-1} {x} {not a name}"));
    }

    [Fact]
    public void Fill_replaces_known_and_blanks_unknown()
    {
        Assert.Equal("ветка main / ", Template.Fill("ветка {branch} / {nope}", Values(("branch", "main"))));
    }

    [Fact]
    public void FillUrl_encodes()
    {
        Assert.Equal("http://x/?q=a%20b%26c", Template.FillUrl("http://x/?q={q}", Values(("q", "a b&c"))));
    }

    [Fact]
    public void TryFillCommand_accepts_safe_value()
    {
        bool ok = Template.TryFillCommand("git checkout {branch}", Values(("branch", "feature/x-1")), out var result, out var error);

        Assert.True(ok);
        Assert.Equal("git checkout feature/x-1", result);
        Assert.Equal("", error);
    }

    [Theory]
    [InlineData("a & del")]
    [InlineData("x|y")]
    [InlineData("$(whoami)")]
    [InlineData("a\"b")]
    [InlineData("%PATH%")]
    public void TryFillCommand_rejects_unsafe_value(string value)
    {
        bool ok = Template.TryFillCommand("git checkout {branch}", Values(("branch", value)), out _, out var error);

        Assert.False(ok);
        Assert.Contains("\"branch\"", error);
    }

    [Fact]
    public void TryFillCommand_rejects_missing_value()
    {
        bool ok = Template.TryFillCommand("npm run build -- --mode {env}", Values(), out _, out var error);

        Assert.False(ok);
        Assert.Contains("\"env\"", error);
    }

    [Fact]
    public void TryFillCommand_leaves_command_without_placeholders_unchanged()
    {
        Assert.True(Template.TryFillCommand("echo {not a name} && dir", Values(), out var result, out _));
        Assert.Equal("echo {not a name} && dir", result);
    }
}
