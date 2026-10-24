using System.Windows.Input;
using MicroSnip.Infrastructure;
using Xunit;

namespace MicroSnip.Tests.Infrastructure;

public sealed class HotkeyGestureTests
{
    [Fact]
    public void Default_IsCtrlAltT()
    {
        Assert.Equal("Ctrl+Alt+T", HotkeyGesture.Default.ToString());
        Assert.True(HotkeyGesture.Default.IsValid());
    }

    [Fact]
    public void ToString_FormatsModifiersInCanonicalOrder()
    {
        var gesture = new HotkeyGesture(
            HotkeyModifiers.Win
                | HotkeyModifiers.Shift
                | HotkeyModifiers.Alt
                | HotkeyModifiers.Control,
            Key.F12);

        Assert.Equal("Ctrl+Alt+Shift+Win+F12", gesture.ToString());
    }

    [Theory]
    [InlineData(Key.T)]
    [InlineData(Key.F8)]
    [InlineData(Key.D5)]
    [InlineData(Key.OemPeriod)]
    public void IsValid_AcceptsModifierPlusRegularKey(Key key)
    {
        var gesture = new HotkeyGesture(HotkeyModifiers.Control, key);

        Assert.True(gesture.IsValid());
    }

    [Fact]
    public void IsValid_RejectsMissingModifier()
    {
        var gesture = new HotkeyGesture(HotkeyModifiers.None, Key.T);

        Assert.False(gesture.IsValid());
    }

    [Fact]
    public void IsValid_RejectsMissingKey()
    {
        var gesture = new HotkeyGesture(HotkeyModifiers.Control, Key.None);

        Assert.False(gesture.IsValid());
    }

    [Fact]
    public void IsValid_RejectsModifierAsKey()
    {
        var gesture = new HotkeyGesture(HotkeyModifiers.Control, Key.LeftCtrl);

        Assert.False(gesture.IsValid());
    }

    [Fact]
    public void IsValid_RejectsWinKeyAsKey()
    {
        var gesture = new HotkeyGesture(HotkeyModifiers.Win, Key.LWin);

        Assert.False(gesture.IsValid());
    }
}
