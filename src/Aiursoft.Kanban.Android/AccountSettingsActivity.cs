using Android.App;
using Android.Content;
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
using Google.Android.Material.Dialog;
using Google.Android.Material.MaterialSwitch;
using Google.Android.Material.ProgressIndicator;
using Google.Android.Material.Snackbar;
using Google.Android.Material.TextField;

namespace Aiursoft.Kanban.Android;

[Activity(Label = "Profile settings", Exported = false, Theme = "@style/AppTheme")]
public sealed class AccountSettingsActivity : AppCompatActivity
{
    private const int AvatarPickerRequestCode = 7701;
    private const long MaxAvatarBytes = 10L * 1024 * 1024;
    private static readonly string[] LanguageCodes = ["en", "zh", "ja", "ko"];
    private static readonly string[] LanguageLabels =
        ["English", "中文 (Chinese)", "日本語 (Japanese)", "한국어 (Korean)"];

    private View _root = null!;
    private global::AndroidX.Core.Widget.NestedScrollView _scroll = null!;
    private MaterialToolbar _toolbar = null!;
    private ImageView _avatar = null!;
    private MaterialButton _changeAvatar = null!;
    private TextView _email = null!;
    private LinearLayout _profileSection = null!;
    private TextInputLayout _displayNameBox = null!;
    private TextInputEditText _displayName = null!;
    private MaterialButton _saveProfile = null!;
    private MaterialSwitch _dailyReport = null!;
    private MaterialSwitch _weeklyReport = null!;
    private Spinner _language = null!;
    private MaterialButton _saveReports = null!;
    private LinearLayout _passwordSection = null!;
    private TextInputLayout _currentPasswordBox = null!;
    private TextInputEditText _currentPassword = null!;
    private TextInputLayout _newPasswordBox = null!;
    private TextInputEditText _newPassword = null!;
    private TextInputLayout _confirmPasswordBox = null!;
    private TextInputEditText _confirmPassword = null!;
    private MaterialButton _changePassword = null!;
    private TextView _deleteHint = null!;
    private MaterialButton _deleteAccount = null!;
    private CircularProgressIndicator _progress = null!;
    private readonly CancellationTokenSource _imageCancellation = new();
    private AccountProfileResponse? _model;
    private Bitmap? _avatarBitmap;
    private string? _loadedAvatarUrl;
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

        SetContentView(Resource.Layout.activity_account_settings);
        BindViews();
        ConfigureChrome();
        _language.Adapter = new ArrayAdapter<string>(this,
            global::Android.Resource.Layout.SimpleSpinnerDropDownItem, LanguageLabels);
        ConfigureAvatarShape();
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

