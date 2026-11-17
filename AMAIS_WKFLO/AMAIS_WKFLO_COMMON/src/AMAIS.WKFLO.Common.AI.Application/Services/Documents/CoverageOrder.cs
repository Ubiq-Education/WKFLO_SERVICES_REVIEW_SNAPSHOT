namespace AMAIS.WKFLO.Common.AI.Application.Services.Documents
{
    /// <summary>
    /// The indices 0..count-1 in bit-reversal order (0, n/2, n/4, 3n/4, ...). Indexing chunks in this order means that
    /// stopping early (chunk cap or time budget) still leaves the indexed chunks spread evenly over the whole document.
    /// </summary>
    public static class CoverageOrder
    {
        public static IEnumerable<int> Generate(int count)
        {
            if (count <= 0)
                yield break;

            var bits = 0;
            while ((1L << bits) < count)
                bits++;

            var size = 1 << bits;
            for (var i = 0; i < size; i++)
            {
                var reversed = Reverse(i, bits);
                if (reversed < count)
                    yield return reversed;
            }
        }

        private static int Reverse(int value, int bits)
        {
            var result = 0;
            for (var b = 0; b < bits; b++)
            {
                result = (result << 1) | (value & 1);
                value >>= 1;
            }

            return result;
        }
    }
}
