using Android.App;
using Android.Content;
using Android.Content.Res;
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.OS;
using Android.Provider;
using Android.Views;
using Android.Widget;
using AndroidX.AppCompat.App;
using Aiursoft.Kanban.Android.Oidc;
using Aiursoft.Kanban.SDK;
using Aiursoft.Kanban.SDK.Models;
using Google.Android.Material.AppBar;
using Google.Android.Material.Button;
using Google.Android.Material.Card;
using Google.Android.Material.ProgressIndicator;
using Google.Android.Material.Snackbar;
using Google.Android.Material.TextField;
using Color = Android.Graphics.Color;

namespace Aiursoft.Kanban.Android;

[Activity(
    Label = "AI Assistant",
    Exported = false,
    Theme = "@style/AppTheme",
    WindowSoftInputMode = SoftInput.AdjustResize)]
public sealed class AgentActivity : AppCompatActivity
{
    public const string BoardIdExtra = "board_id";
    private const int ExcelPickerRequestCode = 7401;
    private const long MaxExcelBytes = 10L * 1024 * 1024;
    private const string ConversationStateKey = "agent_conversation_id";
    private const string BoardStateKey = "agent_board_id";
    private const string DraftStateKey = "agent_message_draft";
    private const string ExcelMimeType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    private View _root = null!;
    private View _content = null!;
    private MaterialToolbar _toolbar = null!;
    private Spinner _boardSpinner = null!;
    private MaterialButton _newChat = null!;
    private ScrollView _messageScroll = null!;
    private LinearLayout _messages = null!;
    private LinearProgressIndicator _thinking = null!;
    private MaterialCardView _fileChip = null!;
    private TextView _fileName = null!;
    private MaterialButton _removeFile = null!;
    private MaterialButton _attach = null!;
    private TextInputLayout _inputBox = null!;
    private TextInputEditText _input = null!;
    private MaterialButton _send = null!;
    private CircularProgressIndicator _progress = null!;

    private List<BoardSummaryDto> _boards = [];
    private Guid? _conversationId;
    private int _boardId;
    private string _agentState = "Ready";
    private string? _excelMarkdown;
    private string? _excelFileName;
    private string _renderSignature = string.Empty;
    private CancellationTokenSource? _pollCancellation;
    private bool _loaded;
    private bool _sending;
    private bool _busy;

    private AppSession Session => ((KanbanApplication)Application!).Session;
    private KanbanApiClient Api => Session.RequireApi();

    public static Intent CreateIntent(Context context, int boardId = 0) =>
        new Intent(context, typeof(AgentActivity)).PutExtra(BoardIdExtra, boardId);

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        if (!Session.IsAuthenticated)
        {
            ReturnToLogin();
            return;
        }

        _boardId = savedInstanceState?.GetInt(BoardStateKey)
            ?? Intent?.GetIntExtra(BoardIdExtra, 0)
            ?? 0;
        var savedConversation = savedInstanceState?.GetString(ConversationStateKey);
        if (Guid.TryParse(savedConversation, out var conversationId))
        {
            _conversationId = conversationId;
        }

