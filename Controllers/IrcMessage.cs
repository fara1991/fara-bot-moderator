using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace FaraBotModerator.Controllers;

/// <summary>
/// 読んだ IRC のメッセージ 1 行。
/// </summary>
/// <param name="Tags">IRCv3 のタグ（エスケープは戻してある。無ければ空）</param>
/// <param name="Nick">送り手（prefix の nick。無ければ null）</param>
/// <param name="Command">コマンド（PRIVMSG・USERNOTICE・001 など。大文字のまま）</param>
/// <param name="Params">パラメーター（最後の「:」つきのものは含まない）</param>
/// <param name="Trailing">最後の「:」つきのパラメーター（無ければ null）</param>
internal sealed record IrcMessage(
    IReadOnlyDictionary<string, string> Tags,
    string? Nick,
    string Command,
    IReadOnlyList<string> Params,
    string? Trailing)
{
    /// <summary>
    /// タグの値を返します。無いときは null。
    /// </summary>
    internal string? Tag(string name) => Tags.TryGetValue(name, out var value) ? value : null;
}

/// <summary>
/// IRC の 1 行を読む。通信に依らない純粋な関数だけを置いて、テストする。
/// </summary>
internal static class IrcParser
{
    /// <summary>
    /// 1 行を読みます。空行・コマンドが無い行は null。
    /// 形: <c>[@tags ][:prefix ]COMMAND [params…][ :trailing]</c>
    /// </summary>
    /// <param name="line">1 行（末尾の改行は無くてもよい）</param>
    internal static IrcMessage? Parse(string line)
    {
        var rest = line.TrimEnd('\r', '\n');
        if (rest.Length == 0) return null;

        var tags = new Dictionary<string, string>(StringComparer.Ordinal);
        if (rest[0] == '@')
        {
            var end = rest.IndexOf(' ');
            if (end < 0) return null;
            foreach (var pair in rest[1..end].Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                var equals = pair.IndexOf('=');
                if (equals < 0) tags[pair] = "";
                else tags[pair[..equals]] = UnescapeTagValue(pair[(equals + 1)..]);
            }

            rest = rest[(end + 1)..].TrimStart(' ');
        }

        string? nick = null;
        if (rest.StartsWith(':'))
        {
            var end = rest.IndexOf(' ');
            if (end < 0) return null;
            var prefix = rest[1..end];
            var bang = prefix.IndexOf('!');
            nick = bang >= 0 ? prefix[..bang] : prefix;
            rest = rest[(end + 1)..].TrimStart(' ');
        }

        string? trailing = null;
        var trailingStart = rest.IndexOf(" :", StringComparison.Ordinal);
        if (trailingStart >= 0)
        {
            trailing = rest[(trailingStart + 2)..];
            rest = rest[..trailingStart];
        }

        var parts = rest.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return null;
        return new IrcMessage(tags, nick, parts[0], parts.Skip(1).ToList(), trailing);
    }

    /// <summary>
    /// タグの値のエスケープ（\s = 空白、\: = ;、\\ = \、\r、\n）を戻します。
    /// </summary>
    internal static string UnescapeTagValue(string value)
    {
        if (!value.Contains('\\', StringComparison.Ordinal)) return value;

        var builder = new StringBuilder(value.Length);
        for (var i = 0; i < value.Length; i++)
        {
            if (value[i] != '\\' || i + 1 >= value.Length)
            {
                // 末尾の単独の \ は、捨てる（IRCv3 の定め）
                if (value[i] != '\\') builder.Append(value[i]);
                continue;
            }

            i++;
            builder.Append(value[i] switch
            {
                's' => ' ',
                ':' => ';',
                'r' => '\r',
                'n' => '\n',
                _ => value[i],
            });
        }

        return builder.ToString();
    }
}

