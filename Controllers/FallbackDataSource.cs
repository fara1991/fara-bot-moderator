using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using FaraPokemonAssistance.Core.Data;

namespace FaraBotModerator.Controllers;

/// <summary>
/// 複数の取得元を順に試すデータソース。取得に失敗した（例外が出た）取得元は飛ばして次を使います。
/// 最後に成功した取得元から試すので、片方が落ちている間も毎回失敗を待たずに済みます。
/// </summary>
public sealed class FallbackDataSource : IDataSource
{
    private readonly IReadOnlyList<IDataSource> _sources;
    private int _preferred;

    /// <summary>
    /// FallbackDataSource のコンストラクタ
    /// </summary>
    /// <param name="sources">試す順の取得元（1 件以上）</param>
    public FallbackDataSource(IEnumerable<IDataSource> sources)
    {
        _sources = sources.ToList();
        if (_sources.Count == 0) throw new ArgumentException("取得元が 1 件もありません", nameof(sources));
    }

    /// <inheritdoc />
    public async Task<string?> ReadTextAsync(string relativePath, CancellationToken cancellationToken = default)
    {
        var start = Volatile.Read(ref _preferred);
        Exception? lastError = null;
        for (var i = 0; i < _sources.Count; i++)
        {
            var index = (start + i) % _sources.Count;
            try
            {
                var text = await _sources[index].ReadTextAsync(relativePath, cancellationToken).ConfigureAwait(false);
                Volatile.Write(ref _preferred, index);
                return text;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
            {
                lastError = ex;
            }
        }

        throw new HttpRequestException($"どの取得元からも {relativePath} を取得できませんでした", lastError);
    }
}
