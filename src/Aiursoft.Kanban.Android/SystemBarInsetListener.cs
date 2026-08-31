using Android.Views;

namespace Aiursoft.Kanban.Android;

internal sealed class SystemBarInsetListener(
    int baseLeft,
    int baseTop,
    int baseRight,
    int baseBottom,
    bool applyTop,
    bool applyBottom) : Java.Lang.Object, View.IOnApplyWindowInsetsListener
{
    public WindowInsets OnApplyWindowInsets(View view, WindowInsets insets)
    {
        var left = OperatingSystem.IsAndroidVersionAtLeast(30)
            ? insets.GetInsets(WindowInsets.Type.SystemBars()).Left
            : insets.SystemWindowInsetLeft;
        var top = OperatingSystem.IsAndroidVersionAtLeast(30)
            ? insets.GetInsets(WindowInsets.Type.SystemBars()).Top
            : insets.SystemWindowInsetTop;
        var right = OperatingSystem.IsAndroidVersionAtLeast(30)
            ? insets.GetInsets(WindowInsets.Type.SystemBars()).Right
            : insets.SystemWindowInsetRight;
        var bottom = OperatingSystem.IsAndroidVersionAtLeast(30)
            ? insets.GetInsets(WindowInsets.Type.SystemBars()).Bottom
            : insets.SystemWindowInsetBottom;
        view.SetPadding(
            baseLeft + left,
            baseTop + (applyTop ? top : 0),
            baseRight + right,
            baseBottom + (applyBottom ? bottom : 0));
        return insets;
    }
}
