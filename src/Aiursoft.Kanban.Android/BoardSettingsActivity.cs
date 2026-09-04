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
using Google.Android.Material.TextField;
using Color = Android.Graphics.Color;

namespace Aiursoft.Kanban.Android;

[Activity(Label = "Board settings", Exported = false, Theme = "@style/AppTheme")]
public sealed class BoardSettingsActivity : AppCompatActivity
{
    private const string BoardIdExtra = "board_id";
    private static readonly string[] ColumnStatuses = ["NotStarted", "InProgress", "Completed"];
    private static readonly string[] ColumnStatusLabels = ["Not started", "In progress", "Completed"];

    private View _root = null!;
    private global::AndroidX.Core.Widget.NestedScrollView _scroll = null!;
    private MaterialToolbar _toolbar = null!;
    private LinearLayout _ownerSettings = null!;
    private TextInputLayout _nameBox = null!;
    private TextInputEditText _name = null!;
    private TextInputLayout _orderBox = null!;
    private TextInputEditText _order = null!;
    private MaterialButton _saveBoard = null!;
    private MaterialButton _sharing = null!;
    private MaterialButton _addColumn = null!;
    private LinearLayout _columns = null!;
    private MaterialButton _deleteBoard = null!;
    private CircularProgressIndicator _progress = null!;
    private BoardDto? _board;
    private int _boardId;
    private bool _loaded;
    private bool _busy;

    private AppSession Session => ((KanbanApplication)Application!).Session;
    private KanbanApiClient Api => Session.RequireApi();

    public static Intent CreateIntent(Context context, int boardId) =>
        new Intent(context, typeof(BoardSettingsActivity)).PutExtra(BoardIdExtra, boardId);

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

