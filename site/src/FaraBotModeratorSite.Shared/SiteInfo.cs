namespace FaraBotModeratorSite;

/// <summary>
/// このサイトの名前・作者・URL・保存のキー・SNS。ブラウザ（Web）が共通部品の設定（SiteOptions）に入れ、
/// ビルドツール（Build）がページの題名・説明を作るときにも読む。サイト名や外部リンクは、ここにだけ書く。
/// 3 サイト（pokemon・avatar・bot）で、フッターの形を同じにする。
/// </summary>
public static class SiteInfo
{
    public const string Name = "FaraBotModerator";

    public const string Description = "Twitch 配信者向けの多機能チャットボット。チャット表示・自動翻訳・読み上げ・イベント通知・タイマー・ポケモンのダメージ計算コマンドをひとつに。";

    public const string RepositoryUrl = "https://github.com/fara1991/fara-bot-moderator";
    public const string ReleasesUrl = "https://github.com/fara1991/fara-bot-moderator/releases/latest";
    public const string PokemonSiteUrl = "https://pokemon.fara-labs.com/";

    /// <summary>作者のホームページ（フッターの Fara・サイドメニューの「Fara のポートフォリオ」）。</summary>
    public const string PortfolioUrl = "https://fara-labs.com/";

    public const string Author = "Fara";

    /// <summary>サイトを公開した年（© の年）。</summary>
    public const int PublishedYear = 2026;

    /// <summary>本番のホスト。</summary>
    public const string Origin = "https://twitch-bot.fara-labs.com";

    /// <summary>確認用サイトのホスト名（開いているホストがこれなら、fara-labs.com へのリンクを確認用に切り替える）。</summary>
    public const string DevelopHost = "twitch-bot-dev.fara-labs.com";

    /// <summary>
    /// ライト／ダークを入れる localStorage のキー。以前の静的サイトと同じ名前のまま（値は kit の JSON に変わる。wwwroot/js/site-init.js が旧い値を書き直す）。
    /// index.html の theme-init.js の data-storage-key と同じ値にする。
    /// </summary>
    public const string StorageKey = "fara-bot-moderator-site.theme";

    /// <summary>PC のサイドバーを隠しているか（"1" で隠す）の localStorage のキー。以前の静的サイトと同じ名前・同じ値の形。index.html の theme-init.js の data-sidebar-key と同じ値にする。</summary>
    public const string SidebarKey = "fara-bot-moderator-site.sidebar-hidden";

    /// <summary>
    /// フッターの SNS（サービス名, URL）。URL は、ポートフォリオ（fara-labs.com）の wwwroot/data/profile.json に合わせている。
    /// 名前・色・アイコンは kit の SocialServices が持つ。Discord は、ポートフォリオに無いため、作者が指定した URL。
    /// </summary>
    public static readonly IReadOnlyList<(string Service, string Url)> Social =
    [
        ("github", "https://github.com/fara1991"),
        ("twitter", "https://twitter.com/game_fara"),
        ("twitch", "https://twitch.tv/game_fara"),
        ("discord", "https://discord.com/invite/B38eCSYyk5"),
    ];
}
