using System;
using System.Text.Json;

namespace FaraBotModerator.Controllers;

/// <summary>
/// EventSub（WebSocket）のメッセージの種類。
/// </summary>
internal enum EventSubMessageKind
{
    /// <summary>
    /// 知らない種類（無視する）。
    /// </summary>
    Unknown,

    /// <summary>
    /// 接続できた知らせ。セッション ID が入っている。
    /// </summary>
    Welcome,

    /// <summary>
    /// 接続が生きている知らせ（一定時間、何も来ないときに届く）。
    /// </summary>
    KeepAlive,

    /// <summary>
    /// 購読したイベントの通知。
    /// </summary>
    Notification,

    /// <summary>
    /// 別の URL へ接続し直すよう求める知らせ。
    /// </summary>
    Reconnect,

    /// <summary>
    /// 購読が取り消された知らせ。
    /// </summary>
    Revocation,
}

/// <summary>
/// 読んだ EventSub のメッセージ 1 件。
/// </summary>
/// <param name="Kind">メッセージの種類</param>
/// <param name="MessageId">メッセージの ID（同じ通知が 2 度届いたときに、見分けるため）</param>
/// <param name="SubscriptionType">購読の種類（通知・取り消しのとき。例: channel.follow）</param>
/// <param name="SessionId">セッション ID（Welcome のとき）</param>
/// <param name="KeepAliveTimeoutSeconds">キープアライブの間隔（秒。Welcome のとき）</param>
/// <param name="ReconnectUrl">接続し直す URL（Reconnect のとき）</param>
/// <param name="Event">イベントの中身（通知のとき）</param>
internal sealed record EventSubMessage(
    EventSubMessageKind Kind,
    string MessageId,
    string? SubscriptionType,
    string? SessionId,
    int? KeepAliveTimeoutSeconds,
    string? ReconnectUrl,
    JsonElement? Event);

/// <summary>
/// フォローのイベント。
/// </summary>
/// <param name="UserLogin">フォローしたユーザーのログイン名</param>
/// <param name="UserName">フォローしたユーザーの表示名</param>
internal sealed record FollowEvent(string UserLogin, string UserName);

/// <summary>
/// ビッツ（チアー）のイベント。
/// </summary>
/// <param name="UserId">ビッツを投げたユーザーの ID（匿名のときは null）</param>
/// <param name="UserName">ビッツを投げたユーザーの表示名（匿名のときは null）</param>
/// <param name="Bits">ビッツの量</param>
internal sealed record CheerEvent(string? UserId, string? UserName, int Bits);

/// <summary>
/// チャンネルポイントの交換のイベント。
/// </summary>
/// <param name="UserLogin">交換したユーザーのログイン名</param>
/// <param name="RewardTitle">報酬のタイトル</param>
/// <param name="RewardCost">報酬のポイント</param>
internal sealed record RedemptionEvent(string UserLogin, string RewardTitle, int RewardCost);

/// <summary>
/// EventSub のメッセージ（JSON）を読む。通信に依らない純粋な関数だけを置いて、テストする。
/// </summary>
internal static class EventSubParser
{
    /// <summary>
    /// メッセージ 1 件を読みます。JSON でない・形が違うときは <see cref="JsonException"/>。
    /// </summary>
    /// <param name="json">受け取った本文</param>
    internal static EventSubMessage Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("metadata", out var metadata))
            throw new JsonException("EventSub のメッセージに metadata がありません。");

        var kind = GetString(metadata, "message_type") switch
        {
            "session_welcome" => EventSubMessageKind.Welcome,
            "session_keepalive" => EventSubMessageKind.KeepAlive,
            "notification" => EventSubMessageKind.Notification,
            "session_reconnect" => EventSubMessageKind.Reconnect,
            "revocation" => EventSubMessageKind.Revocation,
            _ => EventSubMessageKind.Unknown,
        };
        var messageId = GetString(metadata, "message_id") ?? "";
        var subscriptionType = GetString(metadata, "subscription_type");

        string? sessionId = null;
        int? keepAlive = null;
        string? reconnectUrl = null;
        JsonElement? eventElement = null;
        if (root.TryGetProperty("payload", out var payload) && payload.ValueKind == JsonValueKind.Object)
        {
            if (payload.TryGetProperty("session", out var session) && session.ValueKind == JsonValueKind.Object)
            {
                sessionId = GetString(session, "id");
                reconnectUrl = GetString(session, "reconnect_url");
                if (session.TryGetProperty("keepalive_timeout_seconds", out var seconds) && seconds.ValueKind == JsonValueKind.Number)
                    keepAlive = seconds.GetInt32();
            }

            if (kind == EventSubMessageKind.Revocation && subscriptionType is null
                && payload.TryGetProperty("subscription", out var subscription) && subscription.ValueKind == JsonValueKind.Object)
                subscriptionType = GetString(subscription, "type");

            // 呼び出し側が、document の破棄後も使えるよう、複製する
            if (payload.TryGetProperty("event", out var eventValue) && eventValue.ValueKind == JsonValueKind.Object)
                eventElement = eventValue.Clone();
        }

        return new EventSubMessage(kind, messageId, subscriptionType, sessionId, keepAlive, reconnectUrl, eventElement);
    }

    /// <summary>
    /// フォローのイベントを読みます。ユーザーが読み取れないときは null。
    /// </summary>
    internal static FollowEvent? ParseFollow(JsonElement e)
    {
        var login = GetString(e, "user_login");
        return login is null ? null : new FollowEvent(login, GetString(e, "user_name") ?? login);
    }

    /// <summary>
    /// ビッツのイベントを読みます。匿名のときは、ユーザーが null になります。
    /// </summary>
    internal static CheerEvent ParseCheer(JsonElement e)
    {
        var anonymous = e.TryGetProperty("is_anonymous", out var flag) && flag.ValueKind == JsonValueKind.True;
        var bits = e.TryGetProperty("bits", out var value) && value.ValueKind == JsonValueKind.Number ? value.GetInt32() : 0;
        return anonymous
            ? new CheerEvent(null, null, bits)
            : new CheerEvent(GetString(e, "user_id"), GetString(e, "user_name"), bits);
    }

    /// <summary>
    /// チャンネルポイントの交換のイベントを読みます。読み取れないときは null。
    /// </summary>
    internal static RedemptionEvent? ParseRedemption(JsonElement e)
    {
        var login = GetString(e, "user_login");
        if (login is null || !e.TryGetProperty("reward", out var reward) || reward.ValueKind != JsonValueKind.Object) return null;

        var cost = reward.TryGetProperty("cost", out var value) && value.ValueKind == JsonValueKind.Number ? value.GetInt32() : 0;
        return new RedemptionEvent(login, GetString(reward, "title") ?? "", cost);
    }

    private static string? GetString(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
