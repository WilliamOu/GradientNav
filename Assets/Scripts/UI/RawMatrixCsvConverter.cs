using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.UI;

public class RawMatrixCsvConverter : MonoBehaviour
{
    [SerializeField] private TitleSceneManager titleSceneManager;
    [SerializeField] private Button convertButton;

    [Header("Defaults")]
    [SerializeField] private float defaultScaleCm = 10f;
    [SerializeField] private float defaultFrameSeconds = 1f;
    [SerializeField] private string interp2D = "bilinear";
    [SerializeField] private string interp3D = "none";
    [SerializeField] private string frameMode = "clamp";

    [Header("CSV Format Assumptions")]
    [SerializeField] private bool hasHeaderRow = true;
    [SerializeField] private bool hasHeaderColumn = true;

    private void Start()
    {
        convertButton.onClick.AddListener(ConvertAllRawMatrices);
    }

    [ContextMenu("Convert Raw Matrix CSVs")]
    public void ConvertAllRawMatrices()
    {
        string rawDir = Path.Combine(Application.persistentDataPath, "Raw Matrices");
        string outRoot = Path.Combine(Application.persistentDataPath, "Matrices");

        Directory.CreateDirectory(rawDir);
        Directory.CreateDirectory(outRoot);

        string[] csvFiles = Directory.GetFiles(rawDir, "*.csv", SearchOption.TopDirectoryOnly);

        if (csvFiles.Length == 0)
        {
            Debug.LogWarning($"No CSV files found in: {rawDir}");
            titleSceneManager.WriteMessage($"No CSV files found in: {rawDir}", Color.yellow);
            return;
        }

        int convertedCount = 0;

        foreach (string csvPath in csvFiles)
        {
            try
            {
                ConvertOne(csvPath, outRoot);
                convertedCount++;
            }
            catch (Exception ex)
            {
                Debug.LogError($"Failed to convert '{csvPath}': {ex.Message}");
                titleSceneManager.WriteMessage($"Failed to convert '{csvPath}': {ex.Message}", Color.red);
            }
        }

        Debug.Log($"Converted {convertedCount} raw matrix CSV file(s).");
        titleSceneManager.WriteMessage($"Converted {convertedCount} raw matrix CSV file(s).", Color.white);
    }

    private void ConvertOne(string csvPath, string outRoot)
    {
        string fileNameNoExt = Path.GetFileNameWithoutExtension(csvPath);
        string outDir = Path.Combine(outRoot, fileNameNoExt);
        Directory.CreateDirectory(outDir);

        string[] lines = File.ReadAllLines(csvPath);
        if (lines.Length == 0)
            throw new Exception("CSV is empty.");

        List<List<string>> rows = new List<List<string>>();
        foreach (string line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            rows.Add(SplitCsvLine(line));
        }

        if (rows.Count == 0)
            throw new Exception("CSV has no non-empty rows.");

        int rowStart = hasHeaderRow ? 1 : 0;
        int colStart = hasHeaderColumn ? 1 : 0;

        if (rows.Count <= rowStart)
            throw new Exception("CSV does not contain any data rows.");

        int width = rows[rowStart].Count - colStart;
        int height = rows.Count - rowStart;

        if (width <= 0 || height <= 0)
            throw new Exception($"Invalid data dimensions. width={width}, height={height}");

        byte[] data = new byte[width * height]; // single frame

        for (int y = 0; y < height; y++)
        {
            List<string> row = rows[rowStart + y];

            if (row.Count - colStart != width)
                throw new Exception($"Row {rowStart + y + 1} has inconsistent column count. Expected {width} data columns, got {row.Count - colStart}.");

            for (int x = 0; x < width; x++)
            {
                string token = row[colStart + x].Trim();

                if (!TryParseFloatFlexible(token, out float value))
                    throw new Exception($"Could not parse numeric value at CSV data cell x={x}, y={y}: '{token}'");

                int byteVal = Mathf.Clamp(Mathf.RoundToInt(value), 0, 255);
                data[y * width + x] = (byte)byteVal;
            }
        }

        float scaleCm = InferScaleCmFromFilename(fileNameNoExt, defaultScaleCm);

        string dataPath = Path.Combine(outDir, "data.bin");
        File.WriteAllBytes(dataPath, data);

        string metaPath = Path.Combine(outDir, "meta.json");
        string metaJson = BuildMetaJson(
            width: width,
            height: height,
            frames: 1,
            scaleCm: scaleCm,
            frameSeconds: defaultFrameSeconds,
            interp2D: interp2D,
            interp3D: interp3D,
            frameMode: frameMode
        );

        File.WriteAllText(metaPath, metaJson, Encoding.UTF8);

        Debug.Log(
            $"Converted '{Path.GetFileName(csvPath)}' -> '{outDir}' " +
            $"(width={width}, height={height}, frames=1, scaleCm={scaleCm})");
    }

    private static string BuildMetaJson(
        int width,
        int height,
        int frames,
        float scaleCm,
        float frameSeconds,
        string interp2D,
        string interp3D,
        string frameMode)
    {
        return
$@"{{
  ""formatVersion"": 1,
  ""dataFile"": ""data.bin"",
  ""dataEncoding"": ""u8_raw"",
  ""width"": {width},
  ""height"": {height},
  ""frames"": {frames},
  ""scaleCm"": {scaleCm.ToString(CultureInfo.InvariantCulture)},
  ""interp2D"": ""{interp2D}"",
  ""interp3D"": ""{interp3D}"",
  ""frameSeconds"": {frameSeconds.ToString(CultureInfo.InvariantCulture)},
  ""frameMode"": ""{frameMode}"",
  ""outOfBounds"": ""zero"",
  ""timeSource"": ""unityTime""
}}";
    }

    private static float InferScaleCmFromFilename(string fileNameNoExt, float fallback)
    {
        // Matches things like "(10cm)" or "10cm"
        Match m = Regex.Match(fileNameNoExt, @"\(?\b(\d+(?:\.\d+)?)\s*cm\b\)?", RegexOptions.IgnoreCase);
        if (m.Success && float.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out float scale))
            return scale;

        return fallback;
    }

    private static bool TryParseFloatFlexible(string s, out float value)
    {
        s = s.Trim();

        if (float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
            return true;

        // fallback for locales that may have weird formatting
        if (float.TryParse(s, out value))
            return true;

        value = 0f;
        return false;
    }

    private static List<string> SplitCsvLine(string line)
    {
        var result = new List<string>();
        if (line == null) return result;

        bool inQuotes = false;
        var cur = new StringBuilder();

        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];

            if (c == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    cur.Append('"');
                    i++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }
            }
            else if (c == ',' && !inQuotes)
            {
                result.Add(cur.ToString().Trim());
                cur.Clear();
            }
            else
            {
                cur.Append(c);
            }
        }

        result.Add(cur.ToString().Trim());
        return result;
    }
}