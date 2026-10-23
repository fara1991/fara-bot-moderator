namespace FaraBotModeratorSite.Data;

/// <summary>appsettings.json の Site セクション。サイト名や外部リンクはここにだけ書く。</summary>
public sealed class SiteOptions
{
    public string Title { get; set; } = "FaraBotModerator";
    public string Description { get; set; } = "";
    public string RepositoryUrl { get; set; } = "";
    public string ReleasesUrl { get; set; } = "";
    public string PortfolioUrl { get; set; } = "";
    public string PokemonSiteUrl { get; set; } = "";
    /// <summary>フッターの SNS アイコン。空のものは出さない。</summary>
    public string GitHubUrl { get; set; } = "";
    public string TwitterUrl { get; set; } = "";
    public string TwitchUrl { get; set; } = "";
    public string DiscordUrl { get; set; } = "";
    /// <summary>説明の元になる Manual.md の場所（プロジェクトのフォルダからの相対パス）。</summary>
    public string ManualPath { get; set; } = "../Manual.md";
    /// <summary>フッターの © に出す、サイトを公開した年。</summary>
    public int PublishedYear { get; set; } = 2026;
}
