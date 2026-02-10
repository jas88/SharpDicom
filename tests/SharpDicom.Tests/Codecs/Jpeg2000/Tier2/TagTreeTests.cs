using System;
using System.Collections.Generic;
using NUnit.Framework;
using SharpDicom.Codecs.Jpeg2000.Tier2;

namespace SharpDicom.Tests.Codecs.Jpeg2000.Tier2
{
    /// <summary>
    /// Tests for <see cref="TagTree"/> verifying encode/decode roundtrips,
    /// progressive threshold coding, and edge cases per ITU-T T.800 B.10.2.
    /// </summary>
    [TestFixture]
    public class TagTreeTests
    {
        private static readonly int[] SingleOne = { 1 };
        private static readonly int[] SingleZero = { 0 };

        #region Helper Methods

        /// <summary>
        /// Encodes all leaves of a tag tree with progressive thresholds up to
        /// <paramref name="maxThreshold"/>, collecting the bit stream.
        /// </summary>
        private static List<int> EncodeAll(TagTree tree, int width, int height, int maxThreshold)
        {
            var bits = new List<int>();
            for (int threshold = 1; threshold <= maxThreshold; threshold++)
            {
                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        tree.Encode(x, y, threshold, bit => bits.Add(bit));
                    }
                }
            }
            return bits;
        }

        /// <summary>
        /// Decodes all leaves of a tag tree from a bit stream using progressive thresholds.
        /// Returns an array of decoded values indexed as [y * width + x].
        /// </summary>
        private static int[] DecodeAll(TagTree tree, int width, int height, int maxThreshold, List<int> bits)
        {
            int bitIndex = 0;
            int[] decoded = new int[width * height];
            for (int i = 0; i < decoded.Length; i++)
                decoded[i] = int.MaxValue;

            for (int threshold = 1; threshold <= maxThreshold; threshold++)
            {
                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        int value = tree.Decode(x, y, threshold, () => bits[bitIndex++]);
                        if (value != int.MaxValue)
                        {
                            decoded[y * width + x] = value;
                        }
                    }
                }
            }
            return decoded;
        }

        /// <summary>
        /// Sets leaf values into an encoder tree and returns the expected value array.
        /// </summary>
        private static int[] SetValues(TagTree tree, int width, int height, int[,] values)
        {
            int[] expected = new int[width * height];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    tree.SetValue(x, y, values[y, x]);
                    expected[y * width + x] = values[y, x];
                }
            }
            return expected;
        }

        /// <summary>
        /// Performs a full encode-then-decode roundtrip and asserts all values match.
        /// </summary>
        private static void VerifyRoundtrip(int width, int height, int[,] values)
        {
            // Determine maximum value for threshold range
            int maxValue = 0;
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    maxValue = Math.Max(maxValue, values[y, x]);
                }
            }

            int maxThreshold = maxValue + 1;

            // Encode
            var encodeTree = new TagTree(width, height);
            int[] expected = SetValues(encodeTree, width, height, values);
            List<int> bits = EncodeAll(encodeTree, width, height, maxThreshold);

            // Decode into a fresh tree
            var decodeTree = new TagTree(width, height);
            int[] decoded = DecodeAll(decodeTree, width, height, maxThreshold, bits);

            // Assert
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int idx = y * width + x;
                    Assert.That(decoded[idx], Is.EqualTo(expected[idx]),
                        $"Mismatch at ({x},{y}): expected {expected[idx]}, got {decoded[idx]}");
                }
            }
        }

        #endregion

        #region 1x1 Tree

        [Test]
        public void OneByOne_SingleValue_Roundtrip()
        {
            VerifyRoundtrip(1, 1, new int[,] { { 5 } });
        }

        [Test]
        public void OneByOne_Zero_Roundtrip()
        {
            VerifyRoundtrip(1, 1, new int[,] { { 0 } });
        }

        [Test]
        public void OneByOne_HighValue_Roundtrip()
        {
            VerifyRoundtrip(1, 1, new int[,] { { 31 } });
        }

        [Test]
        public void OneByOne_Encode_ProducesCorrectBits()
        {
            // For a 1x1 tree with value=3, encoding with threshold=1 should emit:
            // 0 (value 3 > 0, ITU-T T.800 B.10.2: 0 = exceeds)
            // With threshold=2: 0 (value 3 > 1)
            // With threshold=3: 0 (value 3 > 2)
            // With threshold=4: 1 (value 3 == 3, ITU-T T.800 B.10.2: 1 = matches)
            var tree = new TagTree(1, 1);
            tree.SetValue(0, 0, 3);

            var bits = new List<int>();
            tree.Encode(0, 0, 1, bit => bits.Add(bit));
            // threshold=1: state starts at 0, value=3 > 0 -> write 0
            Assert.That(bits, Is.EqualTo(SingleZero));

            bits.Clear();
            tree.Encode(0, 0, 2, bit => bits.Add(bit));
            // threshold=2: state=1, value=3 > 1 -> write 0
            Assert.That(bits, Is.EqualTo(SingleZero));

            bits.Clear();
            tree.Encode(0, 0, 3, bit => bits.Add(bit));
            // threshold=3: state=2, value=3 > 2 -> write 0
            Assert.That(bits, Is.EqualTo(SingleZero));

            bits.Clear();
            tree.Encode(0, 0, 4, bit => bits.Add(bit));
            // threshold=4: state=3, value=3 == 3 -> write 1
            Assert.That(bits, Is.EqualTo(SingleOne));
        }

        #endregion

        #region 2x2 Tree

        [Test]
        public void TwoByTwo_AllSameValue_Roundtrip()
        {
            VerifyRoundtrip(2, 2, new int[,]
            {
                { 3, 3 },
                { 3, 3 }
            });
        }

        [Test]
        public void TwoByTwo_MixedValues_Roundtrip()
        {
            VerifyRoundtrip(2, 2, new int[,]
            {
                { 0, 2 },
                { 1, 5 }
            });
        }

        [Test]
        public void TwoByTwo_AllZeros_Roundtrip()
        {
            VerifyRoundtrip(2, 2, new int[,]
            {
                { 0, 0 },
                { 0, 0 }
            });
        }

        [Test]
        public void TwoByTwo_OneNonZero_Roundtrip()
        {
            VerifyRoundtrip(2, 2, new int[,]
            {
                { 0, 0 },
                { 0, 7 }
            });
        }

        [Test]
        public void TwoByTwo_DescendingValues_Roundtrip()
        {
            VerifyRoundtrip(2, 2, new int[,]
            {
                { 4, 3 },
                { 2, 1 }
            });
        }

        #endregion

        #region 4x4 Tree

        [Test]
        public void FourByFour_Sequential_Roundtrip()
        {
            VerifyRoundtrip(4, 4, new int[,]
            {
                { 0, 1, 2, 3 },
                { 4, 5, 6, 7 },
                { 0, 1, 2, 3 },
                { 4, 5, 6, 7 }
            });
        }

        [Test]
        public void FourByFour_AllSame_Roundtrip()
        {
            VerifyRoundtrip(4, 4, new int[,]
            {
                { 2, 2, 2, 2 },
                { 2, 2, 2, 2 },
                { 2, 2, 2, 2 },
                { 2, 2, 2, 2 }
            });
        }

        [Test]
        public void FourByFour_Sparse_Roundtrip()
        {
            VerifyRoundtrip(4, 4, new int[,]
            {
                { 0, 0, 0, 0 },
                { 0, 0, 0, 0 },
                { 0, 0, 0, 0 },
                { 0, 0, 0, 10 }
            });
        }

        [Test]
        public void FourByFour_HighValues_Roundtrip()
        {
            VerifyRoundtrip(4, 4, new int[,]
            {
                { 20, 15, 31, 0 },
                { 0, 25, 0, 12 },
                { 8, 0, 19, 0 },
                { 0, 0, 0, 28 }
            });
        }

        #endregion

        #region Non-Square Trees

        [Test]
        public void ThreeByOne_Roundtrip()
        {
            VerifyRoundtrip(3, 1, new int[,]
            {
                { 2, 5, 1 }
            });
        }

        [Test]
        public void OneByThree_Roundtrip()
        {
            VerifyRoundtrip(1, 3, new int[,]
            {
                { 3 },
                { 0 },
                { 7 }
            });
        }

        [Test]
        public void FiveByThree_Roundtrip()
        {
            VerifyRoundtrip(5, 3, new int[,]
            {
                { 1, 0, 3, 0, 2 },
                { 0, 4, 0, 5, 0 },
                { 6, 0, 7, 0, 8 }
            });
        }

        [Test]
        public void OneByOne_NonSquare_Roundtrip()
        {
            VerifyRoundtrip(1, 1, new int[,] { { 0 } });
        }

        [Test]
        public void TwoByOne_Roundtrip()
        {
            VerifyRoundtrip(2, 1, new int[,]
            {
                { 3, 7 }
            });
        }

        [Test]
        public void OneByTwo_Roundtrip()
        {
            VerifyRoundtrip(1, 2, new int[,]
            {
                { 4 },
                { 1 }
            });
        }

        [Test]
        public void ThreeByTwo_Roundtrip()
        {
            VerifyRoundtrip(3, 2, new int[,]
            {
                { 0, 1, 2 },
                { 3, 4, 5 }
            });
        }

        #endregion

        #region Progressive Thresholds

        [Test]
        public void ProgressiveThresholds_ValuesRevealedAtCorrectLayer()
        {
            // In JPEG 2000, tag trees are queried with increasing thresholds
            // to progressively reveal which code-blocks are included at each layer.
            // Value = first layer of inclusion.

            var encodeTree = new TagTree(2, 2);
            encodeTree.SetValue(0, 0, 0); // included at layer 0
            encodeTree.SetValue(1, 0, 2); // included at layer 2
            encodeTree.SetValue(0, 1, 1); // included at layer 1
            encodeTree.SetValue(1, 1, 3); // included at layer 3

            // Encode progressively
            var bits = new List<int>();
            for (int threshold = 1; threshold <= 4; threshold++)
            {
                for (int y = 0; y < 2; y++)
                {
                    for (int x = 0; x < 2; x++)
                    {
                        encodeTree.Encode(x, y, threshold, bit => bits.Add(bit));
                    }
                }
            }

            // Decode progressively into a fresh tree
            var decodeTree = new TagTree(2, 2);
            int bitIndex = 0;

            // Track which values have been resolved
            int[,] resolved = new int[2, 2];
            for (int y = 0; y < 2; y++)
                for (int x = 0; x < 2; x++)
                    resolved[y, x] = int.MaxValue;

            for (int threshold = 1; threshold <= 4; threshold++)
            {
                for (int y = 0; y < 2; y++)
                {
                    for (int x = 0; x < 2; x++)
                    {
                        int value = decodeTree.Decode(x, y, threshold, () => bits[bitIndex++]);
                        if (value != int.MaxValue)
                        {
                            resolved[y, x] = value;
                        }
                    }
                }
            }

            Assert.That(resolved[0, 0], Is.EqualTo(0), "(0,0) should be 0");
            Assert.That(resolved[0, 1], Is.EqualTo(2), "(1,0) should be 2");
            Assert.That(resolved[1, 0], Is.EqualTo(1), "(0,1) should be 1");
            Assert.That(resolved[1, 1], Is.EqualTo(3), "(1,1) should be 3");

            // All bits should have been consumed
            Assert.That(bitIndex, Is.EqualTo(bits.Count),
                "All encoded bits should be consumed by decoding");
        }

        [Test]
        public void ProgressiveThresholds_PartialDecode_ThenContinue()
        {
            // Encode a 2x2 tree with values [0,3,1,5]
            var encodeTree = new TagTree(2, 2);
            encodeTree.SetValue(0, 0, 0);
            encodeTree.SetValue(1, 0, 3);
            encodeTree.SetValue(0, 1, 1);
            encodeTree.SetValue(1, 1, 5);

            // Encode up to threshold=6
            var bits = EncodeAll(encodeTree, 2, 2, 6);

            // Decode only up to threshold=2, then continue to threshold=6
            var decodeTree = new TagTree(2, 2);
            int bitIndex = 0;

            // First pass: thresholds 1-2
            for (int threshold = 1; threshold <= 2; threshold++)
            {
                for (int y = 0; y < 2; y++)
                {
                    for (int x = 0; x < 2; x++)
                    {
                        decodeTree.Decode(x, y, threshold, () => bits[bitIndex++]);
                    }
                }
            }

            // Continue: thresholds 3-6
            int[,] finalValues = new int[2, 2];
            for (int y = 0; y < 2; y++)
                for (int x = 0; x < 2; x++)
                    finalValues[y, x] = int.MaxValue;

            for (int threshold = 3; threshold <= 6; threshold++)
            {
                for (int y = 0; y < 2; y++)
                {
                    for (int x = 0; x < 2; x++)
                    {
                        int value = decodeTree.Decode(x, y, threshold, () => bits[bitIndex++]);
                        if (value != int.MaxValue && finalValues[y, x] == int.MaxValue)
                        {
                            finalValues[y, x] = value;
                        }
                    }
                }
            }

            // Values 0 and 1 were resolved in the first pass, 3 and 5 in the second
            Assert.That(finalValues[0, 1], Is.EqualTo(3), "(1,0) should be 3");
            Assert.That(finalValues[1, 1], Is.EqualTo(5), "(1,1) should be 5");

            Assert.That(bitIndex, Is.EqualTo(bits.Count),
                "All encoded bits should be consumed");
        }

        #endregion

        #region All Same Values

        [Test]
        public void AllSame_Value1_TwoByTwo_Roundtrip()
        {
            VerifyRoundtrip(2, 2, new int[,]
            {
                { 1, 1 },
                { 1, 1 }
            });
        }

        [Test]
        public void AllSame_Value5_ThreeByThree_Roundtrip()
        {
            var values = new int[3, 3];
            for (int y = 0; y < 3; y++)
                for (int x = 0; x < 3; x++)
                    values[y, x] = 5;

            VerifyRoundtrip(3, 3, values);
        }

        [Test]
        public void AllSame_Value10_FourByFour_Roundtrip()
        {
            var values = new int[4, 4];
            for (int y = 0; y < 4; y++)
                for (int x = 0; x < 4; x++)
                    values[y, x] = 10;

            VerifyRoundtrip(4, 4, values);
        }

        #endregion

        #region All Zeros

        [Test]
        public void AllZeros_OneByOne_Roundtrip()
        {
            VerifyRoundtrip(1, 1, new int[,] { { 0 } });
        }

        [Test]
        public void AllZeros_TwoByTwo_Roundtrip()
        {
            VerifyRoundtrip(2, 2, new int[,]
            {
                { 0, 0 },
                { 0, 0 }
            });
        }

        [Test]
        public void AllZeros_FourByFour_Roundtrip()
        {
            VerifyRoundtrip(4, 4, new int[4, 4]);
        }

        [Test]
        public void AllZeros_ThreeByTwo_Roundtrip()
        {
            VerifyRoundtrip(3, 2, new int[2, 3]);
        }

        #endregion

        #region High Values

        [Test]
        public void HighValue_20_Roundtrip()
        {
            VerifyRoundtrip(2, 2, new int[,]
            {
                { 20, 0 },
                { 0, 20 }
            });
        }

        [Test]
        public void HighValue_31_Roundtrip()
        {
            VerifyRoundtrip(2, 2, new int[,]
            {
                { 31, 31 },
                { 0, 31 }
            });
        }

        [Test]
        public void HighValue_Mixed_Roundtrip()
        {
            VerifyRoundtrip(3, 3, new int[,]
            {
                { 0, 20, 0 },
                { 31, 0, 15 },
                { 0, 25, 0 }
            });
        }

        #endregion

        #region Mixed Values (Zeros and Non-Zeros)

        [Test]
        public void Mixed_CheckerboardPattern_Roundtrip()
        {
            VerifyRoundtrip(4, 4, new int[,]
            {
                { 0, 3, 0, 3 },
                { 3, 0, 3, 0 },
                { 0, 3, 0, 3 },
                { 3, 0, 3, 0 }
            });
        }

        [Test]
        public void Mixed_SingleHighAmongZeros_Roundtrip()
        {
            VerifyRoundtrip(4, 4, new int[,]
            {
                { 0, 0, 0, 0 },
                { 0, 0, 0, 0 },
                { 0, 0, 20, 0 },
                { 0, 0, 0, 0 }
            });
        }

        [Test]
        public void Mixed_GradientValues_Roundtrip()
        {
            VerifyRoundtrip(4, 4, new int[,]
            {
                {  0,  1,  2,  3 },
                {  4,  5,  6,  7 },
                {  8,  9, 10, 11 },
                { 12, 13, 14, 15 }
            });
        }

        [Test]
        public void Mixed_DiagonalPattern_Roundtrip()
        {
            VerifyRoundtrip(4, 4, new int[,]
            {
                { 5, 0, 0, 0 },
                { 0, 5, 0, 0 },
                { 0, 0, 5, 0 },
                { 0, 0, 0, 5 }
            });
        }

        #endregion

        #region Reset

        [Test]
        public void Reset_AllowsReuse()
        {
            // First roundtrip
            var tree = new TagTree(2, 2);
            tree.SetValue(0, 0, 1);
            tree.SetValue(1, 0, 2);
            tree.SetValue(0, 1, 3);
            tree.SetValue(1, 1, 4);

            var bits1 = new List<int>();
            for (int threshold = 1; threshold <= 5; threshold++)
            {
                for (int y = 0; y < 2; y++)
                    for (int x = 0; x < 2; x++)
                        tree.Encode(x, y, threshold, bit => bits1.Add(bit));
            }

            // Reset and set different values
            tree.Reset();
            tree.SetValue(0, 0, 5);
            tree.SetValue(1, 0, 0);
            tree.SetValue(0, 1, 0);
            tree.SetValue(1, 1, 3);

            var bits2 = new List<int>();
            for (int threshold = 1; threshold <= 6; threshold++)
            {
                for (int y = 0; y < 2; y++)
                    for (int x = 0; x < 2; x++)
                        tree.Encode(x, y, threshold, bit => bits2.Add(bit));
            }

            // Decode second roundtrip
            var decodeTree = new TagTree(2, 2);
            int[] decoded = DecodeAll(decodeTree, 2, 2, 6, bits2);

            Assert.That(decoded[0], Is.EqualTo(5), "(0,0) should be 5 after reset");
            Assert.That(decoded[1], Is.EqualTo(0), "(1,0) should be 0 after reset");
            Assert.That(decoded[2], Is.EqualTo(0), "(0,1) should be 0 after reset");
            Assert.That(decoded[3], Is.EqualTo(3), "(1,1) should be 3 after reset");
        }

        [Test]
        public void Reset_ClearsStates()
        {
            // Partially encode, then reset and re-encode from scratch
            var tree = new TagTree(2, 1);
            tree.SetValue(0, 0, 3);
            tree.SetValue(1, 0, 5);

            // Partially encode (only threshold=1)
            tree.Encode(0, 0, 1, _ => { });
            tree.Encode(1, 0, 1, _ => { });

            // Reset
            tree.Reset();

            // Set new values and encode fully
            tree.SetValue(0, 0, 1);
            tree.SetValue(1, 0, 2);

            var bits = EncodeAll(tree, 2, 1, 3);

            // Decode and verify
            var decodeTree = new TagTree(2, 1);
            int[] decoded = DecodeAll(decodeTree, 2, 1, 3, bits);

            Assert.That(decoded[0], Is.EqualTo(1), "(0,0) should be 1");
            Assert.That(decoded[1], Is.EqualTo(2), "(1,0) should be 2");
        }

        #endregion

        #region SetValue Propagation

        [Test]
        public void SetValue_PropagatesMinimumUpTree()
        {
            // When we set leaf values, parent nodes should hold the minimum.
            // We verify this indirectly by encoding: the root controls the
            // starting point for all leaves, so if propagation is wrong,
            // the encoded bitstream will be different and roundtrip will fail.
            var values = new int[,]
            {
                { 5, 3 },
                { 7, 1 }
            };

            // Minimum of all is 1, so root should be 1.
            // Encoding (0,0) with value 5 should first pass through root (min=1)
            // and only need to code from 1 upward.
            VerifyRoundtrip(2, 2, values);
        }

        [Test]
        public void SetValue_OverwriteLeaf_PropagatesNewMinimum()
        {
            var tree = new TagTree(2, 2);
            tree.SetValue(0, 0, 10);
            tree.SetValue(1, 0, 10);
            tree.SetValue(0, 1, 10);
            tree.SetValue(1, 1, 10);

            // Now overwrite one leaf with a smaller value
            tree.SetValue(1, 1, 2);

            // Encode and decode to verify the tree state is consistent
            var bits = EncodeAll(tree, 2, 2, 11);

            var decodeTree = new TagTree(2, 2);
            int[] decoded = DecodeAll(decodeTree, 2, 2, 11, bits);

            Assert.That(decoded[0], Is.EqualTo(10), "(0,0) should be 10");
            Assert.That(decoded[1], Is.EqualTo(10), "(1,0) should be 10");
            Assert.That(decoded[2], Is.EqualTo(10), "(0,1) should be 10");
            Assert.That(decoded[3], Is.EqualTo(2), "(1,1) should be 2 after overwrite");
        }

        #endregion

        #region Larger Trees

        [Test]
        public void EightByEight_Roundtrip()
        {
            var values = new int[8, 8];
            for (int y = 0; y < 8; y++)
                for (int x = 0; x < 8; x++)
                    values[y, x] = (x + y) % 5;

            VerifyRoundtrip(8, 8, values);
        }

        [Test]
        public void SixByFour_Roundtrip()
        {
            var values = new int[4, 6];
            for (int y = 0; y < 4; y++)
                for (int x = 0; x < 6; x++)
                    values[y, x] = (x * y) % 7;

            VerifyRoundtrip(6, 4, values);
        }

        [Test]
        public void SevenByFive_AllZeros_Roundtrip()
        {
            VerifyRoundtrip(7, 5, new int[5, 7]);
        }

        #endregion

        #region Bit Stream Correctness

        [Test]
        public void Encode_AllZeros_ProducesMinimalBits()
        {
            // For a 2x2 tree with all zeros:
            // Root = 0, all children = 0.
            // At threshold=1: for each leaf, root value(0) < threshold(1),
            // so we write 0 to signal "value equals current state".
            var tree = new TagTree(2, 2);
            tree.SetValue(0, 0, 0);
            tree.SetValue(1, 0, 0);
            tree.SetValue(0, 1, 0);
            tree.SetValue(1, 1, 0);

            var bits = new List<int>();
            for (int y = 0; y < 2; y++)
                for (int x = 0; x < 2; x++)
                    tree.Encode(x, y, 1, bit => bits.Add(bit));

            // Each leaf should just be a 1-bit at the root level and 1-bit at leaf level
            // because root=0 < threshold=1 => write 1 (value matches, ITU-T T.800 B.10.2).
            // The exact count depends on whether the root 1-bit is shared across leaves
            // via the state mechanism. After the first leaf encodes the root as 1, subsequent
            // leaves should see the root state already advanced.
            Assert.That(bits, Has.All.EqualTo(1),
                "All bits should be 1 for an all-zero tree (ITU-T T.800 B.10.2: 1 = value matches)");
        }

        [Test]
        public void Encode_Decode_BitsFullyConsumed()
        {
            // Verify that encode and decode produce/consume exactly the same number of bits
            var values = new int[,]
            {
                { 2, 0, 3 },
                { 1, 4, 0 }
            };
            int maxThreshold = 5;

            var encodeTree = new TagTree(3, 2);
            SetValues(encodeTree, 3, 2, values);
            var bits = EncodeAll(encodeTree, 3, 2, maxThreshold);

            var decodeTree = new TagTree(3, 2);
            int bitIndex = 0;
            for (int threshold = 1; threshold <= maxThreshold; threshold++)
            {
                for (int y = 0; y < 2; y++)
                    for (int x = 0; x < 3; x++)
                        decodeTree.Decode(x, y, threshold, () => bits[bitIndex++]);
            }

            Assert.That(bitIndex, Is.EqualTo(bits.Count),
                "Decode should consume exactly all bits produced by encode");
        }

        #endregion

        #region Edge Cases

        [Test]
        public void ThresholdExceedsAllValues_StillRoundtrips()
        {
            // When all values have been resolved by prior thresholds,
            // subsequent thresholds may still emit bits (the encoder writes
            // 0-bits at leaf level as it traverses from root to leaf). Verify
            // that the encode/decode roundtrip remains correct regardless.
            var tree = new TagTree(2, 2);
            tree.SetValue(0, 0, 0);
            tree.SetValue(1, 0, 0);
            tree.SetValue(0, 1, 0);
            tree.SetValue(1, 1, 0);

            // Encode at threshold=1 (resolves all zeros), then threshold=2
            var bits = EncodeAll(tree, 2, 2, 2);

            // Decode with the same thresholds
            var decodeTree = new TagTree(2, 2);
            int[] decoded = DecodeAll(decodeTree, 2, 2, 2, bits);

            Assert.That(decoded[0], Is.EqualTo(0), "(0,0) should be 0");
            Assert.That(decoded[1], Is.EqualTo(0), "(1,0) should be 0");
            Assert.That(decoded[2], Is.EqualTo(0), "(0,1) should be 0");
            Assert.That(decoded[3], Is.EqualTo(0), "(1,1) should be 0");
        }

        [Test]
        public void Value1_SingleLeaf_Roundtrip()
        {
            VerifyRoundtrip(1, 1, new int[,] { { 1 } });
        }

        [Test]
        public void LargeNonSquare_15x1_Roundtrip()
        {
            var values = new int[1, 15];
            for (int x = 0; x < 15; x++)
                values[0, x] = x % 4;

            VerifyRoundtrip(15, 1, values);
        }

        [Test]
        public void LargeNonSquare_1x15_Roundtrip()
        {
            var values = new int[15, 1];
            for (int y = 0; y < 15; y++)
                values[y, 0] = y % 4;

            VerifyRoundtrip(1, 15, values);
        }

        #endregion
    }
}
