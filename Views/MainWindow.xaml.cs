using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
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
    public MainWindow()
    {
        InitializeComponent();
        InitializeEncodeRegister();
        InitializeSecretValue();

        DataContext = this;
        
        // Task
        _ = RunWithExceptionHandlingAsync(StartTimerAsync, "Timer");
        _ = RunWithExceptionHandlingAsync(StartWebServerAsync, "WebServer");
        _ = RunWithExceptionHandlingAsync(StartMonitoringAsync, "Monitoring");
        _ = RunWithExceptionHandlingAsync(StartTwitchLibEventSubAsync, "TwitchPubSub");

        Loaded += (_, _) =>
        {
            var secretKeys = SecretKeyController.LoadKeys();
            _twitchApiController ??= new TwitchApiController(secretKeys);
            _twitchClientController ??= new TwitchClientController(secretKeys, _twitchApiController);

            _chatWindow ??= new ChatWindow(_twitchClientController);
            _chatWindow.Show();

            _raidListWindow ??= new RaidListWindow(_twitchApiController);
            _raidListWindow.Show();
        };
    }

    /// <summary>
    ///     Windowsが提供するEncodingを利用できるようにする。SHIFT-JIS等も使用可能になる。
    /// </summary>
    private void InitializeEncodeRegister()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    private void InitializeSecretValue()
    {
        var secretKeys = SecretKeyController.LoadKeys();
        
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
        var timer1Count = 0;
        var timer2Count = 0;
        var timer3Count = 0;
        var timer4Count = 0;
        while (true)
        {
            while (_twitchClientController is null) await Task.Delay(1);

            // Timerは秒数管理しても大丈夫
            await Task.Delay(1000);

            // Interval Timer
            if (CycleTimer1CheckBox.IsChecked ?? false) timer1Count++;
            else timer1Count = 0;
            if (CycleTimer2CheckBox.IsChecked ?? false) timer2Count++;
            else timer2Count = 0;
            if (CycleTimer3CheckBox.IsChecked ?? false) timer3Count++;
            else timer3Count = 0;
            if (CycleTimer4CheckBox.IsChecked ?? false) timer4Count++;
            else timer4Count = 0;

            var cycleTimer1Minute = (int) CycleTimer1Slider.Value * 60;
            if (timer1Count >= cycleTimer1Minute && cycleTimer1Minute > 0)
            {
                timer1Count %= cycleTimer1Minute;
                _twitchClientController.SendModeratorMessage(CycleTimer1TextBox.Text);
            }

            var cycleTimer2Minute = (int) CycleTimer2Slider.Value * 60;
            if (timer2Count >= cycleTimer2Minute && cycleTimer2Minute > 0)
            {
                timer2Count %= cycleTimer2Minute;
                _twitchClientController.SendModeratorMessage(CycleTimer2TextBox.Text);
            }

            var cycleTimer3Minute = (int) CycleTimer3Slider.Value * 60;
            if (timer3Count >= cycleTimer3Minute && cycleTimer3Minute > 0)
            {
                timer3Count %= cycleTimer3Minute;
                _twitchClientController.SendModeratorMessage(CycleTimer3TextBox.Text);
            }

            var cycleTimer4Minute = (int) CycleTimer4Slider.Value * 60;
            if (timer4Count >= cycleTimer4Minute && cycleTimer4Minute > 0)
            {
                timer4Count %= cycleTimer4Minute;
                _twitchClientController.SendModeratorMessage(CycleTimer4TextBox.Text);
            }

            // FixedTimer
            if (FixedTimer1CheckBox.IsChecked ?? false)
            {
                var date = FixedTimer1DatePicker.SelectedDate;
                var time = FixedTimer1TimePicker.SelectedTime;
                FixedTimerMessage(date, time, FixedTimer1TextBox.Text);
            }

            if (FixedTimer2CheckBox.IsChecked ?? false)
            {
                var date = FixedTimer2DatePicker.SelectedDate;
                var time = FixedTimer2TimePicker.SelectedTime;
                FixedTimerMessage(date, time, FixedTimer2TextBox.Text);
            }

            if (FixedTimer3CheckBox.IsChecked ?? false)
            {
                var date = FixedTimer3DatePicker.SelectedDate;
                var time = FixedTimer3TimePicker.SelectedTime;
                FixedTimerMessage(date, time, FixedTimer3TextBox.Text);
            }

            if (FixedTimer4CheckBox.IsChecked ?? false)
            {
                var date = FixedTimer4DatePicker.SelectedDate;
                var time = FixedTimer4TimePicker.SelectedTime;
                FixedTimerMessage(date, time, FixedTimer4TextBox.Text);
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
            if (BouyomiChanConnectCheckBox.IsChecked == true && 
                TwitchConnectionStateLabel.Content.ToString() == "State: Connect")
            {
                if (_twitchClientController != null && !await _twitchClientController.BouyomiChanController.IsBouyomiChanRunningAsync())
                {
                    Dispatcher.Invoke(() =>
                    {
                        MessageBox.Show("棒読みちゃんが終了しました。Disconnectする前に棒読みちゃんを閉じないでください。\n安全のためTwitchから切断します。", "警告", MessageBoxButton.OK, MessageBoxImage.Warning);
                        TwitchDisconnect();
                    });
                }
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
    /// <exception cref="HttpRequestException"></exception>
    private async Task UpdateRefreshTokenAsync()
    {
        var parameter =
            $"client_id={TwitchApiClientIdPasswordBox.Password}" +
            $"&client_secret={TwitchApiClientSecretPasswordBox.Password}" +
            "&grant_type=refresh_token" +
            $"&refresh_token={Settings.Default.RefreshToken}";
        try
        {
            var response = await PostResponseBodyAsync(parameter, "https://id.twitch.tv/oauth2/token");
            var jsonString =
                JsonSerializer.Deserialize<TwitchRefreshTokenModel>(response.responseBody, response.option);

            Settings.Default.AccessToken = jsonString?.AccessToken;
            Settings.Default.RefreshToken = jsonString?.RefreshToken;
            Settings.Default.Save();
        }
        catch (Exception ex)
        {
            LogController.OutputLog(ex.Message);
            RequestApiAccessToken();
        }
    }

    /// <summary>
    ///     Twitch API Access Tokenを取得、更新します。
    ///     Token取得はPOST通信をするため、WebViewは通さずPostRequestを行う。
    /// </summary>
    /// <param name="url"></param>
    /// <exception cref="HttpRequestException"></exception>
    private async Task UpdateAccessTokenAsync(string url)
    {
        var state = Regex.Match(url, @"state=(.*)").Groups[1];
        if (state.ToString() != Settings.Default.OAuth2State)
        {
            var message = "The token differs between request and response.";
            LogController.OutputLog(message);
            throw new HttpRequestException(message);
        }

        var code = Regex.Match(url, @"code=(.*)&").Groups[1];
        var parameter =
            $"client_id={TwitchApiClientIdPasswordBox.Password}" +
            $"&client_secret={TwitchApiClientSecretPasswordBox.Password}" +
            $"&code={code}" +
            "&grant_type=authorization_code" +
            $"&redirect_uri=http://localhost:{Settings.Default.Port}";
        var response = await PostResponseBodyAsync(parameter, "https://id.twitch.tv/oauth2/token");

        // Token取得失敗は何もしない
        if (response.responseBody.Contains("Invalid authorization code")) return;

        var jsonString =
            JsonSerializer.Deserialize<TwitchOAuthTokenModel>(response.responseBody, response.option);
        Settings.Default.AccessToken = jsonString?.AccessToken;
        Settings.Default.RefreshToken = jsonString?.RefreshToken;
        if (jsonString?.ExpiresIn != null)
            Settings.Default.expiresDateTime = DateTime.Now.AddSeconds(jsonString.ExpiresIn);
        Settings.Default.Save();

        UnlockWindowControl();
    }

    /// <summary>
    ///     accessTokenに必要な認証コードを取得するURLを開く
    /// </summary>
    private void RequestApiAccessToken()
    {
        var buffer = RandomNumberGenerator.GetBytes(50);
        var stateGenerator =
            buffer.Select(x => x % 62)
                .Select(x =>
                {
                    // 特殊文字は含めない
                    return x switch
                    {
                        < 10 => (char) ('0' + x),
                        < 36 => (char) ('A' + x - 10),
                        _ => (char) ('a' + x - 36)
                    };
                }).ToArray();
        var state = new string(stateGenerator);
        Settings.Default.OAuth2State = state;
        Settings.Default.Save();

        //refreshTokenはaccessToken期限切れなら設定
        var requestUrl =
            "https://id.twitch.tv/oauth2/authorize" +
            $"?client_id={TwitchApiClientIdPasswordBox.Password}" +
            $"&redirect_uri=http://localhost%3A{Settings.Default.Port}" +
            "&response_type=code" +
            "&scope=bits%3Aread " + // bitsリーダーボード表示
            "channel%3Amanage%3Apredictions " + // prediction作成、終了
            "channel%3Amanage%3Araids " + // raid開始、キャンセル
            "channel%3amanage%3Aredemptions " + // チャンネルポイント管理
            "channel%3Amanage%3Aschedule " + // スケジュール管理
            "channel%3Aread%3Ahype_train " + // ハイプトレイン取得
            "channel%3Aread%3Apolls " + // アンケート表示
            "channel%3Aread%3Apredictions " + // predictions取得
            "channel%3Aread%3Aredemptions " + // チャンネルポイント一覧
            "channel%3Aread%3Astream_key " + // ストリームキー表示
            "channel%3Aread%3Asubscriptions " + // SubScription一覧表示
            "channel%3Aread%3Avips " + // Vipメンバー一覧表示
            "moderation%3Aread " + // Moderators, Bans, Timeouts, Automod設定
            "moderator%3Amanage%3Aannouncements " + // Moderator権限者によるannouncementコマンド実行
            "user%3Aread%3Abroadcast " + // broadcast設定取得
            "user%3Aread%3Afollows " + // follower取得
            "user%3Aread%3Asubscriptions " + // SubScriptionメンバー一覧取得
            "user%3Aread%3Aemail " + // mail取得
            "channel%3Amoderate " + // Moderator権限実行
            "chat%3Aedit " + // Chat送信
            "chat%3Aread " + // Chat受信
            "whispers%3Aread " + // Whisper受信
            "channel%3Amanage%3Araids " + // raid管理
            "moderator%3Amanage%3Ashoutouts " + // shoutoutコマンド実行権限
            "moderator%3Aread%3Afollowers" + //EventSub Follow通知
            $"&state={state}"; // ランダムなUID
        try
        {
            LockWindowControl(true);
            FaraBotModeratorWebView.Source = new Uri(requestUrl); // WebView表示して操作
        }
        catch (Exception ex)
        {
            UnlockWindowControl();
            LogController.OutputLog(ex.Message);
        }
    }

    /// <summary>
    ///     指定URLに対してPost Requestを送信します。
    /// </summary>
    /// <param name="parameter"></param>
    /// <param name="url"></param>
    /// <returns></returns>
    private async Task<(string responseBody, JsonSerializerOptions option)> PostResponseBodyAsync(string parameter,
        string url)
    {
        using var httpClient = new HttpClient();
        var content = new StringContent(parameter, Encoding.Default, "application/x-www-form-urlencoded");
        var response = await httpClient.PostAsync(url, content);

        var responseBody = await response.Content.ReadAsStringAsync();
        var option = new JsonSerializerOptions
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        return (responseBody, option);
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
            }
        };

        SecretKeyController.SaveKeys(secretKeys);
    }

    /// <summary>
    /// </summary>
    private async void TwitchConnect()
    {
        var secretKeys = SecretKeyController.LoadKeys();

        // トークンの有効期限チェック
        if (DateTime.Now > Settings.Default.expiresDateTime)
        {
            MessageBox.Show("アクセストークンの有効期限が切れています。Authorizeボタンから再認可を行ってください。", "警告", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _twitchApiController ??= new TwitchApiController(secretKeys);
        // var channelId = _twitchApiController.GetTwitchChannelId();

        var isTokenValid = _twitchApiController.ValidateToken();
        LogController.OutputLog($"Token validation result: {isTokenValid}");

        if (!isTokenValid)
        {
            MessageBox.Show("アクセストークンが無効または期限切れです。Authorizeボタンから再認可を行ってください。", "警告", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _twitchClientController ??= new TwitchClientController(secretKeys, _twitchApiController);

        // 棒読みちゃん接続チェック
        if (BouyomiChanConnectCheckBox.IsChecked == true)
        {
            if (!await _twitchClientController.BouyomiChanController.IsBouyomiChanRunningAsync())
            {
                MessageBox.Show("棒読みちゃんが起動していません。起動してから接続してください。", "警告", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
        }

        _twitchClientController.Connect();

        // EventSub
        _twitchEventSubController = new TwitchEventSubController(_twitchClientController, _twitchApiController);
        await _twitchEventSubController.ConnectAsync();

        // Window追加
        _chatWindow ??= new ChatWindow(_twitchClientController);
        _chatWindow.Show();

        _raidListWindow ??= new RaidListWindow(_twitchApiController);
        _raidListWindow.Show();

        LockWindowControl();
    }

    /// <summary>
    /// </summary>
    private async void TwitchDisconnect()
    {
        if (_twitchEventSubController != null) await _twitchEventSubController.DisconnectAsync();
        _twitchClientController?.Disconnect();

        _twitchClientController = null;
        _twitchApiController = null;
        _twitchEventSubController = null;

        UnlockWindowControl();
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
    private void TwitchConnectionButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            SaveSecretValue();

            var state = TwitchConnectionStateLabel.Content.ToString();
            if (state is not null && state.Equals("State: Connect"))
            {
                TwitchDisconnect();
            }
            else
            {
                TwitchConnect();
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
    private void TwitchDisconnectButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            TwitchDisconnect();
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