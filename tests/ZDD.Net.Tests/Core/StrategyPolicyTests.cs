using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Xunit;
using ZDD.Net.Core;
using ZDD.Net.Frontier;

namespace ZDD.Net.Tests.Core
{
    /// <summary>
    /// 戦略インタフェース（<see cref="IDdEval{TValue}"/> / <see cref="IWeightOps{TWeight}"/> /
    /// <see cref="IDdSpec{TState}"/> ほかのスペック）が
    /// <b>interface 型として受け渡されていない</b>ことを機械的に確かめる。
    /// </summary>
    /// <remarks>
    /// <para>
    /// これらは「型引数 ＋ <c>struct</c> 制約で受ける」と決めてある
    /// （docs/PLAN.md §10-2、スペックは docs/ROADMAP.md のレビュー観点）。
    /// interface 型で受けると、ノードや状態 1 個ごとに走る <c>EvalNode</c> / <c>Add</c> / <c>Compare</c> /
    /// <c>GetChild</c> が仮想呼び出しになり、同じコードが数倍遅くなる。ボックス化も起きる。
    /// </para>
    /// <para>
    /// <b>約束は約束のままでは守られない</b>ので、テストにしておく。公開 API の署名は
    /// <c>where T : struct, IDdEval&lt;…&gt;</c> の制約でコンパイル時に守られるが、
    /// 内部でいったん interface 型の変数に受け直す書き方は素通りしてしまう。
    /// ここではメソッドの引数・戻り値・フィールドの型を総当たりで見る。
    /// </para>
    /// </remarks>
    public class StrategyPolicyTests
    {
        /// <summary>interface 型のまま受け渡してはならない型。</summary>
        private static readonly Type[] StrategyInterfaces =
        {
            typeof(IDdEval<>),
            typeof(IWeightOps<>),
            typeof(IDdSpec<>),
            typeof(IArrayDdSpec),
            typeof(IHybridDdSpec<>),
        };

        [Fact]
        public void NoMemberOfTheLibraryTakesAStrategyAsAnInterface()
        {
            string[] offenders = typeof(Zdd).Assembly.GetTypes().SelectMany(OffendersIn).ToArray();

            Assert.True(
                offenders.Length == 0,
                "Strategies must be taken as a type parameter with a struct constraint, but these take them " +
                $"as an interface: {string.Join(", ", offenders)}.");
        }

        /// <summary>
        /// 検査自身が<b>入れ子の型</b>を見落とさないことを確かめる（テストのテスト）。
        /// </summary>
        /// <remarks>
        /// 戦略を <c>IEnumerable&lt;IDdEval&lt;…&gt;&gt;</c> や <c>Func&lt;…, IWeightOps&lt;…&gt;&gt;</c>、
        /// 配列に包んで渡しても、interface 型で受け渡していることに変わりはない。
        /// 素通ししていると上の 2 つが「違反ゼロ」を静かに報告し続けるので、
        /// わざと違反した型を 1 つ置いて、検査がそれを捕まえることを確かめる。
        /// </remarks>
        [Fact]
        public void TheCheckSeesStrategiesNestedInsideOtherTypes()
        {
            string[] offenders = OffendersIn(typeof(NestedStrategyUser)).ToArray();

            Assert.Equal(
                new[]
                {
                    $"{nameof(NestedStrategyUser)}.{nameof(NestedStrategyUser.Consume)}(evaluators)",
                    $"{nameof(NestedStrategyUser)}.{nameof(NestedStrategyUser.Factory)}() -> Func`2",
                    $"{nameof(NestedStrategyUser)}.{nameof(NestedStrategyUser.Evaluators)}",
                    $"{nameof(NestedStrategyUser)}.{nameof(NestedStrategyUser.ArraySpec)}",
                },
                offenders);
        }

        [Fact]
        public void EveryPublicStrategyParameterIsConstrainedToAStruct()
        {
            List<string> offenders = new List<string>();

            foreach (Type type in typeof(Zdd).Assembly.GetTypes())
            {
                foreach (MethodBase method in Members(type))
                {
                    if (!method.IsGenericMethodDefinition)
                    {
                        continue;
                    }

                    foreach (Type argument in method.GetGenericArguments())
                    {
                        bool isStrategy = argument.GetGenericParameterConstraints().Any(IsStrategyInterface);

                        if (!isStrategy)
                        {
                            continue;
                        }

                        bool isStruct = argument.GenericParameterAttributes
                            .HasFlag(GenericParameterAttributes.NotNullableValueTypeConstraint);

                        if (!isStruct)
                        {
                            offenders.Add($"{type.Name}.{method.Name}<{argument.Name}>");
                        }
                    }
                }
            }

            Assert.True(
                offenders.Count == 0,
                $"These type parameters carry a strategy constraint without 'struct': {string.Join(", ", offenders)}.");
        }

