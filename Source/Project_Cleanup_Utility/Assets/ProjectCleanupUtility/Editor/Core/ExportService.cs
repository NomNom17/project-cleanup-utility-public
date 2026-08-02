// -----------------------------------------------------------------------
// Project Cleanup Utility
// Copyright (C) 2026 NomNom
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program. If not, see <https://www.gnu.org/licenses/>.
//
// Source: https://github.com/NomNom17/Project-Cleanup-Utility
// -----------------------------------------------------------------------

using ProjectCleanupUtility.Data;
using ProjectCleanupUtility.Utilities;
using System.Collections.Generic;
using System.IO;

namespace ProjectCleanupUtility.Core
{
    public class ExportService
    {
        /// <summary>
        /// Writes the scan report as a CSV file with a summary header block followed by the asset data rows.
        /// </summary>
        /// <param name="scanResult">The completed scan result (summary stats).</param>
        /// <param name="displayedAssets">The currently filtered/sorted asset list to export - export what you see.</param>
        /// <param name="scanLog">The current scan log entries, included as commented header lines.</param>
        /// <param name="filePath">Destination file path.</param>
        public void ExportToCsv(ScanResult scanResult, List<AssetInfo> displayedAssets, List<string> scanLog, string filePath)
        {
            using var writer = new StreamWriter(filePath);

            // Summary block (prefixed with # so spreadsheet apps treat them as comments)
            writer.WriteLine($"# Project Cleanup Utility - Scan Report");
            writer.WriteLine($"# Scan Date: {scanResult.ScanTimestamp:yyyy-MM-dd HH:mm:ss}");
            writer.WriteLine($"# Scan Duration: {scanResult.ScanDurationSeconds:F2}s");
            writer.WriteLine($"# Total Assets Scanned: {scanResult.TotalAssetCount}");
            writer.WriteLine($"# Unused Assets Found: {scanResult.UnusedAssetCount}");
            writer.WriteLine($"# Reclaimable Size (Bytes): {scanResult.UnusedSizeBytes}");
            writer.WriteLine($"# Unused Percentage: {scanResult.UnusedPercentage:F1}%");
            writer.WriteLine($"# Assets In Export: {displayedAssets.Count} (current filter)");

            // Scan log entries if any
            if (scanLog != null && scanLog.Count > 0)
            {
                writer.WriteLine("#");
                writer.WriteLine($"# Scan Log ({scanLog.Count} entries):");

                foreach (var entry in scanLog)
                    writer.WriteLine($"# {entry}");
            }

            writer.WriteLine();

            // Column headers
            writer.WriteLine("\"Name\",\"Category\",\"Size (Bytes)\",\"Refs\",\"Deps\",\"Safety\",\"Path\",\"GUID\"");

            // Data rows - export the current filtered view
            foreach (var asset in displayedAssets)
            {
                string name = asset.Name.Replace("\"", "\"\"");
                string assetPath = asset.Path.Replace("\"", "\"\"");
                string category = AssetCategoryResolver.GetDisplayName(asset.Category);

                writer.WriteLine($"\"{name}\",\"{category}\",{asset.SizeBytes}," + $"{asset.ReferenceCount},{asset.DependencyCount},\"{SafetyLabel(asset.Safety)}\"," + $"\"{assetPath}\",\"{asset.GUID}\"");
            }
        }

