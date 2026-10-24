namespace FaraBotModeratorSite.Pages;

/// <summary>
/// ページごとの題名と説明。ブラウザ（PageTitle）と、ビルドツールが作る静的 HTML の head（title・description・OGP）で同じ文を使う。
/// </summary>
public static class PageMeta
{
    /// <summary>「ページ名 | サイト名」の形の題名。</summary>
    public static string Titled(string title) => $"{title} | {SiteInfo.Name}";

    public const string HomeTitle = SiteInfo.Name + " | Twitch 配信者向けチャットボット";
    public const string HomeDescription = SiteInfo.Description;

    public static readonly string DocsTitle = Titled("ドキュメント");
    public const string DocsDescription = SiteInfo.Name + " の使い方。初期設定、リアクションイベント、翻訳、タイマー、コマンド実行（ポケモンなど）まで。";

    public static readonly string ChangelogTitle = Titled("更新履歴");
    public const string ChangelogDescription = SiteInfo.Name + " の更新履歴です。機能の追加や、説明・サイトの変更を新しい順に載せています。";

    public static readonly string PrivacyTitle = Titled("プライバシーポリシー");
    public const string PrivacyDescription = SiteInfo.Name + " のサイトで、どんな情報を何のために扱うかのまとめです。";

    public static readonly string ReportTitle = Titled("問い合わせ");
    public const string ReportDescription = SiteInfo.Name + " の不具合・要望・ご意見を、作者に直接送るフォームです。";

    public static readonly string NotFoundTitle = Titled("ページが見つかりません");

    public static string DocTitle(DocPage doc) => Titled(doc.Title);
}