        /// <summary>
        /// 型そのものが戦略を型引数で持つ場合（状態表など）も、<c>struct</c> 制約が付いていること。
        /// </summary>
        /// <remarks>
        /// メソッドの型引数だけ見ていると、<c>class StructLevelStateTable&lt;TSpec, TState&gt;</c> のように
        /// 型のほうで戦略を受ける実装が素通りする。フィールドに置いた戦略は
        /// 状態 1 個ごとに呼ばれるので、ここが仮想呼び出しになると影響はいちばん大きい。
        /// </remarks>
        [Fact]
        public void EveryStrategyTypeParameterOfATypeIsConstrainedToAStruct()
        {
            List<string> offenders = new List<string>();
            int checkedParameters = 0;

            foreach (Type type in typeof(Zdd).Assembly.GetTypes())
            {
                if (!type.IsGenericTypeDefinition)
                {
                    continue;
                }

                foreach (Type argument in type.GetGenericArguments())
                {
                    if (!argument.GetGenericParameterConstraints().Any(IsStrategyInterface))
                    {
                        continue;
                    }

                    checkedParameters++;

                    bool isStruct = argument.GenericParameterAttributes
                        .HasFlag(GenericParameterAttributes.NotNullableValueTypeConstraint);

                    if (!isStruct)
                    {
                        offenders.Add($"{type.Name}<{argument.Name}>");
                    }
                }
            }

            Assert.True(
                offenders.Count == 0,
                $"These type parameters carry a strategy constraint without 'struct': {string.Join(", ", offenders)}.");

            // 検査対象が 1 つも無ければ、この検査は何も守っていない。
            Assert.True(checkedParameters > 0, "No type takes a strategy as a type parameter, so this check proves nothing.");
        }

        /// <summary>そもそも戦略を型引数で受ける API が実在することを確かめる（上の 2 つが空振りしないように）。</summary>
        [Fact]
        public void TheWeightApisAreGenericOverTheStrategy()
        {
            MethodInfo[] methods = typeof(Zdd).GetMethods()
                .Where(method => method.IsGenericMethodDefinition)
                .Where(method => method.GetGenericArguments()
                    .Any(argument => argument.GetGenericParameterConstraints().Any(IsStrategyInterface)))
                .ToArray();

            Assert.Contains(methods, method => method.Name == nameof(Zdd.MaxWeight));
            Assert.Contains(methods, method => method.Name == nameof(Zdd.MinWeight));
            Assert.Contains(methods, method => method.Name == nameof(Zdd.TopK));
        }

        private static IEnumerable<MethodBase> Members(Type type)
        {
            const BindingFlags Flags =
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static
                | BindingFlags.DeclaredOnly;

            return type.GetMethods(Flags).Cast<MethodBase>().Concat(type.GetConstructors(Flags));
        }

        /// <summary>
        /// 戦略インタフェースを、包まれている場合も含めて見つける。
        /// </summary>
        /// <remarks>
        /// <c>ref</c> / 配列 / ポインタは中身を見て、ジェネリックな型はその型引数を再帰的に見る。
        /// <c>IDdEval&lt;T&gt;</c> そのものだけを見ていると、<c>IEnumerable&lt;IDdEval&lt;T&gt;&gt;</c> や
        /// <c>Func&lt;…, IWeightOps&lt;T&gt;&gt;</c> のように包んで渡す抜け道が残る。
        /// 型引数の入れ子は有限の木なので、再帰は必ず止まる。
        /// </remarks>
        private static bool IsStrategyInterface(Type type)
        {
            if (type.IsByRef || type.IsArray || type.IsPointer)
            {
                return IsStrategyInterface(type.GetElementType()!);
            }

            if (!type.IsGenericType)
            {
                // IArrayDdSpec のようにジェネリックでない戦略もあるので、素の型でも照合する。
                return StrategyInterfaces.Contains(type);
            }

            return StrategyInterfaces.Contains(type.GetGenericTypeDefinition())
                || type.GetGenericArguments().Any(IsStrategyInterface);
        }

        /// <summary>1 つの型の中で、戦略を interface 型のまま受け渡している箇所を挙げる。</summary>
        private static IEnumerable<string> OffendersIn(Type type)
        {
            foreach (MethodBase method in Members(type))
            {
                foreach (ParameterInfo parameter in method.GetParameters())
                {
                    if (IsStrategyInterface(parameter.ParameterType))
                    {
                        yield return $"{type.Name}.{method.Name}({parameter.Name})";
                    }
                }

                if (method is MethodInfo function && IsStrategyInterface(function.ReturnType))
                {
                    yield return $"{type.Name}.{method.Name}() -> {function.ReturnType.Name}";
                }
            }

            foreach (FieldInfo field in type.GetFields(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static))
            {
                if (IsStrategyInterface(field.FieldType))
                {
                    yield return $"{type.Name}.{field.Name}";
                }
            }
        }

        /// <summary>
        /// わざと戦略を包んで受け渡している型。<see cref="TheCheckSeesStrategiesNestedInsideOtherTypes"/> 専用で、
        /// 本体（<c>src/ZDD.Net</c>）には置かない。
        /// </summary>
        private sealed class NestedStrategyUser
        {
            /// <summary>配列に包んだ戦略。</summary>
            public IDdEval<int>[]? Evaluators = null;

            /// <summary>ジェネリックでない戦略（型引数に包まれていないので、素の照合で捕まえる）。</summary>
            public IArrayDdSpec? ArraySpec = null;

            /// <summary>コレクションに包んだ戦略。</summary>
            public static void Consume(IEnumerable<IDdEval<int>> evaluators) => _ = evaluators;

            /// <summary>デリゲートの戻り値に包んだ戦略。</summary>
            /// <remarks>
            /// ここが <see cref="IDdEval{TValue}"/> なのは、<see cref="IWeightOps{TWeight}"/> は
            /// <c>static abstract</c> メンバを持つため<b>そもそも型引数にできない</b>（CS8920）から。
            /// 包んで渡す抜け道が残るのは <see cref="IDdEval{TValue}"/> の側だけである。
            /// </remarks>
            public static Func<int, IDdEval<long>>? Factory() => null;
        }
    }
}