/// <summary>
/// チャットのメッセージ（PRIVMSG）。
/// </summary>
/// <param name="Username">送った人のログイン名</param>
/// <param name="DisplayName">送った人の表示名（無ければログイン名）</param>
/// <param name="UserId">送った人のユーザー ID（無ければ空）</param>
/// <param name="Message">メッセージ本文（/me の囲みは外してある）</param>
/// <param name="IsBroadcaster">配信者か</param>
/// <param name="IsModerator">モデレーターか</param>
/// <param name="EmoteNames">このメッセージに含まれていたエモートの名前（/me のときは空）</param>
internal sealed record ChatMessageEvent(
    string Username,
    string DisplayName,
    string UserId,
    string Message,
    bool IsBroadcaster,
    bool IsModerator,
    IReadOnlyList<string> EmoteNames);

/// <summary>
/// USERNOTICE（サブスク・Raid・お知らせなど）の種類。
/// </summary>
internal enum UserNoticeKind
{
    /// <summary>
    /// 扱わない種類。
    /// </summary>
    Other,

    /// <summary>
    /// 新規サブスク（Prime 以外）。
    /// </summary>
    NewSubscriber,

    /// <summary>
    /// 新規サブスク（Prime）。
    /// </summary>
    PrimeSubscriber,

    /// <summary>
    /// 継続サブスク（再サブスク）。
    /// </summary>
    ReSubscriber,

    /// <summary>
    /// サブスクのギフト。
    /// </summary>
    GiftedSubscription,

    /// <summary>
    /// Raid。
    /// </summary>
    Raid,

    /// <summary>
    /// お知らせ（/announcement）。
    /// </summary>
    Announcement,
}

/// <summary>
/// USERNOTICE（サブスク・Raid・お知らせなど）。
/// </summary>
/// <param name="Kind">種類</param>
/// <param name="Channel">チャンネル名（# は付かない）</param>
/// <param name="DisplayName">表示名（サブスクした人・ギフトを贈った人）</param>
/// <param name="UserId">ユーザー ID</param>
/// <param name="CumulativeMonths">累計のサブスク月数（タグの文字のまま。無ければ null）</param>
/// <param name="RaiderLogin">Raid してきた人のログイン名</param>
/// <param name="Message">お知らせの本文</param>
internal sealed record UserNoticeEvent(
    UserNoticeKind Kind,
    string Channel,
    string DisplayName,
    string UserId,
    string? CumulativeMonths,
    string RaiderLogin,
    string Message);

