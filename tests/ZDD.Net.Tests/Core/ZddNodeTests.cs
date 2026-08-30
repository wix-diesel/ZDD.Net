using System.Runtime.CompilerServices;
using Xunit;
using ZDD.Net.Core;

namespace ZDD.Net.Tests.Core
{
    public class ZddNodeTests
    {
        [Fact]
        public void NodeIsSixteenBytes()
        {
            // ノード表のメモリ使用量はこのサイズに比例する（docs/PLAN.md §4.1）。
            // フィールドの追加・型変更でここが動いたら、その影響を意識して変更すること。
            Assert.Equal(16, Unsafe.SizeOf<ZddNode>());
        }

        [Fact]
        public void FieldsAreZeroByDefault()
        {
            ZddNode node = default;

            Assert.Equal(0, node.Level);
            Assert.Equal(0, node.Lo);
            Assert.Equal(0, node.Hi);
            Assert.Equal(0, node.Next);
        }
    }
}
