using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using FaraBotModerator.Controllers;
using FaraBotModerator.Models;
using FaraBotModerator.Properties;
using Microsoft.Web.WebView2.Core;
using BattleFormat = FaraPokemonBattleAssistant.Core.Models.BattleFormat;
using RemoteRosterStore = FaraPokemonBattleAssistant.Core.Roster.RemoteRosterStore;
using RosterUnavailableException = FaraPokemonBattleAssistant.Core.Roster.RosterUnavailableException;

namespace FaraBotModerator.Views;

/// <summary>
///     Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : INotifyPropertyChanged
{
    /// <summary>
    /// 
    /// </summary>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// 
    /// </summary>
    /// <param name="propertyName"></param>
    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    /// <summary>
    /// </summary>
    private ChatWindow? _chatWindow;

    /// <summary>
    /// </summary>
    private RaidListWindow? _raidListWindow;

    /// <summary>
    /// 登録データ（ポケモンシリーズ）の一覧のウィンドウ（開いていなければ null）
    /// </summary>
    private PokemonRosterWindow? _pokemonRosterWindow;

    /// <summary>
    /// 最後に読み込んだ登録データ（読み込めていない・アクセスキーが無いときは null）。一覧のウィンドウを開いたときに使います
    /// </summary>
    private IReadOnlyList<RosterGameView>? _pokemonRosterGames;

    /// <summary>
    /// 登録データの読み込みの通し番号（古い読み込みの結果で上書きしないため）
    /// </summary>
    private int _pokemonRosterLoadVersion;

    /// <summary>
    /// </summary>
    private TwitchApiController? _twitchApiController;

    /// <summary>
    /// </summary>
    private TwitchClientController? _twitchClientController;

    /// <summary>
    /// </summary>
    private TwitchEventSubController? _twitchEventSubController;

    /// <summary>
    /// 使用中のポケモンの登録データのアクセスキー（空なら未設定）。入力欄の文字ではなく、発行・入力で確定した値を保存します。
    /// アクセスキーはパスワードと同じ扱いで、ログや画面のメッセージには出しません。
    /// </summary>
    private string _pokemonSyncKey = "";

    /// <summary>
    /// </summary>
    private TwitchTestEventController? _twitchTestEventController;

    /// <summary>
    /// テスト用のコントローラーを作ったときの、チャット接続（TwitchClientController）。接続をし直したら作り直すため
    /// </summary>
    private TwitchClientController? _testEventClient;

    /// <summary>
    /// </summary>
    private readonly TwitchAuthService _twitchAuthService = new();

    /// <summary>
    /// </summary>
    public MainWindow()
    {
        InitializeComponent();
        InitializeEncodeRegister();
        PokemonCommandController.RegisteredFormatChanged += OnPokemonRegisteredFormatChanged;

        DataContext = this;
        
        // Task
        _ = RunWithExceptionHandlingAsync(StartTimerAsync, "Timer");
        _ = RunWithExceptionHandlingAsync(StartWebServerAsync, "WebServer");
        _ = RunWithExceptionHandlingAsync(StartMonitoringAsync, "Monitoring");
        _ = RunWithExceptionHandlingAsync(StartTwitchLibEventSubAsync, "TwitchPubSub");

        Loaded += async (_, _) =>
        {
            // 初期化処理の実行
            await InitializeApplicationAsync();
        };
    }

    /// <summary>
    /// アプリケーションの初期化処理を非同期で行います。
    /// secrets.jsonの読み込みが完了するまでUIをブロックします。
    /// </summary>
    private async Task InitializeApplicationAsync()
    {
        LoadingTextBlock.Text = "Loading secrets.json...";
        LoadingOverlay.Visibility = Visibility.Visible;
        try
        {
            // 非同期での読み込みと初期化
            LogController.OutputLog("Starting to load secret keys...");
            var secretKeys = await Task.Run(SecretKeyController.LoadKeys);
            LogController.OutputLog("Secret keys loaded successfully.");

            // UIスレッドで実行する必要がある処理
            InitializeSecretValue(secretKeys);
            LogController.OutputLog("UI initialized with secret values.");
            
            try
            {
                LogController.OutputLog("Initializing WebView2...");
                await FaraBotModeratorWebView.EnsureCoreWebView2Async();
                LogController.OutputLog("WebView2 initialized successfully.");
            }
            catch (Exception ex)
            {
                LogController.OutputLog($"WebView2 initialization failed: {ex.Message}");
                // WebView2が初期化できなくてもアプリ全体が落ちないようにする
            }

            _twitchApiController ??= new TwitchApiController(secretKeys);
            await _twitchApiController.InitializeAsync();
            _twitchClientController ??= new TwitchClientController(secretKeys, _twitchApiController);

            _chatWindow ??= new ChatWindow(_twitchClientController);
            _chatWindow.Show();

            _raidListWindow ??= new RaidListWindow(_twitchApiController);
            _raidListWindow.Show();
        }
        catch (Exception ex)
        {
            LogController.OutputLog($"Initialization failed: {ex.Message}");
            MessageBox.Show($"初期化に失敗しました: {ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            // 読み込み完了後にオーバーレイを非表示にする
            LoadingOverlay.Visibility = Visibility.Collapsed;
        }
    }

    /// <summary>
    ///     Windowsが提供するEncodingを利用できるようにする。SHIFT-JIS等も使用可能になる。
    /// </summary>
    private void InitializeEncodeRegister()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    private void InitializeSecretValue(SecretKeyModel secretKeys)
    {
        // Twitch
        TwitchClientUserNameTextBox.Text = secretKeys.Twitch.Client.UserName;
        TwitchClientDisplayNameTextBox.Text = secretKeys.Twitch.Client.DisplayName;
        TwitchApiClientIdPasswordBox.Password = secretKeys.Twitch.Api.ClientId;
        TwitchApiClientSecretPasswordBox.Password = secretKeys.Twitch.Api.Secret;
        
        // DeepL
        DeepLApiKeyPasswordBox.Password = secretKeys.DeepL.ApiKey;

        // Beat Saber
        BeatSaberBsrDataGrid.ItemsSource = TextRegexController.LoadBsrChatModel().BeatSaberChat;

        // BouyomiChan
        BouyomiChanConnectCheckBox.IsChecked = secretKeys.BouyomiChan.Checked;

        // Pokemon
        PokemonBattleFormatComboBox.SelectedIndex = PokemonBattleFormatIndex(secretKeys.Pokemon.BattleFormat);
        PokemonDataSourceComboBox.SelectedIndex =
            PokemonCommandController.ParseDataSource(secretKeys.Pokemon.DataSource) == PokemonDataSource.Develop ? 1 : 0;
        _pokemonSyncKey = RemoteRosterStore.IsValidKey(secretKeys.Pokemon.SyncKey) ? secretKeys.Pokemon.SyncKey : "";
        SetPokemonSyncKeyInput(_pokemonSyncKey);
        UpdatePokemonSyncKeyButtons();

        // Events
        SetEventValue(FollowEventCheckBox, FollowEventTextBox, secretKeys.Event.Follow);
        SetEventValue(RaidEventCheckBox, RaidEventTextBox, secretKeys.Event.Raid);
        SetEventValue(SubscriptionEventCheckBox, SubscriptionEventTextBox, secretKeys.Event.Subscription);
        SetEventValue(BitsEventCheckBox, BitsEventTextBox, secretKeys.Event.Bits);
        SetEventValue(GiftEventCheckBox, GiftEventTextBox, secretKeys.Event.Gift);
        SetEventValue(ChannelPointEventCheckBox, ChannelPointEventTextBox, secretKeys.Event.ChannelPoint);

        // Cycle Timers
        SetCycleTimerValue(CycleTimer1CheckBox, CycleTimer1Slider, CycleTimer1TextBox, secretKeys.CycleMessage.Timer1);
        SetCycleTimerValue(CycleTimer2CheckBox, CycleTimer2Slider, CycleTimer2TextBox, secretKeys.CycleMessage.Timer2);
        SetCycleTimerValue(CycleTimer3CheckBox, CycleTimer3Slider, CycleTimer3TextBox, secretKeys.CycleMessage.Timer3);
        SetCycleTimerValue(CycleTimer4CheckBox, CycleTimer4Slider, CycleTimer4TextBox, secretKeys.CycleMessage.Timer4);

        // Fixed Timers
        SetFixedTimerValue(FixedTimer1CheckBox, FixedTimer1DatePicker, FixedTimer1TimePicker, FixedTimer1TextBox, secretKeys.FixedMessage.Timer1);
        SetFixedTimerValue(FixedTimer2CheckBox, FixedTimer2DatePicker, FixedTimer2TimePicker, FixedTimer2TextBox, secretKeys.FixedMessage.Timer2);
        SetFixedTimerValue(FixedTimer3CheckBox, FixedTimer3DatePicker, FixedTimer3TimePicker, FixedTimer3TextBox, secretKeys.FixedMessage.Timer3);
        SetFixedTimerValue(FixedTimer4CheckBox, FixedTimer4DatePicker, FixedTimer4TimePicker, FixedTimer4TextBox, secretKeys.FixedMessage.Timer4);
    }

    private void SetEventValue(CheckBox checkBox, TextBox textBox, IEventModel model)
    {
        checkBox.IsChecked = model.Checked;
        textBox.Text = model.Message;
    }

    private void SetCycleTimerValue(CheckBox checkBox, Slider slider, TextBox textBox, CycleTimerModel model)
    {
        checkBox.IsChecked = model.Checked;
        slider.Value = model.Interval;
        textBox.Text = model.Message;
    }

    private void SetFixedTimerValue(CheckBox checkBox, DatePicker datePicker, MaterialDesignThemes.Wpf.TimePicker timePicker, TextBox textBox, FixedTimerModel model)
    {
        checkBox.IsChecked = model.Checked;
        if (DateTime.TryParse(model.DatetimeString, out var dt))
        {
            datePicker.SelectedDate = dt;
            timePicker.SelectedTime = dt;
        }
        textBox.Text = model.Message;
    }

    private async Task RunWithExceptionHandlingAsync(Func<Task> taskFunc, string taskName)
    {
        try
        {
            await taskFunc();
        }
        catch (Exception ex)
        {
            // ログ出力や通知など
            LogController.OutputLog($"Error in {taskName}: {ex.Message}");
        }
    }

    /// <summary>
    /// </summary>
    /// <returns></returns>
    private async Task StartTimerAsync()
    {
        var cycleTimerCounts = new int[4];

        // UI element arrays for cycle timers
        CheckBox[] cycleCheckBoxes = null!;
        Slider[] cycleSliders = null!;
        TextBox[] cycleTextBoxes = null!;

        // UI element arrays for fixed timers
        CheckBox[] fixedCheckBoxes = null!;
        DatePicker[] fixedDatePickers = null!;
        MaterialDesignThemes.Wpf.TimePicker[] fixedTimePickers = null!;
        TextBox[] fixedTextBoxes = null!;

        // Initialize UI element arrays on the UI thread
        await Dispatcher.InvokeAsync(() =>
        {
            cycleCheckBoxes = [CycleTimer1CheckBox, CycleTimer2CheckBox, CycleTimer3CheckBox, CycleTimer4CheckBox];
            cycleSliders = [CycleTimer1Slider, CycleTimer2Slider, CycleTimer3Slider, CycleTimer4Slider];
            cycleTextBoxes = [CycleTimer1TextBox, CycleTimer2TextBox, CycleTimer3TextBox, CycleTimer4TextBox];

            fixedCheckBoxes = [FixedTimer1CheckBox, FixedTimer2CheckBox, FixedTimer3CheckBox, FixedTimer4CheckBox];
            fixedDatePickers = [FixedTimer1DatePicker, FixedTimer2DatePicker, FixedTimer3DatePicker, FixedTimer4DatePicker];
            fixedTimePickers = [FixedTimer1TimePicker, FixedTimer2TimePicker, FixedTimer3TimePicker, FixedTimer4TimePicker];
            fixedTextBoxes = [FixedTimer1TextBox, FixedTimer2TextBox, FixedTimer3TextBox, FixedTimer4TextBox];
        });

        while (true)
        {
            while (_twitchClientController is null) await Task.Delay(1);

            await Task.Delay(1000);

            // Process cycle timers
            for (var i = 0; i < 4; i++)
            {
                var isChecked = false;
                var intervalMinutes = 0;
                var message = "";

                await Dispatcher.InvokeAsync(() =>
                {
                    isChecked = cycleCheckBoxes[i].IsChecked ?? false;
                    intervalMinutes = (int)cycleSliders[i].Value;
                    message = cycleTextBoxes[i].Text;
                });

                if (isChecked)
                    cycleTimerCounts[i]++;
                else
                    cycleTimerCounts[i] = 0;

                var intervalSeconds = intervalMinutes * 60;
                if (cycleTimerCounts[i] >= intervalSeconds && intervalSeconds > 0)
                {
                    cycleTimerCounts[i] %= intervalSeconds;
                    _twitchClientController.SendModeratorMessage(message);
                }
            }

            // Process fixed timers
            for (var i = 0; i < 4; i++)
            {
                var isChecked = false;
                DateTime? date = null;
                DateTime? time = null;
                var message = "";

                await Dispatcher.InvokeAsync(() =>
                {
                    isChecked = fixedCheckBoxes[i].IsChecked ?? false;
                    date = fixedDatePickers[i].SelectedDate;
                    time = fixedTimePickers[i].SelectedTime;
                    message = fixedTextBoxes[i].Text;
                });

                if (isChecked)
                {
                    FixedTimerMessage(date, time, message);
                }
            }
        }
    }

    /// <summary>
    /// </summary>
    /// <param name="date"></param>
    /// <param name="time"></param>
    /// <param name="message"></param>
    private void FixedTimerMessage(DateTime? date, DateTime? time, string message)
    {
        if (date is null || time is null) return;

        var datetime = new DateTime(date.Value.Year, date.Value.Month, date.Value.Day, time.Value.Hour,
            time.Value.Minute, time.Value.Second);
        if (DateTime.Now >= datetime && (DateTime.Now - datetime).TotalSeconds < 1)
            _twitchClientController?.SendModeratorMessage(message);
    }

    /// <summary>
    /// </summary>
    /// <returns></returns>
    private async Task StartWebServerAsync()
    {
        while (true)
        {
            await Task.Delay(100);
            // 1 周期の失敗で監視を止めない（戻りの URL を処理できなくても、次の周期でまた見る）
            try
            {
                if (FaraBotModeratorWebView.Source is null) continue;

                // 認証を始めていない・処理済み・戻りの形でない URL は無視する（処理した戻りの URL は、画面に残っていても 2 度処理しない）
                var url = FaraBotModeratorWebView.Source.ToString();
                if (!TwitchAuthService.ShouldHandleAuthUrl(url, _lastHandledAuthUrl, Settings.Default.OAuth2State, Settings.Default.Port))
                    continue;

                _lastHandledAuthUrl = url;
                await UpdateAccessTokenAsync(url);
            }
            catch (Exception ex)
            {
                LogController.OutputLog($"<Error> 認証の戻りの処理に失敗しました（次の周期で続けます）: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// 最後に処理した認証の戻りの URL（同じ URL を 2 度処理しないため）。
    /// </summary>
    private string _lastHandledAuthUrl = "";

    /// <summary>
    /// </summary>
    /// <returns></returns>
    private async Task StartMonitoringAsync()
    {
        while (true)
        {
            await Task.Delay(1000);
            try
            {
                await MonitorOnceAsync();
            }
            catch (Exception ex)
            {
                // 1 周期の失敗で監視を止めない（トークン期限・棒読みちゃん・チャット表示は、次の周期でまた動く）
                LogController.OutputLog($"<Error> 監視の処理に失敗しました（次の周期で続けます）: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// 監視の 1 周期分（トークン期限・チャット表示・棒読みちゃんの接続確認）。
    /// </summary>
    private async Task MonitorOnceAsync()
    {
        // Token期限
        if (DateTime.Now > Settings.Default.expiresDateTime)
        {
            TwitchApiNotificationCanvas.Visibility = Visibility.Visible;
            if (!string.IsNullOrEmpty(Settings.Default.RefreshToken)) await UpdateRefreshTokenAsync();
        }
        else
        {
            TwitchApiNotificationCanvas.Visibility = Visibility.Hidden;
        }

        TwitchApiExpireDateTimeTextBlock.Text = $"Token expiration: {Settings.Default.expiresDateTime}";

        if (_twitchClientController is not null) AddGridViewChatData();

        // 棒読みちゃん接続チェック
        if (BouyomiChanConnectCheckBox.IsChecked != true || TwitchConnectionStateLabel.Content.ToString() != "State: Connect") return;
        if (_twitchClientController != null && !await BouyomiChanController.IsBouyomiChanRunningAsync())
        {
            // Dispatcher.InvokeAsync は Task を返すので、中の TwitchDisconnect まで待つために await を 2 回書く
            await await Dispatcher.InvokeAsync(async () =>
            {
                MessageBox.Show("棒読みちゃんが終了しました。Disconnectする前に棒読みちゃんを閉じないでください。\n安全のためTwitchから切断します。", "警告", MessageBoxButton.OK, MessageBoxImage.Warning);
                await TwitchDisconnect();
            });
        }
    }

    private async Task StartTwitchLibEventSubAsync()
    {
        while (true)
        {
            while (_twitchClientController is null) await Task.Delay(100);

            await Task.Delay(500);
            try
            {
                UpdateConnectionStateOnce();
            }
            catch (Exception ex)
            {
                // 1 周期の失敗で状態表示の更新を止めない
                LogController.OutputLog($"<Error> 接続状態の表示に失敗しました（次の周期で続けます）: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// 接続状態の表示と、テスト送信ボタンの有効・無効を 1 回更新します。
    /// </summary>
    private void UpdateConnectionStateOnce()
    {
        var isConnected = _twitchClientController is { IsConnected: true } &&
                          _twitchEventSubController is { IsConnected: true } &&
                          DateTime.Now <= Settings.Default.expiresDateTime;

        TwitchConnectionStateLabel.Content = isConnected
            ? @"State: Connect"
            : @"State: Disconnect";

        // Test Sendボタンの状態を制御
        FollowTestButton.IsEnabled = isConnected;
        RaidTestButton.IsEnabled = isConnected;
        SubscriptionTestButton.IsEnabled = isConnected;
        BitsTestButton.IsEnabled = isConnected;
        GiftTestButton.IsEnabled = isConnected;
        ChannelPointTestButton.IsEnabled = isConnected;
    }

    private void AddGridViewChatData()
    {
        if (_chatWindow is null) return;

        // 溜まっているチャットを、上限までまとめて表示に追加する（1 周期に 1 件だと、1 秒に 2 件以上のとき、表示が遅れ続けるため）
        for (var i = 0; i < MaxChatRowsPerTick; i++)
        {
            var chatModelData = _twitchClientController?.PickChatData();
            if (chatModelData is null) return;
            AddChatRow(_chatWindow, chatModelData);
        }
    }

    /// <summary>
    /// 1 回の監視の周期で表示するチャットの上限（溜まったときに UI を止めないため。残りは次の周期で出す）。
    /// </summary>
    private const int MaxChatRowsPerTick = 50;

    private static void AddChatRow(ChatWindow chatWindow, ChatModel chatModelData)
    {
        var beforeScrollBottom = true;

        // 表示の部品の並びが無いとき（まだ描画されていない・作りが変わった）は、既定（一番下）として扱う
        if (GetChildOrNull(GetChildOrNull(chatWindow.TwitchChatDataGrid, 0), 0) is ScrollViewer viewer)
        {
            var offset = viewer.VerticalOffset;
            var extent = viewer.ScrollableHeight;
            var viewport = viewer.ViewportHeight;

            beforeScrollBottom = offset + viewport >= extent;
        }

        chatWindow.TwitchChatDataGrid.Items.Add(chatModelData);
        if (beforeScrollBottom) chatWindow.TwitchChatDataGrid.ScrollIntoView(chatModelData);
    }

    /// <summary>
    /// 子要素を番号で取ります。その番号の子が無いときは null（VisualTreeHelper.GetChild は範囲外で例外になるため）。
    /// </summary>
    private static DependencyObject? GetChildOrNull(DependencyObject? parent, int index)
    {
        if (parent is null || VisualTreeHelper.GetChildrenCount(parent) <= index) return null;
        return VisualTreeHelper.GetChild(parent, index);
    }

    /// <summary>
    ///     Token期限切れの時に呼び出してTokenを更新します。
    /// </summary>
    private async Task UpdateRefreshTokenAsync()
    {
        var success = await _twitchAuthService.RefreshAccessTokenAsync(
            TwitchApiClientIdPasswordBox.Password,
            TwitchApiClientSecretPasswordBox.Password);

        if (!success)
        {
            RequestApiAccessToken();
        }
    }

    /// <summary>
    ///     Twitch API Access Tokenを取得、更新します。
    ///     Token取得はPOST通信をするため、WebViewは通さずPostRequestを行う。
    /// </summary>
    /// <param name="url"></param>
    private async Task UpdateAccessTokenAsync(string url)
    {
        // 戻りの URL は ?code=…&scope=…&state=… の形。値だけを取り出す（貪欲な一致だと、後ろの &scope=… まで code に入ってしまう）
        var state = TwitchAuthService.ExtractQueryValue(url, "state");
        var code = TwitchAuthService.ExtractQueryValue(url, "code");

        var success = await _twitchAuthService.ExchangeAuthorizationCodeAsync(
            code,
            TwitchApiClientIdPasswordBox.Password,
            TwitchApiClientSecretPasswordBox.Password,
            Settings.Default.OAuth2State,
            state);

        if (success)
        {
            UnlockWindowControl();
        }
    }

    /// <summary>
    ///     accessTokenに必要な認証コードを取得するURLを開く
    /// </summary>
    private void RequestApiAccessToken()
    {
        try
        {
            var requestUrl = _twitchAuthService.GenerateAuthorizationUrl(TwitchApiClientIdPasswordBox.Password);
            LockWindowControl(true);
            FaraBotModeratorWebView.Source = new Uri(requestUrl);
        }
        catch (Exception ex)
        {
            UnlockWindowControl();
            LogController.OutputLog(ex.Message);
        }
    }

    /// <summary>
    ///     Control全体をロック
    /// </summary>
    private void LockWindowControl(bool isAuthorize = false)
    {
        SetWindowControlEnabled(false, isAuthorize);
    }

    /// <summary>
    ///     Control全体のロックを解除
    /// </summary>
    private void UnlockWindowControl()
    {
        SetWindowControlEnabled(true);
    }

    private void SetWindowControlEnabled(bool isEnabled, bool isAuthorize = false)
    {
        Dispatcher.Invoke(() =>
        {
            // Main Settings
            TwitchClientUserNameTextBox.IsEnabled = isEnabled;
            TwitchClientDisplayNameTextBox.IsEnabled = isEnabled;
            TwitchApiClientIdPasswordBox.IsEnabled = isEnabled;
            TwitchApiClientSecretPasswordBox.IsEnabled = isEnabled;
            
            if (isEnabled) TwitchApiAuthorizeButton.IsEnabled = true;
            else if (!isAuthorize) TwitchApiAuthorizeButton.IsEnabled = false;

            BouyomiChanConnectCheckBox.IsEnabled = isEnabled;
            TwitchConnectionButton.IsEnabled = isEnabled;
            
            if (isEnabled) 
            {
                TwitchDisconnectButton.IsEnabled = true;
                TwitchPageButton.IsEnabled = true;
                DeepLSiteGoButton.IsEnabled = true;
            }
            else if (isAuthorize)
            {
                TwitchDisconnectButton.IsEnabled = false;
            }

            // Reaction Event Settings
            FollowEventCheckBox.IsEnabled = isEnabled;
            FollowEventTextBox.IsEnabled = isEnabled;
            RaidEventCheckBox.IsEnabled = isEnabled;
            RaidEventTextBox.IsEnabled = isEnabled;
            SubscriptionEventCheckBox.IsEnabled = isEnabled;
            SubscriptionEventTextBox.IsEnabled = isEnabled;
            BitsEventCheckBox.IsEnabled = isEnabled;
            BitsEventTextBox.IsEnabled = isEnabled;
            GiftEventCheckBox.IsEnabled = isEnabled;
            GiftEventTextBox.IsEnabled = isEnabled;
            ChannelPointEventCheckBox.IsEnabled = isEnabled;
            ChannelPointEventTextBox.IsEnabled = isEnabled;

            // Auto Bot Settings - Translate
            DeepLApiKeyPasswordBox.IsEnabled = isEnabled;

            // Auto Bot Settings - Timer
            SetCycleTimerEnabled(isEnabled, CycleTimer1CheckBox, CycleTimer1Slider, CycleTimer1TextBox);
            SetCycleTimerEnabled(isEnabled, CycleTimer2CheckBox, CycleTimer2Slider, CycleTimer2TextBox);
            SetCycleTimerEnabled(isEnabled, CycleTimer3CheckBox, CycleTimer3Slider, CycleTimer3TextBox);
            SetCycleTimerEnabled(isEnabled, CycleTimer4CheckBox, CycleTimer4Slider, CycleTimer4TextBox);

            SetFixedTimerEnabled(isEnabled, FixedTimer1CheckBox, FixedTimer1DatePicker, FixedTimer1TimePicker, FixedTimer1TextBox);
            SetFixedTimerEnabled(isEnabled, FixedTimer2CheckBox, FixedTimer2DatePicker, FixedTimer2TimePicker, FixedTimer2TextBox);
            SetFixedTimerEnabled(isEnabled, FixedTimer3CheckBox, FixedTimer3DatePicker, FixedTimer3TimePicker, FixedTimer3TextBox);
            SetFixedTimerEnabled(isEnabled, FixedTimer4CheckBox, FixedTimer4DatePicker, FixedTimer4TimePicker, FixedTimer4TextBox);
        });
    }

    private void SetCycleTimerEnabled(bool isEnabled, CheckBox checkBox, Slider slider, TextBox textBox)
    {
        checkBox.IsEnabled = isEnabled;
        slider.IsEnabled = isEnabled;
        textBox.IsEnabled = isEnabled;
    }

    private void SetFixedTimerEnabled(bool isEnabled, CheckBox checkBox, DatePicker datePicker, MaterialDesignThemes.Wpf.TimePicker timePicker, TextBox textBox)
    {
        checkBox.IsEnabled = isEnabled;
        datePicker.IsEnabled = isEnabled;
        timePicker.IsEnabled = isEnabled;
        textBox.IsEnabled = isEnabled;
    }

    /// <summary>
    ///     Secretの値を保存します。
    /// </summary>
    /// <param name="syncKey">保存するアクセスキー（省略時は今のアクセスキー。切り替える前に新しいキーを保存するときに使う）</param>
    private void SaveSecretValue(string? syncKey = null)
    {
        var timer1 =
            (FixedTimer1DatePicker.SelectedDate is not null
                ? FixedTimer1DatePicker.SelectedDate.Value.ToShortDateString()
                : "2023/1/1") + " " +
            (FixedTimer1TimePicker.SelectedTime is not null
                ? FixedTimer1TimePicker.SelectedTime.Value.ToLongTimeString()
                : "00:00:00");
        var timer2 =
            (FixedTimer2DatePicker.SelectedDate is not null
                ? FixedTimer2DatePicker.SelectedDate.Value.ToShortDateString()
                : "2023/1/1") + " " +
            (FixedTimer2TimePicker.SelectedTime is not null
                ? FixedTimer2TimePicker.SelectedTime.Value.ToLongTimeString()
                : "00:00:00");
        var timer3 =
            (FixedTimer3DatePicker.SelectedDate is not null
                ? FixedTimer3DatePicker.SelectedDate.Value.ToShortDateString()
                : "2023/1/1") + " " +
            (FixedTimer3TimePicker.SelectedTime is not null
                ? FixedTimer3TimePicker.SelectedTime.Value.ToLongTimeString()
                : "00:00:00");
        var timer4 =
            (FixedTimer4DatePicker.SelectedDate is not null
                ? FixedTimer4DatePicker.SelectedDate.Value.ToShortDateString()
                : "2023/1/1") + " " +
            (FixedTimer4TimePicker.SelectedTime is not null
                ? FixedTimer4TimePicker.SelectedTime.Value.ToLongTimeString()
                : "00:00:00");

        var secretKeys = new SecretKeyModel
        {
            Twitch = new TwitchSecretKeyModel
            {
                Client = new TwitchClientKeyModel
                {
                    UserName = TwitchClientUserNameTextBox.Text, // TwitchのURLの末尾の名前
                    DisplayName = TwitchClientDisplayNameTextBox.Text
                },
                Api = new TwitchApiKeyModel
                {
                    ClientId = TwitchApiClientIdPasswordBox.Password,
                    Secret = TwitchApiClientSecretPasswordBox.Password
                }
            },
            DeepL = new DeepLKeyModel
            {
                ApiKey = DeepLApiKeyPasswordBox.Password
            },
            BouyomiChan = new BouyomiChanModel
            {
                Checked = BouyomiChanConnectCheckBox.IsChecked ?? false
            },
            Event = new ReactionEventModel
            {
                Follow = new ReactionFollowEvent
                {
                    Checked = FollowEventCheckBox.IsChecked ?? false,
                    Message = FollowEventTextBox.Text
                },
                Raid = new ReactionRaidEvent
                {
                    Checked = RaidEventCheckBox.IsChecked ?? false,
                    Message = RaidEventTextBox.Text
                },
                Subscription = new ReactionSubscriptionEvent
                {
                    Checked = SubscriptionEventCheckBox.IsChecked ?? false,
                    Message = SubscriptionEventTextBox.Text
                },
                Bits = new ReactionBitsEvent
                {
                    Checked = BitsEventCheckBox.IsChecked ?? false,
                    Message = BitsEventTextBox.Text
                },
                Gift = new ReactionGiftEvent
                {
                    Checked = GiftEventCheckBox.IsChecked ?? false,
                    Message = GiftEventTextBox.Text
                },
                ChannelPoint = new ReactionChannelPointEvent
                {
                    Checked = ChannelPointEventCheckBox.IsChecked ?? false,
                    Message = ChannelPointEventTextBox.Text
                }
            },
            CycleMessage = new CycleMessageModel
            {
                Timer1 = new CycleTimerModel
                {
                    Checked = CycleTimer1CheckBox.IsChecked ?? false,
                    Interval = (int) CycleTimer1Slider.Value,
                    Message = CycleTimer1TextBox.Text
                },
                Timer2 = new CycleTimerModel
                {
                    Checked = CycleTimer2CheckBox.IsChecked ?? false,
                    Interval = (int) CycleTimer2Slider.Value,
                    Message = CycleTimer2TextBox.Text
                },
                Timer3 = new CycleTimerModel
                {
                    Checked = CycleTimer3CheckBox.IsChecked ?? false,
                    Interval = (int) CycleTimer3Slider.Value,
                    Message = CycleTimer3TextBox.Text
                },
                Timer4 = new CycleTimerModel
                {
                    Checked = CycleTimer4CheckBox.IsChecked ?? false,
                    Interval = (int) CycleTimer4Slider.Value,
                    Message = CycleTimer4TextBox.Text
                }
            },
            FixedMessage = new FixedMessageModel
            {
                Timer1 = new FixedTimerModel
                {
                    Checked = FixedTimer1CheckBox.IsChecked ?? false,
                    DatetimeString = timer1,
                    Message = FixedTimer1TextBox.Text
                },
                Timer2 = new FixedTimerModel
                {
                    Checked = FixedTimer2CheckBox.IsChecked ?? false,
                    DatetimeString = timer2,
                    Message = FixedTimer2TextBox.Text
                },
                Timer3 = new FixedTimerModel
                {
                    Checked = FixedTimer3CheckBox.IsChecked ?? false,
                    DatetimeString = timer3,
                    Message = FixedTimer3TextBox.Text
                },
                Timer4 = new FixedTimerModel
                {
                    Checked = FixedTimer4CheckBox.IsChecked ?? false,
                    DatetimeString = timer4,
                    Message = FixedTimer4TextBox.Text
                }
            },
            // Preserve existing BeatSaber settings (no UI, edited via secrets.json)
            BeatSaber = SecretKeyController.LoadKeys().BeatSaber,
            Pokemon = new PokemonModel
            {
                BattleFormat = SelectedPokemonBattleFormat(),
                DataSource = SelectedPokemonDataSource(),
                SyncKey = syncKey ?? _pokemonSyncKey
            }
        };

        SecretKeyController.SaveKeys(secretKeys);
    }

    /// <summary>
    /// </summary>
    private async Task TwitchConnect()
    {
        LoadingTextBlock.Text = "Connecting to Twitch...";
        LoadingOverlay.Visibility = Visibility.Visible;
        try
        {
            var secretKeys = SecretKeyController.LoadKeys();

            // トークンの有効期限チェック
            if (DateTime.Now > Settings.Default.expiresDateTime)
            {
                MessageBox.Show("アクセストークンの有効期限が切れています。Authorizeボタンから再認可を行ってください。", "警告", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            _twitchApiController = new TwitchApiController(secretKeys);
            await _twitchApiController.InitializeAsync();

            var isTokenValid = await _twitchApiController.ValidateTokenAsync();
            LogController.OutputLog($"Token validation result: {isTokenValid}");

            if (!isTokenValid)
            {
                MessageBox.Show("アクセストークンが無効または期限切れです。Authorizeボタンから再認可を行ってください。", "警告", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            _twitchClientController = new TwitchClientController(secretKeys, _twitchApiController);

            // 棒読みちゃん接続チェック
            if (BouyomiChanConnectCheckBox.IsChecked == true)
            {
                if (!await BouyomiChanController.IsBouyomiChanRunningAsync())
                {
                    MessageBox.Show("棒読みちゃんが起動していません。起動してから接続してください。", "警告", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
            }

            _twitchClientController.Connect();

            // EventSub
            _twitchEventSubController = new TwitchEventSubController(_twitchClientController, _twitchApiController);
            await TwitchEventSubController.ConnectAsync();

            LockWindowControl();
        }
        catch (Exception ex)
        {
            LogController.OutputLog($"Twitch connect failed: {ex.Message}");
            MessageBox.Show($"Twitchへの接続に失敗しました: {ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            LoadingOverlay.Visibility = Visibility.Collapsed;
        }
    }

    /// <summary>
    /// </summary>
    private async Task TwitchDisconnect()
    {
        LoadingTextBlock.Text = "Disconnecting from Twitch...";
        LoadingOverlay.Visibility = Visibility.Visible;
        try
        {
            if (_twitchEventSubController != null)
            {
                await TwitchEventSubController.DisconnectAsync();
            }

            if (_twitchClientController != null)
            {
                _twitchClientController.Disconnect();
            }

            _twitchClientController = null;
            _twitchApiController = null;
            _twitchEventSubController = null;

            UnlockWindowControl();
        }
        catch (Exception ex)
        {
            LogController.OutputLog($"Twitch disconnect failed: {ex.Message}");
        }
        finally
        {
            LoadingOverlay.Visibility = Visibility.Collapsed;
        }
    }

    /// <summary>
    ///     UserNameで設定したユーザのTwitchページ表示
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    private void TwitchPageButton_Click(object sender, RoutedEventArgs e)
    {
        FaraBotModeratorWebView.Source = new Uri($"https://twitch.tv/{TwitchClientUserNameTextBox.Text}");
    }

    /// <summary>
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    private async void TwitchConnectionButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            SaveSecretValue();

            var state = TwitchConnectionStateLabel.Content.ToString();
            if (state is not null && state.Equals("State: Connect"))
            {
                await TwitchDisconnect();
            }
            else
            {
                await TwitchConnect();
            }
        }
        catch (Exception ex)
        {
            LogController.OutputLog(ex.Message);
        }
    }

    /// <summary>
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    private async void TwitchDisconnectButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await TwitchDisconnect();
        }
        catch (Exception ex)
        {
            LogController.OutputLog(ex.Message);
        }
    }


    private void OpenChatWindowButton_Click(object sender, RoutedEventArgs e)
    {
        var secretKeys = SecretKeyController.LoadKeys();
        _twitchApiController ??= new TwitchApiController(secretKeys);
        _twitchClientController ??= new TwitchClientController(secretKeys, _twitchApiController);

        if (_chatWindow == null || !IsWindowOpen<ChatWindow>())
        {
            _chatWindow = new ChatWindow(_twitchClientController);
            _chatWindow.Show();
        }
        else
        {
            _chatWindow.Activate();
            if (_chatWindow.WindowState == WindowState.Minimized)
                _chatWindow.WindowState = WindowState.Normal;
        }
    }

    private void OpenRaidListWindowButton_Click(object sender, RoutedEventArgs e)
    {
        var secretKeys = SecretKeyController.LoadKeys();
        _twitchApiController ??= new TwitchApiController(secretKeys);

        if (_raidListWindow == null || !IsWindowOpen<RaidListWindow>())
        {
            _raidListWindow = new RaidListWindow(_twitchApiController);
            _raidListWindow.Show();
        }
        else
        {
            _raidListWindow.Activate();
            if (_raidListWindow.WindowState == WindowState.Minimized)
                _raidListWindow.WindowState = WindowState.Normal;
        }
    }

    private bool IsWindowOpen<T>() where T : Window
    {
        return Application.Current.Windows.OfType<T>().Any();
    }

    /// <summary>
    ///     Twitch API Token取得前のTwitch Loginページを表示します。
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    private void TwitchApiAuthorizeButton_Click(object sender, RoutedEventArgs e)
    {
        RequestApiAccessToken();
    }

    /// <summary>
    ///     DeepLサイトを表示します。
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    private void DeepLSiteGoButton_Click(object sender, RoutedEventArgs e)
    {
        MenuTabControl.SelectedIndex = 0;
        FaraBotModeratorWebView.Source = new Uri("https://www.deepl.com/ja/your-account/keys");
    }

    /// <summary>
    ///     終了時の処理
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    private void MainWindow_Closed(object sender, EventArgs e)
    {
        PokemonCommandController.RegisteredFormatChanged -= OnPokemonRegisteredFormatChanged;
        // チャットの format set が画面に反映される前に閉じても、古いコンボの値で上書きしないよう、最新の登録形式を画面に戻してから保存する
        if (_twitchClientController is not null)
            PokemonBattleFormatComboBox.SelectedIndex = PokemonBattleFormatIndex(_twitchClientController.PokemonBattleFormat);
        SaveSecretValue();
        _chatWindow?.Close();
        _raidListWindow?.Close();
        _pokemonRosterWindow?.Close();
    }

    /// <summary>
    ///     WebViewが読み込まれた後の処理
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    private void FaraBotModeratorWebView_NavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        FaraBotModeratorWebView.CoreWebView2.Settings.AreDefaultScriptDialogsEnabled = false; //ダイアログ表示を抑止
        FaraBotModeratorWebView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false; //コンテキストメニューを抑止
        FaraBotModeratorWebView.CoreWebView2.Settings.AreDevToolsEnabled = false; //開発者ツールを無効化
        FaraBotModeratorWebView.CoreWebView2.Settings.IsBuiltInErrorPageEnabled = false; //ブラウザに組み込まれているエラーページを無効化
        FaraBotModeratorWebView.CoreWebView2.Settings.IsZoomControlEnabled = false; //ズームコントロールを無効化
        FaraBotModeratorWebView.CoreWebView2.Settings.IsStatusBarEnabled = false; //ステータスバーを非表示
    }

    private void CycleTimerSlider_ManipulationStarted(object sender, ManipulationStartedEventArgs e)
    {
        ((Slider) sender).ToolTip = ((Slider) sender).Value.ToString(CultureInfo.CurrentCulture);
    }

    private void CycleTimerSlider_ManipulationDelta(object sender, ManipulationDeltaEventArgs e)
    {
        ((Slider) sender).ToolTip = ((Slider) sender).Value.ToString(CultureInfo.CurrentCulture);
    }

    private void HelpButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.ToolTip is ToolTip toolTip)
        {
            toolTip.IsOpen = true;
        }
    }

    /// <summary>
    /// ポケモンの対戦形式コンボボックスで選択中の値（Tag の "Singles" / "Doubles"）を返します。「未登録」は null です。
    /// </summary>
    private string? SelectedPokemonBattleFormat()
    {
        var tag = (PokemonBattleFormatComboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString();
        return PokemonCommandController.ParseRegisteredFormat(tag)?.ToString();
    }

    /// <summary>
    /// 登録している対戦形式（secrets.json の値）に対応するコンボボックスの項目番号を返します（0: 未登録、1: シングル、2: ダブル）。
    /// </summary>
    private static int PokemonBattleFormatIndex(string? battleFormat)
    {
        return PokemonCommandController.ParseRegisteredFormat(battleFormat) switch
        {
            BattleFormat.Singles => 1,
            BattleFormat.Doubles => 2,
            _ => 0
        };
    }

    /// <summary>
    /// チャットの format set / format clear で登録形式が変わったとき、画面の「対戦形式」を同じ値にします。
    /// </summary>
    private void OnPokemonRegisteredFormatChanged(string? battleFormat)
    {
        Dispatcher.InvokeAsync(() => PokemonBattleFormatComboBox.SelectedIndex = PokemonBattleFormatIndex(battleFormat));
    }

    /// <summary>
    /// ポケモンの対戦形式を切り替えたとき、接続中であれば即座に反映します。
    /// </summary>
    private void PokemonBattleFormatComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // XAML 読み込み中（IsSelected の初期化）にも呼ばれるため、未接続なら何もしない
        if (_twitchClientController is null) return;
        _twitchClientController.SetPokemonBattleFormat(SelectedPokemonBattleFormat());
    }

    /// <summary>
    /// ポケモンのデータの取得元コンボボックスで選択中の値（Tag の "Production" / "Develop"）を返します。
    /// </summary>
    private string SelectedPokemonDataSource()
    {
        return (PokemonDataSourceComboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "Production";
    }

    /// <summary>
    /// ポケモンのデータの取得元を切り替えたとき、接続中であれば即座に反映します（登録データのクラウドも同じサイトに切り替わります）。
    /// </summary>
    private void PokemonDataSourceComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // XAML 読み込み中（IsSelected の初期化）にも呼ばれるため、未接続なら何もしない
        if (_twitchClientController is null) return;
        _twitchClientController.SetPokemonDataSource(SelectedPokemonDataSource());
        if (IsPokemonGameSelected() || _pokemonRosterWindow is not null) _ = LoadPokemonRosterAsync();
    }

    /// <summary>
    /// 選択中のデータの取得元を返します。
    /// </summary>
    private PokemonDataSource SelectedPokemonDataSourceValue()
    {
        return PokemonCommandController.ParseDataSource(SelectedPokemonDataSource());
    }

    /// <summary>
    /// アクセスキーの入力欄（表示中なら TextBox、隠しているなら PasswordBox）の文字を返します。
    /// </summary>
    private string EnteredPokemonSyncKey()
    {
        return (PokemonSyncKeyTextBox.Visibility == Visibility.Visible
            ? PokemonSyncKeyTextBox.Text
            : PokemonSyncKeyPasswordBox.Password).Trim();
    }

    /// <summary>
    /// アクセスキーの入力欄に値を入れます。
    /// </summary>
    private void SetPokemonSyncKeyInput(string key)
    {
        PokemonSyncKeyPasswordBox.Password = key;
        PokemonSyncKeyTextBox.Text = key;
    }

    /// <summary>
    /// アクセスキーの有無に合わせてボタンを有効・無効にします。
    /// </summary>
    /// <param name="busy">クラウドと通信中か（通信中はすべて無効）</param>
    private void UpdatePokemonSyncKeyButtons(bool busy = false)
    {
        var hasKey = RemoteRosterStore.IsValidKey(_pokemonSyncKey);
        PokemonSyncKeyIssueButton.IsEnabled = !busy;
        PokemonSyncKeyJoinButton.IsEnabled = !busy;
        PokemonSyncKeyRotateButton.IsEnabled = !busy && hasKey;
        PokemonSyncKeyStopButton.IsEnabled = !busy && hasKey;
        PokemonSyncKeyCopyButton.IsEnabled = hasKey;
    }

    /// <summary>
    /// アクセスキーを確定します。先に設定ファイルへ保存できたときだけ、メモリ・入力欄・接続中のコマンドを切り替えて、登録データを読み直します。
    /// 保存できなかったときは、今のアクセスキーのまま（新しいキーは画面に出して控えてもらう）。
    /// </summary>
    /// <param name="key">アクセスキー（空ならデータ共有をやめる）</param>
    /// <returns>切り替えて保存できたか</returns>
    private async Task<bool> ApplyPokemonSyncKeyAsync(string key)
    {
        if (!TrySaveSecretValue(key))
        {
            if (string.IsNullOrEmpty(key))
                MessageBox.Show("データ共有をやめる設定を設定ファイル（secrets.json）へ保存できませんでした（ログを確認してください）。今のアクセスキーのまま使えます。",
                    "アクセスキー", MessageBoxButton.OK, MessageBoxImage.Warning);
            else
                MessageBox.Show("アクセスキーを設定ファイル（secrets.json）へ保存できませんでした。今のアクセスキーのままです。\n新しいアクセスキーを控えてから、もう一度試してください（「コピー」では今のキーがコピーされます）:\n\n" + key,
                    "アクセスキー", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        _pokemonSyncKey = key;
        SetPokemonSyncKeyInput(key);
        _twitchClientController?.SetPokemonSyncKey(key);
        UpdatePokemonSyncKeyButtons();
        await LoadPokemonRosterAsync();
        return true;
    }

    /// <summary>
    /// 設定ファイルへ保存します。失敗したら、理由（例外の種類だけ。キーは含めない）をログに残して false を返します。
    /// </summary>
    /// <param name="syncKey">保存するアクセスキー（省略時は今のアクセスキー）</param>
    private bool TrySaveSecretValue(string? syncKey = null)
    {
        try
        {
            SaveSecretValue(syncKey);
            return true;
        }
        catch (Exception ex)
        {
            LogController.OutputLog($"<Error> secrets.json の保存に失敗しました: {ex.GetType().Name}");
            return false;
        }
    }

    /// <summary>
    /// アクセスキーの表示・非表示を切り替えます（配信画面に映さないよう、ふだんは隠しておきます）。
    /// </summary>
    private void PokemonSyncKeyShowButton_Click(object sender, RoutedEventArgs e)
    {
        var show = PokemonSyncKeyTextBox.Visibility != Visibility.Visible;
        if (show) PokemonSyncKeyTextBox.Text = PokemonSyncKeyPasswordBox.Password;
        else PokemonSyncKeyPasswordBox.Password = PokemonSyncKeyTextBox.Text;
        PokemonSyncKeyTextBox.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        PokemonSyncKeyPasswordBox.Visibility = show ? Visibility.Collapsed : Visibility.Visible;
        PokemonSyncKeyShowButton.Content = show ? "隠す" : "表示";
    }

    /// <summary>
    /// 使用中のアクセスキーをクリップボードにコピーします（サイトの「アクセスキーを入力して共有」に貼り付ける用）。
    /// </summary>
    private void PokemonSyncKeyCopyButton_Click(object sender, RoutedEventArgs e)
    {
        if (!RemoteRosterStore.IsValidKey(_pokemonSyncKey)) return;
        try
        {
            Clipboard.SetText(_pokemonSyncKey);
            PokemonSyncStatusTextBlock.Text = "アクセスキーをコピーしました";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"コピーできませんでした: {ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>
    /// 新しいアクセスキーを発行します（クラウドの置き場は空から始まります）。
    /// </summary>
    private async void PokemonSyncKeyIssueButton_Click(object sender, RoutedEventArgs e)
    {
        if (RemoteRosterStore.IsValidKey(_pokemonSyncKey) &&
            MessageBox.Show("今のアクセスキーの代わりに新しいアクセスキーを発行します。\n今のアクセスキーの登録データは新しいアクセスキーに引き継がれません（引き継ぐときは「アクセスキーを作り直す」を使います）。\n\n続けますか？",
                "確認", MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK)
            return;

        UpdatePokemonSyncKeyButtons(busy: true);
        PokemonSyncStatusTextBlock.Text = "発行中…";
        try
        {
            var key = await PokemonSyncKeyController.IssueAsync(SelectedPokemonDataSourceValue());
            if (!await ApplyPokemonSyncKeyAsync(key)) return;
            MessageBox.Show("アクセスキーを発行しました（データ共有を始めました）。\n\nサイトの「データ共有」ページで、このアクセスキーを貼り付けて「アクセスキーを入力して共有」を押すと、サイトと同じ登録データを使えます（「コピー」でコピーできます）。\n\nアクセスキーはパスワードと同じ扱いです。配信画面に映さないでください。",
                "アクセスキー", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (RosterUnavailableException ex)
        {
            PokemonSyncStatusTextBlock.Text = "発行できませんでした";
            MessageBox.Show($"アクセスキーを発行できませんでした: {ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        catch (Exception ex)
        {
            // async void の中で例外を出すとアプリが落ちるので、必ず受ける
            LogController.OutputLog($"<Error> アクセスキーの発行に失敗しました: {ex.GetType().Name}");
            PokemonSyncStatusTextBlock.Text = "発行できませんでした";
            MessageBox.Show($"アクセスキーを発行できませんでした: {ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            UpdatePokemonSyncKeyButtons();
        }
    }

    /// <summary>
    /// 入力欄に貼り付けたアクセスキー（サイトで作ったもの）を設定します。クラウドから読めることを確かめてから使います。
    /// </summary>
    private async void PokemonSyncKeyJoinButton_Click(object sender, RoutedEventArgs e)
    {
        var key = EnteredPokemonSyncKey();
        if (!RemoteRosterStore.IsValidKey(key))
        {
            MessageBox.Show("アクセスキーの形式が正しくありません（43 文字の英数字・-・_）。\nサイトの「データ共有」ページでコピーしたアクセスキーを「アクセスキー」の欄に貼り付けてから押してください。",
                "アクセスキー", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        UpdatePokemonSyncKeyButtons(busy: true);
        PokemonSyncStatusTextBlock.Text = "確認中…";
        try
        {
            var state = await PokemonSyncKeyController.LoadAsync(SelectedPokemonDataSourceValue(), key);
            if (!await ApplyPokemonSyncKeyAsync(key)) return;
            var (pokemon, teams) = PokemonSyncKeyController.Count(state);
            if (pokemon == 0 && teams == 0)
                MessageBox.Show("アクセスキーを設定しました（データ共有を始めました）。クラウドにはまだ登録がありません（アクセスキーの打ち間違いや、データの取得元が本番・開発用で違っていないか確認してください）。",
                    "アクセスキー", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (RosterUnavailableException ex)
        {
            PokemonSyncStatusTextBlock.Text = RemoteRosterStore.IsValidKey(_pokemonSyncKey) ? "設定できませんでした（今のアクセスキーのまま）" : "設定できませんでした";
            MessageBox.Show($"このアクセスキーは使えませんでした: {ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        catch (Exception ex)
        {
            LogController.OutputLog($"<Error> アクセスキーの設定に失敗しました: {ex.GetType().Name}");
            PokemonSyncStatusTextBlock.Text = "設定できませんでした";
            MessageBox.Show($"アクセスキーを設定できませんでした: {ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            UpdatePokemonSyncKeyButtons();
        }
    }

    /// <summary>
    /// アクセスキーを作り直します。登録データを新しいキーに移し、古いキーは使えなくします。
    /// </summary>
    private async void PokemonSyncKeyRotateButton_Click(object sender, RoutedEventArgs e)
    {
        if (!RemoteRosterStore.IsValidKey(_pokemonSyncKey)) return;
        if (MessageBox.Show("アクセスキーを作り直します。\n登録データは新しいアクセスキーに移り、今のアクセスキーは使えなくなります。\nサイトや他の PC でも、新しいアクセスキーを入力し直す必要があります。\n\n続けますか？",
                "確認", MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK)
            return;

        UpdatePokemonSyncKeyButtons(busy: true);
        PokemonSyncStatusTextBlock.Text = "作り直し中…";
        try
        {
            // 1. 新しいキーの置き場へ写す（古いキーはまだ消さない。今のキーもまだ画面と設定ファイルに残したまま）
            var pending = await PokemonSyncKeyController.PrepareRotateAsync(SelectedPokemonDataSourceValue(), _pokemonSyncKey);
            // 2. 新しいキーを設定ファイルへ保存できてから、画面を切り替える。保存できなければ今のキーのまま（古いキーは消さない）
            if (!await ApplyPokemonSyncKeyAsync(pending.NewKey))
            {
                PokemonSyncStatusTextBlock.Text = "作り直しましたが、保存できませんでした（今のアクセスキーのまま）";
                return;
            }
            // 3. 古いキーを消す（消せなくても、新しいキーへの切り替えは済んでいる。ここで例外が出ても、作り直し自体は済んでいるので警告にする）
            bool oldKeyDeleted;
            try
            {
                oldKeyDeleted = await PokemonSyncKeyController.FinishRotateAsync(SelectedPokemonDataSourceValue(), pending);
            }
            catch (Exception ex)
            {
                LogController.OutputLog($"<Error> 古いアクセスキーの削除に失敗しました: {ex.GetType().Name}");
                oldKeyDeleted = false;
            }
            MessageBox.Show(
                (oldKeyDeleted
                    ? "アクセスキーを作り直しました。古いアクセスキーは使えなくなりました。"
                    : "アクセスキーを作り直しました。ただし、古いアクセスキーは残っています。写している間に古いアクセスキーへ書かれた内容は、新しいアクセスキーに入っていないかもしれません。古いアクセスキーは使わないでください。") +
                "\n\nサイトの「データ共有」ページで、新しいアクセスキーを入力し直してください（「コピー」でコピーできます）。",
                "アクセスキー", MessageBoxButton.OK, oldKeyDeleted ? MessageBoxImage.Information : MessageBoxImage.Warning);
        }
        catch (RosterUnavailableException ex)
        {
            PokemonSyncStatusTextBlock.Text = "作り直せませんでした（今のアクセスキーのまま）";
            MessageBox.Show($"アクセスキーを作り直せませんでした（今のアクセスキーのままです）: {ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        catch (Exception ex)
        {
            LogController.OutputLog($"<Error> アクセスキーの作り直しに失敗しました: {ex.GetType().Name}");
            PokemonSyncStatusTextBlock.Text = "作り直せませんでした（今のアクセスキーのまま）";
            MessageBox.Show($"アクセスキーを作り直せませんでした（今のアクセスキーのままです）: {ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            UpdatePokemonSyncKeyButtons();
        }
    }

    /// <summary>
    /// このボットでアクセスキーを使うのをやめます（クラウドのデータは残ります）。
    /// </summary>
    private async void PokemonSyncKeyStopButton_Click(object sender, RoutedEventArgs e)
    {
        if (!RemoteRosterStore.IsValidKey(_pokemonSyncKey)) return;
        if (MessageBox.Show("このボットでのデータ共有をやめます（アクセスキーを使うのをやめます）。\nクラウドの登録データは残り、サイトはそのまま使えます。データ共有をやめると、チャットの登録系のコマンド（!poke add など）は使えなくなります。\n\n続けますか？",
                "確認", MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK)
            return;
        await ApplyPokemonSyncKeyAsync("");
    }

    /// <summary>
    /// Game の選択でポケモンシリーズが選ばれているかを返します。
    /// </summary>
    private bool IsPokemonGameSelected()
    {
        return (GameSelectComboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() == "Pokemon";
    }

    /// <summary>
    /// Game の選択に合わせて、Beat Saber の変換パターンか Pokemon の設定のどちらかを表示します。
    /// </summary>
    private void GameSelectComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // XAML 読み込み中（IsSelected の初期化）にも呼ばれるため、まだ作られていない要素があれば何もしない
        if (BeatSaberBsrDataGrid is null || PokemonSettingsPanel is null) return;
        var isPokemon = IsPokemonGameSelected();
        PokemonSettingsPanel.Visibility = isPokemon ? Visibility.Visible : Visibility.Collapsed;
        BeatSaberBsrDataGrid.Visibility = isPokemon ? Visibility.Collapsed : Visibility.Visible;
        if (!isPokemon) BeatSaberBsrDataGrid.ItemsSource = TextRegexController.LoadBsrChatModel().BeatSaberChat;
        else _ = LoadPokemonRosterAsync();
    }

    /// <summary>
    /// 登録したポケモン・チームを読み込み、Game タブの要約と「一覧を開く」のウィンドウに表示します（読むだけで、書き換えはしません）。
    /// 続けて呼ばれたときは、最後に始めた読み込みの結果だけを表示します。
    /// </summary>
    private async Task LoadPokemonRosterAsync()
    {
        var version = ++_pokemonRosterLoadVersion;
        if (!RemoteRosterStore.IsValidKey(_pokemonSyncKey))
        {
            PokemonSyncStatusTextBlock.Text = "未設定";
            ShowPokemonRoster(null,
                "アクセスキーが設定されていません（上の「アクセスキー」で発行するか、サイトで作ったアクセスキーを入力してください）",
                false);
            return;
        }

        ShowPokemonRoster(_pokemonRosterGames, "読み込み中…", true);
        try
        {
            var games = await PokemonRosterViewer.LoadAsync(SelectedPokemonDataSourceValue(), _pokemonSyncKey);
            if (version != _pokemonRosterLoadVersion) return;
            PokemonSyncStatusTextBlock.Text = "データ共有中";
            ShowPokemonRoster(games,
                games.Count == 0
                    ? "まだ登録がありません（チャットの !poke add -m かサイトで登録します）"
                    : games.All(g => g.NamesResolved)
                        ? $"{DateTime.Now:HH:mm:ss} に読み込み"
                        : $"{DateTime.Now:HH:mm:ss} に読み込み。データを取得できなかったため、特性・持ち物は ID で表示しています（データの取得元を確認）",
                false);
        }
        catch (Exception ex)
        {
            if (version != _pokemonRosterLoadVersion) return;
            PokemonSyncStatusTextBlock.Text = "サイトから読み込めません";
            ShowPokemonRoster(null, $"読み込めませんでした: {ex.Message}", false);
        }
    }

    /// <summary>
    /// 登録データを Game タブ（1 行の要約）と、開いていれば一覧のウィンドウに表示します。
    /// </summary>
    /// <param name="games">登録のあるゲームの一覧（読み込めていない・アクセスキーが無いときは null）</param>
    /// <param name="status">状態の表示</param>
    /// <param name="loading">読み込み中か（読み込み中は前の内容を表示したまま、再読み込みを押せなくする）</param>
    private void ShowPokemonRoster(IReadOnlyList<RosterGameView>? games, string status, bool loading)
    {
        if (!loading)
        {
            _pokemonRosterGames = games;
            PokemonRosterSummaryTextBlock.Text = PokemonRosterSummary(games);
            PokemonRosterOpenWindowButton.IsEnabled = games is not null;
        }

        PokemonRosterStatusTextBlock.Text = status;
        PokemonRosterReloadButton.IsEnabled = !loading;
        _pokemonRosterWindow?.ShowRoster(games, status, loading);
    }

    /// <summary>
    /// 登録データの 1 行の要約を返します（例: ポケモンチャンピオンズ: 登録 15 体・チーム 3（使用中: シングル1・ダブル2））。
    /// チームの番号はシングル・ダブルで別々で、使用中も形式ごとに 1 つです。
    /// 登録のあるゲームが 3 つ以上なら、最初のゲームと「ほか N ゲーム」にします。
    /// </summary>
    private static string PokemonRosterSummary(IReadOnlyList<RosterGameView>? games)
    {
        if (games is null) return "—";
        if (games.Count == 0) return "登録なし";

        static string Summary(RosterGameView game)
        {
            var active = string.Join("・", game.Teams.Where(t => t.Active.Length > 0).Select(t => t.Number));
            return $"{game.Name}: 登録 {game.Pokemon.Count} 体・チーム {game.Teams.Count}" +
                   (active.Length == 0 ? "" : $"（使用中: {active}）");
        }

        return games.Count <= 2
            ? string.Join(" ／ ", games.Select(Summary))
            : $"{Summary(games[0])} ほか {games.Count - 1} ゲーム";
    }

    /// <summary>
    /// 登録したポケモンとチームの一覧を別のウィンドウで開きます。開いていれば、そのウィンドウを前に出します。
    /// </summary>
    private void PokemonRosterOpenWindowButton_Click(object sender, RoutedEventArgs e)
    {
        if (_pokemonRosterWindow == null || !IsWindowOpen<PokemonRosterWindow>())
        {
            _pokemonRosterWindow = new PokemonRosterWindow(LoadPokemonRosterAsync);
            _pokemonRosterWindow.Closed += (_, _) => _pokemonRosterWindow = null;
            _pokemonRosterWindow.ShowRoster(_pokemonRosterGames, PokemonRosterStatusTextBlock.Text,
                !PokemonRosterReloadButton.IsEnabled);
            _pokemonRosterWindow.Show();
        }
        else
        {
            _pokemonRosterWindow.Activate();
            if (_pokemonRosterWindow.WindowState == WindowState.Minimized)
                _pokemonRosterWindow.WindowState = WindowState.Normal;
        }
    }

    private async void PokemonRosterReloadButton_Click(object sender, RoutedEventArgs e)
    {
        await LoadPokemonRosterAsync();
    }

    /// <summary>
    /// データの取得元のサイトの「ポケモン登録」ページをブラウザで開きます。
    /// </summary>
    private void PokemonRosterOpenSiteButton_Click(object sender, RoutedEventArgs e)
    {
        Process.Start(new ProcessStartInfo(PokemonSyncKeyController.RosterPageUrl(SelectedPokemonDataSourceValue()))
        {
            UseShellExecute = true
        });
    }

    private void GameResetButton_Click(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show("設定を初期値に戻しますか？", "確認", MessageBoxButton.OKCancel, MessageBoxImage.Warning);
        if (result != MessageBoxResult.OK) return;

        if (IsPokemonGameSelected())
        {
            // 未登録（シングル扱い）/ Production に戻す（接続中なら SelectionChanged で即座に反映される）
            PokemonBattleFormatComboBox.SelectedIndex = 0;
            PokemonDataSourceComboBox.SelectedIndex = 0;
            return;
        }

        TextRegexController.ResetBsrChatModel();
        BeatSaberBsrDataGrid.ItemsSource = TextRegexController.LoadBsrChatModel().BeatSaberChat;
    }

    private void FollowTestButton_Click(object sender, RoutedEventArgs e)
    {
        InitializeTestEventController();
        _twitchTestEventController?.TestFollowEvent();
    }

    private void RaidTestButton_Click(object sender, RoutedEventArgs e)
    {
        InitializeTestEventController();
        _twitchTestEventController?.TestRaidEvent();
    }

    private void SubscriptionTestButton_Click(object sender, RoutedEventArgs e)
    {
        InitializeTestEventController();
        _twitchTestEventController?.TestSubscriptionEvent();
    }

    private void BitsTestButton_Click(object sender, RoutedEventArgs e)
    {
        InitializeTestEventController();
        _twitchTestEventController?.TestBitsEvent();
    }

    private void GiftTestButton_Click(object sender, RoutedEventArgs e)
    {
        InitializeTestEventController();
        _twitchTestEventController?.TestGiftEvent();
    }

    private void ChannelPointTestButton_Click(object sender, RoutedEventArgs e)
    {
        InitializeTestEventController();
        _twitchTestEventController?.TestChannelPointEvent();
    }

    private void InitializeTestEventController()
    {
        // 接続を作り直していたら（TwitchClientController が変わっていたら）、古い接続へ送らないように作り直す
        if (_twitchTestEventController != null && ReferenceEquals(_testEventClient, _twitchClientController)) return;
        
        var secretKeys = SecretKeyController.LoadKeys();
        _twitchApiController ??= new TwitchApiController(secretKeys);
        _twitchClientController ??= new TwitchClientController(secretKeys, _twitchApiController);
        
        // TwitchClientController内部で生成されているBouyomiChanControllerを取得する必要があるが、
        // TwitchClientControllerが非公開にしているため、本来はDIなどで管理するのが望ましい。
        // ここではTwitchClientControllerから取得できるようにするか、新しく生成するか。
        // TwitchClientController.csを見ると _bouyomiChanController は private。
        // 今回は暫定的にTwitchClientControllerにGetterを追加するか、
        // あるいは MainWindowで管理するようにリファクタリングする。
        // ひとまずTwitchClientControllerにGetterを追加する。
        
        _testEventClient = _twitchClientController;
        _twitchTestEventController = new TwitchTestEventController(
            _twitchClientController, 
            _twitchClientController.BouyomiChanController, 
            secretKeys);
    }
}