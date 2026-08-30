using Xunit;
using Xunit.Abstractions;
using ZDD.Net.Tests.Properties.Harness;

namespace ZDD.Net.Tests.Properties.Properties
{
    /// <summary>
    /// 単項演算（<c>Change</c> / <c>OnSet</c> / <c>OffSet</c>）の法則。
    /// </summary>
    /// <remarks>
    /// <c>OnSet</c> と <c>OffSet</c> は族を item の有無で 2 つに割る。割った両側から元の族を
    /// 組み直せることが要（ZDD の枝分かれそのもので、ここが狂うと全演算が狂う）。
    /// </remarks>
    public class UnaryOperationProperties
    {
        private readonly ITestOutputHelper _output;

        public UnaryOperationProperties(ITestOutputHelper output) => _output = output;

        [Fact]
        public void ChangeIsAnInvolution() =>
            FamilyLaw.WithItem(
                "f.Change(i).Change(i) == f",
                (manager, f, item) => (f.Change(item).Change(item), f),
                _output);

        [Fact]
        public void OnSetAndOffSetRebuildTheFamily() =>
            FamilyLaw.WithItem(
                "f == f.OffSet(i) | f.OnSet(i) * {{i}}",
                (manager, f, item) => (f, f.OffSet(item) | (f.OnSet(item) * manager.Singleton(item))),
                _output);

        [Fact]
        public void OnSetDropsTheItem() =>
            FamilyLaw.WithItem(
                "f.OnSet(i).OnSet(i) == ∅",
                (manager, f, item) => (f.OnSet(item).OnSet(item), manager.Empty),
                _output);

        [Fact]
        public void OffSetDropsTheItem() =>
            FamilyLaw.WithItem(
                "f.OffSet(i).OnSet(i) == ∅",
                (manager, f, item) => (f.OffSet(item).OnSet(item), manager.Empty),
                _output);

        [Fact]
        public void OffSetKeepsWhatItAlreadyHas() =>
            FamilyLaw.WithItem(
                "f.OffSet(i).OffSet(i) == f.OffSet(i)",
                (manager, f, item) => (f.OffSet(item).OffSet(item), f.OffSet(item)),
                _output);

        [Fact]
        public void ChangeIsTheProductWithASingletonOnTheOffSetPart() =>
            FamilyLaw.WithItem(
                "f.Change(i) == f.OffSet(i) * {{i}} | f.OnSet(i)",
                (manager, f, item) => (f.Change(item), (f.OffSet(item) * manager.Singleton(item)) | f.OnSet(item)),
                _output);

        [Fact]
        public void FlippingTheSameItemTwiceChangesNothing() =>
            FamilyLaw.WithItem(
                "f.Flip(i, i) == f",
                (manager, f, item) => (f.Flip(item, item), f),
                _output);

        [Fact]
        public void FlipIsChangeForASingleItem() =>
            FamilyLaw.WithItem(
                "f.Flip(i) == f.Change(i)",
                (manager, f, item) => (f.Flip(item), f.Change(item)),
                _output);
    }
}
