using System;
using System.Buffers.Binary;
using System.Diagnostics;
using ZDD.Net.Internal;

namespace ZDD.Net.Frontier
{
    /// <summary>
    /// How an <see cref="IArrayDdSpec"/> state is packed into bytes: <c>value - Bias</c> in
    /// <see cref="BytesPerSlot"/> bytes per slot, widened whenever a value no longer fits.
    /// </summary>
    /// <remarks>
    /// A mate/comp slot only ever holds a frontier slot number or a small sentinel, so one byte per
    /// slot is usually enough where v0.2 spent four. One instance is shared by every level of a
    /// build, so a widening learned at one level is not re-learned at the next; the tables snapshot
    /// it and re-encode their entries when <see cref="Version"/> moves (docs/PLAN.md §2, M3-2). The
    /// window only ever grows, which is what lets a table re-encode without re-reading its states.
    /// </remarks>
    internal sealed class PackedStateLayout
    {
        /// <summary>
        /// Where the initial one-byte window starts: low enough for the small negative sentinels a
        /// mate/comp array uses (<c>-1</c>, <c>-2</c>, ...) without giving up much of the byte, so
        /// slot numbers up to 247 still cost one byte each.
        /// </summary>
        private const int InitialBias = -8;

        /// <summary>Creates the narrowest layout: one byte per slot, holding <c>-8 .. 247</c>.</summary>
        public PackedStateLayout()
        {
            BytesPerSlot = 1;
            Bias = InitialBias;
        }

        /// <summary>Bytes each slot occupies: 1, 2 or 4.</summary>
        public int BytesPerSlot { get; private set; }

        /// <summary>The value stored as zero; always 0 once <see cref="BytesPerSlot"/> reaches 4.</summary>
        public int Bias { get; private set; }

        /// <summary>Incremented by every <see cref="Extend"/>; tells a table its entries are stale.</summary>
        public int Version { get; private set; }

        /// <summary>The number of bytes <paramref name="arrayLength"/> slots take under this layout.</summary>
        /// <param name="arrayLength">The spec's <see cref="IArrayDdSpec.ArrayLength"/>.</param>
        /// <exception cref="InvalidOperationException">The stride does not fit in an <see cref="int"/>.</exception>
        public int StrideFor(int arrayLength)
        {
            long stride = (long)arrayLength * BytesPerSlot;

            if (stride > Array.MaxLength)
            {
                ThrowHelper.ThrowInvalidOperationException(
                    $"A state of {arrayLength} slot(s) needs {stride} byte(s) at {BytesPerSlot} byte(s) per " +
                    $"slot, which exceeds the largest array .NET can allocate ({Array.MaxLength}).");
            }

            return (int)stride;
        }

        /// <summary>Packs <paramref name="state"/> under the current layout.</summary>
        /// <param name="state">The state to pack.</param>
        /// <param name="destination">Receives <c>state.Length * BytesPerSlot</c> bytes; partially written on failure.</param>
        /// <returns><see langword="false"/> if a value falls outside the layout's window; call <see cref="Extend"/> and retry.</returns>
        public bool TryPack(ReadOnlySpan<int> state, Span<byte> destination) =>
            TryPack(state, destination, Bias, BytesPerSlot);

        /// <summary>Widens the layout until <paramref name="state"/> fits, bumping <see cref="Version"/>.</summary>
        /// <param name="state">The state the pack attempt rejected.</param>
        /// <remarks>
        /// The new window covers the old one as well as <paramref name="state"/>: states packed
        /// elsewhere are only known to lie inside the old window, and they must stay packable. That
        /// is also why the window never narrows — nothing here knows what is already stored.
        /// </remarks>
        public void Extend(ReadOnlySpan<int> state)
        {
            long min = Bias;
            long max = Bias + MaxEncodable(BytesPerSlot);

            for (int i = 0; i < state.Length; i++)
            {
                int value = state[i];

                if (value < min)
                {
                    min = value;
                }
                else if (value > max)
                {
                    max = value;
                }
            }

            // Covering the old window alone already needs the width it has, so every widening moves
            // up a size: one byte holds a span of 256, two hold 65536, and four hold every int. A
            // layout therefore re-encodes at most twice over a whole build.
            long span = max - min;
            int bytesPerSlot = span <= byte.MaxValue ? 1 : span <= ushort.MaxValue ? 2 : 4;

            Debug.Assert(bytesPerSlot > BytesPerSlot, "A widening must take a larger slot than the one that did not fit.");
            BytesPerSlot = bytesPerSlot;

            // Four bytes hold every int as its own two's complement, so the bias is not needed there
            // and dropping it keeps the widest layout free of any further re-encoding.
            Bias = bytesPerSlot == 4 ? 0 : (int)min;
            Version++;
        }

