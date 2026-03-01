using System.IO;
using System.Text;
using UnityEngine;

public class MatrixTestGenerator : MonoBehaviour
{
    [ContextMenu("Generate Test Matrix (Time Fade, Loop)")]
    private void Generate()
    {
        string folderName = "TestTimeFade_500cm";
        string baseDir = Path.Combine(Application.persistentDataPath, "Matrices", folderName);
        Directory.CreateDirectory(baseDir);

        // Matrix settings
        int width = 21;
        int height = 21;
        int frames = 25;

        float scaleCm = 500f;        // 5m per cell
        float frameSeconds = 0.1f;   // 0.1 second per frame

        byte[] data = new byte[width * height * frames];

        // Start bright
        int startBrightness = 255;

        for (int f = 0; f < frames; f++)
        {
            int brightness = Mathf.Clamp(startBrightness - (f * 10), 0, 255);
            byte b = (byte)brightness;

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    // Simple 4-corner map base shape
                    float baseValue = FourCornersValue(x, y, width, height, 6f);
                    byte finalByte = (byte)Mathf.Clamp(Mathf.RoundToInt(baseValue * brightness), 0, 255);

                    int idx = (f * height + y) * width + x;
                    data[idx] = finalByte;
                }
            }
        }

        // Write data.bin
        string dataPath = Path.Combine(baseDir, "data.bin");
        File.WriteAllBytes(dataPath, data);

        // Write meta.json
        string metaPath = Path.Combine(baseDir, "meta.json");
        string metaJson =
$@"{{
  ""formatVersion"": 1,

  ""dataFile"": ""data.bin"",
  ""dataEncoding"": ""u8_raw"",

  ""width"": {width},
  ""height"": {height},
  ""frames"": {frames},

  ""scaleCm"": {scaleCm.ToString(System.Globalization.CultureInfo.InvariantCulture)},

  ""interp2D"": ""bilinear"",
  ""interp3D"": ""linear"",
  ""frameSeconds"": {frameSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture)},

  ""frameMode"": ""loop"",
  ""outOfBounds"": ""zero"",
  ""timeSource"": ""unityTime""
}}";

        File.WriteAllText(metaPath, metaJson, Encoding.UTF8);

        Debug.Log($"Wrote time-fade matrix to: {baseDir}");
    }

    private static float FourCornersValue(int x, int y, int width, int height, float radiusCells)
    {
        Vector2 c00 = new Vector2(0, 0);
        Vector2 c10 = new Vector2(width - 1, 0);
        Vector2 c01 = new Vector2(0, height - 1);
        Vector2 c11 = new Vector2(width - 1, height - 1);

        Vector2 p = new Vector2(x, y);

        float d0 = Vector2.Distance(p, c00);
        float d1 = Vector2.Distance(p, c10);
        float d2 = Vector2.Distance(p, c01);
        float d3 = Vector2.Distance(p, c11);

        float d = Mathf.Min(Mathf.Min(d0, d1), Mathf.Min(d2, d3));

        return Mathf.Clamp01(1f - (d / radiusCells));
    }
}