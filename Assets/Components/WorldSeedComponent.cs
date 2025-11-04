using UnityEngine;

/// <summary>
/// Зберігає спільне зерно (seed) світу.
/// Ідея: всі генератори читають одне і те саме значення.
/// </summary>
[ExecuteInEditMode]
public class WorldSeedComponent : MonoBehaviour
{
    [Tooltip("Глобальний сид процедурної генерації світу. 0 = повністю рандом кожного разу.")]
    public int worldSeed = 12345;
}
