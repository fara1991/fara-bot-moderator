using System.Collections.Concurrent;
using System.Threading.Tasks;
using FaraBotModerator.Models;

namespace FaraBotModerator.Controllers;

/// <summary>
/// チャットの表示待ちの行。送った順に並べ、アイコンは後から届いたものを入れる。
/// </summary>
/// <remarks>
/// 行の順番を守るため、先頭の行のアイコンが届くまで、後ろの行も表示しない（アイコンの取得は呼び出し側で時間を区切る）。
/// 1 つの取り出し役（UI の周期）から取り出す前提。
/// </remarks>
internal sealed class ChatRowQueue
{
    private readonly ConcurrentQueue<(ChatModel Row, Task<string?> Icon)> _rows = new();

    /// <summary>
    /// 行を末尾に入れる（アイコンは、まだ取得中でもよい）。
    /// </summary>
    /// <param name="row">表示する行（<see cref="ChatModel.Icon"/> は取り出すときに入る）</param>
    /// <param name="icon">アイコンの URL を返すタスク（null なら空として表示する）</param>
    public void Enqueue(ChatModel row, Task<string?> icon) => _rows.Enqueue((row, icon));

    /// <summary>
    /// 先頭の行を取り出す。先頭のアイコンがまだ届いていないときは、何も取り出さずに null を返す（順番を守るため）。
    /// </summary>
    /// <returns>表示する行。無い・まだ届いていないときは null</returns>
    public ChatModel? TryPick()
    {
        if (!_rows.TryPeek(out var head) || !head.Icon.IsCompleted) return null;
        if (!_rows.TryDequeue(out var item)) return null;

        item.Row.Icon = item.Icon.IsCompletedSuccessfully ? item.Icon.Result ?? "" : "";
        return item.Row;
    }
}
