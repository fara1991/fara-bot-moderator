using System;
using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using FaraBotModerator.Models;

namespace FaraBotModerator.Controllers;

/// <summary>
/// チャンネルポイントの交換内容に応じた固有の処理（例：Saber Tailorの設定ファイル生成）を行うコントローラー
/// </summary>
public class UniqueChannelPointController
{
    /// <summary>
    /// 「Random Tailor」のチャンネルポイントのタイトル（Twitch 側の名前と同じにする）
    /// </summary>
    private const string RandomTailorTitle = "Random Tailor";

    /// <summary>
    /// ランダムな位置の範囲（mm。下限を含み、上限を含まない）
    /// </summary>
    private const int RandomPositionMinMm = -100;
    private const int RandomPositionMaxMm = 100;

    /// <summary>
    /// ランダムな回転の範囲（度。下限を含み、上限を含まない）
    /// </summary>
    private const int RandomRotationMinDeg = -45;
    private const int RandomRotationMaxDeg = 45;

    private readonly SecretKeyModel _secretKeys;

    /// <summary>
    /// UniqueChannelPointController のコンストラクタ
    /// </summary>
    /// <param name="secretKeys">設定情報</param>
    public UniqueChannelPointController(SecretKeyModel secretKeys)
    {
        _secretKeys = secretKeys;
    }

    /// <summary>
    /// チャンネルポイントのタイトルに応じて適切な処理を実行します。
    /// </summary>
    /// <param name="userName">交換したユーザー名</param>
    /// <param name="channelPointTitle">チャンネルポイントのタイトル</param>
    /// <returns>処理結果のメッセージ（ボットがチャットに送信するためのもの）</returns>
    public string Exec(string userName, string channelPointTitle)
    {
        if (channelPointTitle == RandomTailorTitle) return ExecRandomTailor(userName, _secretKeys.BeatSaber.ResolvedUserDataPath);

        return "";
    }

    /// <summary>
    /// 「Random Tailor」チャンネルポイント交換時の処理。
    /// BeatSaberのSaber Tailorプラグイン用設定ファイルをランダムな値で生成します。
    /// 書けなかったとき（フォルダが無い・書き込めない）は、チャットに通知する文だけを返します（例外は外へ出しません）。
    /// </summary>
    /// <param name="userName">ユーザー名</param>
    /// <param name="beatSaberUserPath">BeatSaberのUserDataディレクトリパス</param>
    /// <returns>チャットに通知する詳細メッセージ</returns>
    private static string ExecRandomTailor(string userName, string beatSaberUserPath)
    {
        if (string.IsNullOrEmpty(beatSaberUserPath))
        {
            return "BeatSaber UserData path is not configured.";
        }

        // PosX, PosY, PosZ, RotX, RotY, RotZ
        var r = new Random();
        var pos = new[]
            {r.Next(RandomPositionMinMm, RandomPositionMaxMm), r.Next(RandomPositionMinMm, RandomPositionMaxMm), r.Next(RandomPositionMinMm, RandomPositionMaxMm)};
        var rot = new[]
            {r.Next(RandomRotationMinDeg, RandomRotationMaxDeg), r.Next(RandomRotationMinDeg, RandomRotationMaxDeg), r.Next(RandomRotationMinDeg, RandomRotationMaxDeg)};

        var d = DateTime.Now;
        // userNameは日本語が入るとSaber Tailorが認識しないらしい
        var fileName =
            $"SaberTailor.{userName}-{d.Year:0000}{d.Month:00}{d.Day:00}{d.Hour:00}{d.Minute:00}{d.Second:00}.json";
        var newRandomTailorFile = Path.Combine(beatSaberUserPath, fileName);

        var saberTailorModel = new SaberTailorModel
        {
            GripLeftPosition = {x = -pos[0], y = -pos[1], z = -pos[2]},
            GripRightPosition = {x = pos[0], y = pos[1], z = pos[2]},
            GripLeftRotation = {x = -rot[0], y = -rot[1], z = -rot[2]},
            GripRightRotation = {x = rot[0], y = rot[1], z = rot[2]}
        };

        var options = new JsonSerializerOptions
        {
            Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
            WriteIndented = true
        };
        var jsonData = JsonSerializer.Serialize(saberTailorModel, options);

        try
        {
            using var writer = new StreamWriter(newRandomTailorFile, false, Encoding.GetEncoding("SHIFT-JIS"));
            writer.WriteLine(jsonData);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            // フォルダが無い・書き込めないのは、利用者の環境の問題。イベントの処理を止めず、チャットに知らせる
            LogController.OutputLog($"<Error> RandomTailor の設定ファイルを書けませんでした: {ex.GetType().Name}");
            return "RandomTailor: BeatSaber の UserData フォルダに設定ファイルを書けませんでした（設定のフォルダの場所を確認してください）。";
        }

        return
            $"New Tailor {fileName}. RandomTailor detail: Pos(x: {pos[0]}mm, y: {pos[1]}mm, z: {pos[2]}mm), Rot(x: {rot[0]}deg, y: {rot[1]}deg, z: {rot[2]}deg)";
    }
}