        /// <summary>The largest value <paramref name="bytesPerSlot"/> bytes can hold above the bias.</summary>
        private static long MaxEncodable(int bytesPerSlot) => bytesPerSlot switch
        {
            1 => byte.MaxValue,
            2 => ushort.MaxValue,
            _ => uint.MaxValue,
        };

        /// <summary>Packs <paramref name="state"/> under an explicit layout, for re-encoding.</summary>
        /// <param name="state">The state to pack.</param>
        /// <param name="destination">Receives <c>state.Length * bytesPerSlot</c> bytes.</param>
        /// <param name="bias">The layout's bias.</param>
        /// <param name="bytesPerSlot">The layout's slot width: 1, 2 or 4.</param>
        /// <returns><see langword="false"/> if a value falls outside the window.</returns>
        public static bool TryPack(ReadOnlySpan<int> state, Span<byte> destination, int bias, int bytesPerSlot)
        {
            Debug.Assert(destination.Length == state.Length * bytesPerSlot, "The destination must be exactly one packed state.");

            switch (bytesPerSlot)
            {
                case 1:
                {
                    // The subtraction may wrap, but only into a value far above the window's top:
                    // the true difference cannot reach 2^32, so a wrapped one never passes the test.
                    Span<byte> target = destination.Slice(0, state.Length);

                    for (int i = 0; i < target.Length; i++)
                    {
                        uint encoded = (uint)(state[i] - bias);
                        if (encoded > byte.MaxValue)
                        {
                            return false;
                        }

                        target[i] = (byte)encoded;
                    }

                    return true;
                }

                case 2:
                    for (int i = 0; i < state.Length; i++)
                    {
                        uint encoded = (uint)(state[i] - bias);
                        if (encoded > ushort.MaxValue)
                        {
                            return false;
                        }

                        BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(i * 2, 2), (ushort)encoded);
                    }

                    return true;

                default:
                    Debug.Assert(bytesPerSlot == 4 && bias == 0, "The widest layout stores raw int values.");

                    for (int i = 0; i < state.Length; i++)
                    {
                        BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(i * 4, 4), (uint)state[i]);
                    }

                    return true;
            }
        }

        /// <summary>Unpacks one state written under an explicit layout.</summary>
        /// <param name="packed">Exactly one packed state: <c>state.Length * bytesPerSlot</c> bytes.</param>
        /// <param name="state">Receives the slot values.</param>
        /// <param name="bias">The layout's bias.</param>
        /// <param name="bytesPerSlot">The layout's slot width: 1, 2 or 4.</param>
        public static void Unpack(ReadOnlySpan<byte> packed, Span<int> state, int bias, int bytesPerSlot)
        {
            Debug.Assert(packed.Length == state.Length * bytesPerSlot, "The source must be exactly one packed state.");

            switch (bytesPerSlot)
            {
                case 1:
                {
                    ReadOnlySpan<byte> source = packed.Slice(0, state.Length);

                    for (int i = 0; i < source.Length; i++)
                    {
                        state[i] = bias + source[i];
                    }

                    break;
                }

                case 2:
                    for (int i = 0; i < state.Length; i++)
                    {
                        state[i] = bias + BinaryPrimitives.ReadUInt16LittleEndian(packed.Slice(i * 2, 2));
                    }

                    break;

                default:
                    Debug.Assert(bytesPerSlot == 4 && bias == 0, "The widest layout stores raw int values.");

                    for (int i = 0; i < state.Length; i++)
                    {
                        state[i] = (int)BinaryPrimitives.ReadUInt32LittleEndian(packed.Slice(i * 4, 4));
                    }

                    break;
            }
        }
    }
}