        /// <summary>
        /// Writes the scan report as an Excel .xlsx file using the Open XML spreadsheet format (which is really just a zip file containing XML). <br></br>
        /// No external dependencies required - we build the archive manually using <see cref="System.IO.Compression.ZipArchive"/>.
        /// </summary>
        /// <param name="scanResult">The completed scan result (summary stats).</param>
        /// <param name="displayedAssets">The currently filtered/sorted asset list to export - export what you see.</param>
        /// <param name="filePath">Destination file path.</param>
        public void ExportToXlsx(ScanResult scanResult, List<AssetInfo> displayedAssets, string filePath)
        {
            // The .xlsx format is a zip containing XML parts.
            // Here we build the minimum viable structure:
            //   [Content_Types].xml
            //   _rels/.rels
            //   xl/workbook.xml
            //   xl/_rels/workbook.xml.rels
            //   xl/styles.xml
            //   xl/sharedStrings.xml
            //   xl/worksheets/sheet1.xml (Summary)
            //   xl/worksheets/sheet2.xml (Assets)

            // Collect all unique strings for the shared strings table
            var sharedStrings = new List<string>();
            var stringIndex = new Dictionary<string, int>();

            int GetOrAddString(string s)
            {
                if (s == null) s = "";

                if (!stringIndex.TryGetValue(s, out int idx))
                {
                    idx = sharedStrings.Count;
                    sharedStrings.Add(s);
                    stringIndex[s] = idx;
                }

                return idx;
            }

            // The export set respects the current filter - displayedAssets already reflects whatever the user has filtered/sorted in the UI. If "Unused Only" is off, this will include all scanned assets; if a category filter is active it will include only that category. This is intentional: export what you see.
            var exportAssets = displayedAssets;

            // Pre-register all strings we'll need
            // Summary sheet strings
            string[] summaryLabels = {
                "Project Cleanup Utility - Scan Report",
                "Scan Date", scanResult.ScanTimestamp.ToString("yyyy-MM-dd HH:mm:ss"),
                "Scan Duration", $"{scanResult.ScanDurationSeconds:F2}s",
                "Total Assets Scanned", scanResult.TotalAssetCount.ToString(),
                "Unused Assets Found", scanResult.UnusedAssetCount.ToString(),
                "Reclaimable Size (Bytes)", scanResult.UnusedSizeBytes.ToString(),
                "Unused Percentage", $"{scanResult.UnusedPercentage:F1}%",
                "Assets In Export", exportAssets.Count.ToString()
            };
            foreach (string s in summaryLabels) GetOrAddString(s);

            // Asset sheet header strings
            string[] headers = { "Name", "Category", "Size (Bytes)", "Refs", "Deps", "Safety", "Path", "GUID" };
            foreach (string h in headers) GetOrAddString(h);

            // Asset data strings
            foreach (var a in exportAssets)
            {
                GetOrAddString(a.Name);
                GetOrAddString(AssetCategoryResolver.GetDisplayName(a.Category));
                GetOrAddString(SafetyLabel(a.Safety));
                GetOrAddString(a.Path);
                GetOrAddString(a.GUID);
            }

            // Dependency sheet header strings
            string[] depHeaders = { "Asset", "Asset Path", "Type", "Related Asset Path" };
            foreach (string h in depHeaders) GetOrAddString(h);
            GetOrAddString("Dependency");
            GetOrAddString("Reference");
            GetOrAddString("(none)");

            // Dependency data strings
            foreach (var a in exportAssets)
            {
                GetOrAddString(a.Name);
                GetOrAddString(a.Path);

                if (a.DependsOn != null) foreach (var d in a.DependsOn) GetOrAddString(d);
                if (a.ReferencedBy != null) foreach (var r in a.ReferencedBy) GetOrAddString(r);
            }

            // Build the xlsx zip
            using var stream = new FileStream(filePath, System.IO.FileMode.Create);
            using var zip = new System.IO.Compression.ZipArchive(stream, System.IO.Compression.ZipArchiveMode.Create);

            // [Content_Types].xml - three sheets: Summary, Assets, Dependencies
            WriteZipEntry(zip, "[Content_Types].xml",
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
                "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>" +
                "<Default Extension=\"xml\" ContentType=\"application/xml\"/>" +
                "<Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>" +
                "<Override PartName=\"/xl/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml\"/>" +
                "<Override PartName=\"/xl/sharedStrings.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sharedStrings+xml\"/>" +
                "<Override PartName=\"/xl/worksheets/sheet1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>" +
                "<Override PartName=\"/xl/worksheets/sheet2.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>" +
                "<Override PartName=\"/xl/worksheets/sheet3.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>" +
                "</Types>");

            // _rels/.rels
            WriteZipEntry(zip, "_rels/.rels",
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/>" +
                "</Relationships>");

            // xl/workbook.xml
            WriteZipEntry(zip, "xl/workbook.xml",
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                "<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">" +
                "<sheets>" +
                "<sheet name=\"Summary\" sheetId=\"1\" r:id=\"rId1\"/>" +
                "<sheet name=\"Assets\" sheetId=\"2\" r:id=\"rId2\"/>" +
                "<sheet name=\"Dependencies\" sheetId=\"3\" r:id=\"rId3\"/>" +
                "</sheets></workbook>");

            // xl/_rels/workbook.xml.rels
            WriteZipEntry(zip, "xl/_rels/workbook.xml.rels",
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet1.xml\"/>" +
                "<Relationship Id=\"rId2\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet2.xml\"/>" +
                "<Relationship Id=\"rId3\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet3.xml\"/>" +
                "<Relationship Id=\"rId4\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/>" +
                "<Relationship Id=\"rId5\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/sharedStrings\" Target=\"sharedStrings.xml\"/>" +
                "</Relationships>");

            // xl/styles.xml
            // Style index reference:
            //   0  - normal
            //   1  - bold (summary labels)
            //   2  - column header: bold white text on dark fill, thin border
            //   3  - zebra even row: light grey fill
            //   4  - Safe:       green fill + dark green bold text
            //   5  - Caution:    amber fill + dark amber bold text
            //   6  - Unsafe:     red fill   + dark red bold text
            //   7  - Dependency: blue fill  + dark blue bold text
            //   8  - Reference:  peach fill + dark brown bold text
            //   9  - title:      bold white text on dark fill, centred

            WriteZipEntry(zip, "xl/styles.xml",
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                "<styleSheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">" +

                // ---- fonts (index 0-6) ----
                "<fonts count=\"7\">" +
                "<font><sz val=\"11\"/><name val=\"Calibri\"/></font>" + // 0 normal
                "<font><b/><sz val=\"11\"/><name val=\"Calibri\"/></font>" + // 1 bold
                "<font><b/><sz val=\"11\"/><color rgb=\"FFFFFFFF\"/><name val=\"Calibri\"/></font>" + // 2 bold white (header)
                "<font><b/><sz val=\"11\"/><color rgb=\"FF276221\"/><name val=\"Calibri\"/></font>" + // 3 dark green (Safe)
                "<font><b/><sz val=\"11\"/><color rgb=\"FF9C5700\"/><name val=\"Calibri\"/></font>" + // 4 dark amber (Caution)
                "<font><b/><sz val=\"11\"/><color rgb=\"FF9C0006\"/><name val=\"Calibri\"/></font>" + // 5 dark red (Unsafe)
                "<font><b/><sz val=\"11\"/><color rgb=\"FF203864\"/><name val=\"Calibri\"/></font>" + // 6 dark blue (Dependency)
                "</fonts>" +

                // ---- fills (index 0-9, first two are required by spec) ----
                "<fills count=\"10\">" +
                "<fill><patternFill patternType=\"none\"/></fill>" + // 0 none (required)
                "<fill><patternFill patternType=\"gray125\"/></fill>" + // 1 gray125 (required)
                "<fill><patternFill patternType=\"solid\"><fgColor rgb=\"FF2F3640\"/></patternFill></fill>" + // 2 dark header
                "<fill><patternFill patternType=\"solid\"><fgColor rgb=\"FFF2F2F2\"/></patternFill></fill>" + // 3 zebra grey
                "<fill><patternFill patternType=\"solid\"><fgColor rgb=\"FFC6EFCE\"/></patternFill></fill>" + // 4 safe green
                "<fill><patternFill patternType=\"solid\"><fgColor rgb=\"FFFFEB9C\"/></patternFill></fill>" + // 5 caution amber
                "<fill><patternFill patternType=\"solid\"><fgColor rgb=\"FFFFC7CE\"/></patternFill></fill>" + // 6 unsafe red
                "<fill><patternFill patternType=\"solid\"><fgColor rgb=\"FFDDEBF7\"/></patternFill></fill>" + // 7 dependency blue
                "<fill><patternFill patternType=\"solid\"><fgColor rgb=\"FFFCE4D6\"/></patternFill></fill>" + // 8 reference peach
                "<fill><patternFill patternType=\"solid\"><fgColor rgb=\"FF833C00\"/></patternFill></fill>" + // 9 (unused, kept for count)
                "</fills>" +

                // thin border (index 0-1)
                "<borders count=\"2\">" +
                "<border><left/><right/><top/><bottom/><diagonal/></border>" + // 0 no border
                "<border>" + // 1 thin all sides
                "<left style=\"thin\"><color rgb=\"FFBFBFBF\"/></left>" +
                "<right style=\"thin\"><color rgb=\"FFBFBFBF\"/></right>" +
                "<top style=\"thin\"><color rgb=\"FFBFBFBF\"/></top>" +
                "<bottom style=\"thin\"><color rgb=\"FFBFBFBF\"/></bottom>" +
                "<diagonal/>" +
                "</border>" +
                "</borders>" +

                "<cellStyleXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\"/></cellStyleXfs>" +

                // cell formats (xf index = style s= attribute)
                "<cellXfs count=\"10\">" +
                "<xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\"/>" + // s=0 normal
                "<xf numFmtId=\"0\" fontId=\"1\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyFont=\"1\"/>" + // s=1 bold
                "<xf numFmtId=\"0\" fontId=\"2\" fillId=\"2\" borderId=\"1\" xfId=\"0\" applyFont=\"1\" applyFill=\"1\" applyBorder=\"1\"/>" + // s=2 col header
                "<xf numFmtId=\"0\" fontId=\"0\" fillId=\"3\" borderId=\"1\" xfId=\"0\" applyFill=\"1\" applyBorder=\"1\"/>" + // s=3 zebra
                "<xf numFmtId=\"0\" fontId=\"3\" fillId=\"4\" borderId=\"1\" xfId=\"0\" applyFont=\"1\" applyFill=\"1\" applyBorder=\"1\"/>" + // s=4 safe
                "<xf numFmtId=\"0\" fontId=\"4\" fillId=\"5\" borderId=\"1\" xfId=\"0\" applyFont=\"1\" applyFill=\"1\" applyBorder=\"1\"/>" + // s=5 caution
                "<xf numFmtId=\"0\" fontId=\"5\" fillId=\"6\" borderId=\"1\" xfId=\"0\" applyFont=\"1\" applyFill=\"1\" applyBorder=\"1\"/>" + // s=6 unsafe
                "<xf numFmtId=\"0\" fontId=\"6\" fillId=\"7\" borderId=\"1\" xfId=\"0\" applyFont=\"1\" applyFill=\"1\" applyBorder=\"1\"/>" + // s=7 dependency
                "<xf numFmtId=\"0\" fontId=\"4\" fillId=\"8\" borderId=\"1\" xfId=\"0\" applyFont=\"1\" applyFill=\"1\" applyBorder=\"1\"/>" + // s=8 reference
                "<xf numFmtId=\"0\" fontId=\"2\" fillId=\"2\" borderId=\"0\" xfId=\"0\" applyFont=\"1\" applyFill=\"1\" applyAlignment=\"1\"><alignment horizontal=\"center\"/></xf>" + // s=9 title
                "</cellXfs></styleSheet>");

            // xl/sharedStrings.xml
            var ssXml = new System.Text.StringBuilder();
            ssXml.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
            ssXml.Append($"<sst xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" count=\"{sharedStrings.Count}\" uniqueCount=\"{sharedStrings.Count}\">");
            foreach (string s in sharedStrings)
            {
                ssXml.Append("<si><t>");
                ssXml.Append(XmlEscape(s));
                ssXml.Append("</t></si>");
            }
            ssXml.Append("</sst>");
            WriteZipEntry(zip, "xl/sharedStrings.xml", ssXml.ToString());

            // Sheet 1: Summary
            var s1 = new System.Text.StringBuilder();
            s1.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
            s1.Append("<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:mc=\"http://schemas.openxmlformats.org/markup-compatibility/2006\">");
            s1.Append("<cols><col min=\"1\" max=\"1\" width=\"30\" customWidth=\"1\"/><col min=\"2\" max=\"2\" width=\"36\" customWidth=\"1\"/></cols>");
            s1.Append("<sheetData>");

            // Title row - bold white on dark, centred (s=9), merged A1:B1
            s1.Append("<row r=\"1\">");
            s1.Append($"<c r=\"A1\" t=\"s\" s=\"9\"><v>{GetOrAddString("Project Cleanup Utility - Scan Report")}</v></c>");
            s1.Append("<c r=\"B1\" s=\"9\"/>");
            s1.Append("</row>");

            // Data rows - bold label in A (s=1), normal value in B (s=0), zebra on even rows
            string[][] summaryRows = {
                new[] { "Scan Date", scanResult.ScanTimestamp.ToString("yyyy-MM-dd HH:mm:ss") },
                new[] { "Scan Duration", $"{scanResult.ScanDurationSeconds:F2}s" },
                new[] { "Total Assets Scanned", scanResult.TotalAssetCount.ToString() },
                new[] { "Unused Assets Found", scanResult.UnusedAssetCount.ToString() },
                new[] { "Reclaimable Size (Bytes)", scanResult.UnusedSizeBytes.ToString() },
                new[] { "Unused Percentage", $"{scanResult.UnusedPercentage:F1}%" },
                new[] { "Assets In Export", exportAssets.Count.ToString() }
            };

            for (int r = 0; r < summaryRows.Length; r++)
            {
                int row = r + 2; // start at row 2
                bool even = (row % 2 == 0);
                int labelStyle = even ? 1 : 1;  // bold label always
                int valueStyle = even ? 3 : 0;  // zebra on even
                s1.Append($"<row r=\"{row}\">");
                s1.Append($"<c r=\"A{row}\" t=\"s\" s=\"{labelStyle}\"><v>{GetOrAddString(summaryRows[r][0])}</v></c>");
                s1.Append($"<c r=\"B{row}\" t=\"s\" s=\"{valueStyle}\"><v>{GetOrAddString(summaryRows[r][1])}</v></c>");
                s1.Append("</row>");
            }
            s1.Append("</sheetData>");

            // Merge A1:B1 for the title
            s1.Append("<mergeCells count=\"1\"><mergeCell ref=\"A1:B1\"/></mergeCells>");
            s1.Append("</worksheet>");
            WriteZipEntry(zip, "xl/worksheets/sheet1.xml", s1.ToString());

            // Sheet 2: Assets
            var s2 = new System.Text.StringBuilder();
            s2.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
            s2.Append("<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">");
            s2.Append("<cols>");
            int[] colWidths = { 30, 18, 14, 6, 6, 10, 60, 36 };

            for (int c = 0; c < colWidths.Length; c++)
                s2.Append($"<col min=\"{c + 1}\" max=\"{c + 1}\" width=\"{colWidths[c]}\" customWidth=\"1\"/>");

            s2.Append("</cols>");
            s2.Append("<sheetData>");

            // Header row - s=2: bold white on dark fill with border
            s2.Append("<row r=\"1\">");
            string[] colLetters = { "A", "B", "C", "D", "E", "F", "G", "H" };

            for (int c = 0; c < headers.Length; c++)
                s2.Append($"<c r=\"{colLetters[c]}1\" t=\"s\" s=\"2\"><v>{GetOrAddString(headers[c])}</v></c>");

            s2.Append("</row>");

            // Data rows - zebra stripe + safety column coloured by value
            for (int i = 0; i < exportAssets.Count; i++)
            {
                var a = exportAssets[i];
                int row = i + 2;
                int baseStyle = (row % 2 == 0) ? 3 : 0; // zebra or plain
                int safetyStyle = a.Safety switch
                {
                    DeletionSafety.Safe => 4,
                    DeletionSafety.Caution => 5,
                    DeletionSafety.Unsafe => 6,
                    _ => baseStyle
                };

                s2.Append($"<row r=\"{row}\">");
                s2.Append($"<c r=\"A{row}\" t=\"s\" s=\"{baseStyle}\"><v>{GetOrAddString(a.Name)}</v></c>");
                s2.Append($"<c r=\"B{row}\" t=\"s\" s=\"{baseStyle}\"><v>{GetOrAddString(AssetCategoryResolver.GetDisplayName(a.Category))}</v></c>");
                s2.Append($"<c r=\"C{row}\" s=\"{baseStyle}\"><v>{a.SizeBytes}</v></c>");
                s2.Append($"<c r=\"D{row}\" s=\"{baseStyle}\"><v>{a.ReferenceCount}</v></c>");
                s2.Append($"<c r=\"E{row}\" s=\"{baseStyle}\"><v>{a.DependencyCount}</v></c>");
                s2.Append($"<c r=\"F{row}\" t=\"s\" s=\"{safetyStyle}\"><v>{GetOrAddString(SafetyLabel(a.Safety))}</v></c>");
                s2.Append($"<c r=\"G{row}\" t=\"s\" s=\"{baseStyle}\"><v>{GetOrAddString(a.Path)}</v></c>");
                s2.Append($"<c r=\"H{row}\" t=\"s\" s=\"{baseStyle}\"><v>{GetOrAddString(a.GUID)}</v></c>");
                s2.Append("</row>");
            }
            s2.Append("</sheetData></worksheet>");
            WriteZipEntry(zip, "xl/worksheets/sheet2.xml", s2.ToString());

            // Sheet 3: Dependencies & References
            // One row per dependency or reference relationship. Each asset expands into N rows - one per DependsOn entry, then one per ReferencedBy entry.
            // Assets with no relationships get a single placeholder row so they still appear in the sheet and aren't silently omitted.
            var s3 = new System.Text.StringBuilder();
            s3.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
            s3.Append("<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">");
            s3.Append("<cols>");
            s3.Append("<col min=\"1\" max=\"1\" width=\"28\" customWidth=\"1\"/>");
            s3.Append("<col min=\"2\" max=\"2\" width=\"52\" customWidth=\"1\"/>");
            s3.Append("<col min=\"3\" max=\"3\" width=\"16\" customWidth=\"1\"/>");
            s3.Append("<col min=\"4\" max=\"4\" width=\"52\" customWidth=\"1\"/>");
            s3.Append("</cols>");
            s3.Append("<sheetData>");

            // Header row - s=2: bold white on dark fill with border
            s3.Append("<row r=\"1\">");
            s3.Append($"<c r=\"A1\" t=\"s\" s=\"2\"><v>{GetOrAddString(depHeaders[0])}</v></c>");
            s3.Append($"<c r=\"B1\" t=\"s\" s=\"2\"><v>{GetOrAddString(depHeaders[1])}</v></c>");
            s3.Append($"<c r=\"C1\" t=\"s\" s=\"2\"><v>{GetOrAddString(depHeaders[2])}</v></c>");
            s3.Append($"<c r=\"D1\" t=\"s\" s=\"2\"><v>{GetOrAddString(depHeaders[3])}</v></c>");
            s3.Append("</row>");

            // Data rows - zebra stripe, Type column coloured: 7=Dependency (blue), 8=Reference (peach)
            int depRow = 2;
            foreach (var a in exportAssets)
            {
                bool hasRelationships = false;

                if (a.DependsOn != null && a.DependsOn.Count > 0)
                {
                    foreach (var dep in a.DependsOn)
                    {
                        int baseStyle = (depRow % 2 == 0) ? 3 : 0;
                        s3.Append($"<row r=\"{depRow}\">");
                        s3.Append($"<c r=\"A{depRow}\" t=\"s\" s=\"{baseStyle}\"><v>{GetOrAddString(a.Name)}</v></c>");
                        s3.Append($"<c r=\"B{depRow}\" t=\"s\" s=\"{baseStyle}\"><v>{GetOrAddString(a.Path)}</v></c>");
                        s3.Append($"<c r=\"C{depRow}\" t=\"s\" s=\"7\"><v>{GetOrAddString("Dependency")}</v></c>");
                        s3.Append($"<c r=\"D{depRow}\" t=\"s\" s=\"{baseStyle}\"><v>{GetOrAddString(dep)}</v></c>");
                        s3.Append("</row>");
                        depRow++;
                    }
                    hasRelationships = true;
                }

                if (a.ReferencedBy != null && a.ReferencedBy.Count > 0)
                {
                    foreach (var refBy in a.ReferencedBy)
                    {
                        int baseStyle = (depRow % 2 == 0) ? 3 : 0;
                        s3.Append($"<row r=\"{depRow}\">");
                        s3.Append($"<c r=\"A{depRow}\" t=\"s\" s=\"{baseStyle}\"><v>{GetOrAddString(a.Name)}</v></c>");
                        s3.Append($"<c r=\"B{depRow}\" t=\"s\" s=\"{baseStyle}\"><v>{GetOrAddString(a.Path)}</v></c>");
                        s3.Append($"<c r=\"C{depRow}\" t=\"s\" s=\"8\"><v>{GetOrAddString("Reference")}</v></c>");
                        s3.Append($"<c r=\"D{depRow}\" t=\"s\" s=\"{baseStyle}\"><v>{GetOrAddString(refBy)}</v></c>");
                        s3.Append("</row>");
                        depRow++;
                    }
                    hasRelationships = true;
                }

                if (!hasRelationships)
                {
                    int baseStyle = (depRow % 2 == 0) ? 3 : 0;
                    s3.Append($"<row r=\"{depRow}\">");
                    s3.Append($"<c r=\"A{depRow}\" t=\"s\" s=\"{baseStyle}\"><v>{GetOrAddString(a.Name)}</v></c>");
                    s3.Append($"<c r=\"B{depRow}\" t=\"s\" s=\"{baseStyle}\"><v>{GetOrAddString(a.Path)}</v></c>");
                    s3.Append($"<c r=\"C{depRow}\" t=\"s\" s=\"{baseStyle}\"><v>{GetOrAddString("(none)")}</v></c>");
                    s3.Append($"<c r=\"D{depRow}\" t=\"s\" s=\"{baseStyle}\"><v>{GetOrAddString("(none)")}</v></c>");
                    s3.Append("</row>");
                    depRow++;
                }
            }

            s3.Append("</sheetData></worksheet>");
            WriteZipEntry(zip, "xl/worksheets/sheet3.xml", s3.ToString());
        }

        /// <summary>
        /// Returns a safety label string from a <see cref="DeletionSafety"/> value.
        /// Shared across all export formats to keep things consistent.
        /// </summary>
        private static string SafetyLabel(DeletionSafety safety) =>
            safety switch
        {
            DeletionSafety.Safe => "Safe",
            DeletionSafety.Caution => "Caution",
            DeletionSafety.Unsafe => "Unsafe",
            _ => "Unknown"
        };

        /// <summary>
        /// Writes a UTF-8 string entry into a <see cref="System.IO.Compression.ZipArchive"/>.
        /// </summary>
        private static void WriteZipEntry(System.IO.Compression.ZipArchive zip, string entryName, string content)
        {
            var entry = zip.CreateEntry(entryName, System.IO.Compression.CompressionLevel.Fastest);
            using var writer = new StreamWriter(entry.Open(), System.Text.Encoding.UTF8);
            writer.Write(content);
        }

        /// <summary>
        /// Escapes special XML characters in a string for safe embedding in XML content.
        /// </summary>
        private static string XmlEscape(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";

            // Strip control characters that are illegal in XML 1.0 (anything below 0x20 except tab 0x09, newline 0x0A, carriage return 0x0D). Scan log entries can contain these if Unity appends stack traces or timestamps with embedded control sequences. A single illegal character anywhere in sharedStrings.xml or a worksheet will corrupt the entire part - which is exactly the "removed records from sheet3.xml" error Excel reports without telling you why.
            var sb = new System.Text.StringBuilder(s.Length);
            foreach (char c in s)
            {
                if (c == 0x09 || c == 0x0A || c == 0x0D || (c >= 0x20 && c != 0xFFFE && c != 0xFFFF))
                    sb.Append(c);

                // else: silently drop the illegal character
            }
            string clean = sb.ToString();

            return clean
                .Replace("&",  "&amp;")
                .Replace("<",  "&lt;")
                .Replace(">",  "&gt;")
                .Replace("\"", "&quot;")
                .Replace("'",  "&apos;");
        }
    }
}
