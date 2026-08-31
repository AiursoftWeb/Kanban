using Android.App;
using Android.Runtime;

[assembly: UsesPermission(Android.Manifest.Permission.Internet)]

namespace Aiursoft.Kanban.Android;

[Application]
public sealed class KanbanApplication(nint handle, JniHandleOwnership ownership)
    : Application(handle, ownership)
{
    public AppSession Session { get; private set; } = null!;

    public override void OnCreate()
    {
        base.OnCreate();
        Session = new AppSession(this);
    }
}
