using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Xunit;
using ZDD.Net.Core;

namespace ZDD.Net.Tests.Core
{
    public class ZddTests
    {
        [Fact]
        public void AHandleIsAtMostSixteenBytes()
        {
            // マネージャ参照 + int 1 本（docs/PLAN.md §4.6）。ここが太ると演算のたびに
            // コピーされる量が増えるので、大きさは固定しておく。
            Assert.True(
                Unsafe.SizeOf<Zdd>() <= 16,
                $"Zdd must stay at most 16 bytes, but was {Unsafe.SizeOf<Zdd>()}.");
        }

        [Fact]
        public void AHandleKnowsItsManager()
        {
            using ZddManager manager = new ZddManager(3);

            Assert.Same(manager, manager.Singleton(1).Manager);
            Assert.Same(manager, manager.Empty.Manager);
            Assert.Same(manager, manager.Base.Manager);
        }

        // ---- 等値 ----

        [Fact]
        public void TheSameFamilyFromTheSameManagerIsEqual()
        {
            using ZddManager manager = new ZddManager(3);

            Zdd left = manager.Singleton(1);
            Zdd right = manager.Singleton(1);

            Assert.True(left.Equals(right));
            Assert.True(left == right);
            Assert.False(left != right);
            Assert.Equal(left.GetHashCode(), right.GetHashCode());
        }

        [Fact]
        public void DifferentFamiliesFromTheSameManagerAreNotEqual()
        {
            using ZddManager manager = new ZddManager(3);

            Zdd left = manager.Singleton(0);
            Zdd right = manager.Singleton(1);

            Assert.False(left.Equals(right));
            Assert.False(left == right);
            Assert.True(left != right);
        }

        [Fact]
        public void TheSameFamilyFromDifferentManagersIsNotEqual()
        {
            using ZddManager first = new ZddManager(3);
            using ZddManager second = new ZddManager(3);

            // ノード ID は同じでも、意味を持つのは作ったマネージャの中だけ。
            Assert.Equal(first.Singleton(1).Id, second.Singleton(1).Id);
            Assert.NotEqual(first.Singleton(1), second.Singleton(1));
            Assert.NotEqual(first.Empty, second.Empty);
            Assert.NotEqual(first.Base, second.Base);
        }

        [Fact]
        public void EqualsAgreesWithTheBoxedComparison()
        {
            using ZddManager manager = new ZddManager(3);

            Zdd single = manager.Singleton(1);

            Assert.True(single.Equals((object)manager.Singleton(1)));
            Assert.False(single.Equals((object)manager.Singleton(2)));
            Assert.False(single.Equals(null));
            Assert.False(single.Equals("not a Zdd"));
        }

        [Fact]
        public void HandlesWorkAsDictionaryKeys()
        {
            using ZddManager manager = new ZddManager(3);

            Dictionary<Zdd, string> map = new Dictionary<Zdd, string>
            {
                [manager.Empty] = "empty",
                [manager.Base] = "base",
                [manager.Singleton(0)] = "single",
            };

            Assert.Equal("empty", map[manager.Empty]);
            Assert.Equal("base", map[manager.Base]);
            Assert.Equal("single", map[manager.Singleton(0)]);
            Assert.False(map.ContainsKey(manager.Singleton(1)));
        }

        // ---- default(Zdd) ----

        [Fact]
        public void ADefaultHandleIsRecognisableAndComparable()
        {
            Zdd first = default;
            Zdd second = default;

            Assert.True(first.IsDefault);
            Assert.Equal(first, second);
            Assert.Equal(first.GetHashCode(), second.GetHashCode());
            Assert.Equal("Zdd(default)", first.ToString());
        }

        [Fact]
        public void ADefaultHandleIsNotAnyRealFamily()
        {
            using ZddManager manager = new ZddManager(3);

            Assert.NotEqual(default, manager.Empty);
            Assert.False(manager.Empty.IsDefault);
        }

        [Fact]
        public void UsingADefaultHandleAsAFamilyThrows()
        {
            Zdd handle = default;

            Assert.Throws<InvalidOperationException>(() => handle.Manager);
            Assert.Throws<InvalidOperationException>(() => handle.IsEmpty);
            Assert.Throws<InvalidOperationException>(() => handle.IsBase);
            Assert.Throws<InvalidOperationException>(() => handle.NodeCount);
            Assert.Throws<InvalidOperationException>(() => handle.Support());
        }

        // ---- ToString ----

        [Fact]
        public void ToStringNamesTheTerminals()
        {
            using ZddManager manager = new ZddManager(3);

            Assert.Equal("Zdd(empty)", manager.Empty.ToString());
            Assert.Equal("Zdd(base)", manager.Base.ToString());
            Assert.Equal($"Zdd(#{manager.Singleton(0).Id})", manager.Singleton(0).ToString());
        }
    }
}
