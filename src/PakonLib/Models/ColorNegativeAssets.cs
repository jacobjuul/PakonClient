using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace PakonLib.Models
{
    /// <summary>
    /// The text assets used as TLA's fallback source for color-negative processing.
    /// This represents the on-disk data only; it does not reproduce TLA's subsequent
    /// native lookup-table generation or PakonImau color processing.
    /// </summary>
    public sealed class ColorNegativeAssets
    {
        public const int LutSampleCount = 16384;
        public const int MatrixCoefficientCount = 12;

        private ColorNegativeAssets(int[] lookupCurve, double[] matrixCoefficients)
        {
            LookupCurve = lookupCurve;
            MatrixCoefficients = matrixCoefficients;
        }

        /// <summary>
        /// Gets the 16,384 quantized values from the two-column client negative LUT file.
        /// </summary>
        public int[] LookupCurve { get; private set; }

        /// <summary>
        /// Gets twelve row-major 3x4 affine RGB matrix coefficients from the client negative matrix file.
        /// </summary>
        public double[] MatrixCoefficients { get; private set; }

        public static ColorNegativeAssets Load(string lookupCurvePath, string matrixPath)
        {
            if (lookupCurvePath == null) throw new ArgumentNullException(nameof(lookupCurvePath));
            if (matrixPath == null) throw new ArgumentNullException(nameof(matrixPath));

            return new ColorNegativeAssets(ReadLookupCurve(lookupCurvePath), ReadMatrixCoefficients(matrixPath));
        }

        /// <summary>
        /// Reads the same 16,384 two-column samples that TLA accepts before it writes its binary cache.
        /// The first accepted line must begin with <c>0.0000</c>, matching the native loader.
        /// </summary>
        public static int[] ReadLookupCurve(string path)
        {
            if (path == null) throw new ArgumentNullException(nameof(path));

            using (var reader = new StreamReader(path))
            {
                string line;
                do
                {
                    line = reader.ReadLine();
                    if (line == null)
                    {
                        throw new InvalidDataException("The LUT does not contain a sample beginning with '0.0000'.");
                    }
                }
                while (!line.StartsWith("0.0000", StringComparison.Ordinal));

                var result = new int[LutSampleCount];
                for (var index = 0; index < result.Length; index++)
                {
                    if (!TryReadPair(line, out _, out var value))
                    {
                        throw new InvalidDataException("The LUT sample at index " + index + " is not a valid numeric pair.");
                    }

                    result[index] = (int)value;
                    if (index + 1 < result.Length)
                    {
                        line = reader.ReadLine();
                        if (line == null)
                        {
                            throw new InvalidDataException("The LUT contains fewer than " + LutSampleCount + " samples.");
                        }
                    }
                }

                return result;
            }
        }

        /// <summary>
        /// Reads twelve <c>coeff_&lt;row&gt;_&lt;column&gt;: &lt;double&gt;</c> values in file order.
        /// </summary>
        public static double[] ReadMatrixCoefficients(string path)
        {
            if (path == null) throw new ArgumentNullException(nameof(path));

            var result = new List<double>(MatrixCoefficientCount);
            foreach (var line in File.ReadLines(path))
            {
                if (line.IndexOf("coeff", StringComparison.Ordinal) < 0) continue;

                var separator = line.IndexOf(':');
                if (separator < 0 || !double.TryParse(line.Substring(separator + 1), NumberStyles.Float,
                    CultureInfo.InvariantCulture, out var coefficient))
                {
                    throw new InvalidDataException("The matrix coefficient line is invalid: '" + line + "'.");
                }

                result.Add(coefficient);
                if (result.Count == MatrixCoefficientCount) return result.ToArray();
            }

            throw new InvalidDataException("The matrix contains fewer than " + MatrixCoefficientCount + " coefficients.");
        }

        private static bool TryReadPair(string line, out float input, out float value)
        {
            input = 0;
            value = 0;
            var parts = line.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
            return parts.Length >= 2 &&
                   float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out input) &&
                   float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }
    }
}
