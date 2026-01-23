using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using FaraBotModerator.Controllers;
using FaraBotModerator.Models;
using Button = System.Windows.Controls.Button;
using Clipboard = System.Windows.Forms.Clipboard;

namespace FaraBotModerator.Views;

/// <summary>
/// 
/// </summary>
public partial class RaidListWindow : Window
{
    private readonly TwitchApiController _twitchApiController;

    /// <summary>
    /// 
    /// </summary>
    public RaidListWindow(TwitchApiController twitchApiController)
    {
        _twitchApiController = twitchApiController;
        InitializeComponent();
    }

    private void SetRaidDataGridView(List<StreamingUserModel> streamingUsers)
    {
        TwitchRaidDataGrid.ItemsSource = streamingUsers;

        // オプション: データの追加後にスクロールを調整したい場合などはここで行う
        // ただし ItemsSource を使う場合は以前のような個別の ScrollIntoView は工夫が必要
    }

    private void DataGridRowRaidButton_OnClick(object sender, RoutedEventArgs e)
    {
        var button = (Button) sender;
        var rowData = button.DataContext;

        if (rowData is StreamingUserModel userModel)
        {
            var command = $"/raid {userModel.LoginId}";
            Clipboard.SetText(command);

            // ボタンと同じレベルにあるPopupを探す
            var grid = VisualTreeHelper.GetParent(button) as Grid;
            Popup popup = null;

            if (grid != null)
            {
                for (int i = 0; i < VisualTreeHelper.GetChildrenCount(grid); i++)
                {
                    var child = VisualTreeHelper.GetChild(grid, i);
                    if (child is Popup foundPopup)
                    {
                        popup = foundPopup;
                        break;
                    }
                }
            }

            if (popup != null)
            {
                // Popupを表示
                popup.IsOpen = true;

                // 2秒後に自動で閉じる
                var timer = new DispatcherTimer();
                timer.Interval = TimeSpan.FromSeconds(2);
                timer.Tick += (s, args) =>
                {
                    popup.IsOpen = false;
                    timer.Stop();
                };
                timer.Start();
            }
        }
    }

    private void TwitchStreamingFollowerButton_OnClick(object sender, RoutedEventArgs e)
    {
        var streamingUsers = _twitchApiController.GetStreamingFollowerUsers();
        SetRaidDataGridView(streamingUsers);
    }

    private void TwitchStreamingSameGameButton_OnClick(object sender, RoutedEventArgs e)
    {
        var streamingUsers = _twitchApiController.GetStreamingSameGameUsers();
        SetRaidDataGridView(streamingUsers);
    }
}