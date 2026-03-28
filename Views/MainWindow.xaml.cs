using System;
using System.Globalization;
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
    /// </summary>
    private TwitchApiController? _twitchApiController;

    /// <summary>
    /// </summary>
    private TwitchClientController? _twitchClientController;

    /// <summary>
    /// </summary>
    private TwitchEventSubController? _twitchEventSubController;

    /// <summary>
    /// </summary>
    private TwitchTestEventController? _twitchTestEventController;

    /// <summary>
    /// </summary>
    private readonly TwitchAuthService _twitchAuthService = new();

    /// <summary>
    /// </summary>
    public MainWindow()
    {
        InitializeComponent();
        InitializeEncodeRegister();

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
        
        // BouyomiChan
        BouyomiChanConnectCheckBox.IsChecked = secretKeys.BouyomiChan.Checked;

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
        var accessTokenQuery = new[] {$"http://localhost:{Settings.Default.Port}", "code=", "scope=", "state="};
        while (true)
        {
            await Task.Delay(100);
            if (FaraBotModeratorWebView.Source is null)
                continue;

            var url = FaraBotModeratorWebView.Source.ToString();
            // URL毎に処理を追加
            if (accessTokenQuery.All(key => url.Contains(key))) await UpdateAccessTokenAsync(url);
        }
    }

    /// <summary>
    /// </summary>
    /// <returns></returns>
    private async Task StartMonitoringAsync()
    {
        while (true)
        {
            await Task.Delay(1000);

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
            if (BouyomiChanConnectCheckBox.IsChecked != true || TwitchConnectionStateLabel.Content.ToString() != "State: Connect") continue;
            if (_twitchClientController != null && !await BouyomiChanController.IsBouyomiChanRunningAsync())
            {
                await Dispatcher.InvokeAsync(async () =>
                {
                    MessageBox.Show("棒読みちゃんが終了しました。Disconnectする前に棒読みちゃんを閉じないでください。\n安全のためTwitchから切断します。", "警告", MessageBoxButton.OK, MessageBoxImage.Warning);
                    await TwitchDisconnect();
                });
            }
        }
    }

    private async Task StartTwitchLibEventSubAsync()
    {
        while (true)
        {
            while (_twitchClientController is null) await Task.Delay(100);

            await Task.Delay(500);
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
    }

    private void AddGridViewChatData()
    {
        if (_chatWindow is null) return;

        var chatModelData = _twitchClientController?.PickChatData();
        if (chatModelData is null) return;

        var beforeScrollBottom = true;

        var beforeScrollViewer =
            VisualTreeHelper.GetChild(VisualTreeHelper.GetChild(_chatWindow.TwitchChatDataGrid, 0), 0);
        if (beforeScrollViewer is ScrollViewer viewer)
        {
            var offset = viewer.VerticalOffset;
            var extent = viewer.ScrollableHeight;
            var viewport = viewer.ViewportHeight;

            beforeScrollBottom = offset + viewport >= extent;
        }

        _chatWindow.TwitchChatDataGrid.Items.Add(chatModelData);
        if (beforeScrollBottom) _chatWindow.TwitchChatDataGrid.ScrollIntoView(chatModelData);
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
        var state = Regex.Match(url, @"state=(.*)").Groups[1].ToString();
        var code = Regex.Match(url, @"code=(.*)&").Groups[1].ToString();

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
    private void SaveSecretValue()
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
            BeatSaber = SecretKeyController.LoadKeys().BeatSaber
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

            var isTokenValid = _twitchApiController.ValidateToken();
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
        FaraBotModeratorWebView.Source = new Uri("https://www.deepl.com/ja/account/summary");
    }

    /// <summary>
    ///     終了時の処理
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    private void MainWindow_Closed(object sender, EventArgs e)
    {
        SaveSecretValue();
        _chatWindow?.Close();
        _raidListWindow?.Close();
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
        if (_twitchTestEventController != null) return;
        
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
        
        _twitchTestEventController = new TwitchTestEventController(
            _twitchClientController, 
            _twitchClientController.BouyomiChanController, 
            secretKeys);
    }
}