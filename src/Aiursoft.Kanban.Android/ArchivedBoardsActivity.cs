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
using Google.Android.Material.ProgressIndicator;
using Google.Android.Material.Snackbar;

namespace Aiursoft.Kanban.Android;

[Activity(Label = "Archived Boards", Exported = false, Theme = "@style/AppTheme")]
public sealed class ArchivedBoardsActivity : AppCompatActivity
{
    private View _root = null!;
    private global::AndroidX.Core.Widget.NestedScrollView _scroll = null!;
    private LinearLayout _content = null!;
    private CircularProgressIndicator _progress = null!;
    private ArchivedBoardListResponse? _model;
    private bool _loaded;
    private bool _busy;

    private AppSession Session => ((KanbanApplication)Application!).Session;
    private KanbanApiClient Api => Session.RequireApi();

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        if (!Session.IsAuthenticated)
        {
            ReturnToLogin();
            return;
        }

        SetContentView(Resource.Layout.activity_archived_boards);
        _root = FindViewById<View>(Resource.Id.archived_boards_root)!;
        _scroll = FindViewById<global::AndroidX.Core.Widget.NestedScrollView>(
            Resource.Id.archived_boards_scroll)!;
        _content = FindViewById<LinearLayout>(Resource.Id.archived_boards_content)!;
        _progress = FindViewById<CircularProgressIndicator>(Resource.Id.archived_boards_progress)!;
        var toolbar = FindViewById<MaterialToolbar>(Resource.Id.archived_boards_toolbar)!;
        toolbar.SetOnApplyWindowInsetsListener(new ToolbarInsetListener(Dp(64)));
        _content.SetOnApplyWindowInsetsListener(
            new SystemBarInsetListener(Dp(16), Dp(18), Dp(16), Dp(32), false, true));
        toolbar.NavigationClick += (_, _) => Finish();
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

