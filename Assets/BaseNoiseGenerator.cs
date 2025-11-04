using UnityEngine;

public static class BaseNoiseGenerator
{
    /// <summary>
    /// Генерує карту висот:
    /// 1) biasSource задає базовий рельєф (континенти/океани/гори)
    /// 2) шум додає дрібну варіацію навколо цієї бази
    /// 3) результат просто клємпиться в [0..1], без глобальної нормалізації,
    ///    щоб океан залишався низько, а материк високо.
    /// </summary>
    /// <param name="targetWidth">Ширина карти (наприклад resolution терейну)</param>
    /// <param name="targetHeight">Висота карти</param>
    /// <param name="seed">Cид світу</param>
    /// <param name="noiseScale">Масштаб шуму (чим менше – тим більші плями)</param>
    /// <param name="octaves">Кількість октав фрактального шуму</param>
    /// <param name="persistence">Зменшення амплітуди між октавами</param>
    /// <param name="lacunarity">Збільшення частоти між октавами</param>
    /// <param name="biasSource">biasData з ElevationBiasComponent або null</param>
    /// <param name="biasStrength">
    /// Наскільки сильно biasSource впливає на базовий рівень.
    /// Використовується як множник сирого bias перед маппінгом у [0..1].
    /// Типова ідея: 1.0 = як є, 2.0 = ще контрастніше. 
    /// </param>
    /// <returns>float[width,height] у [0..1] вже готовий для Terrain</returns>
    public static float[,] GenerateHeightmap(
        int targetWidth,
        int targetHeight,
        int seed,
        float noiseScale,
        int octaves,
        float persistence,
        float lacunarity,
        float[,] biasSource,
        float biasStrength
    )
    {
        if (noiseScale <= 0f)
            noiseScale = 0.0001f;

        float[,] heightData = new float[targetWidth, targetHeight];

        // стабільний рандом по сидy -> офсет шуму
        System.Random prng = (seed == 0)
            ? new System.Random()
            : new System.Random(seed);

        float offsetX = prng.Next(-100000, 100000);
        float offsetY = prng.Next(-100000, 100000);

        // це наскільки сильно шум колише поверхню навколо bias-бази
        // 0.25f означає +/-0.125 локальні варіації
        // винеси потім у параметр, якщо треба
        float noiseAmplitude = 0.25f;

        for (int y = 0; y < targetHeight; y++)
        {
            for (int x = 0; x < targetWidth; x++)
            {
                // --- 1. отримаємо базовий рівень із bias ---
                float baseHeight01 = 0.5f; // дефолт, якщо bias нема
                if (biasSource != null)
                {
                    // беремо сирий bias (типу -0.5 океан, +0.5 континент, +гори)
                    float rawBias = SampleBilinear(biasSource, x, y, targetWidth, targetHeight);

                    // підсилюємо його користувацьким множником
                    rawBias *= biasStrength;

                    // мапимо в "очікувану висоту"
                    // ідея: bias -0.5 -> 0.0 (глибина)
                    //       bias  0.0 -> 0.5 (середина)
                    //       bias +0.5 -> 1.0 (високо)
                    //       гори з +0.8 -> вище 1, але ми потім клэмпнемо
                    baseHeight01 = 0.5f + rawBias;

                    // не даємо вийти за дозволений діапазон, бо террейн хоче [0..1]
                    baseHeight01 = Mathf.Clamp01(baseHeight01);
                }

                // --- 2. дрібний рельєф шуму навколо бази ---
                // фрактальний перлін вертає щось близьке до [0..1]
                // переводимо в відхилення навколо 0: [-0.5 .. 0.5]
                float nx = (x + offsetX) / noiseScale;
                float ny = (y + offsetY) / noiseScale;

                float perlin = FractalPerlin(nx, ny, octaves, persistence, lacunarity); // ~0..1
                float deviation = (perlin - 0.5f); // ~[-0.5..0.5]

                float noisyHeight = baseHeight01 + deviation * noiseAmplitude;

                // --- 3. клємп у [0..1] без глобальної нормалізації ---
                if (noisyHeight < 0f) noisyHeight = 0f;
                if (noisyHeight > 1f) noisyHeight = 1f;

                heightData[x, y] = noisyHeight;
            }
        }

        return heightData;
    }

    /// <summary>
    /// Класичний фрактальний перлін (усереднений по амплітуді, вихід ~0..1).
    /// </summary>
    private static float FractalPerlin(
        float x,
        float y,
        int octaves,
        float persistence,
        float lacunarity
    )
    {
        float total = 0f;
        float amplitude = 1f;
        float frequency = 1f;
        float amplitudeSum = 0f;

        for (int i = 0; i < octaves; i++)
        {
            float sampleX = x * frequency;
            float sampleY = y * frequency;

            float p = Mathf.PerlinNoise(sampleX, sampleY); // 0..1
            total += p * amplitude;

            amplitudeSum += amplitude;
            amplitude *= persistence;
            frequency *= lacunarity;
        }

        if (amplitudeSum > 0f)
            total /= amplitudeSum;

        return total; // ~0..1
    }

    /// <summary>
    /// Білайнерна вибірка з biasSource (який може мати інший розмір).
    /// </summary>
    private static float SampleBilinear(float[,] src, int tx, int ty, int tw, int th)
    {
        int bw = src.GetLength(0);
        int bh = src.GetLength(1);

        if (bw == 0 || bh == 0)
            return 0f;

        // u,v в координатах bias
        float u = (tx / (float)(tw - 1)) * (bw - 1);
        float v = (ty / (float)(th - 1)) * (bh - 1);

        int x0 = Mathf.FloorToInt(u);
        int y0 = Mathf.FloorToInt(v);
        int x1 = Mathf.Min(x0 + 1, bw - 1);
        int y1 = Mathf.Min(y0 + 1, bh - 1);

        float txLerp = u - x0;
        float tyLerp = v - y0;

        float a = src[x0, y0];
        float b = src[x1, y0];
        float c = src[x0, y1];
        float d = src[x1, y1];

        float ab = Mathf.Lerp(a, b, txLerp);
        float cd = Mathf.Lerp(c, d, txLerp);
        float res = Mathf.Lerp(ab, cd, tyLerp);
        return res;
    }
}