        SetContentView(Resource.Layout.activity_agent);
        BindViews();
        ConfigureChrome();
        WireEvents();
        _input.Text = savedInstanceState?.GetString(DraftStateKey) ?? string.Empty;
        RenderWelcome();
        _ = LoadBoardsAsync();
    }

    protected override void OnResume()
    {
        base.OnResume();
        if (_loaded && _conversationId.HasValue && _pollCancellation == null)
        {
            StartPolling();
        }
    }

    protected override void OnPause()
    {
        StopPolling();
        base.OnPause();
    }

    protected override void OnSaveInstanceState(Bundle outState)
    {
        outState.PutInt(BoardStateKey, _boardId);
        outState.PutString(ConversationStateKey, _conversationId?.ToString());
        outState.PutString(DraftStateKey, _input.Text ?? string.Empty);
        base.OnSaveInstanceState(outState);
    }

    protected override void OnActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        base.OnActivityResult(requestCode, resultCode, data);
        if (requestCode != ExcelPickerRequestCode || resultCode != Result.Ok || data?.Data == null)
        {
            return;
        }
        _ = ConvertExcelAsync(data.Data);
    }

    protected override void OnDestroy()
    {
        StopPolling();
        base.OnDestroy();
    }

    private void BindViews()
    {
        _root = FindViewById<View>(Resource.Id.agent_root)!;
        _content = FindViewById<View>(Resource.Id.agent_content)!;
        _toolbar = FindViewById<MaterialToolbar>(Resource.Id.agent_toolbar)!;
        _boardSpinner = FindViewById<Spinner>(Resource.Id.agent_board_spinner)!;
        _newChat = FindViewById<MaterialButton>(Resource.Id.agent_new_chat_button)!;
        _messageScroll = FindViewById<ScrollView>(Resource.Id.agent_message_scroll)!;
        _messages = FindViewById<LinearLayout>(Resource.Id.agent_messages)!;
        _thinking = FindViewById<LinearProgressIndicator>(Resource.Id.agent_thinking)!;
        _fileChip = FindViewById<MaterialCardView>(Resource.Id.agent_file_chip)!;
        _fileName = FindViewById<TextView>(Resource.Id.agent_file_name)!;
        _removeFile = FindViewById<MaterialButton>(Resource.Id.agent_remove_file_button)!;
        _attach = FindViewById<MaterialButton>(Resource.Id.agent_attach_button)!;
        _inputBox = FindViewById<TextInputLayout>(Resource.Id.agent_input_box)!;
        _input = FindViewById<TextInputEditText>(Resource.Id.agent_input)!;
        _send = FindViewById<MaterialButton>(Resource.Id.agent_send_button)!;
        _progress = FindViewById<CircularProgressIndicator>(Resource.Id.agent_progress)!;
    }

    private void ConfigureChrome()
    {
        _toolbar.SetOnApplyWindowInsetsListener(new ToolbarInsetListener(Dp(64)));
        _content.SetOnApplyWindowInsetsListener(
            new SystemBarInsetListener(Dp(12), Dp(12), Dp(12), Dp(12), false, true));
        _toolbar.NavigationContentDescription = "Back to Kanban";
        _toolbar.NavigationClick += (_, _) => Finish();
    }

    private void WireEvents()
    {
        _newChat.Click += async (_, _) => await StartNewChatAsync();
        _attach.Click += (_, _) => ChooseExcel();
        _removeFile.Click += (_, _) => ClearExcel();
        _send.Click += async (_, _) => await SendMessageAsync();
    }

    private async Task LoadBoardsAsync()
    {
        try
        {
            SetBusy(true);
            var response = await Api.GetBoardsAsync();
            _boards = response.Boards;
            var names = new[] { "All accessible boards" }
                .Concat(_boards.Select(board => board.Name))
                .ToArray();
            var adapter = new ArrayAdapter<string>(
                this,
                global::Android.Resource.Layout.SimpleSpinnerItem,
                names);
            adapter.SetDropDownViewResource(global::Android.Resource.Layout.SimpleSpinnerDropDownItem);
            _boardSpinner.Adapter = adapter;
            var selection = _boardId <= 0
                ? 0
                : Math.Max(0, _boards.FindIndex(board => board.Id == _boardId) + 1);
            _boardSpinner.SetSelection(selection);
            _boardId = selection == 0 ? 0 : _boards[selection - 1].Id;
            _boardSpinner.ItemSelected += (_, args) =>
            {
                if (_conversationId.HasValue)
                {
                    return;
                }
                _boardId = args.Position == 0 ? 0 : _boards[args.Position - 1].Id;
            };
            _loaded = true;
            UpdateControls();
            if (_conversationId.HasValue)
            {
                StartPolling();
            }
        }
        catch (Exception exception)
        {
            ShowError(exception);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task SendMessageAsync()
    {
        var message = _input.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(message))
        {
            _inputBox.Error = "Type a message first";
            _input.RequestFocus();
            return;
        }
        if (_agentState is "Thinking" or "AwaitingApproval")
        {
            Snackbar.Make(_root, "Resolve the current response before sending another message.", Snackbar.LengthLong)
                .Show();
            return;
        }

        try
        {
            _inputBox.Error = null;
            _sending = true;
            _agentState = "Thinking";
            _toolbar.Subtitle = "Thinking…";
            _thinking.Visibility = ViewStates.Visible;
            UpdateControls();
            var response = await Api.SendAgentMessageAsync(new AgentSendMessageRequest
            {
                BoardId = _boardId,
                Message = message,
                ConversationId = _conversationId,
                ExcelMarkdown = _excelMarkdown
            });
            _conversationId = response.ConversationId;
            _input.Text = string.Empty;
            ClearExcel();
            _renderSignature = string.Empty;
            StartPolling();
        }
        catch (Exception exception)
        {
            _agentState = "Ready";
            _toolbar.Subtitle = "Ready";
            _thinking.Visibility = ViewStates.Gone;
            _input.Text = message;
            ShowError(exception);
        }
        finally
        {
            _sending = false;
            UpdateControls();
        }
    }

    private void StartPolling()
    {
        StopPolling();
        if (!_conversationId.HasValue)
        {
            return;
        }
        _pollCancellation = new CancellationTokenSource();
        _ = PollConversationAsync(_conversationId.Value, _pollCancellation.Token);
    }

    private async Task PollConversationAsync(Guid conversationId, CancellationToken cancellationToken)
    {
        var retryDelay = TimeSpan.FromSeconds(2);
        while (!cancellationToken.IsCancellationRequested && _conversationId == conversationId)
        {
            try
            {
                var status = await Api.GetAgentStatusAsync(conversationId);
                cancellationToken.ThrowIfCancellationRequested();
                RenderStatus(status);
                retryDelay = TimeSpan.FromSeconds(2);
                if (status.State is "Completed" or "Error")
                {
                    StopPollingAfterCompletion(cancellationToken);
                    return;
                }
                var delay = status.State == "AwaitingApproval"
                    ? TimeSpan.FromSeconds(5)
                    : TimeSpan.FromSeconds(3);
                await Task.Delay(delay, cancellationToken);
            }
            catch (System.OperationCanceledException)
            {
                return;
            }
            catch (KanbanAuthenticationRequiredException)
            {
                ReturnToLogin();
                return;
            }
            catch (Exception exception)
            {
                _toolbar.Subtitle = "Network issue · retrying";
                if (_messages.ChildCount == 0)
                {
                    ShowError(exception);
                }
                try
                {
                    await Task.Delay(retryDelay, cancellationToken);
                }
                catch (System.OperationCanceledException)
                {
                    return;
                }
                retryDelay = TimeSpan.FromSeconds(Math.Min(retryDelay.TotalSeconds * 2, 15));
            }
        }
    }

    private void StopPollingAfterCompletion(CancellationToken activeToken)
    {
        if (_pollCancellation?.Token == activeToken)
        {
            _pollCancellation.Dispose();
            _pollCancellation = null;
        }
    }

    private void StopPolling()
    {
        var cancellation = _pollCancellation;
        _pollCancellation = null;
        if (cancellation == null)
        {
            return;
        }
        cancellation.Cancel();
        cancellation.Dispose();
    }

    private void RenderStatus(AgentStatusResponse status)
    {
        _agentState = status.State;
        _boardId = status.BoardId;
        if (_loaded && status.BoardId > 0)
        {
            var boardIndex = _boards.FindIndex(board => board.Id == status.BoardId);
            if (boardIndex >= 0 && _boardSpinner.SelectedItemPosition != boardIndex + 1)
            {
                _boardSpinner.SetSelection(boardIndex + 1);
            }
        }
        _toolbar.Subtitle = status.State switch
        {
            "Thinking" => "Thinking…",
            "AwaitingApproval" => "Waiting for approval",
            "Error" => "Error",
            _ => "Ready"
        };
        _thinking.Visibility = status.State == "Thinking" ? ViewStates.Visible : ViewStates.Gone;
        UpdateControls();

        var signature = string.Join('|',
            status.State,
            status.Messages.Count,
            status.Messages.LastOrDefault()?.Content,
            status.PendingAdvice.Count,
            string.Join(',', status.PendingAdvice.Select(advice => advice.AdviceId)),
            status.ErrorMessage);
        if (signature == _renderSignature)
        {
            return;
        }
        _renderSignature = signature;
        _messages.RemoveAllViews();
        foreach (var message in status.Messages)
        {
            if (message.Role == "tool" || string.IsNullOrWhiteSpace(message.Content))
            {
                continue;
            }
            AddMessage(message.Role, message.Content);
        }
        if (status.Messages.All(message => string.IsNullOrWhiteSpace(message.Content)))
        {
            AddMessage("assistant", WelcomeMessage());
        }
        if (status.PendingAdvice.Count > 1)
        {
            AddApproveAll(status.PendingAdvice.Count);
        }
        foreach (var advice in status.PendingAdvice)
        {
            AddAdvice(advice);
        }
        if (status.State == "Error" && !string.IsNullOrWhiteSpace(status.ErrorMessage))
        {
            AddMessage("assistant", $"Error: {status.ErrorMessage}");
        }
        _messageScroll.Post(() => _messageScroll.FullScroll(FocusSearchDirection.Down));
    }

    private void RenderWelcome()
    {
        _messages.RemoveAllViews();
        AddMessage("assistant", WelcomeMessage());
    }

    private static string WelcomeMessage() =>
        "Hi! I'm your Kanban assistant. I can help manage cards, columns, and boards. " +
        "Try “Show me the board” or “Create a card for the login bug.”";

    private void AddMessage(string role, string content)
    {
        var isUser = string.Equals(role, "user", StringComparison.OrdinalIgnoreCase);
        var row = new LinearLayout(this)
        {
            Orientation = global::Android.Widget.Orientation.Horizontal
        };
        row.SetGravity(isUser ? GravityFlags.End : GravityFlags.Start);
        var bubble = new MaterialCardView(this)
        {
            Radius = Dp(16),
            CardElevation = 0
        };
        bubble.SetCardBackgroundColor(GetColor(
            isUser ? Resource.Color.brand_primary : Resource.Color.surface));
        if (!isUser)
        {
            bubble.StrokeColor = GetColor(Resource.Color.outline);
            bubble.StrokeWidth = Dp(1);
        }
        var text = new TextView(this)
        {
            Text = content,
            TextSize = 15
        };
        text.SetTextColor(new Color(GetColor(
            isUser ? global::Android.Resource.Color.White : Resource.Color.text_primary)));
        text.SetTextIsSelectable(true);
        text.SetPadding(Dp(14), Dp(11), Dp(14), Dp(11));
        bubble.AddView(text);

        var spacerWeight = 0.18f;
        if (isUser)
        {
            row.AddView(new Space(this), new LinearLayout.LayoutParams(0, 1, spacerWeight));
        }
        row.AddView(bubble, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1 - spacerWeight));
        if (!isUser)
        {
            row.AddView(new Space(this), new LinearLayout.LayoutParams(0, 1, spacerWeight));
        }
        var rowLayout = new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent);
        rowLayout.SetMargins(0, Dp(5), 0, Dp(5));
        _messages.AddView(row, rowLayout);
    }

    private void AddApproveAll(int count)
    {
        var button = SecondaryButton($"Approve all {count} actions");
        button.ContentDescription = $"Approve all {count} proposed actions";
        button.Click += async (_, _) => await ApproveAllAsync(button);
        var layout = new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, Dp(48));
        layout.SetMargins(0, Dp(8), 0, Dp(4));
        _messages.AddView(button, layout);
    }

    private void AddAdvice(AgentAdviceDto advice)
    {
        var shell = SurfaceCard();
        shell.StrokeColor = GetColor(Resource.Color.brand_primary);
        var content = new LinearLayout(this)
        {
            Orientation = global::Android.Widget.Orientation.Vertical
        };
        content.SetPadding(Dp(16), Dp(14), Dp(16), Dp(14));
        content.AddView(Text("PROPOSED ACTION", 11, Resource.Color.brand_primary, true));
        AddTo(content, Text(advice.ToolDisplayName, 17, Resource.Color.text_primary, true), 5, 6);
        if (advice.Parameters.Count > 0)
        {
            foreach (var parameter in advice.Parameters)
            {
                var row = new LinearLayout(this)
                {
                    Orientation = global::Android.Widget.Orientation.Horizontal
                };
                row.AddView(Text(parameter.DisplayKey, 13, Resource.Color.text_secondary),
                    new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 0.42f));
                var value = Text(parameter.Value ?? string.Empty, 13, Resource.Color.text_primary, true);
                value.Gravity = GravityFlags.End;
                row.AddView(value, new LinearLayout.LayoutParams(
                    0, ViewGroup.LayoutParams.WrapContent, 0.58f));
                AddTo(content, row, 5, 0);
            }
        }
        else if (!string.IsNullOrWhiteSpace(advice.ParameterDisplay))
        {
            content.AddView(Text(advice.ParameterDisplay, 14, Resource.Color.text_primary));
        }
        if (!string.IsNullOrWhiteSpace(advice.ResolvedName))
        {
            AddTo(content, Text(advice.ResolvedName, 13, Resource.Color.text_secondary), 8, 0);
        }

        var actions = new LinearLayout(this)
        {
            Orientation = global::Android.Widget.Orientation.Horizontal
        };
        var reject = DangerOutlineButton("Reject");
        var approve = PrimaryButton("Approve");
        actions.AddView(reject, new LinearLayout.LayoutParams(0, Dp(48), 1));
        var approveLayout = new LinearLayout.LayoutParams(0, Dp(48), 1);
        approveLayout.SetMargins(Dp(10), 0, 0, 0);
        actions.AddView(approve, approveLayout);
        AddTo(content, actions, 14, 0);
        reject.Click += async (_, _) => await ResolveAdviceAsync(advice.AdviceId, approve: false, reject, approve);
        approve.Click += async (_, _) => await ResolveAdviceAsync(advice.AdviceId, approve: true, reject, approve);
        shell.AddView(content);
        var layout = new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent);
        layout.SetMargins(0, Dp(7), 0, Dp(7));
        _messages.AddView(shell, layout);
    }

    private async Task ResolveAdviceAsync(
        Guid adviceId,
        bool approve,
        MaterialButton rejectButton,
        MaterialButton approveButton)
    {
        if (!_conversationId.HasValue)
        {
            return;
        }
        try
        {
            rejectButton.Enabled = false;
            approveButton.Enabled = false;
            StopPolling();
            if (approve)
            {
                await Api.ApproveAgentAdviceAsync(_conversationId.Value, adviceId);
            }
            else
            {
                await Api.RejectAgentAdviceAsync(_conversationId.Value, adviceId);
            }
            _renderSignature = string.Empty;
            StartPolling();
        }
        catch (Exception exception)
        {
            rejectButton.Enabled = true;
            approveButton.Enabled = true;
            ShowError(exception);
            StartPolling();
        }
    }

    private async Task ApproveAllAsync(MaterialButton button)
    {
        if (!_conversationId.HasValue)
        {
            return;
        }
        try
        {
            button.Enabled = false;
            StopPolling();
            await Api.ApproveAllAgentAdviceAsync(_conversationId.Value);
            _renderSignature = string.Empty;
            StartPolling();
        }
        catch (Exception exception)
        {
            button.Enabled = true;
            ShowError(exception);
            StartPolling();
        }
    }

    private async Task StartNewChatAsync()
    {
        try
        {
            _newChat.Enabled = false;
            StopPolling();
            if (_conversationId.HasValue)
            {
                await Api.CancelAgentConversationAsync(_conversationId.Value);
            }
        }
        catch (Exception exception)
        {
            ShowError(exception);
        }
        finally
        {
            _conversationId = null;
            _agentState = "Ready";
            _renderSignature = string.Empty;
            _toolbar.Subtitle = "Ready";
            _thinking.Visibility = ViewStates.Gone;
            _newChat.Enabled = true;
            ClearExcel();
            RenderWelcome();
            UpdateControls();
        }
    }

    private void ChooseExcel()
    {
        var picker = new Intent(Intent.ActionOpenDocument);
        picker.AddCategory(Intent.CategoryOpenable);
        picker.SetType(ExcelMimeType);
        picker.AddFlags(ActivityFlags.GrantReadUriPermission | ActivityFlags.GrantPersistableUriPermission);
        try
        {
            StartActivityForResult(Intent.CreateChooser(picker, "Attach Excel workbook"), ExcelPickerRequestCode);
        }
        catch (ActivityNotFoundException)
        {
            Snackbar.Make(_root, "No document picker is available on this device.", Snackbar.LengthLong).Show();
        }
    }

    private async Task ConvertExcelAsync(global::Android.Net.Uri uri)
    {
        var (name, size) = ReadDocumentMetadata(uri);
        if (!string.Equals(System.IO.Path.GetExtension(name), ".xlsx", StringComparison.OrdinalIgnoreCase))
        {
            Snackbar.Make(_root, "Only .xlsx workbooks are supported.", Snackbar.LengthLong).Show();
            return;
        }
        if (size > MaxExcelBytes)
        {
            Snackbar.Make(_root, "The workbook cannot exceed 10 MB.", Snackbar.LengthLong).Show();
            return;
        }

        try
        {
            TryPersistReadPermission(uri);
            SetBusy(true);
            await using var stream = ContentResolver?.OpenInputStream(uri)
                ?? throw new IOException("Could not open the selected workbook.");
            var response = await Api.ConvertAgentExcelAsync(stream, name);
            _excelMarkdown = response.Markdown;
            _excelFileName = response.FileName;
            ShowExcel();
            Snackbar.Make(_root, "Workbook attached", Snackbar.LengthShort).Show();
        }
        catch (Exception exception)
        {
            ClearExcel();
            ShowError(exception);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private (string Name, long? Size) ReadDocumentMetadata(global::Android.Net.Uri uri)
    {
        var name = "workbook.xlsx";
        long? size = null;
        try
        {
            using var cursor = ContentResolver?.Query(
                uri,
                [IOpenableColumns.DisplayName, IOpenableColumns.Size],
                null,
                null,
                null);
            if (cursor?.MoveToFirst() == true)
            {
                var nameIndex = cursor.GetColumnIndex(IOpenableColumns.DisplayName);
                if (nameIndex >= 0 && !cursor.IsNull(nameIndex))
                {
                    name = cursor.GetString(nameIndex) ?? name;
                }
                var sizeIndex = cursor.GetColumnIndex(IOpenableColumns.Size);
                if (sizeIndex >= 0 && !cursor.IsNull(sizeIndex))
                {
                    size = cursor.GetLong(sizeIndex);
                }
            }
        }
        catch
        {
            // The server still validates extension, length, and workbook contents.
        }
        return (name, size);
    }

    private void TryPersistReadPermission(global::Android.Net.Uri uri)
    {
        try
        {
            ContentResolver?.TakePersistableUriPermission(uri, ActivityFlags.GrantReadUriPermission);
        }
        catch
        {
            // The temporary grant is enough to convert immediately.
        }
    }

    private void ShowExcel()
    {
        _fileName.Text = $"Excel · {_excelFileName}";
        _fileChip.Visibility = ViewStates.Visible;
    }

    private void ClearExcel()
    {
        _excelMarkdown = null;
        _excelFileName = null;
        _fileName.Text = string.Empty;
        _fileChip.Visibility = ViewStates.Gone;
    }

    private void UpdateControls()
    {
        var processing = _agentState is "Thinking" or "AwaitingApproval";
        _boardSpinner.Enabled = _loaded && !_conversationId.HasValue && !_sending && !_busy;
        _input.Enabled = _loaded && !processing && !_sending && !_busy;
        _inputBox.Enabled = _input.Enabled;
        _send.Enabled = _input.Enabled;
        _attach.Enabled = _input.Enabled;
        _removeFile.Enabled = !_busy && !_sending;
        _newChat.Enabled = !_busy && !_sending;
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        _progress.Visibility = busy ? ViewStates.Visible : ViewStates.Gone;
        _content.Alpha = busy ? 0.55f : 1f;
        _content.Enabled = !busy;
        UpdateControls();
    }

    private MaterialCardView SurfaceCard()
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

    private MaterialButton PrimaryButton(string text)
    {
        var button = new MaterialButton(this)
        {
            Text = text,
            TextSize = 14,
            CornerRadius = Dp(14)
        };
        button.SetAllCaps(false);
        return button;
    }

    private MaterialButton SecondaryButton(string text)
    {
        var button = new MaterialButton(this, null, global::Android.Resource.Attribute.BorderlessButtonStyle)
        {
            Text = text,
            TextSize = 13,
            CornerRadius = Dp(14)
        };
        button.SetAllCaps(false);
        button.SetTextColor(ColorStateList.ValueOf(ColorOf(Resource.Color.brand_primary)));
        return button;
    }

    private MaterialButton DangerOutlineButton(string text)
    {
        var button = SecondaryButton(text);
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
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent);
        layout.SetMargins(0, Dp(top), 0, Dp(bottom));
        parent.AddView(view, layout);
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

    private static string FriendlyMessage(Exception exception)
    {
        var message = exception.Message;
        return message.Length > 180 ? message[..180] : message;
    }

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
