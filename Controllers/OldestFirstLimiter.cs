using System;
using System.Collections.Generic;

namespace FaraBotModerator.Controllers;

/// <summary>
/// 追加した順に項目を覚えて、上限を超えたとき、一番古いものから外す（チャットの行を、増え続けさせないため）。UI スレッドだけから使う（スレッドセーフではない）。
/// </summary>
/// <typeparam name="T">項目の型</typeparam>
/// <param name="limit">残す項目の上限（1 以上）</param>
internal sealed class OldestFirstLimiter<T>(int limit)
{
    private readonly int _limit = limit >= 1 ? limit : throw new ArgumentOutOfRangeException(nameof(limit), limit, "上限は 1 以上にしてください。");
    private readonly Queue<T> _items = new();

    /// <summary>
    /// 今、覚えている項目の数。
    /// </summary>
    internal int Count => _items.Count;

    /// <summary>
    /// 項目を追加します。上限を超えたぶんの、一番古い項目（追加した順）を、古いほうから返します（呼び出し元が、表から消す）。
    /// </summary>
    /// <param name="item">追加する項目</param>
    /// <returns>表から消す項目（上限を超えていないときは、空）</returns>
    internal IReadOnlyList<T> Add(T item)
    {
        _items.Enqueue(item);

        var removed = new List<T>();
        while (_items.Count > _limit) removed.Add(_items.Dequeue());
        return removed;
    }
}
