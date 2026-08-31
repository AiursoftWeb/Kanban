using Android.App;
using Android.Content;
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.OS;
using Android.Views;
using Android.Views.InputMethods;
using Android.Widget;
using AndroidX.AppCompat.App;
using AndroidX.Activity;
using AndroidX.Core.View;
using AndroidX.DrawerLayout.Widget;
using Aiursoft.Kanban.Android.Oidc;
using Aiursoft.Kanban.SDK;
using Aiursoft.Kanban.SDK.Models;
using Google.Android.Material.BottomSheet;
using Google.Android.Material.AppBar;
using Google.Android.Material.Button;
using Google.Android.Material.Card;
using Google.Android.Material.Dialog;
using Google.Android.Material.FloatingActionButton;
using Google.Android.Material.ProgressIndicator;
using Google.Android.Material.Snackbar;
using Google.Android.Material.TextField;
using ColorStateList = Android.Content.Res.ColorStateList;

namespace Aiursoft.Kanban.Android;

[Activity(Label = "Kanban", Exported = false, Theme = "@style/AppTheme")]
public sealed class MainActivity : AppCompatActivity
{
    private DrawerLayout _drawer = null!;
    private MaterialToolbar _toolbar = null!;
    private View _workspace = null!;
    private HorizontalScrollView _boardScroll = null!;
    private LinearLayout _boardCanvas = null!;
    private LinearLayout _emptyState = null!;
    private TextView _emptyTitle = null!;
    private TextView _emptyMessage = null!;
    private CircularProgressIndicator _progress = null!;
    private ExtendedFloatingActionButton _newCard = null!;
    private LinearLayout _myBoards = null!;
    private LinearLayout _sharedBoards = null!;
    private MaterialButton _newBoard = null!;
    private MaterialButton _signOut = null!;
    private TextView _drawerServer = null!;
    private List<BoardSummaryDto> _boards = [];
    private BoardDto? _board;
    private bool _loaded;

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

