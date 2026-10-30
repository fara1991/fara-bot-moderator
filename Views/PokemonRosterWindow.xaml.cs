using System;
using System.Diagnostics.CodeAnalysis;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using FaraBotModerator.Controllers;

namespace FaraBotModerator.Views;

/// <summary>
///     ポケモンシリーズの登録データ（登録したポケモン・チーム）を一覧で表示するウィンドウ。読むだけで、書き換えはしません。
///     読み込みはメインウィンドウが行い、結果を <see cref="ShowRoster" /> で受け取ります。
/// </summary>
public partial class PokemonRosterWindow : Window
{
    private readonly Func<Task> _reload;

    /// <summary>
    /// </summary>
    /// <param name="reload">再読み込みを押したときに呼ぶ処理（メインウィンドウの登録データの読み込み）</param>
    public PokemonRosterWindow(Func<Task> reload)
    {
        _reload = reload;
        InitializeComponent();
        ShowRoster(null, "", false);
    }

    /// <summary>
    ///     登録データを表示します。選んでいたゲームは、読み直したあとも選んだままにします。
    /// </summary>
    /// <param name="games">登録のあるゲームの一覧（読み込めていない・アクセスキーが無いときは null）</param>
    /// <param name="status">状態の表示（読み込んだ時刻やエラー）</param>
    /// <param name="loading">読み込み中か（読み込み中は再読み込みを押せなくする）</param>
    public void ShowRoster(IReadOnlyList<RosterGameView>? games, string status, bool loading)
    {
        RosterStatusTextBlock.Text = status;
        RosterReloadButton.IsEnabled = !loading;
        // 読み込み中は前の内容を表示したままにする
        if (loading) return;

        var selectedKey = (RosterGameComboBox.SelectedItem as RosterGameView)?.Key;
        RosterGameComboBox.ItemsSource = games;
        RosterGameComboBox.SelectedItem =
            games?.FirstOrDefault(g => g.Key == selectedKey) ?? games?.FirstOrDefault();
        if (RosterGameComboBox.SelectedItem is null) ShowGame(null);
    }

    /// <summary>
    ///     選んだゲームの登録ポケモンとチームを表に出します。
    /// </summary>
    private void ShowGame(RosterGameView? game)
    {
        RosterPokemonDataGrid.ItemsSource = game?.Pokemon;
        RosterTeamDataGrid.ItemsSource = game?.Teams;
        RosterTeraColumn.Visibility = game?.HasTeraType == true ? Visibility.Visible : Visibility.Collapsed;
    }

    private void RosterGameComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        ShowGame(RosterGameComboBox.SelectedItem as RosterGameView);
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "画面のイベントハンドラー。例外でアプリを落とさないよう、ここで受けてログ・メッセージで知らせる")]
    [SuppressMessage("Usage", "VSTHRD100:Avoid async void methods", Justification = "イベントハンドラーの型（void を返すデリゲート）の都合で async void が必要。例外は本文の try/catch で受けて、ログ・画面で知らせている")]
    private async void RosterReloadButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await _reload();
        }
        catch (Exception ex)
        {
            // async void の中で例外を出すとアプリが落ちるので、必ず受けてログに残す
            LogController.OutputLog($"<Error> 登録データの一覧の再読み込みに失敗しました: {ex.GetType().Name}");
            MessageBox.Show($"登録データを読み込めませんでした: {ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
