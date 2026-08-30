using System;
using System.Collections.Generic;
using System.Globalization;
using Xunit.Abstractions;

namespace ZDD.Net.Tests.Properties.Harness
{
    /// <summary>
    /// テスト出力を控えておく <see cref="ITestOutputHelper"/>。
    /// 「失敗時に種がログへ出る」ことを検査するために要る。
    /// </summary>
    internal sealed class RecordingOutput : ITestOutputHelper
    {
        private readonly List<string> _lines = new List<string>();

        /// <summary>書き出された行をすべてつないだもの。</summary>
        public string Text => string.Join(Environment.NewLine, _lines);

        public void WriteLine(string message) => _lines.Add(message);

        public void WriteLine(string format, params object[] args) =>
            _lines.Add(string.Format(CultureInfo.InvariantCulture, format, args));
    }
}