        SetContentView(Resource.Layout.activity_board_settings);
        BindViews();
        ConfigureChrome();
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
        _root = FindViewById<View>(Resource.Id.board_settings_root)!;
        _scroll = FindViewById<global::AndroidX.Core.Widget.NestedScrollView>(
            Resource.Id.board_settings_scroll)!;
        _toolbar = FindViewById<MaterialToolbar>(Resource.Id.board_settings_toolbar)!;
        _ownerSettings = FindViewById<LinearLayout>(Resource.Id.board_owner_settings)!;
        _nameBox = FindViewById<TextInputLayout>(Resource.Id.board_name_box)!;
        _name = FindViewById<TextInputEditText>(Resource.Id.board_name_input)!;
        _orderBox = FindViewById<TextInputLayout>(Resource.Id.board_order_box)!;
        _order = FindViewById<TextInputEditText>(Resource.Id.board_order_input)!;
        _saveBoard = FindViewById<MaterialButton>(Resource.Id.save_board_button)!;
        _sharing = FindViewById<MaterialButton>(Resource.Id.manage_sharing_button)!;
        _addColumn = FindViewById<MaterialButton>(Resource.Id.add_column_button)!;
        _columns = FindViewById<LinearLayout>(Resource.Id.board_columns_list)!;
        _deleteBoard = FindViewById<MaterialButton>(Resource.Id.delete_board_button)!;
        _progress = FindViewById<CircularProgressIndicator>(Resource.Id.board_settings_progress)!;
    }

    private void ConfigureChrome()
    {
        _toolbar.SetOnApplyWindowInsetsListener(new ToolbarInsetListener(Dp(64)));
        _scroll.SetOnApplyWindowInsetsListener(
            new SystemBarInsetListener(0, 0, 0, Dp(12), false, true));
        _toolbar.NavigationContentDescription = "Back to board";
        _toolbar.NavigationClick += (_, _) => Finish();
    }

    private void WireEvents()
    {
        _saveBoard.Click += async (_, _) => await SaveBoardAsync();
        _sharing.Click += (_, _) => StartActivity(BoardSharingActivity.CreateIntent(this, _boardId));
        _addColumn.Click += (_, _) => ShowAddColumnDialog();
        _deleteBoard.Click += (_, _) => ConfirmDeleteBoard();
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
            var response = await Api.GetBoardAsync(_boardId);
            Render(response.Board);
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

    private void Render(BoardDto board)
    {
        _board = board;
        _toolbar.Subtitle = board.Name;
        _ownerSettings.Visibility = board.IsOwner ? ViewStates.Visible : ViewStates.Gone;
        _deleteBoard.Visibility = board.IsOwner ? ViewStates.Visible : ViewStates.Gone;
        _addColumn.Visibility = board.CanEdit ? ViewStates.Visible : ViewStates.Gone;
        _name.Text = board.Name;
        _order.Text = board.Order.ToString();
        _columns.RemoveAllViews();
        if (board.Columns.Count == 0)
        {
            var empty = Text(
                board.CanEdit ? "No columns yet. Add the first one here." : "This board has no columns.",
                14,
                Resource.Color.text_secondary);
            empty.SetPadding(Dp(4), Dp(18), Dp(4), Dp(18));
            _columns.AddView(empty);
            return;
        }

        var ordered = board.Columns.OrderBy(column => column.Order).ToList();
        for (var index = 0; index < ordered.Count; index++)
        {
            var layout = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MatchParent,
                ViewGroup.LayoutParams.WrapContent);
            layout.SetMargins(0, Dp(5), 0, Dp(5));
            _columns.AddView(ColumnCard(board, ordered[index], index, ordered.Count), layout);
        }
    }

    private View ColumnCard(BoardDto board, ColumnDto column, int index, int count)
    {
        var card = Surface();
        var body = new LinearLayout(this)
        {
            Orientation = global::Android.Widget.Orientation.Vertical
        };
        body.SetPadding(Dp(14), Dp(14), Dp(14), Dp(12));

        var heading = new LinearLayout(this)
        {
            Orientation = global::Android.Widget.Orientation.Horizontal
        };
        heading.SetGravity(GravityFlags.CenterVertical);
        heading.AddView(Text($"Column {index + 1}", 12, Resource.Color.text_secondary, true),
            new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1));
        heading.AddView(Text($"{column.Cards.Count} cards", 12, Resource.Color.text_secondary));
        body.AddView(heading);

        var nameBox = new TextInputLayout(this)
        {
            Hint = "Column name"
        };
        nameBox.BoxBackgroundMode = TextInputLayout.BoxBackgroundOutline;
        nameBox.SetBoxCornerRadii(Dp(12), Dp(12), Dp(12), Dp(12));
        var name = new TextInputEditText(this)
        {
            Text = column.Name,
            Enabled = board.CanEdit
        };
        name.SetSingleLine(true);
        nameBox.AddView(name);
        AddTo(body, nameBox, 10, 4);

        var status = new Spinner(this, SpinnerMode.Dialog)
        {
            Enabled = board.CanEdit
        };
        status.Adapter = new ArrayAdapter<string>(
            this,
            global::Android.Resource.Layout.SimpleSpinnerDropDownItem,
            ColumnStatusLabels);
        var statusIndex = Array.IndexOf(ColumnStatuses, column.Status);
        status.SetSelection(Math.Max(0, statusIndex));
        body.AddView(status, new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, Dp(50)));

        if (board.CanEdit)
        {
            var actions = new LinearLayout(this)
            {
                Orientation = global::Android.Widget.Orientation.Horizontal
            };
            var up = ActionButton("↑");
            up.ContentDescription = $"Move {column.Name} left";
            up.Enabled = index > 0;
            var down = ActionButton("↓");
            down.ContentDescription = $"Move {column.Name} right";
            down.Enabled = index < count - 1;
            var save = PrimaryButton("Save");
            var delete = DangerButton("Delete");
            actions.AddView(up, new LinearLayout.LayoutParams(0, Dp(46), 0.55f));
            actions.AddView(down, new LinearLayout.LayoutParams(0, Dp(46), 0.55f));
            actions.AddView(save, new LinearLayout.LayoutParams(0, Dp(46), 1.15f));
            actions.AddView(delete, new LinearLayout.LayoutParams(0, Dp(46), 1.15f));
            AddTo(body, actions, 8, 0);

            up.Click += async (_, _) => await MoveColumnAsync(column, index - 1);
            down.Click += async (_, _) => await MoveColumnAsync(column, index + 1);
            save.Click += async (_, _) => await SaveColumnAsync(
                column,
                nameBox,
                name.Text ?? string.Empty,
                ColumnStatuses[status.SelectedItemPosition]);
            delete.Click += (_, _) => ConfirmDeleteColumn(column);
        }

        card.AddView(body);
        return card;
    }

    private async Task SaveBoardAsync()
    {
        if (_busy || _board == null || !_board.IsOwner)
        {
            return;
        }
        var name = _name.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(name))
        {
            _nameBox.Error = "Enter a board name";
            return;
        }
        if (!int.TryParse(_order.Text, out var order))
        {
            _orderBox.Error = "Enter a whole number";
            return;
        }

        try
        {
            _nameBox.Error = null;
            _orderBox.Error = null;
            SetBusy(true, showProgress: true);
            var response = await Api.UpdateBoardAsync(_boardId, new UpdateBoardRequest
            {
                Name = name,
                Order = order
            });
            Render(response.Board);
            Snackbar.Make(_root, "Board settings saved", Snackbar.LengthShort).Show();
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

    private async Task SaveColumnAsync(
        ColumnDto column,
        TextInputLayout nameBox,
        string name,
        string status)
    {
        if (_busy)
        {
            return;
        }
        name = name.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            nameBox.Error = "Enter a column name";
            return;
        }
        try
        {
            nameBox.Error = null;
            SetBusy(true, showProgress: true);
            var response = await Api.UpdateColumnAsync(column.Id, new UpdateColumnRequest
            {
                Name = name,
                Status = status
            });
            Render(response.Board);
            Snackbar.Make(_root, "Column updated", Snackbar.LengthShort).Show();
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

    private async Task MoveColumnAsync(ColumnDto column, int newOrder)
    {
        if (_busy)
        {
            return;
        }
        try
        {
            SetBusy(true, showProgress: false);
            var response = await Api.MoveColumnAsync(column.Id, newOrder);
            Render(response.Board);
        }
        catch (Exception exception)
        {
            ShowError(exception);
        }
        finally
        {
            SetBusy(false, showProgress: false);
        }
    }

    private void ShowAddColumnDialog()
    {
        if (_busy || _board?.CanEdit != true)
        {
            return;
        }
        var box = new TextInputLayout(this) { Hint = "Column name" };
        box.BoxBackgroundMode = TextInputLayout.BoxBackgroundOutline;
        var input = new TextInputEditText(this);
        input.SetSingleLine(true);
        box.AddView(input);
        var wrapper = new FrameLayout(this);
        wrapper.SetPadding(Dp(24), Dp(4), Dp(24), 0);
        wrapper.AddView(box);
        var builder = new MaterialAlertDialogBuilder(this);
        builder.SetTitle("Add column");
        builder.SetView(wrapper);
        builder.SetNegativeButton("Cancel", (_, _) => { });
        builder.SetPositiveButton("Add", (_, _) => { });
        var dialog = builder.Create() ?? throw new InvalidOperationException("Could not create dialog.");
        dialog.Show();
        dialog.GetButton((int)DialogButtonType.Positive)!.Click += async (_, _) =>
        {
            var value = input.Text?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(value))
            {
                box.Error = "Enter a column name";
                return;
            }
            try
            {
                dialog.GetButton((int)DialogButtonType.Positive)!.Enabled = false;
                var response = await Api.CreateColumnAsync(_boardId, new CreateColumnRequest { Name = value });
                dialog.Dismiss();
                Render(response.Board);
                Snackbar.Make(_root, "Column added", Snackbar.LengthShort).Show();
            }
            catch (Exception exception)
            {
                dialog.GetButton((int)DialogButtonType.Positive)!.Enabled = true;
                box.Error = FriendlyMessage(exception);
            }
        };
    }

    private void ConfirmDeleteColumn(ColumnDto column)
    {
        var builder = new MaterialAlertDialogBuilder(this);
        builder.SetTitle("Delete column?");
        builder.SetMessage(column.Cards.Count == 0
            ? $"Delete {column.Name}? This cannot be undone."
            : $"Delete {column.Name} and its {column.Cards.Count} card(s)? This cannot be undone.");
        builder.SetNegativeButton("Cancel", (_, _) => { });
        builder.SetPositiveButton("Delete", (_, _) => _ = DeleteColumnAsync(column));
        builder.Show();
    }

    private async Task DeleteColumnAsync(ColumnDto column)
    {
        if (_busy)
        {
            return;
        }
        try
        {
            SetBusy(true, showProgress: true);
            var response = await Api.DeleteColumnAsync(column.Id);
            Render(response.Board);
            Snackbar.Make(_root, "Column deleted", Snackbar.LengthShort).Show();
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

    private void ConfirmDeleteBoard()
    {
        var board = _board;
        if (board == null || !board.IsOwner || _busy)
        {
            return;
        }
        var builder = new MaterialAlertDialogBuilder(this);
        builder.SetTitle("Delete board permanently?");
        builder.SetMessage(
            $"Delete {board.Name}, all {board.ColumnCount} columns, and all {board.CardCount} cards? This cannot be undone.");
        builder.SetNegativeButton("Cancel", (_, _) => { });
        builder.SetPositiveButton("Delete", (_, _) => _ = DeleteBoardAsync());
        builder.Show();
    }

    private async Task DeleteBoardAsync()
    {
        if (_busy)
        {
            return;
        }
        try
        {
            SetBusy(true, showProgress: true);
            await Api.DeleteBoardAsync(_boardId);
            Session.SelectedBoardId = 0;
            Toast.MakeText(this, "Board deleted", ToastLength.Short)?.Show();
            Finish();
        }
        catch (Exception exception)
        {
            ShowError(exception);
            SetBusy(false, showProgress: true);
        }
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

    private MaterialButton PrimaryButton(string label)
    {
        var button = new MaterialButton(this) { Text = label, TextSize = 13, CornerRadius = Dp(12) };
        button.SetAllCaps(false);
        return button;
    }

    private MaterialButton ActionButton(string label)
    {
        var button = new MaterialButton(this, null, global::Android.Resource.Attribute.BorderlessButtonStyle)
        {
            Text = label,
            TextSize = 13,
            CornerRadius = Dp(12)
        };
        button.SetAllCaps(false);
        button.SetTextColor(ColorStateList.ValueOf(ColorOf(Resource.Color.brand_primary)));
        return button;
    }

    private MaterialButton DangerButton(string label)
    {
        var button = ActionButton(label);
        button.SetTextColor(ColorStateList.ValueOf(ColorOf(Resource.Color.on_danger_container)));
        return button;
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

    private void AddTo(LinearLayout parent, View view, int top, int bottom)
    {
        var layout = new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent,
            ViewGroup.LayoutParams.WrapContent);
        layout.SetMargins(0, Dp(top), 0, Dp(bottom));
        parent.AddView(view, layout);
    }

    private void SetBusy(bool busy, bool showProgress)
    {
        _busy = busy;
        _progress.Visibility = busy && showProgress ? ViewStates.Visible : ViewStates.Gone;
        _scroll.Alpha = busy ? 0.55f : 1f;
        _saveBoard.Enabled = !busy;
        _sharing.Enabled = !busy;
        _addColumn.Enabled = !busy;
        _deleteBoard.Enabled = !busy;
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