        SetContentView(Resource.Layout.activity_main);
        BindViews();
        ConfigureChrome();
        WireEvents();
        OnBackPressedDispatcher.AddCallback(this, new DrawerBackCallback(_drawer, OnBackPressedDispatcher));
        _ = LoadBoardsAsync(Session.SelectedBoardId);
    }

    protected override void OnResume()
    {
        base.OnResume();
        if (_loaded && Session.IsAuthenticated)
        {
            _ = LoadBoardAsync(Session.SelectedBoardId, showProgress: false);
        }
    }

    private void BindViews()
    {
        _drawer = FindViewById<DrawerLayout>(Resource.Id.drawer_layout)!;
        _toolbar = FindViewById<MaterialToolbar>(Resource.Id.toolbar)!;
        _workspace = FindViewById<View>(Resource.Id.workspace)!;
        _boardScroll = FindViewById<HorizontalScrollView>(Resource.Id.board_scroll)!;
        _boardCanvas = FindViewById<LinearLayout>(Resource.Id.board_canvas)!;
        _emptyState = FindViewById<LinearLayout>(Resource.Id.empty_state)!;
        _emptyTitle = FindViewById<TextView>(Resource.Id.empty_title)!;
        _emptyMessage = FindViewById<TextView>(Resource.Id.empty_message)!;
        _progress = FindViewById<CircularProgressIndicator>(Resource.Id.main_progress)!;
        _newCard = FindViewById<ExtendedFloatingActionButton>(Resource.Id.new_card_fab)!;
        _myBoards = FindViewById<LinearLayout>(Resource.Id.my_boards_list)!;
        _sharedBoards = FindViewById<LinearLayout>(Resource.Id.shared_boards_list)!;
        _newBoard = FindViewById<MaterialButton>(Resource.Id.new_board_button)!;
        _signOut = FindViewById<MaterialButton>(Resource.Id.sign_out_button)!;
        _drawerServer = FindViewById<TextView>(Resource.Id.drawer_server)!;
    }

    private void ConfigureChrome()
    {
        _toolbar.SetOnApplyWindowInsetsListener(new ToolbarInsetListener(Dp(64)));
        FindViewById<View>(Resource.Id.drawer_header)!
            .SetOnApplyWindowInsetsListener(new SystemBarInsetListener(Dp(24), Dp(28), Dp(20), Dp(22), true, false));
        FindViewById<View>(Resource.Id.drawer_content)!
            .SetOnApplyWindowInsetsListener(new SystemBarInsetListener(0, 0, 0, 0, false, true));
        _drawerServer.Text = new Uri(Session.Endpoint).Authority;
        _toolbar.NavigationContentDescription = "Open boards";
        _toolbar.InflateMenu(Resource.Menu.main_toolbar);
        _toolbar.NavigationClick += (_, _) => _drawer.OpenDrawer(GravityCompat.Start);
        _toolbar.MenuItemClick += (_, args) =>
        {
            if (args.Item?.ItemId == Resource.Id.action_refresh)
            {
                _ = LoadBoardsAsync(Session.SelectedBoardId, showProgress: false);
                args.Handled = true;
            }
        };
    }

    private void WireEvents()
    {
        _newBoard.Click += (_, _) => ShowNewBoardDialog();
        _newCard.Click += (_, _) => ShowNewCardSheet();
        _signOut.Click += (_, _) =>
        {
            Session.SignOut();
            ReturnToLogin();
        };
    }

    private async Task LoadBoardsAsync(int preferredBoardId = 0, bool showProgress = true)
    {
        try
        {
            SetLoading(showProgress);
            var response = await Api.GetBoardsAsync();
            _boards = response.Boards;
            RenderDrawer();
            _loaded = true;

            if (_boards.Count == 0)
            {
                Session.SelectedBoardId = 0;
                _board = null;
                ShowEmpty("No boards yet", "Open the menu to create your first board.");
                _toolbar.Title = "Kanban";
                _toolbar.Subtitle = "Your workspace";
                return;
            }

            var selected = _boards.FirstOrDefault(board => board.Id == preferredBoardId) ?? _boards[0];
            await LoadBoardAsync(selected.Id, showProgress: false);
        }
        catch (Exception exception)
        {
            ShowEmpty("Could not load boards", "Check the connection, then try again.");
            ShowError(exception, retryBoards: true);
        }
        finally
        {
            SetLoading(false);
        }
    }

    private async Task LoadBoardAsync(int boardId, bool showProgress = true)
    {
        if (boardId <= 0)
        {
            return;
        }
        try
        {
            SetLoading(showProgress);
            var response = await Api.GetBoardAsync(boardId);
            _board = response.Board;
            Session.SelectedBoardId = boardId;
            RenderBoard(response.Board);
            RenderDrawer();
        }
        catch (Exception exception)
        {
            ShowError(exception, retryBoards: false);
        }
        finally
        {
            SetLoading(false);
        }
    }

    private void RenderDrawer()
    {
        _myBoards.RemoveAllViews();
        _sharedBoards.RemoveAllViews();
        AddBoardGroup(_myBoards, _boards.Where(board => board.IsOwner).ToList(), "No boards created yet");
        AddBoardGroup(_sharedBoards, _boards.Where(board => !board.IsOwner).ToList(), "Nothing shared with you yet");
    }

    private void AddBoardGroup(LinearLayout container, IReadOnlyList<BoardSummaryDto> boards, string emptyText)
    {
        if (boards.Count == 0)
        {
            var empty = Text(emptyText, 13, Resource.Color.text_secondary);
            empty.SetPadding(Dp(24), Dp(10), Dp(18), Dp(12));
            container.AddView(empty);
            return;
        }

        foreach (var board in boards)
        {
            var selected = board.Id == Session.SelectedBoardId;
            var row = new LinearLayout(this)
            {
                Orientation = global::Android.Widget.Orientation.Horizontal,
                Clickable = true,
                Focusable = true
            };
            row.SetGravity(GravityFlags.CenterVertical);
            row.SetPadding(Dp(20), Dp(11), Dp(14), Dp(11));
            row.Background = Rounded(
                ColorOf(selected ? Resource.Color.brand_container : global::Android.Resource.Color.Transparent),
                14);

            var icon = new ImageView(this);
            icon.SetImageResource(Resource.Drawable.ic_board);
            icon.ImageTintList = ColorStateList.ValueOf(ColorOf(
                selected ? Resource.Color.brand_primary : Resource.Color.text_secondary));
            row.AddView(icon, new LinearLayout.LayoutParams(Dp(24), Dp(24)));

            var labels = new LinearLayout(this) { Orientation = global::Android.Widget.Orientation.Vertical };
            var title = Text(board.Name, 15, selected ? Resource.Color.on_brand_container : Resource.Color.text_primary, true);
            title.SetSingleLine(true);
            title.Ellipsize = global::Android.Text.TextUtils.TruncateAt.End;
            labels.AddView(title);
            var access = board.CanEdit ? "Can edit" : "View only";
            labels.AddView(Text($"{board.CardCount} cards · {access}", 12, Resource.Color.text_secondary));
            var labelLayout = new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1);
            labelLayout.SetMargins(Dp(14), 0, 0, 0);
            row.AddView(labels, labelLayout);
            row.ContentDescription = $"{board.Name}, {access}";
            row.Click += async (_, _) =>
            {
                _drawer.CloseDrawer(GravityCompat.Start);
                await LoadBoardAsync(board.Id);
            };
            var rowLayout = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent);
            rowLayout.SetMargins(Dp(8), Dp(2), Dp(8), Dp(2));
            container.AddView(row, rowLayout);
        }
    }

    private void RenderBoard(BoardDto board)
    {
        _boardCanvas.RemoveAllViews();
        _toolbar.Title = board.Name;
        _toolbar.Subtitle = board.IsOwner
            ? (board.CanEdit ? "Your board · drag cards to move" : "Your board · view only")
            : (board.CanEdit ? "Shared with you · can edit" : "Shared with you · view only");
        _newCard.Visibility = board.CanEdit && board.Columns.Count > 0 ? ViewStates.Visible : ViewStates.Gone;
        _emptyState.Visibility = ViewStates.Gone;
        _boardScroll.Visibility = ViewStates.Visible;

        if (board.Columns.Count == 0)
        {
            ShowEmpty("This board has no columns", board.CanEdit
                ? "Add columns on the web app, then refresh."
                : "The owner has not added any columns yet.");
            return;
        }

        foreach (var column in board.Columns.OrderBy(column => column.Order))
        {
            _boardCanvas.AddView(CreateColumn(board, column), ColumnLayout());
        }
        _boardScroll.Post(() => _boardScroll.SmoothScrollTo(0, 0));
    }

    private View CreateColumn(BoardDto board, ColumnDto column)
    {
        var shell = new MaterialCardView(this)
        {
            Radius = Dp(20),
            CardElevation = 0
        };
        shell.SetCardBackgroundColor(GetColor(Resource.Color.surface_variant));
        shell.StrokeColor = GetColor(Resource.Color.outline);
        shell.StrokeWidth = Dp(1);

        var content = new LinearLayout(this) { Orientation = global::Android.Widget.Orientation.Vertical };
        content.SetPadding(Dp(14), Dp(14), Dp(14), Dp(16));

        var header = new LinearLayout(this)
        {
            Orientation = global::Android.Widget.Orientation.Horizontal
        };
        header.SetGravity(GravityFlags.CenterVertical);
        var title = Text(column.Name, 17, Resource.Color.text_primary, true);
        header.AddView(title, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1));
        var count = Text(column.Cards.Count.ToString(), 12, Resource.Color.on_brand_container, true);
        count.Gravity = GravityFlags.Center;
        count.Background = Rounded(ColorOf(Resource.Color.brand_container), 14);
        header.AddView(count, new LinearLayout.LayoutParams(Dp(30), Dp(30)));
        content.AddView(header);

        if (board.CanEdit)
        {
            var add = new MaterialButton(this, null, global::Android.Resource.Attribute.BorderlessButtonStyle)
            {
                Text = "+  Add card",
                Gravity = GravityFlags.Start | GravityFlags.CenterVertical
            };
            add.SetAllCaps(false);
            add.SetTextColor(ColorStateList.ValueOf(ColorOf(Resource.Color.brand_primary)));
            add.Click += (_, _) => ShowNewCardSheet(column.Id);
            var addLayout = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MatchParent, Dp(46));
            addLayout.SetMargins(0, Dp(6), 0, Dp(4));
            content.AddView(add, addLayout);
        }

        var cards = new LinearLayout(this)
        {
            Orientation = global::Android.Widget.Orientation.Vertical
        };
        cards.SetGravity(GravityFlags.Top);
        foreach (var card in column.Cards.OrderBy(card => card.Order))
        {
            cards.AddView(CreateCard(board, card), CardLayout());
        }
        if (column.Cards.Count == 0)
        {
            var hint = Text(board.CanEdit ? "Drop a card here" : "No cards", 13, Resource.Color.text_secondary);
            hint.Gravity = GravityFlags.Center;
            hint.SetPadding(Dp(8), Dp(32), Dp(8), Dp(32));
            cards.AddView(hint, new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent));
        }
        content.AddView(cards, new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, 0, 1));
        shell.AddView(content);

        if (board.CanEdit)
        {
            var listener = new ColumnDropListener(
                shell,
                ColorOf(Resource.Color.outline),
                ColorOf(Resource.Color.brand_primary),
                cardId => MoveCardAsync(cardId, column.Id, column.Cards.Count));
            shell.SetOnDragListener(listener);
            cards.SetOnDragListener(listener);
        }
        return shell;
    }

    private View CreateCard(BoardDto board, CardDto card)
    {
        var shell = new MaterialCardView(this)
        {
            Radius = Dp(16),
            CardElevation = Dp(1),
            Clickable = true,
            LongClickable = board.CanEdit
        };
        shell.SetCardBackgroundColor(GetColor(Resource.Color.surface));
        shell.StrokeColor = GetColor(Resource.Color.outline);
        shell.StrokeWidth = Dp(1);

        var row = new LinearLayout(this)
        {
            Orientation = global::Android.Widget.Orientation.Horizontal
        };
        row.SetGravity(GravityFlags.CenterVertical);
        row.SetPadding(Dp(14), Dp(14), Dp(10), Dp(14));
        var label = Text(card.Title, 15, Resource.Color.text_primary, true);
        label.SetMaxLines(4);
        row.AddView(label, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1));
        if (board.CanEdit)
        {
            var handle = new ImageView(this)
            {
                ContentDescription = $"Hold to move {card.Title}",
                LongClickable = true,
                Clickable = true
            };
            handle.SetImageResource(Resource.Drawable.ic_drag_handle);
            handle.ImageTintList = ColorStateList.ValueOf(ColorOf(Resource.Color.text_secondary));
            handle.SetPadding(Dp(9), Dp(8), Dp(7), Dp(8));
            row.AddView(handle, new LinearLayout.LayoutParams(Dp(44), Dp(44)));
            handle.LongClick += (_, args) =>
            {
                StartCardDrag(shell, card);
                args.Handled = true;
            };
            shell.LongClick += (_, args) =>
            {
                StartCardDrag(shell, card);
                args.Handled = true;
            };
        }
        shell.AddView(row);
        shell.ContentDescription = board.CanEdit
            ? $"{card.Title}. Hold and drag to move."
            : card.Title;
        return shell;
    }

    private void StartCardDrag(View cardView, CardDto card)
    {
        cardView.PerformHapticFeedback(FeedbackConstants.LongPress);
        cardView.Alpha = 0.4f;
        var data = ClipData.NewPlainText("kanban-card-id", card.Id.ToString());
        var shadow = new View.DragShadowBuilder(cardView);
        if (OperatingSystem.IsAndroidVersionAtLeast(24))
        {
            cardView.StartDragAndDrop(data, shadow, cardView, 0);
        }
        else
        {
#pragma warning disable CS0618
            cardView.StartDrag(data, shadow, cardView, 0);
#pragma warning restore CS0618
        }
        Snackbar.Make(_workspace, "Drop on another column to move", Snackbar.LengthShort).Show();
    }

    private async Task MoveCardAsync(int cardId, int columnId, int order)
    {
        try
        {
            await Api.MoveCardAsync(cardId, new MoveCardRequest
            {
                TargetColumnId = columnId,
                NewOrder = order
            });
            await LoadBoardAsync(Session.SelectedBoardId, showProgress: false);
            Snackbar.Make(_workspace, "Card moved", Snackbar.LengthShort).Show();
        }
        catch (Exception exception)
        {
            ShowError(exception, retryBoards: false);
        }
    }

    private void ShowNewCardSheet(int? initialColumnId = null)
    {
        var board = _board;
        if (board == null || !board.CanEdit || board.Columns.Count == 0)
        {
            return;
        }

        var dialog = new BottomSheetDialog(this);
        var content = new LinearLayout(this) { Orientation = global::Android.Widget.Orientation.Vertical };
        content.SetPadding(Dp(24), Dp(18), Dp(24), Dp(24));
        content.AddView(Text("Create a card", 24, Resource.Color.text_primary, true));
        content.AddView(Text("Capture it now. Organize it by dragging later.", 14, Resource.Color.text_secondary));

        var titleBox = new TextInputLayout(this) { Hint = "Card title" };
        titleBox.BoxBackgroundMode = TextInputLayout.BoxBackgroundOutline;
        titleBox.SetBoxCornerRadii(Dp(14), Dp(14), Dp(14), Dp(14));
        var title = new TextInputEditText(this);
        title.SetSingleLine(true);
        title.ImeOptions = ImeAction.Done;
        titleBox.AddView(title);
        var titleLayout = new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent);
        titleLayout.SetMargins(0, Dp(18), 0, Dp(10));
        content.AddView(titleBox, titleLayout);

        content.AddView(Text("COLUMN", 12, Resource.Color.text_secondary, true));
        var spinner = new Spinner(this, SpinnerMode.Dialog);
        var columns = board.Columns.OrderBy(column => column.Order).ToList();
        spinner.Adapter = new ArrayAdapter<string>(this,
            global::Android.Resource.Layout.SimpleSpinnerDropDownItem,
            columns.Select(column => column.Name).ToArray());
        var initialIndex = initialColumnId.HasValue
            ? Math.Max(0, columns.FindIndex(column => column.Id == initialColumnId.Value))
            : 0;
        spinner.SetSelection(initialIndex);
        content.AddView(spinner, new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, Dp(52)));

        var create = new MaterialButton(this)
        {
            Text = "Create card",
            TextSize = 16
        };
        create.SetAllCaps(false);
        create.CornerRadius = Dp(16);
        var createLayout = new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, Dp(56));
        createLayout.SetMargins(0, Dp(14), 0, 0);
        content.AddView(create, createLayout);
        create.Click += async (_, _) =>
        {
            var value = title.Text?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(value))
            {
                titleBox.Error = "Enter a card title";
                return;
            }
            create.Enabled = false;
            try
            {
                await Api.CreateCardAsync(columns[spinner.SelectedItemPosition].Id,
                    new CreateCardRequest { Title = value });
                dialog.Dismiss();
                await LoadBoardAsync(board.Id, showProgress: false);
                Snackbar.Make(_workspace, "Card created", Snackbar.LengthShort).Show();
            }
            catch (Exception exception)
            {
                create.Enabled = true;
                titleBox.Error = FriendlyMessage(exception);
            }
        };
        title.EditorAction += (_, args) =>
        {
            if (args.ActionId == ImeAction.Done)
            {
                create.PerformClick();
            }
        };
        dialog.SetContentView(content);
        dialog.SetOnShowListener(new BottomSheetShowListener(title));
        dialog.Show();
    }

    private void ShowNewBoardDialog()
    {
        var box = new TextInputLayout(this) { Hint = "Board name" };
        box.BoxBackgroundMode = TextInputLayout.BoxBackgroundOutline;
        var input = new TextInputEditText(this);
        input.SetSingleLine(true);
        box.AddView(input);
        var wrapper = new FrameLayout(this);
        wrapper.SetPadding(Dp(24), Dp(4), Dp(24), 0);
        wrapper.AddView(box);
        var builder = new MaterialAlertDialogBuilder(this);
        builder.SetTitle("New board");
        builder.SetMessage("Create a private board. You can share it from the web app.");
        builder.SetView(wrapper);
        builder.SetNegativeButton("Cancel", (_, _) => { });
        builder.SetPositiveButton("Create", (_, _) => { });
        var dialog = builder.Create();
        dialog.Show();
        dialog.GetButton((int)DialogButtonType.Positive)!.Click += async (_, _) =>
        {
            var value = input.Text?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(value))
            {
                box.Error = "Enter a board name";
                return;
            }
            try
            {
                var response = await Api.CreateBoardAsync(new CreateBoardRequest { Name = value });
                dialog.Dismiss();
                _drawer.CloseDrawer(GravityCompat.Start);
                await LoadBoardsAsync(response.Board.Id);
            }
            catch (Exception exception)
            {
                box.Error = FriendlyMessage(exception);
            }
        };
    }

    private void ShowEmpty(string title, string message)
    {
        _boardCanvas.RemoveAllViews();
        _boardScroll.Visibility = ViewStates.Gone;
        _newCard.Visibility = ViewStates.Gone;
        _emptyTitle.Text = title;
        _emptyMessage.Text = message;
        _emptyState.Visibility = ViewStates.Visible;
    }

    private void SetLoading(bool loading)
    {
        _progress.Visibility = loading ? ViewStates.Visible : ViewStates.Gone;
        _newBoard.Enabled = !loading;
        _newCard.Enabled = !loading;
    }

    private void ShowError(Exception exception, bool retryBoards)
    {
        if (exception is KanbanAuthenticationRequiredException)
        {
            ReturnToLogin();
            return;
        }

        var bar = Snackbar.Make(_workspace, FriendlyMessage(exception), Snackbar.LengthLong);
        bar.SetAction("Retry", ignoredView =>
        {
            if (retryBoards)
            {
                _ = LoadBoardsAsync(Session.SelectedBoardId);
            }
            else
            {
                _ = LoadBoardAsync(Session.SelectedBoardId);
            }
        });
        bar.Show();
    }

    private void ReturnToLogin()
    {
        var intent = new Intent(this, typeof(LoginActivity));
        intent.AddFlags(global::Android.Content.ActivityFlags.ClearTop |
                        global::Android.Content.ActivityFlags.NewTask |
                        global::Android.Content.ActivityFlags.ClearTask);
        StartActivity(intent);
        Finish();
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

    private Color ColorOf(int colorResource) => new(GetColor(colorResource));

    private GradientDrawable Rounded(Color color, int radius)
    {
        var drawable = new GradientDrawable();
        drawable.SetColor(color);
        drawable.SetCornerRadius(Dp(radius));
        return drawable;
    }

    private LinearLayout.LayoutParams ColumnLayout()
    {
        var layout = new LinearLayout.LayoutParams(Dp(300), ViewGroup.LayoutParams.MatchParent);
        layout.SetMargins(Dp(6), 0, Dp(6), 0);
        return layout;
    }

    private LinearLayout.LayoutParams CardLayout()
    {
        var layout = new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent);
        layout.SetMargins(0, Dp(5), 0, Dp(5));
        return layout;
    }

    private int Dp(int value) => (int)Math.Round(value * Resources!.DisplayMetrics!.Density);

    private static string FriendlyMessage(Exception exception)
    {
        var message = exception.Message;
        return message.Length > 150 ? message[..150] : message;
    }

    private sealed class ColumnDropListener(
        MaterialCardView target,
        Color normalColor,
        Color activeColor,
        Func<int, Task> onDrop) : Java.Lang.Object, View.IOnDragListener
    {
        public bool OnDrag(View? view, DragEvent? dragEvent)
        {
            if (dragEvent == null)
            {
                return false;
            }
            switch (dragEvent.Action)
            {
                case DragAction.Started:
                    return dragEvent.ClipDescription?.HasMimeType(ClipDescription.MimetypeTextPlain) == true;
                case DragAction.Entered:
                    target.StrokeColor = activeColor;
                    target.StrokeWidth = 4;
                    target.ScaleX = 1.01f;
                    target.ScaleY = 1.01f;
                    return true;
                case DragAction.Exited:
                    RestoreTarget();
                    return true;
                case DragAction.Drop:
                    RestoreTarget();
                    var text = dragEvent.ClipData?.GetItemAt(0)?.Text?.ToString();
                    if (int.TryParse(text, out var cardId))
                    {
                        _ = onDrop(cardId);
                        return true;
                    }
                    return false;
                case DragAction.Ended:
                    RestoreTarget();
                    if (dragEvent.LocalState is View source)
                    {
                        source.Alpha = 1;
                    }
                    return true;
                default:
                    return true;
            }
        }

        private void RestoreTarget()
        {
            target.StrokeColor = normalColor;
            target.StrokeWidth = 1;
            target.ScaleX = 1;
            target.ScaleY = 1;
        }
    }

    private sealed class BottomSheetShowListener(View focus) : Java.Lang.Object, IDialogInterfaceOnShowListener
    {
        public void OnShow(IDialogInterface? dialog)
        {
            focus.RequestFocus();
            focus.PostDelayed(() =>
            {
                var manager = (InputMethodManager?)focus.Context?.GetSystemService(InputMethodService);
                manager?.ShowSoftInput(focus, ShowFlags.Implicit);
            }, 180);
        }
    }

    private sealed class ToolbarInsetListener(int contentHeight) : Java.Lang.Object, View.IOnApplyWindowInsetsListener
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

    private sealed class DrawerBackCallback(
        DrawerLayout drawer,
        OnBackPressedDispatcher dispatcher) : OnBackPressedCallback(true)
    {
        public override void HandleOnBackPressed()
        {
            if (drawer.IsDrawerOpen(GravityCompat.Start))
            {
                drawer.CloseDrawer(GravityCompat.Start);
                return;
            }
            Enabled = false;
            dispatcher.OnBackPressed();
            Enabled = true;
        }
    }
}
