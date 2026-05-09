using System;
using System.Collections.Generic;
using System.Text;

namespace rancher_minimap
{
    internal static class DiagnosticTable
    {
        private const int DefaultMaxCellLength = 88;

        public static void Append(StringBuilder builder, string[] headers, IReadOnlyList<string[]> rows, int maxCellLength = DefaultMaxCellLength)
        {
            if (builder == null || headers == null || rows == null)
                return;

            var widths = new int[headers.Length];
            for (var i = 0; i < headers.Length; i++)
                widths[i] = Truncate(headers[i], maxCellLength).Length;

            foreach (var row in rows)
            {
                if (row == null)
                    continue;

                for (var i = 0; i < headers.Length && i < row.Length; i++)
                    widths[i] = Math.Max(widths[i], Truncate(row[i], maxCellLength).Length);
            }

            builder.AppendLine();
            AppendRow(builder, headers, widths, maxCellLength);
            AppendSeparator(builder, widths);
            foreach (var row in rows)
                AppendRow(builder, row, widths, maxCellLength);
        }

        private static void AppendRow(StringBuilder builder, string[] cells, int[] widths, int maxCellLength)
        {
            builder.Append("| ");
            for (var i = 0; i < widths.Length; i++)
            {
                var value = cells != null && i < cells.Length ? Truncate(cells[i], maxCellLength) : string.Empty;
                builder.Append(value.PadRight(widths[i])).Append(" | ");
            }
            builder.AppendLine();
        }

        private static void AppendSeparator(StringBuilder builder, int[] widths)
        {
            builder.Append("| ");
            for (var i = 0; i < widths.Length; i++)
                builder.Append(new string('-', widths[i])).Append(" | ");
            builder.AppendLine();
        }

        private static string Truncate(string value, int maxLength)
        {
            if (string.IsNullOrEmpty(value))
                return "-";

            maxLength = Math.Max(4, maxLength);
            return value.Length <= maxLength ? value : value.Substring(0, maxLength - 3) + "…";
        }
    }
}
