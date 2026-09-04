using Android.App;
using Android.Content;
using Android.Content.Res;
using Android.Graphics;
using Android.OS;
using Android.Views;
using Android.Widget;
using AndroidX.AppCompat.App;
using Aiursoft.Kanban.Android.Oidc;
using Aiursoft.Kanban.SDK;
using Aiursoft.Kanban.SDK.Models;
using Google.Android.Material.AppBar;
using Google.Android.Material.Button;
using Google.Android.Material.Card;
using Google.Android.Material.Dialog;
using Google.Android.Material.MaterialSwitch;
using Google.Android.Material.ProgressIndicator;
using Google.Android.Material.Snackbar;
using Color = Android.Graphics.Color;

namespace Aiursoft.Kanban.Android;

[Activity(Label = "Visibility and sharing", Exported = false, Theme = "@style/AppTheme")]
public sealed class BoardSharingActivity : AppCompatActivity
{
    private const string BoardIdExtra = "board_id";
    private View _root = null!;
    private global::AndroidX.Core.Widget.NestedScrollView _scroll = null!;
    private MaterialToolbar _toolbar = null!;
    private MaterialSwitch _publicSwitch = null!;
    private MaterialButton _shareLink = null!;
    private TextView _addAccessLabel = null!;
    private Spinner _targetType = null!;
    private Spinner _target = null!;
    private Spinner _permission = null!;
    private MaterialButton _addShare = null!;
    private LinearLayout _shares = null!;
    private CircularProgressIndicator _progress = null!;
    private BoardSharingResponse? _model;
    private int _boardId;
    private bool _loaded;
    private bool _rendering;
    private bool _busy;

    private AppSession Session => ((KanbanApplication)Application!).Session;
    private KanbanApiClient Api => Session.RequireApi();

    public static Intent CreateIntent(Context context, int boardId) =>
        new Intent(context, typeof(BoardSharingActivity)).PutExtra(BoardIdExtra, boardId);

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        if (!Session.IsAuthenticated)
        {
            ReturnToLogin();
            return;
        }
        _boardId = Intent?.GetIntExtra(BoardIdExtra, 0) ?? 0;
        if (_boardId <= 0)
        {
            Finish();
            return;
        }