/// <summary>
/// 読んだ IRC のメッセージから、チャットのイベントを作る。
/// </summary>
internal static class IrcEvents
{
    /// <summary>
    /// PRIVMSG をチャットのメッセージにします。PRIVMSG でない・送り手が無いときは null。
    /// </summary>
    internal static ChatMessageEvent? ToChatMessage(IrcMessage message)
    {
        if (message.Command != "PRIVMSG" || message.Nick is null) return null;

        var text = message.Trailing ?? "";
        var isAction = false;
        const string actionPrefix = "\u0001ACTION ";
        if (text.StartsWith(actionPrefix, StringComparison.Ordinal))
        {
            isAction = true;
            text = text[actionPrefix.Length..].TrimEnd('\u0001');
        }

        var badges = (message.Tag("badges") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(badge => badge.Split('/')[0]).ToHashSet(StringComparer.Ordinal);
        var displayName = message.Tag("display-name");
        return new ChatMessageEvent(
            message.Nick,
            string.IsNullOrEmpty(displayName) ? message.Nick : displayName,
            message.Tag("user-id") ?? "",
            text,
            badges.Contains("broadcaster"),
            message.Tag("mod") == "1" || badges.Contains("moderator"),
            // /me のメッセージは、エモートの位置が合わないことがあるので、名前を取らない
            isAction ? [] : ExtractEmoteNames(text, message.Tag("emotes")));
    }

    /// <summary>
    /// USERNOTICE をイベントにします。USERNOTICE でないときは null。扱わない種類は <see cref="UserNoticeKind.Other"/>。
    /// </summary>
    internal static UserNoticeEvent? ToUserNotice(IrcMessage message)
    {
        if (message.Command != "USERNOTICE") return null;

        var channel = (message.Params.FirstOrDefault() ?? "").TrimStart('#');
        var plan = message.Tag("msg-param-sub-plan");
        var kind = message.Tag("msg-id") switch
        {
            "sub" => string.Equals(plan, "Prime", StringComparison.OrdinalIgnoreCase) ? UserNoticeKind.PrimeSubscriber : UserNoticeKind.NewSubscriber,
            "resub" => UserNoticeKind.ReSubscriber,
            "subgift" => UserNoticeKind.GiftedSubscription,
            "raid" => UserNoticeKind.Raid,
            "announcement" => UserNoticeKind.Announcement,
            _ => UserNoticeKind.Other,
        };
        var login = message.Tag("login") ?? "";
        var displayName = message.Tag("display-name");
        return new UserNoticeEvent(
            kind,
            channel,
            string.IsNullOrEmpty(displayName) ? login : displayName,
            message.Tag("user-id") ?? "",
            message.Tag("msg-param-cumulative-months"),
            message.Tag("msg-param-login") ?? login,
            message.Trailing ?? "");
    }

    /// <summary>
    /// <c>emotes</c> タグ（<c>25:0-4,12-16/1902:6-10</c>）の位置から、メッセージ中のエモートの名前を取ります。
    /// 位置は、文字（Unicode のコードポイント）の数で数える。範囲が合わないものは、飛ばす。
    /// </summary>
    /// <param name="message">メッセージ本文</param>
    /// <param name="emotesTag"><c>emotes</c> タグの値</param>
    internal static IReadOnlyList<string> ExtractEmoteNames(string message, string? emotesTag)
    {
        if (string.IsNullOrEmpty(emotesTag)) return [];

        var runes = message.EnumerateRunes().Select(rune => rune.ToString()).ToArray();
        var names = new List<string>();
        foreach (var emote in emotesTag.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            var colon = emote.IndexOf(':');
            if (colon < 0) continue;

            foreach (var range in emote[(colon + 1)..].Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                var dash = range.IndexOf('-');
                if (dash < 0
                    || !int.TryParse(range[..dash], NumberStyles.None, CultureInfo.InvariantCulture, out var start)
                    || !int.TryParse(range[(dash + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out var end)
                    || start > end || end >= runes.Length)
                    continue;

                var name = string.Concat(runes[start..(end + 1)]);
                // 空白を含む・長すぎるものは、位置の読み違いなので、名前として扱わない
                if (name.Length is > 0 and <= 40 && !name.Contains(' ', StringComparison.Ordinal)) names.Add(name);
            }
        }

        return names.Distinct(StringComparer.Ordinal).ToList();
    }
}

/// <summary>
/// 受け取ったメッセージから学んだエモートの名前で、メッセージ中のエモートを取り除く（翻訳・読み上げに使わないため）。
/// </summary>
internal sealed class EmoteFilter
{
    private readonly HashSet<string> _names = new(StringComparer.Ordinal);
    private readonly object _lock = new();

    /// <summary>
    /// エモートの名前を覚えます。
    /// </summary>
    /// <param name="names">エモートの名前</param>
    internal void Learn(IEnumerable<string> names)
    {
        lock (_lock)
        {
            foreach (var name in names) _names.Add(name);
        }
    }

    /// <summary>
    /// メッセージから、覚えているエモート（空白で区切られた単語として現れるもの）を取り除きます。
    /// </summary>
    /// <param name="message">メッセージ</param>
    /// <returns>エモートを除いた、前後の空白の無いメッセージ</returns>
    internal string Remove(string message)
    {
        string[] known;
        lock (_lock) known = _names.ToArray();
        if (known.Length == 0) return message.Trim();

        var words = message.Split(' ').Where(word => !known.Contains(word, StringComparer.Ordinal));
        return string.Join(' ', words).Trim();
    }
}