    private async Task LoadAsync(bool showProgress = true)
    {
        if (_busy)
        {
            return;
        }
        try
        {
            SetBusy(true, showProgress);
            _model = await Api.GetArchivedBoardsAsync();
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
        _content.RemoveAllViews();
        Add(Text("Archived Boards", 26, Resource.Color.text_primary, bold: true), 0, 4);
        Add(Text("Archived boards are read-only until their owner restores them.",
            14, Resource.Color.text_secondary), 0, 20);

        if (model.OwnedBoards.Count == 0 && model.SharedBoards.Count == 0)
        {
            Add(EmptyState(), 0, 0);
            return;
        }

        Add(Section("MY ARCHIVED BOARDS"), 0, 8);
        if (model.OwnedBoards.Count == 0)
        {
            Add(Text("You have no archived boards.", 13, Resource.Color.text_secondary), 2, 12);
        }
        foreach (var board in model.OwnedBoards)
        {
            Add(BoardCard(board), 0, 10);
        }

        Add(Section("SHARED ARCHIVED BOARDS"), 14, 8);
        if (model.SharedBoards.Count == 0)
        {
            Add(Text("No archived boards are shared with you.",
                13, Resource.Color.text_secondary), 2, 0);
        }
        foreach (var board in model.SharedBoards)
        {
            Add(BoardCard(board), 0, 10);
        }
    }

    private View BoardCard(ArchivedBoardDto board)
    {
        var card = Surface();
        card.Clickable = true;
        var body = new LinearLayout(this)
        {
            Orientation = global::Android.Widget.Orientation.Vertical
        };
        body.SetPadding(Dp(16), Dp(14), Dp(16), Dp(14));

        var heading = new LinearLayout(this)
        {
            Orientation = global::Android.Widget.Orientation.Horizontal
        };
        heading.SetGravity(GravityFlags.CenterVertical);
        var title = new LinearLayout(this)
        {
            Orientation = global::Android.Widget.Orientation.Vertical
        };
        title.AddView(Text(board.Name, 17, Resource.Color.text_primary, bold: true));
        title.AddView(Text(board.IsOwner
                ? "Archived · Owner"
                : $"Archived · {board.Permission} · {board.SharedVia}",
            12, Resource.Color.text_secondary));
        heading.AddView(title, new LinearLayout.LayoutParams(
            0, ViewGroup.LayoutParams.WrapContent, 1));
        heading.AddView(Badge("ARCHIVED"));
        body.AddView(heading);

        AddTo(body, Text($"{board.ColumnCount} columns · {board.CardCount} cards",
            13, Resource.Color.text_secondary), 12, 0);
        AddTo(body, Text(
            $"{board.IncompleteCount} open · {board.InProgressCount} in progress · {board.CompletedCount} done",
            13, Resource.Color.text_primary), 4, 0);
        if (board.OverdueCount > 0 || board.UnassignedCount > 0)
        {
            AddTo(body, Text(
                $"{board.OverdueCount} overdue · {board.UnassignedCount} unassigned",
                12,
                board.OverdueCount > 0
                    ? Resource.Color.on_danger_container
                    : Resource.Color.text_secondary), 4, 0);
        }
        AddTo(body, Text(
            board.ArchivedTime.HasValue
                ? $"Archived {board.ArchivedTime.Value.ToLocalTime():yyyy-MM-dd HH:mm}"
                : "Archive time unavailable",
            12, Resource.Color.text_secondary), 6, 0);

        var actions = new LinearLayout(this)
        {
            Orientation = global::Android.Widget.Orientation.Horizontal
        };
        var view = ActionButton("View");
        actions.AddView(view, new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.WrapContent, Dp(44)));
        view.Click += (_, _) => OpenBoard(board.Id);
        if (board.IsOwner)
        {
            var restore = ActionButton("Unarchive");
            var restoreLayout = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.WrapContent, Dp(44));
            restoreLayout.SetMargins(Dp(8), 0, 0, 0);
            actions.AddView(restore, restoreLayout);
            restore.Click += (_, _) => ConfirmRestore(board);
        }
        AddTo(body, actions, 10, 0);
        card.AddView(body);
        card.Click += (_, _) => OpenBoard(board.Id);
        return card;
    }

    private void ConfirmRestore(ArchivedBoardDto board)
    {
        var builder = new MaterialAlertDialogBuilder(this);
        builder.SetTitle("Unarchive board?");
        builder.SetMessage($"{board.Name} will return to your active workspace.");
        builder.SetNegativeButton("Cancel", (_, _) => { });
        builder.SetPositiveButton("Unarchive", (dialog, args) =>
        {
            _ = RestoreAsync(board);
        });
        builder.Show();
    }

    private async Task RestoreAsync(ArchivedBoardDto board)
    {
        try
        {
            SetBusy(true, showProgress: true);
            await Api.SetBoardArchivedAsync(board.Id, archive: false);
            _model?.OwnedBoards.RemoveAll(item => item.Id == board.Id);
            Render();
            Snackbar.Make(_root, "Board restored", Snackbar.LengthLong).Show();
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

    private void OpenBoard(int boardId)
    {
        Session.SelectedBoardId = boardId;
        var intent = new Intent(this, typeof(MainActivity));
        intent.AddFlags(ActivityFlags.ClearTop | ActivityFlags.SingleTop);
        StartActivity(intent);
        Finish();
    }

    private View EmptyState()
    {
        var card = Surface();
        var body = new LinearLayout(this)
        {
            Orientation = global::Android.Widget.Orientation.Vertical
        };
        body.SetGravity(GravityFlags.Center);
        body.SetPadding(Dp(20), Dp(38), Dp(20), Dp(38));
        body.AddView(Text("No archived boards", 20, Resource.Color.text_primary, bold: true));
        body.AddView(Text("Boards you archive will appear here.",
            14, Resource.Color.text_secondary));
        card.AddView(body);
        return card;
    }

    private TextView Badge(string value)
    {
        var badge = Text(value, 11, Resource.Color.text_secondary, bold: true);
        badge.Gravity = GravityFlags.Center;
        badge.SetPadding(Dp(10), Dp(6), Dp(10), Dp(6));
        badge.SetBackgroundColor(new global::Android.Graphics.Color(
            GetColor(Resource.Color.surface_variant)));
        return badge;
    }

    private MaterialButton ActionButton(string value)
    {
        var button = new MaterialButton(
            this, null, global::Android.Resource.Attribute.BorderlessButtonStyle)
        {
            Text = value,
            TextSize = 13
        };
        button.SetAllCaps(false);
        button.SetTextColor(ColorStateList.ValueOf(new global::Android.Graphics.Color(
            GetColor(Resource.Color.brand_primary))));
        return button;
    }

    private MaterialCardView Surface()
    {
        var card = new MaterialCardView(this)
        {
            Radius = Dp(16),
            CardElevation = 0
        };
        card.SetCardBackgroundColor(GetColor(Resource.Color.surface));
        card.StrokeColor = GetColor(Resource.Color.outline);
        card.StrokeWidth = Dp(1);
        return card;
    }

    private TextView Section(string value)
    {
        var text = Text(value, 12, Resource.Color.text_secondary, bold: true);
        text.LetterSpacing = 0.08f;
        return text;
    }

    private TextView Text(string value, float size, int color, bool bold = false)
    {
        var text = new TextView(this)
        {
            Text = value,
            TextSize = size,
            Typeface = bold ? Typeface.DefaultBold : Typeface.Default
        };
        text.SetTextColor(new global::Android.Graphics.Color(GetColor(color)));
        return text;
    }

    private void Add(View view, int top, int bottom) => AddTo(_content, view, top, bottom);

    private void AddTo(ViewGroup parent, View view, int top, int bottom)
    {
        var layout = new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent);
        layout.SetMargins(0, Dp(top), 0, Dp(bottom));
        parent.AddView(view, layout);
    }

    private void SetBusy(bool busy, bool showProgress)
    {
        _busy = busy;
        _progress.Visibility = busy && showProgress ? ViewStates.Visible : ViewStates.Gone;
        _scroll.Alpha = busy && showProgress ? 0.55f : 1f;
    }

    private void ShowError(Exception exception)
    {
        if (exception is KanbanAuthenticationRequiredException)
        {
            ReturnToLogin();
            return;
        }
        var bar = Snackbar.Make(_root, exception.Message, Snackbar.LengthLong);
        bar.SetAction("Retry", view => { _ = LoadAsync(); });
        bar.Show();
    }

    private void ReturnToLogin()
    {
        var intent = new Intent(this, typeof(LoginActivity));
        intent.AddFlags(ActivityFlags.ClearTop | ActivityFlags.NewTask | ActivityFlags.ClearTask);
        StartActivity(intent);
        Finish();
    }

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
