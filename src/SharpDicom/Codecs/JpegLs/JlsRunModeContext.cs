using System;

namespace SharpDicom.Codecs.JpegLs
{
    /// <summary>
    /// Run mode context for JPEG-LS per ITU-T T.87 Section A.7.
    /// Indices 365 and 366 of the A/N arrays are used for run interruption contexts.
    /// </summary>
    internal struct JlsRunModeContext
    {
        /// <summary>Run interruption type (0 or 1).</summary>
        private int _runInterruptionType;

        /// <summary>Accumulated error magnitude.</summary>
        private int _a;

        /// <summary>Sample count.</summary>
        private int _n;

        /// <summary>Count of negative errors.</summary>
        private int _nn;

        /// <summary>
        /// Initializes the run mode context.
        /// </summary>
        public void Initialize(int runInterruptionType, int range)
        {
            _runInterruptionType = runInterruptionType;
            _a = Math.Max(2, (range + 32) / 64);
            _n = 1;
            _nn = 0;
        }

        /// <summary>
        /// Gets the run interruption type (0 or 1).
        /// </summary>
        public int RunInterruptionType => _runInterruptionType;

        /// <summary>
        /// Computes the Golomb coding parameter for run interruption.
        /// Per ITU-T T.87, A.7.2.
        /// </summary>
        public int ComputeK()
        {
            int temp = _a + (_n >> 1) * _runInterruptionType;
            int nTest = _n;
            int k = 0;
            while (nTest < temp && k < 32)
            {
                nTest <<= 1;
                k++;
            }
            return k;
        }

        /// <summary>
        /// Computes the map flag for error value mapping per ITU-T T.87, A.21.
        /// </summary>
        public bool ComputeMap(int errorValue, int k)
        {
            if (k == 0 && errorValue > 0 && 2 * _nn < _n)
                return true;
            if (errorValue < 0 && 2 * _nn >= _n)
                return true;
            if (errorValue < 0 && k != 0)
                return true;
            return false;
        }

        /// <summary>
        /// Unmaps a run interruption error value per ITU-T T.87, A.22.
        /// </summary>
        public int ComputeErrorValue(int temp, int k)
        {
            bool map = (temp & 1) != 0;
            int errorValueAbs = (temp + (map ? 1 : 0)) / 2;

            if (((k != 0) || (2 * _nn >= _n)) == map)
            {
                return -errorValueAbs;
            }
            return errorValueAbs;
        }

        /// <summary>
        /// Updates variables for run interruption sample per ITU-T T.87, A.23.
        /// </summary>
        public void UpdateVariables(int errorValue, int eMappedErrorValue, int resetThreshold)
        {
            if (errorValue < 0)
            {
                _nn++;
            }

            _a += (eMappedErrorValue + 1 - _runInterruptionType) >> 1;

            if (_n == resetThreshold)
            {
                _a >>= 1;
                _n >>= 1;
                _nn >>= 1;
            }

            _n++;
        }
    }
}
