namespace ZDD.Net.Core
{
    /// <summary>
    /// ZDD の内部ノード 1 個分。<c>int</c> 4 本 = 16 バイト固定で、
    /// <see cref="NodeTable"/> が持つ 1 本の配列に AoS（Array of Structures）で連続確保される。
    /// ノードは ID（配列の index）でのみ参照され、参照型としては決して扱わない。
    /// </summary>
    /// <remarks>
    /// フィールドを増やす・型を変える変更はノード表全体のメモリ使用量に直結する
    /// （100 万ノードあたり 16 MB）。16 バイトであることは単体テストで固定している。
    /// </remarks>
    internal struct ZddNode
    {
        /// <summary>
        /// このノードが対応する変数のレベル。1 = 最下位（葉側）… N = 最上位（根側）で、
        /// TdZdd と同じ向き。終端 ⊥/⊤ のレベルは 0（どの変数にも対応しない）。
        /// </summary>
        public int Level;

        /// <summary>
        /// 0-枝。この変数に対応する要素を「含まない」側の子ノード ID。
        /// </summary>
        public int Lo;

        /// <summary>
        /// 1-枝。この変数に対応する要素を「含む」側の子ノード ID。
        /// ゼロサプレス削減規則により <c>Hi != <see cref="NodeTable.Bottom"/></c> でなければならない。
        /// </summary>
        public int Hi;

        /// <summary>
        /// 一意化表（M1-2）がチェーン法を採る場合の次エントリ ID。
        /// オープンアドレス法では使わないため、<see cref="NodeTable"/> は
        /// <see cref="NodeTable.NoNext"/> で初期化する。
        /// </summary>
        public int Next;
    }
}
