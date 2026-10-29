using Android.App;
using Android.Runtime;

[assembly: UsesPermission(Android.Manifest.Permission.Internet)]

namespace Aiursoft.Kanban.Android;

[Application(Icon = "@drawable/ic_kanban")]
public sealed class KanbanApplication(nint handle, JniHandleOwnership ownership)
    : Application(handle, ownership)
{
    public AppSession Session { get; private set; } = null!;

    public override void OnCreate()
    {
        base.OnCreate();
        var theme = GetSharedPreferences("appearance", global::Android.Content.FileCreationMode.Private)!.GetInt("theme", 0);
        AndroidX.AppCompat.App.AppCompatDelegate.DefaultNightMode = theme switch
        {
            1 => AndroidX.AppCompat.App.AppCompatDelegate.ModeNightNo,
            2 => AndroidX.AppCompat.App.AppCompatDelegate.ModeNightYes,
            _ => AndroidX.AppCompat.App.AppCompatDelegate.ModeNightFollowSystem
        };
        Session = new AppSession(this);
    }
}
