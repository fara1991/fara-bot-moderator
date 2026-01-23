using System;
using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using FaraBotModerator.Models;

namespace FaraBotModerator.Controllers;

/// <summary>
/// </summary>
public class UniqueChannelPointController
{
    /// <summary>
    /// </summary>
    public UniqueChannelPointController()
    {
        // 最初にTwitch APIでChannelPoint一覧を取得できるようにしてもいいかも
    }

    /// <summary>
    /// </summary>
    /// <param name="userName"></param>
    /// <param name="channelPointTitle"></param>
    /// <returns></returns>
    public string Exec(string userName, string channelPointTitle)
    {
        if (channelPointTitle == "Random Tailor") return ExecRandomTailor(userName);

        return "";
    }

    /// <summary>
    /// </summary>
    /// <param name="userName"></param>
    /// <returns></returns>
    private static string ExecRandomTailor(string userName)
    {
        // PosX, PosY, PosZ, RotX, RotY, RotZ
        // pos -100~100mm  rot -45~45deg
        var r = new Random();
        var pos = new[]
            {r.Next(-100, 100), r.Next(-100, 100), r.Next(-100, 100)};
        var rot = new[]
            {r.Next(-45, 45), r.Next(-45, 45), r.Next(-45, 45)};

        var beatSaberUserPath = "C:\\Program Files (x86)\\Steam\\steamapps\\common\\Beat Saber\\UserData";
        var d = DateTime.Now;
        // userNameは日本語が入るとSaber Tailorが認識しないらしい
        var fileName =
            $"SaberTailor.{userName}-{d.Year:0000}{d.Month:00}{d.Day:00}{d.Hour:00}{d.Minute:00}{d.Second:00}.json";
        var newRandomTailorFile =
            $"{beatSaberUserPath}\\{fileName}";

        using var writer = new StreamWriter(newRandomTailorFile, false, Encoding.GetEncoding("SHIFT-JIS"));
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
        writer.WriteLine(jsonData);

        return
            $"New Tailor {fileName}. RandomTailor detail: Pos(x: {pos[0]}mm, y: {pos[1]}mm, z: {pos[2]}mm), Rot(x: {rot[0]}deg, y: {rot[1]}deg, z: {rot[2]}deg)";
    }
}