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
public sealed partial class MainActivity : AppCompatActivity
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
    private MaterialButton _archivedBoards = null!;
    private MaterialButton _newBoard = null!;
    private MaterialButton _dashboard = null!;
    private MaterialButton _globalSearch = null!;
    private MaterialButton _aiAssistant = null!;
    private MaterialButton _dailyReports = null!;
    private MaterialButton _weeklyReports = null!;
    private MaterialButton _myTasks = null!;
    private MaterialButton _notifications = null!;
    private MaterialButton _profileSettings = null!;
    private MaterialButton _operationLogs = null!;
    private MaterialButton _signOut = null!;
    private TextView _drawerServer = null!;
    private List<BoardSummaryDto> _boards = [];
    private BoardDto? _board;
    private string _boardQuery = string.Empty;
    private readonly HashSet<string> _boardPriorities = [];
    private readonly HashSet<string> _boardAssigneeIds = [];
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
        ConfigureWorkspace(savedInstanceState);
        OnBackPressedDispatcher.AddCallback(this, new DrawerBackCallback(_drawer, OnBackPressedDispatcher, () => { if (!_showingHome) { ShowBoardsHome(); return true; } return false; }));
        _ = LoadBoardsAsync(Session.SelectedBoardId);
    }

    protected override void OnResume()
    {
        base.OnResume();
        if (_loaded && Session.IsAuthenticated)
        {
            _ = LoadBoardsAsync(Session.SelectedBoardId, showProgress: false);
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
        _archivedBoards = FindViewById<MaterialButton>(Resource.Id.archived_boards_button)!;
        _newBoard = FindViewById<MaterialButton>(Resource.Id.new_board_button)!;
        _dashboard = FindViewById<MaterialButton>(Resource.Id.dashboard_button)!;
        _globalSearch = FindViewById<MaterialButton>(Resource.Id.global_search_button)!;
        _aiAssistant = FindViewById<MaterialButton>(Resource.Id.ai_assistant_button)!;
        _dailyReports = FindViewById<MaterialButton>(Resource.Id.daily_reports_button)!;
        _weeklyReports = FindViewById<MaterialButton>(Resource.Id.weekly_reports_button)!;
        _myTasks = FindViewById<MaterialButton>(Resource.Id.my_tasks_button)!;
        _notifications = FindViewById<MaterialButton>(Resource.Id.notifications_button)!;
        _profileSettings = FindViewById<MaterialButton>(Resource.Id.profile_settings_button)!;
        _operationLogs = FindViewById<MaterialButton>(Resource.Id.operation_logs_button)!;
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
        _toolbar.NavigationContentDescription = UiText.Get("Open boards");
        _toolbar.InflateMenu(Resource.Menu.main_toolbar);
        _toolbar.NavigationClick += (_, _) => { if (_showingHome) _drawer.OpenDrawer(GravityCompat.Start); else ShowBoardsHome(); };
        _toolbar.MenuItemClick += (_, args) =>
        {
            if (args.Item?.ItemId == Resource.Id.action_refresh)
            {
                _ = LoadBoardsAsync(Session.SelectedBoardId, showProgress: false);
                args.Handled = true;
            }
            else if (args.Item?.ItemId == Resource.Id.action_archive_board)
            {
                ShowArchiveBoardDialog();
                args.Handled = true;
            }
            else if (args.Item?.ItemId == Resource.Id.action_add_column)
            {
                ShowNewColumnDialog();
                args.Handled = true;
            }
            else if (args.Item?.ItemId == Resource.Id.action_board_settings)
            {
                OpenBoardSettings();
                args.Handled = true;
            }
            else if (args.Item?.ItemId == Resource.Id.action_share_board)
            {
                OpenBoardSharing();
                args.Handled = true;
            }
            else if (args.Item?.ItemId == Resource.Id.action_gantt)
            {
                OpenGantt();
                args.Handled = true;
            }
            else if (args.Item?.ItemId == Resource.Id.action_filter_board)
            {
                ShowBoardFilterSheet();
                args.Handled = true;
            }
        };
    }

    private void WireEvents()
    {
        _newBoard.Click += (_, _) => ShowNewBoardDialog();
        _newCard.Click += (_, _) => ShowNewCardSheet();
        _archivedBoards.Click += (_, _) => OpenArchivedBoards();
        _dashboard.Click += (_, _) => OpenDashboard();
        _globalSearch.Click += (_, _) => OpenGlobalSearch();
        _aiAssistant.Click += (_, _) => OpenAiAssistant();
        _dailyReports.Click += (_, _) => OpenReports(weekly: false);
        _weeklyReports.Click += (_, _) => OpenReports(weekly: true);
        _myTasks.Click += (_, _) => OpenMyTasks();
        _notifications.Click += (_, _) => OpenNotifications();
        _profileSettings.Click += (_, _) => OpenProfileSettings();
        _operationLogs.Click += (_, _) => OpenOperationLogs();
        _signOut.Click += (_, _) =>
        {
            Session.SignOut();
            ReturnToLogin();
        };
    }

    private void OpenReports(bool weekly)
    {
        _drawer.CloseDrawer(GravityCompat.Start);
        StartActivity(ReportsActivity.CreateIntent(this, weekly));
    }

    private void OpenAiAssistant()
    {
        _drawer.CloseDrawer(GravityCompat.Start);
        StartActivity(AgentActivity.CreateIntent(this, Session.SelectedBoardId));
    }

    private void OpenMyTasks()
    {
        _drawer.CloseDrawer(GravityCompat.Start);
        StartActivity(new Intent(this, typeof(MyTasksActivity)));
    }

    private void OpenGlobalSearch()
    {
        _drawer.CloseDrawer(GravityCompat.Start);
        StartActivity(new Intent(this, typeof(SearchActivity)));
    }

    private void OpenDashboard()
    {
        _drawer.CloseDrawer(GravityCompat.Start);
        StartActivity(new Intent(this, typeof(DashboardActivity)));
    }

    private void OpenNotifications()
    {
        _drawer.CloseDrawer(GravityCompat.Start);
        StartActivity(new Intent(this, typeof(NotificationsActivity)));
    }

    private void OpenProfileSettings()
    {
        _drawer.CloseDrawer(GravityCompat.Start);
        StartActivity(new Intent(this, typeof(AccountSettingsActivity)));
    }

    private void OpenOperationLogs()
    {
        _drawer.CloseDrawer(GravityCompat.Start);
        StartActivity(new Intent(this, typeof(OperationLogsActivity)));
    }

    private void OpenArchivedBoards()
    {
        _drawer.CloseDrawer(GravityCompat.Start);
        StartActivity(new Intent(this, typeof(ArchivedBoardsActivity)));
    }

    private void OpenBoardSettings()
    {
        if (_board?.CanEdit != true)
        {
            return;
        }
        StartActivity(BoardSettingsActivity.CreateIntent(this, _board.Id));
    }

    private void OpenBoardSharing()
    {
        if (_board?.IsOwner != true)
        {
            return;
        }
        StartActivity(BoardSharingActivity.CreateIntent(this, _board.Id));
    }

    private void OpenGantt()
    {
        if (_board == null)
        {
            return;
        }
        StartActivity(GanttActivity.CreateIntent(this, _board.Id));
    }

    private async Task LoadBoardsAsync(int preferredBoardId = 0, bool showProgress = true)
    {
        try
        {
            SetLoading(showProgress);
            var response = await Api.GetBoardsAsync();
            _boards = response.Boards;
            _loaded = true;
            if (_showingHome) { ShowBoardsHome(); return; }

            if (preferredBoardId > 0 && _boards.All(board => board.Id != preferredBoardId))
            {
                _board = null;
                await LoadBoardAsync(preferredBoardId, showProgress: false);
                if (_board?.Id == preferredBoardId)
                {
                    return;
                }
            }

            if (_boards.Count == 0)
            {
                Session.SelectedBoardId = 0;
                _board = null;
                ShowEmpty(UiText.Get("No boards yet"), "Open the menu to create your first board.");
                _toolbar.Title = "Kanban";
                _toolbar.Subtitle = UiText.Get("Your workspace");
                return;
            }

            var selected = _boards.FirstOrDefault(board => board.Id == preferredBoardId) ?? _boards[0];
            await LoadBoardAsync(selected.Id, showProgress: false);
        }
        catch (Exception exception)
        {
            if (!_loaded) ShowEmpty(UiText.Get("Could not load boards"), "Check the connection, then try again.");
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
            if (_showingHome) _homePosition = _home.ScrollY;
            _showingHome = false;
            _home.Visibility = ViewStates.Gone;
            _boardPanel.Visibility = ViewStates.Visible;
            if (_board?.Id != response.Board.Id)
            {
                ClearBoardFilters();
            }
            _board = response.Board;
            Session.SelectedBoardId = boardId;
            RenderBoard(response.Board);
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

    private void RenderBoard(BoardDto board)
    {
        CaptureBoardPosition();
        _boardCanvas.RemoveAllViews();
        _columnScrolls.Clear();
        _toolbar.Title = "Kanban";
        _toolbar.Subtitle = null;
        _toolbar.SetNavigationIcon(Resource.Drawable.ic_arrow_back);
        RenderBoardHeading(board);
        var archiveItem = _toolbar.Menu?.FindItem(Resource.Id.action_archive_board);
        if (archiveItem != null)
        {
            archiveItem.SetVisible(board.IsOwner);
            archiveItem.SetTitle(board.IsArchived ? UiText.Get("Unarchive board") : UiText.Get("Archive board"));
        }
        _toolbar.Menu?.FindItem(Resource.Id.action_add_column)?.SetVisible(board.CanEdit);
        _toolbar.Menu?.FindItem(Resource.Id.action_board_settings)?.SetVisible(board.CanEdit);
        _toolbar.Menu?.FindItem(Resource.Id.action_share_board)?.SetVisible(board.IsOwner);
        _toolbar.Menu?.FindItem(Resource.Id.action_gantt)?.SetVisible(true);
        var filterItem = _toolbar.Menu?.FindItem(Resource.Id.action_filter_board);
        filterItem?.SetVisible(true);
        filterItem?.SetTitle(HasActiveBoardFilter() ? "Filter cards · active" : "Filter cards");
        _newCard.Visibility = board.CanEdit && board.Columns.Count > 0 ? ViewStates.Visible : ViewStates.Gone;
        _emptyState.Visibility = ViewStates.Gone;
        _boardScroll.Visibility = ViewStates.Visible;

        if (board.Columns.Count == 0)
        {
            ShowEmpty(UiText.Get("This board has no columns"), board.CanEdit
                ? "Use Add column in the toolbar to build your workflow."
                : "The owner has not added any columns yet.");
            _toolbar.Menu?.FindItem(Resource.Id.action_add_column)?.SetVisible(board.CanEdit);
            _toolbar.Menu?.FindItem(Resource.Id.action_board_settings)?.SetVisible(board.CanEdit);
            _toolbar.Menu?.FindItem(Resource.Id.action_share_board)?.SetVisible(board.IsOwner);
            _toolbar.Menu?.FindItem(Resource.Id.action_archive_board)?.SetVisible(board.IsOwner);
            _toolbar.Menu?.FindItem(Resource.Id.action_gantt)?.SetVisible(true);
            _toolbar.Menu?.FindItem(Resource.Id.action_filter_board)?.SetVisible(true);
            return;
        }

        foreach (var column in board.Columns.OrderBy(column => column.Order))
        {
            _boardCanvas.AddView(CreateColumn(board, column), ColumnLayout());
        }
        RestoreBoardPosition(board.Id);
    }

    private View CreateColumn(BoardDto board, ColumnDto column)
    {
        var shell = new MaterialCardView(this)
        {
            Radius = Dp(20),
            CardElevation = 0
        };
        shell.SetCardBackgroundColor(GetColor(Resource.Color.surface_canvas));
        shell.StrokeColor = GetColor(Resource.Color.outline);
        shell.StrokeWidth = 0;

        var content = new LinearLayout(this) { Orientation = global::Android.Widget.Orientation.Vertical };
        content.SetPadding(Dp(8), Dp(8), Dp(8), Dp(12));

        var header = new LinearLayout(this)
        {
            Orientation = global::Android.Widget.Orientation.Horizontal
        };
        header.SetGravity(GravityFlags.CenterVertical);
        var title = Text(column.Name, 17, Resource.Color.text_primary, true);
        header.AddView(title, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1));
        var visibleCards = FilterCards(column.Cards);
        var countLabel = HasActiveBoardFilter()
            ? $"{visibleCards.Count}/{column.Cards.Count}"
            : column.Cards.Count.ToString();
        var count = Text(countLabel, 12, Resource.Color.on_brand_container, true);
        count.Gravity = GravityFlags.Center;
        count.Background = Rounded(ColorOf(Resource.Color.brand_container), 14);
        header.AddView(count, new LinearLayout.LayoutParams(
            HasActiveBoardFilter() ? Dp(50) : Dp(30), Dp(30)));
        content.AddView(header);

        if (board.CanEdit)
        {
            var add = new MaterialButton(this, null, global::Android.Resource.Attribute.BorderlessButtonStyle)
            {
                Text = UiText.Get("+  Add card"),
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
        cards.SetPadding(0, 0, 0, Dp(80));
        foreach (var card in visibleCards)
        {
            cards.AddView(CreateCard(board, card), CardLayout());
        }
        if (visibleCards.Count == 0)
        {
            var hint = Text(HasActiveBoardFilter()
                    ? UiText.Get("No matching cards")
                    : board.CanEdit ? UiText.Get("Drop a card here") : UiText.Get("No cards"),
                13,
                Resource.Color.text_secondary);
            hint.Gravity = GravityFlags.Center;
            hint.SetPadding(Dp(8), Dp(32), Dp(8), Dp(32));
            cards.AddView(hint, new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent));
        }
        var scroll = new ScrollView(this) { FillViewport = true, VerticalScrollBarEnabled = false };
        _columnScrolls[column.Id] = scroll;
        scroll.AddView(cards, new ViewGroup.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent));
        content.AddView(scroll, new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, 0, 1));
        shell.AddView(content, new FrameLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));

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
            Radius = Dp(20),
            CardElevation = Dp(1),
            Clickable = true,
            LongClickable = board.CanEdit
        };
        shell.SetCardBackgroundColor(GetColor(Resource.Color.surface));
        shell.StrokeColor = GetColor(Resource.Color.outline);
        shell.StrokeWidth = Dp(1);

        var content = new LinearLayout(this)
        {
            Orientation = global::Android.Widget.Orientation.Vertical
        };
        content.SetPadding(Dp(18), Dp(16), Dp(12), Dp(18));
        var row = new LinearLayout(this)
        {
            Orientation = global::Android.Widget.Orientation.Horizontal
        };
        row.SetGravity(GravityFlags.CenterVertical);
        var label = Text(card.Title, 18, Resource.Color.text_primary, true);
        label.SetMaxLines(4);
        row.AddView(label, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1));
        if (board.CanEdit)
        {
            var handle = new ImageView(this)
            {
                ContentDescription = UiText.Format("Hold to move {0}", card.Title),
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
        content.AddView(row);
        if (!string.IsNullOrWhiteSpace(card.Description))
        {
            var description = Text(card.Description, 13, Resource.Color.text_secondary);
            description.SetMaxLines(2);
            description.Ellipsize = global::Android.Text.TextUtils.TruncateAt.End;
            var descriptionLayout = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MatchParent,
                ViewGroup.LayoutParams.WrapContent);
            descriptionLayout.SetMargins(0, Dp(5), Dp(6), 0);
            content.AddView(description, descriptionLayout);
        }
        if (card.Labels.Count > 0)
        {
            var labels = Text(
                string.Join("  ", card.Labels.Select(item => $"# {item.Name}")),
                12,
                Resource.Color.brand_primary,
                true);
            labels.SetMaxLines(2);
            var labelsLayout = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MatchParent,
                ViewGroup.LayoutParams.WrapContent);
            labelsLayout.SetMargins(0, Dp(7), Dp(6), 0);
            content.AddView(labels, labelsLayout);
        }
        var footer = new LinearLayout(this) { Orientation = global::Android.Widget.Orientation.Horizontal };
        footer.SetGravity(GravityFlags.CenterVertical);
        footer.SetPadding(0, Dp(16), Dp(6), 0);
        if (card.Priority != "None")
        {
            var badge = Text(UiText.Get(card.Priority), 11, card.Priority == "Urgent" ? Resource.Color.on_danger_container : Resource.Color.brand_primary, true);
            badge.SetPadding(Dp(9), Dp(5), Dp(9), Dp(5));
            badge.Background = Rounded(ColorOf(card.Priority == "Urgent" ? Resource.Color.danger_container : Resource.Color.brand_container), 8);
            footer.AddView(badge);
        }
        var assignment = Text(card.AssignedUser?.DisplayName ?? UiText.Get("Unassigned"), 12, Resource.Color.text_secondary);
        assignment.SetSingleLine(true);
        assignment.Ellipsize = global::Android.Text.TextUtils.TruncateAt.End;
        assignment.Gravity = GravityFlags.End;
        footer.AddView(assignment, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1));
        content.AddView(footer);
        var displayDates = ResolveDefaultCardDates(card);
        var parts = new List<string>();
        if (displayDates.Start.HasValue || displayDates.End.HasValue)
            parts.Add($"{UiText.Get(displayDates.Source)}: {FormatBoardDate(displayDates.Start)} – {FormatBoardDate(displayDates.End)}");
        if (card.CommentCount > 0) parts.Add(UiText.Format("{0} replies", card.CommentCount));
        if (card.RecurrenceInterval.HasValue && card.RecurrenceUnit != "None") parts.Add(UiText.Format("Every {0} {1}", card.RecurrenceInterval, UiText.Get(card.RecurrenceUnit)));
        if (parts.Count > 0)
        {
            var meta = Text(string.Join("  ·  ", parts), 12, Resource.Color.text_secondary);
            meta.SetPadding(0, Dp(10), 0, 0);
            content.AddView(meta);
        }
        shell.AddView(content);
        shell.Click += (_, _) => StartActivity(CardDetailActivity.CreateIntent(this, card.Id));
        shell.ContentDescription = board.CanEdit
            ? UiText.Format("{0}. Tap for details. Hold and drag to move.", card.Title)
            : UiText.Format("{0}. Tap for details.", card.Title);
        return shell;
    }

    private static (string Source, DateTime? Start, DateTime? End) ResolveDefaultCardDates(CardDto card)
    {
        if (card.ActualStartTime.HasValue && card.ActualEndTime.HasValue)
        {
            return ("Actual", card.ActualStartTime, card.ActualEndTime);
        }
        if (card.PlannedStartTime.HasValue && card.DueDate.HasValue)
        {
            return ("Planned", card.PlannedStartTime, card.DueDate);
        }
        return card.ActualStartTime.HasValue || card.ActualEndTime.HasValue
            ? ("Actual", card.ActualStartTime, card.ActualEndTime)
            : ("Planned", card.PlannedStartTime, card.DueDate);
    }

    private static string FormatBoardDate(DateTime? value) =>
        value.HasValue ? value.Value.ToLocalTime().ToString("MMM d") : "—";

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
        Snackbar.Make(_workspace, UiText.Get("Drop on another column to move"), Snackbar.LengthShort).Show();
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
            Snackbar.Make(_workspace, UiText.Get("Card moved"), Snackbar.LengthShort).Show();
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
        content.AddView(Text(UiText.Get("Create a card"), 24, Resource.Color.text_primary, true));
        content.AddView(Text(UiText.Get("Capture it now. Organize it by dragging later."), 14, Resource.Color.text_secondary));

        var titleBox = new TextInputLayout(this) { Hint = UiText.Get("Card title") };
        titleBox.BoxBackgroundMode = TextInputLayout.BoxBackgroundFilled;
        titleBox.SetBoxCornerRadii(Dp(14), Dp(14), Dp(14), Dp(14));
        var title = new TextInputEditText(this);
        title.SetSingleLine(true);
        title.ImeOptions = ImeAction.Done;
        titleBox.AddView(title);
        var titleLayout = new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent);
        titleLayout.SetMargins(0, Dp(18), 0, Dp(10));
        content.AddView(titleBox, titleLayout);

        content.AddView(Text(UiText.Get("COLUMN"), 12, Resource.Color.text_secondary, true));
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
            Text = UiText.Get("Create card"),
            TextSize = 16
        };
        create.SetAllCaps(false);
        create.CornerRadius = Dp(20);
        var createLayout = new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, Dp(56));
        createLayout.SetMargins(0, Dp(14), 0, 0);
        content.AddView(create, createLayout);
        create.Click += async (_, _) =>
        {
            var value = title.Text?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(value))
            {
                titleBox.Error = UiText.Get("Enter a card title");
                return;
            }
            create.Enabled = false;
            try
            {
                await Api.CreateCardAsync(columns[spinner.SelectedItemPosition].Id,
                    new CreateCardRequest { Title = value });
                dialog.Dismiss();
                await LoadBoardAsync(board.Id, showProgress: false);
                Snackbar.Make(_workspace, UiText.Get("Card created"), Snackbar.LengthShort).Show();
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
        var box = new TextInputLayout(this) { Hint = UiText.Get("Board name") };
        box.BoxBackgroundMode = TextInputLayout.BoxBackgroundFilled;
        var input = new TextInputEditText(this);
        input.SetSingleLine(true);
        box.AddView(input);
        var wrapper = new FrameLayout(this);
        wrapper.SetPadding(Dp(24), Dp(4), Dp(24), 0);
        wrapper.AddView(box);
        var builder = new MaterialAlertDialogBuilder(this);
        builder.SetTitle(UiText.Get("New board"));
        builder.SetMessage(UiText.Get("Create a private board. You can manage visibility and sharing from board settings."));
        builder.SetView(wrapper);
        builder.SetNegativeButton(UiText.Get("Cancel"), (_, _) => { });
        builder.SetPositiveButton(UiText.Get("Create"), (_, _) => { });
        var dialog = builder.Create() ?? throw new InvalidOperationException("Could not create the board dialog.");
        dialog.Show();
        dialog.GetButton((int)DialogButtonType.Positive)!.Click += async (_, _) =>
        {
            var value = input.Text?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(value))
            {
                box.Error = UiText.Get("Enter a board name");
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

    private void ShowBoardFilterSheet()
    {
        var board = _board;
        if (board == null)
        {
            return;
        }
        var dialog = new BottomSheetDialog(this);
        var content = new LinearLayout(this)
        {
            Orientation = global::Android.Widget.Orientation.Vertical
        };
        content.SetPadding(Dp(24), Dp(18), Dp(24), Dp(24));
        content.AddView(Text(UiText.Get("Filter cards"), 24, Resource.Color.text_primary, true));
        content.AddView(Text(UiText.Get("Search this board and narrow it by priority."), 14, Resource.Color.text_secondary));
        var searchBox = new TextInputLayout(this) { Hint = UiText.Get("Title or description") };
        searchBox.BoxBackgroundMode = TextInputLayout.BoxBackgroundFilled;
        var search = new TextInputEditText(this) { Text = _boardQuery };
        search.SetSingleLine(true);
        searchBox.AddView(search);
        var searchLayout = new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent,
            ViewGroup.LayoutParams.WrapContent);
        searchLayout.SetMargins(0, Dp(16), 0, Dp(10));
        content.AddView(searchBox, searchLayout);
        content.AddView(Text(UiText.Get("PRIORITY"), 12, Resource.Color.text_secondary, true));
        var checks = new Dictionary<string, CheckBox>();
        foreach (var priority in new[] { "Urgent", "High", "Medium", "Low", "None" })
        {
            var check = new CheckBox(this)
            {
                Text = priority,
                Checked = _boardPriorities.Contains(priority)
            };
            check.SetTextColor(ColorOf(Resource.Color.text_primary));
            content.AddView(check, new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MatchParent,
                Dp(44)));
            checks[priority] = check;
        }
        var assigneeChecks = new Dictionary<string, CheckBox>();
        var assignees = board.Columns
            .SelectMany(column => column.Cards)
            .Select(card => card.AssignedUser)
            .OfType<CardUserDto>()
            .GroupBy(user => user.Id)
            .Select(group => group.First())
            .OrderBy(user => user.DisplayName)
            .ToList();
        if (assignees.Count > 0)
        {
            var assigneeHeader = Text(UiText.Get("ASSIGNEE"), 12, Resource.Color.text_secondary, true);
            var assigneeHeaderLayout = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MatchParent,
                ViewGroup.LayoutParams.WrapContent);
            assigneeHeaderLayout.SetMargins(0, Dp(10), 0, 0);
            content.AddView(assigneeHeader, assigneeHeaderLayout);
            foreach (var assignee in assignees)
            {
                var check = new CheckBox(this)
                {
                    Text = assignee.DisplayName,
                    Checked = _boardAssigneeIds.Contains(assignee.Id)
                };
                check.SetTextColor(ColorOf(Resource.Color.text_primary));
                content.AddView(check, new LinearLayout.LayoutParams(
                    ViewGroup.LayoutParams.MatchParent,
                    Dp(44)));
                assigneeChecks[assignee.Id] = check;
            }
        }
        var actions = new LinearLayout(this)
        {
            Orientation = global::Android.Widget.Orientation.Horizontal
        };
        var clear = new MaterialButton(this, null, global::Android.Resource.Attribute.BorderlessButtonStyle)
        {
            Text = UiText.Get("Clear"),
            CornerRadius = Dp(20)
        };
        clear.SetAllCaps(false);
        var apply = new MaterialButton(this) { Text = UiText.Get("Apply"), CornerRadius = Dp(20) };
        apply.SetAllCaps(false);
        actions.AddView(clear, new LinearLayout.LayoutParams(0, Dp(52), 1));
        var applyLayout = new LinearLayout.LayoutParams(0, Dp(52), 1);
        applyLayout.SetMargins(Dp(10), 0, 0, 0);
        actions.AddView(apply, applyLayout);
        var actionsLayout = new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent,
            ViewGroup.LayoutParams.WrapContent);
        actionsLayout.SetMargins(0, Dp(12), 0, 0);
        content.AddView(actions, actionsLayout);
        clear.Click += (_, _) =>
        {
            _boardQuery = string.Empty;
            _boardPriorities.Clear();
            _boardAssigneeIds.Clear();
            dialog.Dismiss();
            RenderBoard(board);
        };
        apply.Click += (_, _) =>
        {
            _boardQuery = search.Text?.Trim() ?? string.Empty;
            _boardPriorities.Clear();
            foreach (var item in checks.Where(item => item.Value.Checked))
            {
                _boardPriorities.Add(item.Key);
            }
            _boardAssigneeIds.Clear();
            foreach (var item in assigneeChecks.Where(item => item.Value.Checked))
            {
                _boardAssigneeIds.Add(item.Key);
            }
            dialog.Dismiss();
            RenderBoard(board);
        };
        dialog.SetContentView(content);
        dialog.Show();
    }

    private List<CardDto> FilterCards(IEnumerable<CardDto> cards) => cards
        .Where(card => string.IsNullOrWhiteSpace(_boardQuery) ||
            card.Title.Contains(_boardQuery, StringComparison.OrdinalIgnoreCase) ||
            (card.Description?.Contains(_boardQuery, StringComparison.OrdinalIgnoreCase) ?? false))
        .Where(card => _boardPriorities.Count == 0 || _boardPriorities.Contains(card.Priority))
        .Where(card => _boardAssigneeIds.Count == 0 ||
            card.AssignedUser != null && _boardAssigneeIds.Contains(card.AssignedUser.Id))
        .OrderBy(card => card.Order)
        .ToList();

    private bool HasActiveBoardFilter() =>
        !string.IsNullOrWhiteSpace(_boardQuery) ||
        _boardPriorities.Count > 0 ||
        _boardAssigneeIds.Count > 0;

    private void ClearBoardFilters()
    {
        _boardQuery = string.Empty;
        _boardPriorities.Clear();
        _boardAssigneeIds.Clear();
    }

    private void ShowNewColumnDialog()
    {
        var board = _board;
        if (board == null || !board.CanEdit)
        {
            return;
        }
        var box = new TextInputLayout(this) { Hint = UiText.Get("Column name") };
        box.BoxBackgroundMode = TextInputLayout.BoxBackgroundFilled;
        var input = new TextInputEditText(this);
        input.SetSingleLine(true);
        box.AddView(input);
        var wrapper = new FrameLayout(this);
        wrapper.SetPadding(Dp(24), Dp(4), Dp(24), 0);
        wrapper.AddView(box);
        var builder = new MaterialAlertDialogBuilder(this);
        builder.SetTitle(UiText.Get("Add column"));
        builder.SetView(wrapper);
        builder.SetNegativeButton(UiText.Get("Cancel"), (_, _) => { });
        builder.SetPositiveButton(UiText.Get("Add"), (_, _) => { });
        var dialog = builder.Create() ?? throw new InvalidOperationException("Could not create the column dialog.");
        dialog.Show();
        dialog.GetButton((int)DialogButtonType.Positive)!.Click += async (_, _) =>
        {
            var value = input.Text?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(value))
            {
                box.Error = UiText.Get("Enter a column name");
                return;
            }
            try
            {
                dialog.GetButton((int)DialogButtonType.Positive)!.Enabled = false;
                var response = await Api.CreateColumnAsync(board.Id, new CreateColumnRequest { Name = value });
                dialog.Dismiss();
                _board = response.Board;
                RenderBoard(response.Board);
                Snackbar.Make(_workspace, UiText.Get("Column added"), Snackbar.LengthShort).Show();
            }
            catch (Exception exception)
            {
                dialog.GetButton((int)DialogButtonType.Positive)!.Enabled = true;
                box.Error = FriendlyMessage(exception);
            }
        };
    }

    private void ShowArchiveBoardDialog()
    {
        var board = _board;
        if (board == null || !board.IsOwner)
        {
            return;
        }
        var archive = !board.IsArchived;
        var builder = new MaterialAlertDialogBuilder(this);
        builder.SetTitle(archive ? UiText.Get("Archive board?") : UiText.Get("Unarchive board?"));
        builder.SetMessage(archive
            ? UiText.Get("The board becomes read-only and moves to Archived.")
            : UiText.Get("The board returns to your active workspace."));
        builder.SetNegativeButton(UiText.Get("Cancel"), (_, _) => { });
        builder.SetPositiveButton(archive ? UiText.Get("Archive") : UiText.Get("Unarchive"), (dialog, args) =>
        {
            _ = SetBoardArchivedAsync(board, archive);
        });
        builder.Show();
    }

    private async Task SetBoardArchivedAsync(BoardDto board, bool archive)
    {
        try
        {
            SetLoading(true);
            var result = await Api.SetBoardArchivedAsync(board.Id, archive);
            Session.SelectedBoardId = archive ? 0 : board.Id;
            await LoadBoardsAsync(Session.SelectedBoardId, showProgress: false);
            Snackbar.Make(_workspace,
                result.Message ?? (archive ? UiText.Get("Board archived") : UiText.Get("Board restored")),
                Snackbar.LengthLong).Show();
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

    private void ShowEmpty(string title, string message)
    {
        _home.Visibility = ViewStates.Gone;
        _boardPanel.Visibility = ViewStates.Gone;
        _boardCanvas.RemoveAllViews();
        _boardScroll.Visibility = ViewStates.Gone;
        _newCard.Visibility = ViewStates.Gone;
        _toolbar.Menu?.FindItem(Resource.Id.action_archive_board)?.SetVisible(false);
        _toolbar.Menu?.FindItem(Resource.Id.action_add_column)?.SetVisible(false);
        _toolbar.Menu?.FindItem(Resource.Id.action_board_settings)?.SetVisible(false);
        _toolbar.Menu?.FindItem(Resource.Id.action_share_board)?.SetVisible(false);
        _toolbar.Menu?.FindItem(Resource.Id.action_gantt)?.SetVisible(false);
        _toolbar.Menu?.FindItem(Resource.Id.action_filter_board)?.SetVisible(false);
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
        bar.SetAction(UiText.Get("Retry"), ignoredView =>
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
            Typeface = bold ? Typeface.Create("sans-serif-medium", TypefaceStyle.Normal) : Typeface.Create("sans-serif", TypefaceStyle.Normal)
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
        var layout = new LinearLayout.LayoutParams(ColumnWidth(), ViewGroup.LayoutParams.MatchParent);
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
        OnBackPressedDispatcher dispatcher,
        Func<bool> navigateBack) : OnBackPressedCallback(true)
    {
        public override void HandleOnBackPressed()
        {
            if (drawer.IsDrawerOpen(GravityCompat.Start))
            {
                drawer.CloseDrawer(GravityCompat.Start);
                return;
            }
            if (navigateBack()) return;
            Enabled = false;
            dispatcher.OnBackPressed();
            Enabled = true;
        }
    }
}
