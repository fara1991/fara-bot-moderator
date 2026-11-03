using System;
using System.Windows;
using System.Windows.Input;
using FaraBotModerator.Controllers;
using FaraBotModerator.Models;

namespace FaraBotModerator.Views;

/// <summary>
///     ChatWindow.xaml の相互作用ロジック
/// </summary>
public partial class ChatWindow : Window
{
    private readonly Func<TwitchClientController?> _getTwitchClientController;

    /// <summary>
    /// 残す行の上限（長い配信で、増え続けないため）。
    /// </summary>
    internal const int MaxRowsKept = 1000;

    /// <summary>
    /// 追加した順に行を覚えて、上限を超えた分の、一番古い行を知らせる。表は列で並べ替えられるので、Items の先頭ではなく、これで古い行を決める。
    /// </summary>
    internal OldestFirstLimiter<ChatModel> Rows { get; } = new(MaxRowsKept);

    /// <summary>
    /// </summary>
    /// <param name="getTwitchClientController">今のチャットクライアントを返す関数（接続し直すと別のものに替わるので、送るたびに取る。接続していなければ null）</param>
    public ChatWindow(Func<TwitchClientController?> getTwitchClientController)
    {
        _getTwitchClientController = getTwitchClientController;
        InitializeComponent();
    }

    /// <summary>
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    private void TwitchRequestChatTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;

        e.Handled = true;
        SendMessageFromTextBox();
    }

    /// <summary>
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    private void TwitchRequestChatButton_Click(object sender, RoutedEventArgs e)
    {
        SendMessageFromTextBox();
    }

    /// <summary>
    /// 入力欄のメッセージを送ります。空のとき、または接続していないときは、何もしません（入力欄は消しません）。
    /// </summary>
    private void SendMessageFromTextBox()
    {
        var message = TwitchRequestChatTextBox.Text;
        if (string.IsNullOrWhiteSpace(message)) return;

        var client = _getTwitchClientController();
        if (client is null) return;

        _ = client.SendApplicationMessageAsync(message);
        TwitchRequestChatTextBox.Text = "";
    }
}