        SetContentView(Resource.Layout.activity_board_sharing);
        BindViews();
        ConfigureChrome();
        ConfigureSpinners();
        WireEvents();
        _ = LoadAsync();
    }

    protected override void OnResume()
    {
        base.OnResume();
        if (_loaded && !_busy)
        {
            _ = LoadAsync(showProgress: false);
        }
    }

    private void BindViews()
    {
        _root = FindViewById<View>(Resource.Id.board_sharing_root)!;
        _scroll = FindViewById<global::AndroidX.Core.Widget.NestedScrollView>(Resource.Id.board_sharing_scroll)!;
        _toolbar = FindViewById<MaterialToolbar>(Resource.Id.board_sharing_toolbar)!;
        _publicSwitch = FindViewById<MaterialSwitch>(Resource.Id.public_board_switch)!;
        _shareLink = FindViewById<MaterialButton>(Resource.Id.share_public_link_button)!;
        _addAccessLabel = FindViewById<TextView>(Resource.Id.add_access_label)!;
        _targetType = FindViewById<Spinner>(Resource.Id.share_target_type_spinner)!;
        _target = FindViewById<Spinner>(Resource.Id.share_target_spinner)!;
        _permission = FindViewById<Spinner>(Resource.Id.share_permission_spinner)!;
        _addShare = FindViewById<MaterialButton>(Resource.Id.add_share_button)!;
        _shares = FindViewById<LinearLayout>(Resource.Id.current_shares_list)!;
        _progress = FindViewById<CircularProgressIndicator>(Resource.Id.board_sharing_progress)!;
    }

    private void ConfigureChrome()
    {
        _toolbar.SetOnApplyWindowInsetsListener(new ToolbarInsetListener(Dp(64)));
        _scroll.SetOnApplyWindowInsetsListener(
            new SystemBarInsetListener(0, 0, 0, Dp(12), false, true));
        _toolbar.NavigationContentDescription = "Back to board settings";
        _toolbar.NavigationClick += (_, _) => Finish();
    }

    private void ConfigureSpinners()
    {
        _targetType.Adapter = new ArrayAdapter<string>(
            this,
            global::Android.Resource.Layout.SimpleSpinnerDropDownItem,
            ["User", "Role"]);
        _permission.Adapter = new ArrayAdapter<string>(
            this,
            global::Android.Resource.Layout.SimpleSpinnerDropDownItem,
            ["Read only", "Editable"]);
    }

    private void WireEvents()
    {
        _targetType.ItemSelected += (_, _) => PopulateTargets();
        _publicSwitch.CheckedChange += async (_, args) =>
        {
            if (_rendering || !_loaded || _busy || _model?.IsPublic == args.IsChecked)
            {
                return;
            }
            await SetVisibilityAsync(args.IsChecked);
        };
        _shareLink.Click += (_, _) => SharePublicLink();
        _addShare.Click += async (_, _) => await AddShareAsync();
    }

    private async Task LoadAsync(bool showProgress = true)
    {
        if (_busy)
        {
            return;
        }
        try
        {
            SetBusy(true, showProgress);
            _model = await Api.GetBoardSharingAsync(_boardId);
            Render();
            _loaded = true;
        }
        catch (Exception exception)
        {
            ShowError(exception);
        }
        finally
        {
            SetBusy(false, showProgress);
        }
    }

    private void Render()
    {
        var model = _model;
        if (model == null)
        {
            return;
        }
        _rendering = true;
        _toolbar.Subtitle = model.BoardName;
        _publicSwitch.Checked = model.IsPublic;
        _shareLink.Visibility = model.IsPublic && !string.IsNullOrWhiteSpace(model.PublicUrl)
            ? ViewStates.Visible
            : ViewStates.Gone;
        var addVisibility = model.IsPublic ? ViewStates.Gone : ViewStates.Visible;
        _addAccessLabel.Visibility = addVisibility;
        _targetType.Visibility = addVisibility;
        _target.Visibility = addVisibility;
        _permission.Visibility = addVisibility;
        _addShare.Visibility = addVisibility;
        PopulateTargets();

        _shares.RemoveAllViews();
        if (model.Shares.Count == 0)
        {
            var empty = Text(
                model.IsPublic
                    ? "This public board has no specific user or role shares."
                    : "No one else has access yet.",
                14,
                Resource.Color.text_secondary);
            empty.SetPadding(Dp(4), Dp(14), Dp(4), Dp(14));
            _shares.AddView(empty);
        }
        else
        {
            foreach (var share in model.Shares)
            {
                var layout = new LinearLayout.LayoutParams(
                    ViewGroup.LayoutParams.MatchParent,
                    ViewGroup.LayoutParams.WrapContent);
                layout.SetMargins(0, Dp(5), 0, Dp(5));
                _shares.AddView(ShareCard(share), layout);
            }
        }
        _rendering = false;
    }

    private void PopulateTargets()
    {
        var model = _model;
        if (model == null)
        {
            _target.Adapter = new ArrayAdapter<string>(
                this,
                global::Android.Resource.Layout.SimpleSpinnerDropDownItem,
                ["Loading…"]);
            return;
        }
        var targets = _targetType.SelectedItemPosition == 0
            ? model.AvailableUsers
            : model.AvailableRoles;
        var names = targets.Count == 0
            ? ["No available targets"]
            : targets.Select(item => item.Name).ToArray();
        _target.Adapter = new ArrayAdapter<string>(
            this,
            global::Android.Resource.Layout.SimpleSpinnerDropDownItem,
            names);
        _target.Enabled = targets.Count > 0 && !_busy;
    }

    private View ShareCard(BoardShareDto share)
    {
        var card = new MaterialCardView(this)
        {
            Radius = Dp(15),
            CardElevation = 0
        };
        card.SetCardBackgroundColor(GetColor(Resource.Color.surface));
        card.StrokeColor = GetColor(Resource.Color.outline);
        card.StrokeWidth = Dp(1);
        var row = new LinearLayout(this)
        {
            Orientation = global::Android.Widget.Orientation.Horizontal
        };
        row.SetGravity(GravityFlags.CenterVertical);
        row.SetPadding(Dp(15), Dp(13), Dp(8), Dp(13));
        var labels = new LinearLayout(this)
        {
            Orientation = global::Android.Widget.Orientation.Vertical
        };
        labels.AddView(Text(share.TargetName, 15, Resource.Color.text_primary, true));
        labels.AddView(Text(
            $"{share.TargetType} · {PermissionLabel(share.Permission)} · since {share.CreationTime.ToLocalTime():yyyy-MM-dd}",
            12,
            Resource.Color.text_secondary));
        row.AddView(labels, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1));
        var remove = new MaterialButton(this, null, global::Android.Resource.Attribute.BorderlessButtonStyle)
        {
            Text = "Remove",
            TextSize = 12,
            CornerRadius = Dp(12)
        };
        remove.SetAllCaps(false);
        remove.SetTextColor(ColorStateList.ValueOf(ColorOf(Resource.Color.on_danger_container)));
        remove.Click += (_, _) => ConfirmRemove(share);
        row.AddView(remove, new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.WrapContent,
            Dp(44)));
        card.AddView(row);
        return card;
    }

    private async Task SetVisibilityAsync(bool isPublic)
    {
        try
        {
            SetBusy(true, showProgress: true);
            _model = await Api.SetBoardVisibilityAsync(_boardId, isPublic);
            Render();
            Snackbar.Make(_root,
                isPublic ? "Board is now public" : "Board is now private",
                Snackbar.LengthShort).Show();
        }
        catch (Exception exception)
        {
            Render();
            ShowError(exception);
        }
        finally
        {
            SetBusy(false, showProgress: true);
        }
    }

    private async Task AddShareAsync()
    {
        var model = _model;
        if (model == null || model.IsPublic || _busy)
        {
            return;
        }
        var users = _targetType.SelectedItemPosition == 0;
        var targets = users ? model.AvailableUsers : model.AvailableRoles;
        if (targets.Count == 0 || _target.SelectedItemPosition < 0 || _target.SelectedItemPosition >= targets.Count)
        {
            Snackbar.Make(_root, "No user or role is available to share with.", Snackbar.LengthLong).Show();
            return;
        }
        var target = targets[_target.SelectedItemPosition];
        try
        {
            SetBusy(true, showProgress: true);
            _model = await Api.AddBoardShareAsync(_boardId, new AddBoardShareRequest
            {
                TargetUserId = users ? target.Id : null,
                TargetRoleId = users ? null : target.Id,
                Permission = _permission.SelectedItemPosition == 0 ? "ReadOnly" : "Editable"
            });
            Render();
            Snackbar.Make(_root, $"Shared with {target.Name}", Snackbar.LengthShort).Show();
        }
        catch (Exception exception)
        {
            ShowError(exception);
        }
        finally
        {
            SetBusy(false, showProgress: true);
        }
    }

    private void ConfirmRemove(BoardShareDto share)
    {
        var builder = new MaterialAlertDialogBuilder(this);
        builder.SetTitle("Remove access?");
        builder.SetMessage($"Remove {share.TargetName} from this board?");
        builder.SetNegativeButton("Cancel", (_, _) => { });
        builder.SetPositiveButton("Remove", (_, _) => _ = RemoveShareAsync(share));
        builder.Show();
    }

    private async Task RemoveShareAsync(BoardShareDto share)
    {
        if (_busy)
        {
            return;
        }
        try
        {
            SetBusy(true, showProgress: true);
            _model = await Api.RemoveBoardShareAsync(_boardId, share.Id);
            Render();
            Snackbar.Make(_root, "Access removed", Snackbar.LengthShort).Show();
        }
        catch (Exception exception)
        {
            ShowError(exception);
        }
        finally
        {
            SetBusy(false, showProgress: true);
        }
    }

    private void SharePublicLink()
    {
        var url = _model?.PublicUrl;
        if (string.IsNullOrWhiteSpace(url))
        {
            return;
        }
        var send = new Intent(Intent.ActionSend);
        send.SetType("text/plain");
        send.PutExtra(Intent.ExtraText, url);
        StartActivity(Intent.CreateChooser(send, "Share board link"));
    }

    private TextView Text(string value, float size, int colorResource, bool bold = false)
    {
        var view = new TextView(this)
        {
            Text = value,
            TextSize = size,
            Typeface = bold ? Typeface.DefaultBold : Typeface.Default
        };
        view.SetTextColor(ColorOf(colorResource));
        return view;
    }

    private void SetBusy(bool busy, bool showProgress)
    {
        _busy = busy;
        _progress.Visibility = busy && showProgress ? ViewStates.Visible : ViewStates.Gone;
        _scroll.Alpha = busy ? 0.55f : 1f;
        _publicSwitch.Enabled = !busy;
        _shareLink.Enabled = !busy;
        _targetType.Enabled = !busy;
        _permission.Enabled = !busy;
        _addShare.Enabled = !busy;
        PopulateTargets();
    }

    private void ShowError(Exception exception)
    {
        if (exception is KanbanAuthenticationRequiredException)
        {
            ReturnToLogin();
            return;
        }
        Snackbar.Make(_root, FriendlyMessage(exception), Snackbar.LengthLong).Show();
    }

    private void ReturnToLogin()
    {
        var intent = new Intent(this, typeof(LoginActivity));
        intent.AddFlags(ActivityFlags.ClearTop | ActivityFlags.NewTask | ActivityFlags.ClearTask);
        StartActivity(intent);
        Finish();
    }

    private static string PermissionLabel(string permission) =>
        permission == "Editable" ? "Can edit" : "View only";

    private static string FriendlyMessage(Exception exception) =>
        exception.Message.Length > 180 ? exception.Message[..180] : exception.Message;

    private Color ColorOf(int colorResource) => new(GetColor(colorResource));

    private int Dp(int value) => (int)Math.Round(value * Resources!.DisplayMetrics!.Density);

    private sealed class ToolbarInsetListener(int contentHeight)
        : Java.Lang.Object, View.IOnApplyWindowInsetsListener
    {
        public WindowInsets OnApplyWindowInsets(View view, WindowInsets insets)
        {
            var top = OperatingSystem.IsAndroidVersionAtLeast(30)
                ? insets.GetInsets(WindowInsets.Type.SystemBars()).Top
                : insets.SystemWindowInsetTop;
            var parameters = view.LayoutParameters!;
            parameters.Height = contentHeight + top;
            view.LayoutParameters = parameters;
            view.SetPadding(view.PaddingLeft, top, view.PaddingRight, view.PaddingBottom);
            return insets;
        }
    }
}
