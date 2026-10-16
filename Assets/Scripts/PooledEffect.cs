using UnityEngine;

/// <summary>
/// Something the <see cref="ObjectPool"/> hands out: a short-lived effect that is rented, played, and
/// then returned rather than being destroyed and made again.
///
/// Subclasses own their own life - they call <see cref="ReturnToPool"/> when they are done. Nothing
/// else has to track them, which is the point: a burst of shots can spawn flashes and impact marks
/// without anyone keeping a list.
/// </summary>
public abstract class PooledEffect : MonoBehaviour
{
    [Tooltip("How many idle copies this effect's pool starts with. Enough to cover a burst without " +
             "the first shot waiting on an Instantiate.")]
    [SerializeField] int prewarm = 8;

    /// <summary>How many idle copies this effect's pool is built with.</summary>
    public int Prewarm => prewarm;

    /// <summary>
    /// The prefab this instance was rented from, or null when it was never rented at all. The pool is
    /// found through it, so an instance can always get itself home.
    /// </summary>
    public PooledEffect Source { get; internal set; }

    /// <summary>Hands this instance back to its pool, ready to be rented again.</summary>
    public void ReturnToPool()
    {
        ObjectPool.Return(this);
    }
}