    protected override void OnActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        base.OnActivityResult(requestCode, resultCode, data);
        if (requestCode == AvatarPickerRequestCode && resultCode == Result.Ok && data?.Data != null)
        {
            _ = UploadAvatarAsync(data.Data);
        }
    }

    protected override void OnDestroy()
    {
        _imageCancellation.Cancel();
        _imageCancellation.Dispose();
        _avatarBitmap?.Dispose();
        base.OnDestroy();
    }

    private void BindViews()
    {
        _root = FindViewById<View>(Resource.Id.account_root)!;
        _scroll = FindViewById<global::AndroidX.Core.Widget.NestedScrollView>(Resource.Id.account_scroll)!;
        _toolbar = FindViewById<MaterialToolbar>(Resource.Id.account_toolbar)!;
        _avatar = FindViewById<ImageView>(Resource.Id.account_avatar)!;
        _changeAvatar = FindViewById<MaterialButton>(Resource.Id.change_avatar_button)!;
        _email = FindViewById<TextView>(Resource.Id.account_email)!;
        _profileSection = FindViewById<LinearLayout>(Resource.Id.profile_name_section)!;
        _displayNameBox = FindViewById<TextInputLayout>(Resource.Id.display_name_box)!;
        _displayName = FindViewById<TextInputEditText>(Resource.Id.display_name_input)!;
        _saveProfile = FindViewById<MaterialButton>(Resource.Id.save_profile_button)!;
        _dailyReport = FindViewById<MaterialSwitch>(Resource.Id.enable_daily_report_switch)!;
        _weeklyReport = FindViewById<MaterialSwitch>(Resource.Id.enable_weekly_report_switch)!;
        _language = FindViewById<Spinner>(Resource.Id.report_language_spinner)!;
        _saveReports = FindViewById<MaterialButton>(Resource.Id.save_report_settings_button)!;
        _passwordSection = FindViewById<LinearLayout>(Resource.Id.password_section)!;
        _currentPasswordBox = FindViewById<TextInputLayout>(Resource.Id.current_password_box)!;
        _currentPassword = FindViewById<TextInputEditText>(Resource.Id.current_password_input)!;
        _newPasswordBox = FindViewById<TextInputLayout>(Resource.Id.new_password_box)!;
        _newPassword = FindViewById<TextInputEditText>(Resource.Id.new_password_input)!;
        _confirmPasswordBox = FindViewById<TextInputLayout>(Resource.Id.confirm_password_box)!;
        _confirmPassword = FindViewById<TextInputEditText>(Resource.Id.confirm_password_input)!;
        _changePassword = FindViewById<MaterialButton>(Resource.Id.change_password_button)!;
        _deleteHint = FindViewById<TextView>(Resource.Id.delete_account_hint)!;
        _deleteAccount = FindViewById<MaterialButton>(Resource.Id.delete_account_button)!;
        _progress = FindViewById<CircularProgressIndicator>(Resource.Id.account_progress)!;
    }

    private void ConfigureChrome()
    {
        _toolbar.SetOnApplyWindowInsetsListener(new ToolbarInsetListener(Dp(64)));
        _scroll.SetOnApplyWindowInsetsListener(
            new SystemBarInsetListener(0, 0, 0, Dp(12), false, true));
        _toolbar.NavigationContentDescription = "Back to Kanban";
        _toolbar.NavigationClick += (_, _) => Finish();
    }

    private void ConfigureAvatarShape()
    {
        var background = new GradientDrawable();
        background.SetShape(ShapeType.Oval);
        background.SetColor(GetColor(Resource.Color.brand_container));
        _avatar.Background = background;
        _avatar.ClipToOutline = true;
    }

    private void WireEvents()
    {
        _changeAvatar.Click += (_, _) => ChooseAvatar();
        _saveProfile.Click += async (_, _) => await SaveProfileAsync();
        _saveReports.Click += async (_, _) => await SaveReportSettingsAsync();
        _changePassword.Click += async (_, _) => await ChangePasswordAsync();
        _deleteAccount.Click += (_, _) => ConfirmDeleteAccount();
    }

    private async Task LoadAsync(bool showProgress = true)
    {
        if (_busy) return;
        try
        {
            SetBusy(true, showProgress);
            _model = await Api.GetAccountProfileAsync();
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
        if (model == null) return;
        _toolbar.Subtitle = model.DisplayName;
        _email.Text = model.Email;
        _profileSection.Visibility = model.CanChangeDisplayName ? ViewStates.Visible : ViewStates.Gone;
        _displayName.Text = model.DisplayName;
        _dailyReport.Checked = model.EnableDailyReport;
        _weeklyReport.Checked = model.EnableWeeklyReport;
        _language.SetSelection(Math.Max(0, Array.IndexOf(LanguageCodes, model.DailyReportLanguage)));
        _passwordSection.Visibility = model.CanChangePassword ? ViewStates.Visible : ViewStates.Gone;
        _deleteHint.Text = model.OwnedBoardCount == 0
            ? "Deleting your account is permanent and cannot be undone."
            : $"You own {model.OwnedBoardCount} board(s). Delete them before deleting your account.";
        _deleteAccount.Enabled = model.OwnedBoardCount == 0 && !_busy;
        if (!string.Equals(_loadedAvatarUrl, model.AvatarUrl, StringComparison.Ordinal))
        {
            _loadedAvatarUrl = model.AvatarUrl;
            _ = LoadAvatarAsync(model.AvatarUrl);
        }
    }

    private async Task SaveProfileAsync()
    {
        var value = _displayName.Text?.Trim() ?? string.Empty;
        if (value.Length is < 2 or > 30)
        {
            _displayNameBox.Error = "Use 2 to 30 characters";
            return;
        }
        try
        {
            _displayNameBox.Error = null;
            SetBusy(true, true);
            _model = await Api.UpdateProfileAsync(new UpdateProfileRequest { DisplayName = value });
            Session.UpdateDisplayName(_model.DisplayName);
            Render();
            Snackbar.Make(_root, "Profile saved", Snackbar.LengthShort).Show();
        }
        catch (Exception exception)
        {
            _displayNameBox.Error = FriendlyMessage(exception);
        }
        finally
        {
            SetBusy(false, true);
        }
    }

    private async Task SaveReportSettingsAsync()
    {
        try
        {
            SetBusy(true, true);
            _model = await Api.UpdateReportSettingsAsync(new UpdateReportSettingsRequest
            {
                EnableDailyReport = _dailyReport.Checked,
                EnableWeeklyReport = _weeklyReport.Checked,
                DailyReportLanguage = LanguageCodes[_language.SelectedItemPosition]
            });
            Render();
            Snackbar.Make(_root, "AI report settings saved", Snackbar.LengthShort).Show();
        }
        catch (Exception exception)
        {
            ShowError(exception);
        }
        finally
        {
            SetBusy(false, true);
        }
    }

    private async Task ChangePasswordAsync()
    {
        var current = _currentPassword.Text ?? string.Empty;
        var value = _newPassword.Text ?? string.Empty;
        var confirmation = _confirmPassword.Text ?? string.Empty;
        _currentPasswordBox.Error = string.IsNullOrWhiteSpace(current) ? "Enter your current password" : null;
        _newPasswordBox.Error = value.Length < 6 ? "Use at least 6 characters" : null;
        _confirmPasswordBox.Error = value != confirmation ? "Passwords do not match" : null;
        if (_currentPasswordBox.Error != null || _newPasswordBox.Error != null || _confirmPasswordBox.Error != null) return;
        try
        {
            SetBusy(true, true);
            await Api.ChangePasswordAsync(new ChangePasswordRequest
            {
                CurrentPassword = current,
                NewPassword = value,
                ConfirmPassword = confirmation
            });
            _currentPassword.Text = _newPassword.Text = _confirmPassword.Text = string.Empty;
            Snackbar.Make(_root, "Password changed", Snackbar.LengthLong).Show();
        }
        catch (Exception exception)
        {
            _currentPasswordBox.Error = FriendlyMessage(exception);
        }
        finally
        {
            SetBusy(false, true);
        }
    }

    private void ChooseAvatar()
    {
        var picker = new Intent(Intent.ActionOpenDocument);
        picker.AddCategory(Intent.CategoryOpenable);
        picker.SetType("image/*");
        picker.AddFlags(ActivityFlags.GrantReadUriPermission | ActivityFlags.GrantPersistableUriPermission);
        try
        {
            StartActivityForResult(Intent.CreateChooser(picker, "Choose profile photo"), AvatarPickerRequestCode);
        }
        catch (ActivityNotFoundException)
        {
            Snackbar.Make(_root, "No image picker is available.", Snackbar.LengthLong).Show();
        }
    }

    private async Task UploadAvatarAsync(global::Android.Net.Uri uri)
    {
        var (name, size, contentType) = ReadDocumentMetadata(uri);
        var extension = System.IO.Path.GetExtension(name).TrimStart('.').ToLowerInvariant();
        if (extension is not ("png" or "jpg" or "jpeg" or "bmp"))
        {
            Snackbar.Make(_root, "Choose a PNG, JPG, JPEG, or BMP image.", Snackbar.LengthLong).Show();
            return;
        }
        if (size > MaxAvatarBytes)
        {
            Snackbar.Make(_root, "The profile photo cannot exceed 10 MB.", Snackbar.LengthLong).Show();
            return;
        }
        try
        {
            SetBusy(true, true);
            var grant = await Api.GetAvatarUploadGrantAsync();
            await using var stream = ContentResolver?.OpenInputStream(uri)
                ?? throw new IOException("Could not open the selected image.");
            var uploaded = await Api.UploadCardImageAsync(grant, stream,
                $"android-avatar-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}.{extension}", contentType);
            _model = await Api.UpdateAvatarAsync(uploaded.Path);
            _loadedAvatarUrl = null;
            Render();
            Snackbar.Make(_root, "Profile photo updated", Snackbar.LengthShort).Show();
        }
        catch (Exception exception)
        {
            ShowError(exception);
        }
        finally
        {
            SetBusy(false, true);
        }
    }

    private (string Name, long? Size, string? ContentType) ReadDocumentMetadata(global::Android.Net.Uri uri)
    {
        var name = "avatar.jpg";
        long? size = null;
        try
        {
            using var cursor = ContentResolver?.Query(uri,
                [IOpenableColumns.DisplayName, IOpenableColumns.Size], null, null, null);
            if (cursor?.MoveToFirst() == true)
            {
                var nameIndex = cursor.GetColumnIndex(IOpenableColumns.DisplayName);
                if (nameIndex >= 0 && !cursor.IsNull(nameIndex)) name = cursor.GetString(nameIndex) ?? name;
                var sizeIndex = cursor.GetColumnIndex(IOpenableColumns.Size);
                if (sizeIndex >= 0 && !cursor.IsNull(sizeIndex)) size = cursor.GetLong(sizeIndex);
            }
        }
        catch
        {
            // The server validates both upload size and raster image contents.
        }
        return (name, size, ContentResolver?.GetType(uri));
    }

    private async Task LoadAvatarAsync(string avatarUrl)
    {
        try
        {
            var bytes = await Api.DownloadCardImageThumbnailAsync(avatarUrl, 256, _imageCancellation.Token);
            var bitmap = BitmapFactory.DecodeByteArray(bytes, 0, bytes.Length);
            if (bitmap == null || _imageCancellation.IsCancellationRequested)
            {
                bitmap?.Dispose();
                return;
            }
            RunOnUiThread(() =>
            {
                if (IsFinishing || IsDestroyed)
                {
                    bitmap.Dispose();
                    return;
                }
                var old = _avatarBitmap;
                _avatarBitmap = bitmap;
                _avatar.ImageTintList = null;
                _avatar.SetImageBitmap(bitmap);
                old?.Dispose();
            });
        }
        catch (System.OperationCanceledException)
        {
            // Activity is closing.
        }
        catch
        {
            // Keep the placeholder if the remote avatar is unavailable.
        }
    }

    private void ConfirmDeleteAccount()
    {
        if (_model?.OwnedBoardCount != 0 || _busy) return;
        var builder = new MaterialAlertDialogBuilder(this);
        builder.SetTitle("Delete account permanently?");
        builder.SetMessage("Your account data will be removed. This cannot be undone.");
        builder.SetNegativeButton("Cancel", (_, _) => { });
        builder.SetPositiveButton("Delete account", (_, _) => _ = DeleteAccountAsync());
        builder.Show();
    }

    private async Task DeleteAccountAsync()
    {
        try
        {
            SetBusy(true, true);
            await Api.DeleteAccountAsync();
            Session.SignOut();
            ReturnToLogin();
        }
        catch (Exception exception)
        {
            ShowError(exception);
            SetBusy(false, true);
        }
    }

    private void SetBusy(bool busy, bool showProgress)
    {
        _busy = busy;
        _progress.Visibility = busy && showProgress ? ViewStates.Visible : ViewStates.Gone;
        _scroll.Alpha = busy ? 0.55f : 1f;
        _changeAvatar.Enabled = !busy;
        _saveProfile.Enabled = !busy;
        _saveReports.Enabled = !busy;
        _changePassword.Enabled = !busy;
        _deleteAccount.Enabled = !busy && _model?.OwnedBoardCount == 0;
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
