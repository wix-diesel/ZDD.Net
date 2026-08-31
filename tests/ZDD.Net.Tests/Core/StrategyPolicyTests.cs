using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Xunit;
using ZDD.Net.Core;

namespace ZDD.Net.Tests.Core
{
    /// <summary>
    /// 戦略インタフェース（<see cref="IDdEval{TValue}"/> / <see cref="IWeightOps{TWeight}"/>）が
    /// <b>interface 型として受け渡されていない</b>ことを機械的に確かめる。
    /// </summary>
    /// <remarks>
    /// <para>
    /// この 2 つは「型引数 ＋ <c>struct</c> 制約で受ける」と決めてある（docs/PLAN.md §10-2）。
    /// interface 型で受けると、ノード 1 個ごとに走る <c>EvalNode</c> / <c>Add</c> / <c>Compare</c> が
    /// 仮想呼び出しになり、同じコードが数倍遅くなる。ボックス化も起きる。
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
        };

        [Fact]
        public void NoMemberOfTheLibraryTakesAStrategyAsAnInterface()
        {
            List<string> offenders = new List<string>();

            foreach (Type type in typeof(Zdd).Assembly.GetTypes())
            {
                foreach (MethodBase method in Members(type))
                {
                    foreach (ParameterInfo parameter in method.GetParameters())
                    {
                        if (IsStrategyInterface(parameter.ParameterType))
                        {
                            offenders.Add($"{type.Name}.{method.Name}({parameter.Name})");
                        }
                    }

                    if (method is MethodInfo function && IsStrategyInterface(function.ReturnType))
                    {
                        offenders.Add($"{type.Name}.{method.Name}() -> {function.ReturnType.Name}");
                    }
                }

                foreach (FieldInfo field in type.GetFields(
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static))
                {
                    if (IsStrategyInterface(field.FieldType))
                    {
                        offenders.Add($"{type.Name}.{field.Name}");
                    }
                }
            }

            Assert.True(
                offenders.Count == 0,
                "Strategies must be taken as a type parameter with a struct constraint, but these take them " +
                $"as an interface: {string.Join(", ", offenders)}.");
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

        private static bool IsStrategyInterface(Type type)
        {
            Type target = type.IsByRef ? type.GetElementType()! : type;

            return target.IsGenericType
                && StrategyInterfaces.Contains(target.GetGenericTypeDefinition());
        }
    }
}
